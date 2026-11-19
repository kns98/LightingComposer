// -----------------------------------------------------------------------------
// File: Rendering/VulkanRasterCpu.cs
// Purpose: CPU-side scene preparation for the Vulkan rasterizer.
//
// Pure scene math, geometry metadata, texture placement decisions, and
// diagnostics. Contains no Vulkan API references by design, so it stays
// unit-testable without a GPU.
// -----------------------------------------------------------------------------

// Import diagnostics for stopwatch timing
using System.Diagnostics;
// Import vector math types
using System.Numerics;
// Import struct layout attributes
using System.Runtime.InteropServices;
// Import string building utilities
using System.Text;
// Import cancellation token support
using System.Threading;
// Import camera basis types
using LightingShowcase.CameraSystem;
// Import scene light types
using LightingShowcase.Lighting;
// Import Vec3 math types
using LightingShowcase.Math3D;
// Import scene graph types
using LightingShowcase.SceneGraph;
// Let top-level types call class helpers
using static LightingShowcase.Rendering.VulkanRasterCpu;

// Declare rendering namespace
namespace LightingShowcase.Rendering;

/// <summary>CPU-side scene preparation for the Vulkan rasterizer.</summary>
// Declare Vulkan raster CPU helper class
internal static class VulkanRasterCpu
// Begin CPU helper class
{
    // Expose stage log path
    public static string StageLogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-stage-log.txt");

    // Cap GPU light count
    internal const int MaxGpuLights = 32;
    // Define camera field of view
    internal const double CameraFovDegrees = 72.0;
    // Define camera near plane
    internal const double CameraNear = 0.035;
    // Define camera far plane
    internal const double CameraFar = 5000.0;

    // Default to per-fragment GPU texture sampling so high-frequency glTF atlas
    // textures match the CPU shadow rasterizer. Set
    // LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES=1 only as a diagnostic fallback.
    // Declare GPU sampling toggle property
    internal static bool UseGpuTextureSampling =>
        // Check baked textures env flag one
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "1", StringComparison.OrdinalIgnoreCase) &&
        // Check baked textures env flag true
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "true", StringComparison.OrdinalIgnoreCase);

    // Declare raster debug mode property
    internal static int RasterDebugMode
    // Begin debug mode property
    {
        // Begin debug mode getter
        get
        // Begin getter body
        {
            // Read raster debug env variable
            string? value = Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_RASTER_DEBUG");
            // Map debug name to code
            return value?.Trim().ToLowerInvariant() switch
            // Begin debug switch
            {
                // Select UV debug view
                "uv" => 1,
                // Select atlas debug view
                "atlas" => 2,
                // Select texture debug view
                "texture" => 3,
                // Select material debug view
                "material" => 4,
                // Select normal debug view
                "normal" => 5,
                // Select metallic debug view
                "metallic" => 6,
                // Select roughness debug view
                "roughness" => 7,
                // Select occlusion debug view
                "occlusion" => 8,
                // Select emissive debug view
                "emissive" => 9,
                // Select direct light debug view
                "direct" => 10,
                // Select IBL debug view
                "ibl" => 11,
                // Default to normal rendering
                _ => 0
            // End debug switch
            };
        // End getter body
        }
    // End debug mode property
    }

    /// <summary>Builds per-group opaque and transparent vertex ranges for the scene.</summary>
    // Build per-group vertex ranges
    internal static IReadOnlyDictionary<int, PreviewRangeSet> BuildGroupVertexRanges(Scene scene)
    // Begin BuildGroupVertexRanges method
    {
        // Collect per-group range builders
        Dictionary<int, (List<VertexRange> Opaque, List<VertexRange> Transparent)> builders = new();
        // Track opaque vertex offset
        uint opaqueCursor = 0;
        // Track transparent vertex write cursor
        uint transparentCursor = 0;

        // Iterate scene triangles for grouping
        foreach (Triangle triangle in scene.Triangles)
        // Begin per-triangle range loop
        {
            // Skip triangles with non-finite vertices
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                // Skip invalid triangle
                continue;

            // Classify triangle material transparency
            bool isTransparent = IsTransparentMaterial(triangle.Material);
            // Pick range start from cursor
            uint start = isTransparent ? transparentCursor : opaqueCursor;
            // Check triangle belongs to group
            if (triangle.GroupId >= 0)
            // Begin grouped range block
            {
                // Look up group range builder
                if (!builders.TryGetValue(triangle.GroupId, out var builder))
                // Begin missing builder block
                {
                    // Create empty range builder pair
                    builder = (new List<VertexRange>(), new List<VertexRange>());
                    // Register new group builder
                    builders.Add(triangle.GroupId, builder);
                // End missing builder block
                }
                // Append triangle vertices to group
                AppendRange(isTransparent ? builder.Transparent : builder.Opaque, start, 3);
            // End grouped range block
            }

            // Check transparent material branch
            if (isTransparent)
                // Advance transparent vertex cursor
                transparentCursor += 3;
            // Handle opaque material branch
            else
                // Advance opaque vertex cursor
                opaqueCursor += 3;
        // End per-triangle range loop
        }

        // Convert builders to range sets
        return builders.ToDictionary(
            // Use group id as key
            pair => pair.Key,
            // Build opaque transparent range set
            pair => new PreviewRangeSet(pair.Value.Opaque, pair.Value.Transparent));
    // End BuildGroupVertexRanges method
    }

    /// <summary>Sorts vertex ranges and merges overlapping neighbors.</summary>
    // Declare range merging helper
    internal static IReadOnlyList<VertexRange> MergeRanges(List<VertexRange> ranges)
    // Begin MergeRanges method
    {
        // Check nothing to merge
        if (ranges.Count <= 1)
            // Return short range list
            return ranges;

        // Sort ranges by start offset
        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        // Allocate merged range list
        List<VertexRange> merged = new(ranges.Count);
        // Iterate sorted vertex ranges
        foreach (VertexRange range in ranges)
            // Merge range into output
            AppendRange(merged, range.Start, range.Count);
        // Return merged ranges
        return merged;
    // End MergeRanges method
    }

    /// <summary>Appends a vertex range, merging it into the previous range when overlapping.</summary>
    // Declare range append helper
    internal static void AppendRange(List<VertexRange> ranges, uint start, uint count)
    // Begin AppendRange method
    {
        // Check empty range
        if (count == 0)
            // Ignore empty range
            return;
        // Check existing ranges present
        if (ranges.Count > 0)
        // Begin merge with previous block
        {
            // Read last stored range
            VertexRange previous = ranges[^1];
            // Compute previous range end
            uint previousEnd = previous.Start + previous.Count;
            // Check ranges overlap or touch
            if (start <= previousEnd)
            // Begin overlap merge block
            {
                // Compute merged range end
                uint end = Math.Max(previousEnd, start + count);
                // Replace last range with merged
                ranges[^1] = new VertexRange(previous.Start, end - previous.Start);
                // Finish merged append
                return;
            // End overlap merge block
            }
        // End merge with previous block
        }
        // Append disjoint vertex range
        ranges.Add(new VertexRange(start, count));
    // End AppendRange method
    }

    /// <summary>Computes the scene bounding center and radius from all triangle vertices.</summary>
    // Declare scene bounds helper
    internal static void ComputeSceneBounds(Scene scene, out Vec3 center, out double radius)
    // Begin ComputeSceneBounds method
    {
        // Handle empty scene bounds
        if (scene.Triangles.Count == 0) { center = Vec3.Zero; radius = 10; return; }
        // Seed bounds from first vertex
        Vec3 min = scene.Triangles[0].A, max = min;
        // Iterate triangles for bounds
        foreach (Triangle t in scene.Triangles)
        // Begin bounds accumulation loop
        {
            // Include first vertex in bounds
            Include(t.A);
            // Include second vertex in bounds
            Include(t.B);
            // Include third vertex in bounds
            Include(t.C);
        // End bounds accumulation loop
        }
        // Compute bounds center point
        center = (min + max) / 2.0;
        // Compute bounds radius minimum
        radius = Math.Max(1.0, (max - center).Length());

        /// <summary>Expands the running bounds to include a point.</summary>
        // Declare bounds include helper
        void Include(Vec3 p)
        // Begin Include method
        {
            // Expand bounds minimum corner
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            // Expand bounds maximum corner
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        // End Include method
        }
    // End ComputeSceneBounds method
    }

    /// <summary>Derives the camera far plane from cached bounds and camera distance.</summary>
    // Declare far plane helper
    internal static double ComputeCachedCameraFarPlane(Vec3 boundingCenter, double boundingRadius, Vec3 cameraPosition) =>
        // Clamp far plane to bounds
        Math.Clamp((boundingCenter - cameraPosition).Length() + boundingRadius * 1.1 + 1.0, 10.0, CameraFar);

    /// <summary>Counts opaque and transparent triangles and assigns material ids with texture placements.</summary>
    // Declare geometry metadata builder method
    internal static RasterGeometryBuildResult BuildGeometryMetadata(
        // Accept source scene parameter
        Scene scene,
        // Accept texture placements parameter
        IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements,
        // Accept GPU sampling flag
        bool useGpuTextureSampling)
    // Begin BuildGeometryMetadata method
    {
        // Initialize opaque triangle counter
        int opaqueTriangleCount = 0;
        // Initialize transparent triangle counter
        int transparentTriangleCount = 0;
        // Initialize clipped triangle counter
        int nearClippedTriangles = 0;
        // Create material id map
        Dictionary<Material, int> materialIds = new();
        // Create raster material list
        List<RasterMaterial> materials = new();

        // Loop over scene triangles
        foreach (Triangle triangle in scene.Triangles)
        // Begin triangle scan loop
        {
            // Reject nonfinite triangle vertices
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
            // Begin degenerate triangle handler
            {
                // Count degenerate triangle
                nearClippedTriangles++;
                // Skip degenerate triangle
                continue;
            // End degenerate triangle handler
            }

            // Test triangle material transparency
            if (IsTransparentMaterial(triangle.Material))
                // Count transparent triangle
                transparentTriangleCount++;
            // Handle opaque triangle case
            else
                // Count opaque triangle
                opaqueTriangleCount++;

            // Check for known material
            if (!materialIds.ContainsKey(triangle.Material))
            // Begin new material handler
            {
                // Begin base texture placement lookup
                RasterTexturePlacement basePlacement = useGpuTextureSampling
                    // Look up base texture placement
                    ? TexturePlacement(texturePlacements, triangle.Material.Texture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Begin metallic roughness placement lookup
                RasterTexturePlacement mrPlacement = useGpuTextureSampling
                    // Look up metallic roughness placement
                    ? TexturePlacement(texturePlacements, triangle.Material.MetallicRoughnessTexture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Begin normal texture placement lookup
                RasterTexturePlacement normalPlacement = useGpuTextureSampling
                    // Look up normal texture placement
                    ? TexturePlacement(texturePlacements, triangle.Material.NormalTexture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Begin emissive texture placement lookup
                RasterTexturePlacement emissivePlacement = useGpuTextureSampling
                    // Look up emissive texture placement
                    ? TexturePlacement(texturePlacements, triangle.Material.EmissiveTexture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Begin occlusion texture placement lookup
                RasterTexturePlacement occlusionPlacement = useGpuTextureSampling
                    // Look up occlusion texture placement
                    ? TexturePlacement(texturePlacements, triangle.Material.OcclusionTexture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Begin transmission texture placement lookup
                RasterTexturePlacement transmissionPlacement = useGpuTextureSampling
                    // Look up transmission texture placement
                    ? TexturePlacement(texturePlacements, triangle.Material.TransmissionTexture)
                    // Use no placement when baking
                    : RasterTexturePlacement.None;
                // Register new material id
                materialIds.Add(triangle.Material, materials.Count);
                // Append new raster material record
                materials.Add(new RasterMaterial(
                    // Pack material with texture placements
                    triangle.Material, basePlacement, mrPlacement, normalPlacement, emissivePlacement, occlusionPlacement, transmissionPlacement));
            // End new material handler
            }
        // End triangle scan loop
        }

        // Return new geometry build result
        return new RasterGeometryBuildResult
        // Begin result initializer block
        {
            // Store material id map
            MaterialIds = materialIds,
            // Store raster materials array
            Materials = materials.ToArray(),
            // Store opaque triangle count
            OpaqueTriangleCount = opaqueTriangleCount,
            // Store transparent triangle count
            TransparentTriangleCount = transparentTriangleCount,
            // Store clipped triangle count
            NearClippedTriangles = nearClippedTriangles,
            // Store source triangle count
            SourceTriangleCount = scene.Triangles.Count
        // End geometry result initializer
        };
    // End BuildGeometryMetadata method
    }

    /// <summary>Computes a triangle centroid depth along the camera forward axis.</summary>
    // Declare triangle depth helper method
    internal static double TriangleViewDepth(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    // Begin TriangleViewDepth method
    {
        // Compute triangle centroid point
        Vec3 centroid = (triangle.A + triangle.B + triangle.C) / 3.0;
        // Project centroid onto forward axis
        return (centroid - cameraPosition).Dot(basis.Forward);
    // End TriangleViewDepth method
    }

    /// <summary>Returns true when a triangle has finite data and sits fully in front of the camera.</summary>
    // Declare renderable triangle test method
    internal static bool IsRenderableTriangle(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    // Begin IsRenderableTriangle method
    {
        // Reject nonfinite triangle vertices
        if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C))
            // Mark bad vertices unrenderable
            return false;
        // Reject nonfinite triangle normal
        if (!IsFinite(triangle.Normal))
            // Mark bad normal unrenderable
            return false;

        // Avoid sending wildly distant coordinates or invalid camera-space values
        // into the Vulkan clipper. The CPU rasterizer naturally rejects these via
        // TryProjectCamera; doing it on the CPU side keeps the GPU path from ever
        // seeing NaN/Inf/overflow-prone vertices.
        // Test triangle against near plane
        return IsTriangleInFrontOfCamera(triangle, cameraPosition, basis);
    // End IsRenderableTriangle method
    }

    /// <summary>Returns true when all three vector components are finite.</summary>
    // Declare finite vector test method
    internal static bool IsFinite(Vec3 value) =>
        // Test all components for finiteness
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>Returns true when all three triangle corners are beyond the near plane.</summary>
    // Declare near plane test method
    internal static bool IsTriangleInFrontOfCamera(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    // Begin IsTriangleInFrontOfCamera method
    {
        // Test vertex A past near
        return CameraDepth(triangle.A, cameraPosition, basis) > CameraNear &&
               // Test vertex B past near
               CameraDepth(triangle.B, cameraPosition, basis) > CameraNear &&
               // Test vertex C past near
               CameraDepth(triangle.C, cameraPosition, basis) > CameraNear;
    // End IsTriangleInFrontOfCamera method
    }

    /// <summary>Computes a point depth along the camera forward axis.</summary>
    // Declare camera depth helper method
    internal static double CameraDepth(Vec3 point, Vec3 cameraPosition, CameraBasis basis)
    // Begin CameraDepth method
    {
        // Project point onto forward axis
        double depth = (point - cameraPosition).Dot(basis.Forward);
        // Clamp nonfinite depth to negative infinity
        return double.IsFinite(depth) ? depth : double.NegativeInfinity;
    // End CameraDepth method
    }

    /// <summary>Packs a material base color and effective alpha into a vector.</summary>
    // Declare material color packing method
    internal static Vector4 MaterialColorAlpha(Material material)
    // Begin MaterialColorAlpha method
    {
        // Compute effective material alpha
        double effectiveAlpha = EffectiveMaterialAlpha(material);
        // Return new color vector
        return new Vector4(
            // Clamp red channel
            (float)Math.Clamp(material.Color.X, 0.0, 1.0),
            // Clamp green channel
            (float)Math.Clamp(material.Color.Y, 0.0, 1.0),
            // Clamp blue channel
            (float)Math.Clamp(material.Color.Z, 0.0, 1.0),
            // Clamp alpha channel
            (float)Math.Clamp(effectiveAlpha, 0.0, 1.0));
    // End MaterialColorAlpha method
    }

    /// <summary>Samples a material color and alpha at the given UV.</summary>
    // Declare baked color sampling method
    internal static Vector4 BakedMaterialColorAlpha(Material material, Vec2 uv)
    // Begin BakedMaterialColorAlpha method
    {
        // Sample material color at UV
        Vec3 color = material.Sample(uv.U, uv.V);
        // Sample material alpha at UV
        double alpha = material.SampleAlpha(uv.U, uv.V);
        // Return new baked color vector
        return new Vector4(
            // Clamp red channel
            (float)Math.Clamp(color.X, 0.0, 1.0),
            // Clamp green channel
            (float)Math.Clamp(color.Y, 0.0, 1.0),
            // Clamp blue channel
            (float)Math.Clamp(color.Z, 0.0, 1.0),
            // Clamp alpha channel
            (float)Math.Clamp(alpha, 0.0, 1.0));
    // End BakedMaterialColorAlpha method
    }

    /// <summary>Packs a material emission color and strength into a vector.</summary>
    // Declare emission packing method
    internal static Vector4 MaterialEmission(Material material)
    // Begin MaterialEmission method
    {
        // Return new emission vector
        return new Vector4(
            // Clamp emission red nonnegative
            (float)Math.Max(0.0, material.EmissionColor.X),
            // Clamp emission green nonnegative
            (float)Math.Max(0.0, material.EmissionColor.Y),
            // Clamp emission blue nonnegative
            (float)Math.Max(0.0, material.EmissionColor.Z),
            // Clamp emission strength nonnegative
            (float)Math.Max(0.0, material.Emission));
    // End MaterialEmission method
    }

    /// <summary>Returns the material emission, ignoring the UV parameter.</summary>
    // Declare baked emission method
    internal static Vector4 BakedMaterialEmission(Material material, Vec2 uv)
    // Begin BakedMaterialEmission method
    {
        // Return unbaked material emission
        return MaterialEmission(material);
    // End BakedMaterialEmission method
    }

    /// <summary>Returns true when a material uses alpha blend mode.</summary>
    // Declare transparency test method
    internal static bool IsTransparentMaterial(Material material)
    // Begin IsTransparentMaterial method
    {
        // KHR_materials_transmission does not make a surface geometrically
        // transparent. OPAQUE and MASK transmission materials still represent
        // a covered surface and must write depth. Sending them through the
        // alpha-blended, depth-write-disabled pass lets farther lamp parts draw
        // over the nearer glass as the camera orbits. Only genuine alpha BLEND
        // materials belong in the transparent pass.
        // Test for alpha blend mode
        return material.AlphaMode == MaterialAlphaMode.Blend;
    // End IsTransparentMaterial method
    }

    /// <summary>Returns the clamped base alpha of a material.</summary>
    // Declare effective alpha method
    internal static double EffectiveMaterialAlpha(Material material)
    // Begin EffectiveMaterialAlpha method
    {
        // Match ShadowRasterRenderer, not the ray tracer: the fast CPU raster mode
        // uses base alpha only as a discard mask and does not reduce opacity for
        // transmission.  Treating transmissive/glass materials as blended in the
        // Vulkan preview was the main source of objects looking like they were
        // drawing through each other.
        // Clamp base material alpha
        return Math.Clamp(material.Alpha, 0.0, 1.0);
    // End EffectiveMaterialAlpha method
    }

    /// <summary>Collects enabled scene lights into GPU light records.</summary>
    // Declare GPU light buffer builder
    internal static RasterLight[] BuildLightBuffer(Scene scene)
    // Begin BuildLightBuffer method
    {
        // Create GPU light list
        List<RasterLight> lights = new();
        // Loop over scene lights
        foreach (SceneLight light in scene.Lights)
        // Begin light collection loop
        {
            // Skip disabled lights
            if (!light.Enabled)
                // Skip to next light
                continue;
            // Append GPU light record
            lights.Add(new RasterLight(light));
            // Test GPU light capacity
            if (lights.Count >= MaxGpuLights)
                // Stop at light limit
                break;
        // End light collection loop
        }
        // Return packed light array
        return lights.ToArray();
    // End BuildLightBuffer method
    }

    /// <summary>Computes the camera far plane from the deepest visible scene vertex.</summary>
    // Declare far plane computation method
    internal static double ComputeCameraFarPlane(Scene scene, Vec3 cameraPosition, CameraBasis basis)
    // Begin ComputeCameraFarPlane method
    {
        // The CPU shadow rasterizer stores camera-space depth directly in a
        // double z-buffer. The old Vulkan preview used a fixed 5000-unit far
        // plane, which compressed the room scene into a tiny part of a D24
        // depth buffer and made close/copied/coplanar props fight for depth.
        // Pick the far plane from the current camera view so Vulkan gets depth
        // precision close to the CPU path.
        // Initialize maximum scene depth
        double maxDepth = CameraNear + 1.0;
        // Loop over scene triangles
        foreach (Triangle triangle in scene.Triangles)
        // Begin depth scan loop
        {
            // Include vertex A depth
            IncludeDepth(triangle.A);
            // Include vertex B depth
            IncludeDepth(triangle.B);
            // Include vertex C depth
            IncludeDepth(triangle.C);
        // End depth scan loop
        }

        // Compute eight percent depth margin
        double margin = Math.Max(1.0, maxDepth * 0.08);
        // Clamp far plane to limits
        return Math.Clamp(maxDepth + margin, 10.0, CameraFar);

        /// <summary>Tracks the maximum finite camera depth of visited points.</summary>
        // Declare local depth tracker
        void IncludeDepth(Vec3 point)
        // Begin IncludeDepth local function
        {
            // Compute point camera depth
            double depth = CameraDepth(point, cameraPosition, basis);
            // Test for new maximum depth
            if (double.IsFinite(depth) && depth > maxDepth)
                // Record new maximum depth
                maxDepth = depth;
        // End IncludeDepth local function
        }
    // End ComputeCameraFarPlane method
    }

    /// <summary>Returns the atlas placement for a texture, or none when absent.</summary>
    // Declare texture placement lookup helper
    internal static RasterTexturePlacement TexturePlacement(IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, TextureMap? texture)
    // Begin placement lookup block
    {
        // Handle missing texture reference
        if (texture == null)
            // Return none placement for null
            return RasterTexturePlacement.None;
        // Return mapped placement or none
        return texturePlacements.TryGetValue(texture, out RasterTexturePlacement placement) ? placement : RasterTexturePlacement.None;
    // End placement lookup block
    }

    // Detect stage logging enablement flag
    internal static bool StageLoggingEnabled => IsVulkanDebugEnabled() || string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_STAGE_LOG"), "1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resets the raster stage log with the current resolution when logging is enabled.</summary>
    // Declare stage log reset helper
    internal static void ResetStageLog(int width, int height)
    // Begin log reset block
    {
        // Skip reset when logging disabled
        if (!StageLoggingEnabled) return;
        // Begin log write guard
        try
        // Begin guarded file write
        {
            // Truncate stage log file
            File.WriteAllText(StageLogPath,
                // Write log header line
                $"LightingShowcase Vulkan raster stage log{Environment.NewLine}" +
                // Write resolution header line
                $"Resolution: {width}x{height}{Environment.NewLine}" +
                // Write start timestamp line
                $"Started: {DateTime.Now:O}{Environment.NewLine}");
        // End guarded file write
        }
        // Ignore stage log write errors
        catch { }
    // End log reset block
    }

    /// <summary>Appends a timestamped entry to the raster stage log when logging is enabled.</summary>
    // Declare stage marker helper
    internal static void Stage(string name)
    // Begin stage marker block
    {
        // Skip marker when logging disabled
        if (!StageLoggingEnabled) return;
        // Append timestamped stage marker
        try { File.AppendAllText(StageLogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}"); }
        // Ignore stage marker write errors
        catch { }
    // End stage marker block
    }

    /// <summary>Logs the stage name and throws when cancellation was requested.</summary>
    // Declare cancellation check helper
    internal static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    // Begin cancellation check block
    {
        // Continue when not yet cancelled
        if (!cancellationToken.IsCancellationRequested)
            // Return without throwing here
            return;
        // Log cancellation stage name
        Stage("Cancellation requested before/at: " + stageName);
        // Throw cancellation for current token
        throw new OperationCanceledException(cancellationToken);
    // End cancellation check block
    }


    /// <summary>Returns true when the Vulkan debug environment variable is set.</summary>
    // Declare Vulkan debug flag check
    internal static bool IsVulkanDebugEnabled()
    // Begin debug flag block
    {
        // Read Vulkan debug environment variable
        string? value = Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_DEBUG");
        // Accept numeric debug value
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               // Accept true debug value
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               // Accept yes debug value
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    // End debug flag block
    }

    /// <summary>Maps a raster debug mode code to its short name.</summary>
    // Declare debug mode name helper
    internal static string DebugModeName(int mode) => mode switch
    // Begin debug mode switch
    {
        // Name UV debug view
        1 => "uv",
        // Name atlas debug view
        2 => "atlas",
        // Name texture debug view
        3 => "texture",
        // Name material debug view
        4 => "material",
        // Name normal debug view
        5 => "normal",
        // Name metallic debug view
        6 => "metallic",
        // Name roughness debug view
        7 => "roughness",
        // Name occlusion debug view
        8 => "occlusion",
        // Name emissive debug view
        9 => "emissive",
        // Name direct lighting view
        10 => "direct",
        // Name image lighting view
        11 => "ibl",
        // Default to debug output off
        _ => "off"
    // End debug mode switch
    };

    /// <summary>Converts a Vec3 to a single-precision Vector3.</summary>
    // Convert Vec3 to Vector3
    internal static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
    /// <summary>Converts a Vec3 and w component to a single-precision Vector4.</summary>
    // Convert Vec3 plus w component
    internal static Vector4 ToVector4(Vec3 value, float w) => new((float)value.X, (float)value.Y, (float)value.Z, w);

// End VulkanRasterCpu class
}
// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare GPU vertex struct
internal readonly struct RasterVertex
// Begin vertex struct
{
    // Hold vertex position
    public readonly Vector3 Position;
    // Hold vertex normal
    public readonly Vector3 Normal;
    // Hold vertex UV
    public readonly Vector2 Uv;
    // Hold material index
    public readonly float MaterialIndex;

    /// <summary>Initializes a raster vertex with position, normalized normal, UV, and material index.</summary>
    // Construct raster vertex
    public RasterVertex(Vec3 position, Vec3 normal, Vec2 uv, int materialIndex)
    // Begin constructor body
    {
        // Convert vertex position
        Position = ToVector3(position);
        // Normalize and convert normal
        Normal = ToVector3(normal.Normalize());
        // Convert UV coordinates
        Uv = new Vector2((float)uv.U, (float)uv.V);
        // Store material index
        MaterialIndex = materialIndex;
    // End constructor body
    }
// End vertex struct
}

// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare camera constants struct
internal readonly struct RasterCameraConstants
// Begin camera struct
{
    // Hold camera position
    public readonly Vector4 CameraPosition;
    // Hold camera forward vector
    public readonly Vector4 CameraForward;
    // Hold camera right vector
    public readonly Vector4 CameraRight;
    // Hold camera up vector
    public readonly Vector4 CameraUp;
    public readonly Vector4 Projection; // x=aspect, y=tanHalfFov, z=near, w=far
    public readonly Vector4 Counts;     // x=lightCount, y=textureCount, z=debugMode, w=materialCount

    /// <summary>Initializes camera projection constants from position, basis, resolution, and counts.</summary>
    // Construct camera constants
    public RasterCameraConstants(Vec3 position, CameraBasis basis, int width, int height, int lightCount, int textureCount, int debugMode, int materialCount, double farPlane)
    // Begin constructor body
    {
        // Pack camera position
        CameraPosition = ToVector4(position, 1.0f);
        // Pack forward vector
        CameraForward = ToVector4(basis.Forward, 0.0f);
        // Pack right vector
        CameraRight = ToVector4(basis.Right, 0.0f);
        // Pack up vector
        CameraUp = ToVector4(basis.Up, 0.0f);
        // Compute aspect ratio safely
        float aspect = width / (float)Math.Max(1, height);
        // Compute half FOV tangent
        float tanHalfFov = (float)Math.Tan((CameraFovDegrees * Math.PI / 180.0) * 0.5);
        // Pack projection parameters
        Projection = new Vector4(aspect, tanHalfFov, (float)CameraNear, (float)Math.Clamp(farPlane, CameraNear + 1.0, CameraFar));
        // Begin counts vector
        Counts = new Vector4(
            // Clamp light count
            Math.Clamp(lightCount, 0, MaxGpuLights),
            // Clamp texture count
            Math.Max(0, textureCount),
            // Clamp debug mode
            Math.Max(0, debugMode),
            // Clamp material count
            Math.Max(0, materialCount));
    // End constructor body
    }
// End camera struct
}

// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare transform constants struct
internal readonly struct RasterTransformConstants
// Begin transform struct
{
    // Hold pivot and enabled flag
    public readonly Vector4 PivotEnabled;
    // Hold translation vector
    public readonly Vector4 Translation;
    // Hold rotation vector
    public readonly Vector4 Rotation;
    // Hold scale vector
    public readonly Vector4 Scale;

    /// <summary>Initializes transform preview constants, using identity when no preview applies.</summary>
    // Construct transform constants
    public RasterTransformConstants(VulkanRasterTransformPreview? preview)
    // Begin constructor body
    {
        // Check for identity preview
        if (preview == null || preview.IsIdentity)
        // Begin identity branch
        {
            // Clear pivot and flag
            PivotEnabled = Vector4.Zero;
            // Clear translation
            Translation = Vector4.Zero;
            // Clear rotation
            Rotation = Vector4.Zero;
            // Set unit scale
            Scale = Vector4.One;
            // Return identity transform
            return;
        // End identity branch
        }

        // Pack pivot point
        PivotEnabled = ToVector4(preview.Pivot, 1.0f);
        // Pack preview position
        Translation = ToVector4(preview.Position, 0.0f);
        // Pack preview rotation
        Rotation = ToVector4(preview.Rotation, 0.0f);
        // Pack preview scale
        Scale = ToVector4(preview.Scale, 0.0f);
    // End constructor body
    }
// End transform struct
}

// Define vertex range record
internal readonly record struct VertexRange(uint Start, uint Count);

// Declare preview range set
internal sealed record PreviewRangeSet(
    // Hold opaque vertex ranges
    IReadOnlyList<VertexRange> Opaque,
    // Hold transparent vertex ranges
    IReadOnlyList<VertexRange> Transparent);

// Declare triangle patch record
internal readonly record struct RasterTrianglePatch(
    // Flag transparent patch
    bool Transparent,
    // Hold patch start vertex
    uint VertexStart,
    // Hold source triangle
    Triangle Source,
    // Hold edited corner mask
    byte CornerMask,
    // Hold original vertices
    RasterVertex[] OriginalVertices);

// Declare mesh edit state
internal sealed class MeshEditPatchState
// Begin patch state class
{
    // Hold selection identifier
    public required int SelectionId { get; init; }
    // Hold group identifier
    public required int GroupId { get; init; }
    // Hold triangle patches
    public required IReadOnlyList<RasterTrianglePatch> Patches { get; init; }
// End patch state class
}

// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare GPU light struct
internal readonly struct RasterLight
// Begin light struct
{
    public readonly Vector4 PositionKind;   // xyz=position, w=0 point / 1 directional / 2 spot
    public readonly Vector4 DirectionRange; // xyz=direction light travels, w=range
    public readonly Vector4 ColorIntensity; // rgb=color, w=intensity
    public readonly Vector4 ConeEnabled;    // x=cos inner, y=cos outer, z=castsShadow placeholder, w=enabled

    /// <summary>Packs a scene light into GPU position, direction, color, and cone constants.</summary>
    // Construct GPU light
    public RasterLight(SceneLight light)
    // Begin constructor body
    {
        // Map light kind to code
        float kind = light.Kind switch
        // Begin kind switch
        {
            // Encode directional light
            SceneLightKind.Directional => 1.0f,
            // Encode spot light
            SceneLightKind.Spot => 2.0f,
            // Default to point light
            _ => 0.0f
        // End kind switch
        };

        // Pack position and kind
        PositionKind = ToVector4(light.Position, kind);
        // Pack direction and range
        DirectionRange = ToVector4(light.Direction.Normalize(), (float)light.Range);
        // Pack color and intensity
        ColorIntensity = ToVector4(light.Color, (float)light.Intensity);
        // Begin cone vector
        ConeEnabled = new Vector4(
            // Compute inner cone cosine
            (float)Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)),
            // Compute outer cone cosine
            (float)Math.Cos(light.OuterConeAngle),
            // Encode shadow flag
            light.CastsShadow ? 1.0f : 0.0f,
            // Encode enabled flag
            light.Enabled ? 1.0f : 0.0f);
    // End constructor body
    }
// End light struct
}

// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare texture placement struct
internal readonly struct RasterTexturePlacement
// Begin placement struct
{
    public readonly Vector4 AtlasTransform; // xy=atlas UV offset, zw=atlas UV scale
    public readonly Vector4 TextureAddress; // x=wrapU, y=wrapV: 0 repeat, 1 clamp, 2 mirror
    public readonly Vector4 TextureTransform; // xy=offset, zw=scale; rotation in TextureAddress.z
    // Flag texture presence
    public readonly bool HasTexture;

    /// <summary>Initializes a texture atlas placement with transform, page, and wrap codes.</summary>
    // Construct texture placement
    public RasterTexturePlacement(float offsetX, float offsetY, float scaleX, float scaleY, int pageIndex, TextureMap texture)
    // Begin constructor body
    {
        // Pack atlas transform
        AtlasTransform = new Vector4(offsetX, offsetY, scaleX, scaleY);
        // w stores the texture-array page. z remains the glTF texture-transform rotation.
        // Pack wrap modes and page
        TextureAddress = new Vector4(AddressModeCode(texture.WrapU), AddressModeCode(texture.WrapV), (float)texture.Rotation, pageIndex);
        // Pack UV offset and scale
        TextureTransform = new Vector4((float)texture.OffsetU, (float)texture.OffsetV, (float)texture.ScaleU, (float)texture.ScaleV);
        // Mark texture present
        HasTexture = true;
    // End constructor body
    }

    // Provide empty placement
    public static RasterTexturePlacement None => new();

    /// <summary>Maps a texture address mode to its GPU code.</summary>
    // Map wrap mode to code
    internal static float AddressModeCode(TextureAddressMode mode) => mode switch
    // Begin address switch
    {
        // Encode clamp to edge
        TextureAddressMode.ClampToEdge => 1.0f,
        // Encode mirrored repeat
        TextureAddressMode.MirroredRepeat => 2.0f,
        // Default to repeat
        _ => 0.0f
    // End address switch
    };
// End placement struct
}

// Enforce sequential GPU layout
[StructLayout(LayoutKind.Sequential)]
// Declare GPU material struct
internal readonly struct RasterMaterial
// Begin material struct
{
    // Hold base color and alpha
    public readonly Vector4 BaseColorAlpha;
    // Hold emissive factor
    public readonly Vector4 EmissiveFactor;
    public readonly Vector4 PbrFactors;      // x=metallic, y=roughness, z=normal scale, w=occlusion strength
    public readonly Vector4 AlphaFlags;      // x=alpha mode, y=cutoff, z=double sided, w=transmission
    public readonly Vector4 OpticalFactors;  // x=ior, y=thickness, z=clearcoat, w=clearcoat roughness
    public readonly Vector4 AttenuationColorDistance; // rgb=attenuation color, w=distance (0=infinite)
    public readonly Vector4 TextureFlags;    // base, metallic-roughness, normal, emissive
    public readonly Vector4 TextureFlags2;   // x=occlusion, y=transmission, z=clearcoat reuses transmission mask

    // Hold base atlas transform
    public readonly Vector4 BaseAtlasTransform;
    // Hold base texture addressing
    public readonly Vector4 BaseTextureAddress;
    // Hold base UV transform
    public readonly Vector4 BaseTextureTransform;

    // Hold metallic roughness atlas transform
    public readonly Vector4 MrAtlasTransform;
    // Hold metallic roughness addressing
    public readonly Vector4 MrTextureAddress;
    // Hold metallic roughness UV transform
    public readonly Vector4 MrTextureTransform;

    // Hold normal atlas transform
    public readonly Vector4 NormalAtlasTransform;
    // Hold normal texture addressing
    public readonly Vector4 NormalTextureAddress;
    // Hold normal UV transform
    public readonly Vector4 NormalTextureTransform;

    // Hold emissive atlas transform
    public readonly Vector4 EmissiveAtlasTransform;
    // Hold emissive texture addressing
    public readonly Vector4 EmissiveTextureAddress;
    // Hold emissive UV transform
    public readonly Vector4 EmissiveTextureTransform;

    // Hold occlusion atlas transform
    public readonly Vector4 OcclusionAtlasTransform;
    // Hold occlusion texture addressing
    public readonly Vector4 OcclusionTextureAddress;
    // Hold occlusion UV transform
    public readonly Vector4 OcclusionTextureTransform;

    // Hold transmission atlas transform
    public readonly Vector4 TransmissionAtlasTransform;
    // Hold transmission texture addressing
    public readonly Vector4 TransmissionTextureAddress;
    // Hold transmission UV transform
    public readonly Vector4 TransmissionTextureTransform;

    /// <summary>Packs a material and its texture placements into GPU constants.</summary>
    // Begin material constructor
    public RasterMaterial(
        // Accept source material
        Material material,
        // Accept base placement
        RasterTexturePlacement basePlacement,
        // Accept metallic roughness placement
        RasterTexturePlacement mrPlacement,
        // Accept normal placement
        RasterTexturePlacement normalPlacement,
        // Accept emissive placement
        RasterTexturePlacement emissivePlacement,
        // Accept occlusion placement
        RasterTexturePlacement occlusionPlacement,
        // Accept transmission placement
        RasterTexturePlacement transmissionPlacement)
    // Begin constructor body
    {
        // Pack base color alpha
        BaseColorAlpha = MaterialColorAlpha(material);
        // Pack emissive factor
        EmissiveFactor = MaterialEmission(material);
        // Begin PBR factors
        PbrFactors = new Vector4(
            // Pack metallic factor
            (float)material.Metallic,
            // Pack roughness factor
            (float)material.Roughness,
            // Pack normal scale
            (float)material.NormalScale,
            // Pack occlusion strength
            (float)material.OcclusionStrength);
        // Begin alpha flags
        AlphaFlags = new Vector4(
            // Pack alpha mode
            (float)(int)material.AlphaMode,
            // Pack alpha cutoff
            (float)material.AlphaCutoff,
            // Encode double sided flag
            material.DoubleSided ? 1.0f : 0.0f,
            // Pack transmission factor
            (float)material.Transmission);
        // Begin optical factors
        OpticalFactors = new Vector4(
            // Pack index of refraction
            (float)material.Ior,
            // Pack thickness
            (float)material.Thickness,
            // Pack clearcoat factor
            (float)material.Clearcoat,
            // Pack clearcoat roughness
            (float)material.ClearcoatRoughness);
        // Begin attenuation vector
        AttenuationColorDistance = new Vector4(
            // Pack attenuation red
            (float)material.AttenuationColor.X,
            // Pack attenuation green
            (float)material.AttenuationColor.Y,
            // Pack attenuation blue
            (float)material.AttenuationColor.Z,
            // Pack attenuation distance
            (float)material.AttenuationDistance);
        // Begin texture flags
        TextureFlags = new Vector4(
            // Flag base texture
            basePlacement.HasTexture ? 1.0f : 0.0f,
            // Flag metallic roughness texture
            mrPlacement.HasTexture ? 1.0f : 0.0f,
            // Flag normal texture
            normalPlacement.HasTexture ? 1.0f : 0.0f,
            // Flag emissive texture
            emissivePlacement.HasTexture ? 1.0f : 0.0f);
        // Begin second texture flags
        TextureFlags2 = new Vector4(
            // Flag occlusion texture
            occlusionPlacement.HasTexture ? 1.0f : 0.0f,
            // Flag transmission texture
            transmissionPlacement.HasTexture ? 1.0f : 0.0f,
            // Flag clearcoat mask reuse
            material.ClearcoatUsesTransmissionTexture ? 1.0f : 0.0f,
            // Pad unused flag slot
            0.0f);

        // Copy base atlas transform
        BaseAtlasTransform = basePlacement.AtlasTransform;
        // Copy base texture addressing
        BaseTextureAddress = basePlacement.TextureAddress;
        // Copy base UV transform
        BaseTextureTransform = basePlacement.TextureTransform;
        // Copy metallic roughness atlas transform
        MrAtlasTransform = mrPlacement.AtlasTransform;
        // Copy metallic roughness addressing
        MrTextureAddress = mrPlacement.TextureAddress;
        // Copy metallic roughness UV transform
        MrTextureTransform = mrPlacement.TextureTransform;
        // Copy normal atlas transform
        NormalAtlasTransform = normalPlacement.AtlasTransform;
        // Copy normal texture addressing
        NormalTextureAddress = normalPlacement.TextureAddress;
        // Copy normal UV transform
        NormalTextureTransform = normalPlacement.TextureTransform;
        // Copy emissive atlas transform
        EmissiveAtlasTransform = emissivePlacement.AtlasTransform;
        // Copy emissive texture addressing
        EmissiveTextureAddress = emissivePlacement.TextureAddress;
        // Copy emissive UV transform
        EmissiveTextureTransform = emissivePlacement.TextureTransform;
        // Copy occlusion atlas transform
        OcclusionAtlasTransform = occlusionPlacement.AtlasTransform;
        // Copy occlusion texture addressing
        OcclusionTextureAddress = occlusionPlacement.TextureAddress;
        // Copy occlusion UV transform
        OcclusionTextureTransform = occlusionPlacement.TextureTransform;
        // Copy transmission atlas transform
        TransmissionAtlasTransform = transmissionPlacement.AtlasTransform;
        // Copy transmission texture addressing
        TransmissionTextureAddress = transmissionPlacement.TextureAddress;
        // Copy transmission UV transform
        TransmissionTextureTransform = transmissionPlacement.TextureTransform;
    // End constructor body
    }
// End material struct
}

// Declare texture upload record
internal sealed class RasterTextureUpload
// Begin upload class
{
    // Hold source texture map
    public required TextureMap Texture { get; init; }
    // Hold atlas X offset
    public required int X { get; init; }
    // Hold atlas Y offset
    public required int Y { get; init; }
    // Hold atlas page index
    public required int Page { get; init; }
// End upload class
}

// Declare texture build result
internal sealed class TextureBuildResult
// Begin build result class
{
    // Map textures to placements
    public required IReadOnlyDictionary<TextureMap, RasterTexturePlacement> TexturePlacements { get; init; }
    // Hold pending texture uploads
    public required IReadOnlyList<RasterTextureUpload> Uploads { get; init; }
    // Hold atlas width
    public required int Width { get; init; }
    // Hold atlas height
    public required int Height { get; init; }
    // Count packed textures
    public required int TextureCount { get; init; }
    // Count atlas pages
    public required int PageCount { get; init; }
    // Flag GPU sampling use
    public required bool UsesGpuTextureSampling { get; init; }
    // Explain sampling fallback
    public string? FallbackReason { get; init; }
// End build result class
}

// Declare geometry build result
internal sealed class RasterGeometryBuildResult
// Begin geometry result class
{
    // Map materials to indices
    public required IReadOnlyDictionary<Material, int> MaterialIds { get; init; }
    // Hold GPU materials
    public required RasterMaterial[] Materials { get; set; }
    // Count opaque triangles
    public int OpaqueTriangleCount { get; init; }
    // Count transparent triangles
    public int TransparentTriangleCount { get; init; }
    // Count near clipped triangles
    public int NearClippedTriangles { get; init; }
    // Count source triangles
    public int SourceTriangleCount { get; init; }
    // Derive opaque vertex count
    public int OpaqueVertexCount => checked(OpaqueTriangleCount * 3);
    // Derive transparent vertex count
    public int TransparentVertexCount => checked(TransparentTriangleCount * 3);
    // Derive material count
    public int MaterialCount => Materials.Length;
    // Derive total triangle count
    public int TotalTriangles => checked(OpaqueTriangleCount + TransparentTriangleCount);
// End geometry result class
}
