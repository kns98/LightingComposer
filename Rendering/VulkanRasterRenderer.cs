// -----------------------------------------------------------------------------
// File: Rendering/VulkanRasterRenderer.cs
// Purpose: Vulkan graphics-pipeline raster preview.
//
// This renderer is intentionally separate from VulkanSceneComputeRenderer.  It
// uses Vulkan's normal vertex/fragment raster pipeline for fast preview frames:
// vertex buffer -> depth-tested triangle rasterization -> fragment lighting ->
// off-screen color texture -> staging readback -> cross-platform RGBA image.
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
// Import Veldrid graphics API
using Veldrid;
// Import SPIRV shader compilation
using Veldrid.SPIRV;

// Declare rendering namespace
namespace LightingShowcase.Rendering;

/// <summary>Vulkan hardware rasterizer for the Render tab preview.</summary>
// Declare Vulkan raster renderer class
public static class VulkanRasterRenderer
// Begin renderer class
{
    // Expose stage log path
    public static string StageLogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-stage-log.txt");

    // Cap GPU light count
    private const int MaxGpuLights = 32;
    // Define camera field of view
    private const double CameraFovDegrees = 72.0;
    // Define camera near plane
    private const double CameraNear = 0.035;
    // Define camera far plane
    private const double CameraFar = 5000.0;

    // Default to per-fragment GPU texture sampling so high-frequency glTF atlas
    // textures match the CPU shadow rasterizer. Set
    // LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES=1 only as a diagnostic fallback.
    // Declare GPU sampling toggle property
    private static bool UseGpuTextureSampling =>
        // Check baked textures env flag one
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "1", StringComparison.OrdinalIgnoreCase) &&
        // Check baked textures env flag true
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "true", StringComparison.OrdinalIgnoreCase);

    // Declare raster debug mode property
    private static int RasterDebugMode
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

    // Guard shared graphics device
    private static readonly object DeviceSync = new();
    // Guard render operations
    private static readonly object RenderSync = new();
    // Cache shared graphics device
    private static GraphicsDevice? sharedGraphicsDevice;
    // Cache shared raster pipelines
    private static SharedRasterResources? sharedRasterResources;
    // Cache prepared scene buffers
    private static PreparedRasterScene? preparedScene;
    // Interactive and settled frames use different resolutions. Keep a small
    // LRU so orbit/release does not recreate color, depth, staging and sync
    // resources every time the viewer switches between those sizes.
    // Limit cached target sizes
    private const int MaxCachedTargetSizes = 2;
    // Cache render targets by size
    private static readonly Dictionary<(int Width, int Height), RasterTargets> sharedTargets = new();
    // Track target use order
    private static long targetUseSerial;
    // Flag preflight completion
    private static bool preflightCompleted;

    // Declare shared pipeline holder
    private sealed class SharedRasterResources : IDisposable
    // Begin shared resources class
    {
        // Hold shader resource layout
        public required ResourceLayout Layout { get; init; }
        // Hold compiled shaders
        public required Shader[] Shaders { get; init; }
        // Hold opaque pipeline
        public required Pipeline OpaquePipeline { get; init; }
        // Hold transparent pipeline
        public required Pipeline TransparentPipeline { get; init; }
        // Hold wireframe depth prepass pipeline
        public required Pipeline DepthPrepassPipeline { get; init; }
        // Hold solid wireframe pipeline
        public required Pipeline WireframePipeline { get; init; }
        // Hold dashed wireframe pipeline
        public required Pipeline WireframeDashedPipeline { get; init; }

        /// <summary>Disposes the shared raster pipelines, shaders, and resource layout.</summary>
        // Release shared raster resources
        public void Dispose()
        // Begin Dispose body
        {
            // Dispose opaque pipeline safely
            try { OpaquePipeline.Dispose(); } catch { }
            // Dispose transparent pipeline safely
            try { TransparentPipeline.Dispose(); } catch { }
            // Dispose prepass pipeline safely
            try { DepthPrepassPipeline.Dispose(); } catch { }
            // Dispose wireframe pipeline safely
            try { WireframePipeline.Dispose(); } catch { }
            // Dispose dashed pipeline safely
            try { WireframeDashedPipeline.Dispose(); } catch { }
            // Check shaders before disposal
            if (Shaders != null)
            // Begin shader null check
            {
                // Loop over shaders
                foreach (Shader shader in Shaders)
                // Begin shader loop
                {
                    // Dispose one shader safely
                    try { shader.Dispose(); } catch { }
                // End shader loop
                }
            // End shader null check
            }
            // Dispose resource layout safely
            try { Layout.Dispose(); } catch { }
        // End Dispose body
        }
    // End shared resources class
    }


    // Declare prepared scene cache
    private sealed class PreparedRasterScene : IDisposable
    // Begin prepared scene class
    {
        // Hold source scene reference
        public required Scene Scene { get; init; }
        // Track scene cache stamp
        public required SceneCacheStamp CacheStamp { get; set; }
        // Map materials to indices
        public required IReadOnlyDictionary<Material, int> MaterialIds { get; init; }
        // Hold opaque vertex buffer
        public required DeviceBuffer OpaqueVertexBuffer { get; init; }
        // Hold transparent vertex buffer
        public required DeviceBuffer TransparentVertexBuffer { get; init; }
        // Hold camera constants buffer
        public required DeviceBuffer CameraBuffer { get; init; }
        // Hold identity transform buffer
        public required DeviceBuffer IdentityTransformBuffer { get; init; }
        // Hold preview transform buffer
        public required DeviceBuffer PreviewTransformBuffer { get; init; }
        // Hold light buffer
        public required DeviceBuffer LightBuffer { get; init; }
        // Hold material buffer
        public required DeviceBuffer MaterialBuffer { get; init; }
        // Hold texture atlas
        public required Texture AtlasTexture { get; init; }
        // Hold atlas sampler
        public required Sampler AtlasSampler { get; init; }
        // Hold identity resource set
        public required ResourceSet IdentityResourceSet { get; init; }
        // Hold preview resource set
        public required ResourceSet PreviewResourceSet { get; init; }
        // Hold group vertex ranges
        public required IReadOnlyDictionary<int, PreviewRangeSet> GroupRanges { get; init; }
        // Hold preview vertex ranges
        public Dictionary<int, PreviewRangeSet> PreviewRanges { get; } = new();
        // Track active mesh edit
        public MeshEditPatchState? ActiveMeshEdit { get; set; }
        // Count GPU lights
        public required int LightCount { get; init; }
        // Count opaque vertices
        public required int OpaqueVertexCount { get; init; }
        // Count transparent vertices
        public required int TransparentVertexCount { get; init; }
        // Count materials
        public required int MaterialCount { get; init; }
        // Count source triangles
        public required int SourceTriangleCount { get; init; }
        // Count total triangles
        public required int TotalTriangleCount { get; init; }
        // Count near clipped triangles
        public required int NearClippedTriangleCount { get; init; }
        // Count textures
        public required int TextureCount { get; init; }
        // Count atlas pages
        public required int TexturePageCount { get; init; }
        // Flag requested GPU sampling
        public required bool GpuTextureSamplingRequested { get; init; }
        // Flag active GPU sampling
        public required bool UsesGpuTextureSampling { get; init; }
        // Explain texture fallback
        public string? TextureFallbackReason { get; init; }
        // Hold scene bounding radius
        public required double BoundingRadius { get; set; }
        // Hold scene bounding center
        public required Vec3 BoundingCenter { get; set; }

        /// <summary>Disposes the cached scene buffers, atlas, and resource sets.</summary>
        // Release prepared scene resources
        public void Dispose()
        // Begin Dispose body
        {
            // Dispose preview resource set
            try { PreviewResourceSet.Dispose(); } catch { }
            // Dispose identity resource set
            try { IdentityResourceSet.Dispose(); } catch { }
            // Dispose atlas sampler
            try { AtlasSampler.Dispose(); } catch { }
            // Dispose atlas texture
            try { AtlasTexture.Dispose(); } catch { }
            // Dispose material buffer
            try { MaterialBuffer.Dispose(); } catch { }
            // Dispose light buffer
            try { LightBuffer.Dispose(); } catch { }
            // Dispose preview transform buffer
            try { PreviewTransformBuffer.Dispose(); } catch { }
            // Dispose identity transform buffer
            try { IdentityTransformBuffer.Dispose(); } catch { }
            // Dispose camera buffer
            try { CameraBuffer.Dispose(); } catch { }
            // Dispose transparent vertex buffer
            try { TransparentVertexBuffer.Dispose(); } catch { }
            // Dispose opaque vertex buffer
            try { OpaqueVertexBuffer.Dispose(); } catch { }
        // End Dispose body
        }
    // End prepared scene class
    }

    // Declare render target cache
    private sealed class RasterTargets : IDisposable
    // Begin targets class
    {
        // Hold target width
        public required int Width { get; init; }
        // Hold target height
        public required int Height { get; init; }
        // Hold color texture
        public required Texture ColorTexture { get; init; }
        // Hold depth texture
        public required Texture DepthTexture { get; init; }
        // Hold staging texture
        public required Texture StagingTexture { get; init; }
        // Hold framebuffer
        public required Framebuffer Framebuffer { get; init; }
        // Hold command list
        public required CommandList CommandList { get; init; }
        // Hold GPU fence
        public required Fence Fence { get; init; }
        // Track last use serial
        public long LastUseSerial { get; set; }

        /// <summary>Disposes the cached render targets, command list, and fence.</summary>
        // Release render target resources
        public void Dispose()
        // Begin Dispose body
        {
            // Dispose fence safely
            try { Fence.Dispose(); } catch { }
            // Dispose command list safely
            try { CommandList.Dispose(); } catch { }
            // Dispose framebuffer safely
            try { Framebuffer.Dispose(); } catch { }
            // Dispose staging texture safely
            try { StagingTexture.Dispose(); } catch { }
            // Dispose depth texture safely
            try { DepthTexture.Dispose(); } catch { }
            // Dispose color texture safely
            try { ColorTexture.Dispose(); } catch { }
        // End Dispose body
        }
    // End targets class
    }

    // Enforce sequential GPU layout
    [StructLayout(LayoutKind.Sequential)]
    // Declare GPU vertex struct
    private readonly struct RasterVertex
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
    private readonly struct RasterCameraConstants
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
    private readonly struct RasterTransformConstants
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
    private readonly record struct VertexRange(uint Start, uint Count);

    // Declare preview range set
    private sealed record PreviewRangeSet(
        // Hold opaque vertex ranges
        IReadOnlyList<VertexRange> Opaque,
        // Hold transparent vertex ranges
        IReadOnlyList<VertexRange> Transparent);

    // Declare triangle patch record
    private readonly record struct RasterTrianglePatch(
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
    private sealed class MeshEditPatchState
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
    private readonly struct RasterLight
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
    private readonly struct RasterTexturePlacement
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
        private static float AddressModeCode(TextureAddressMode mode) => mode switch
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
    private readonly struct RasterMaterial
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
    private sealed class RasterTextureUpload
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
    private sealed class TextureBuildResult
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
    private sealed class RasterGeometryBuildResult
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

    /// <summary>Releases the shared Vulkan graphics device and compiled raster pipeline.</summary>
    // Dispose shared Vulkan device
    public static void DisposeSharedDevice()
    // Begin DisposeSharedDevice body
    {
        // Disposal must not race an in-flight render. Veldrid object disposal during
        // command submission/readback can produce native driver crashes that bypass
        // normal C# exception handling.
        // Lock render operations
        lock (RenderSync)
        // Begin render lock
        {
            // Lock graphics device
            lock (DeviceSync)
            // Begin device lock
            {
                // Capture shared device
                GraphicsDevice? device = sharedGraphicsDevice;
                // Clear shared device
                sharedGraphicsDevice = null;
                // Capture shared resources
                SharedRasterResources? resources = sharedRasterResources;
                // Clear shared resources
                sharedRasterResources = null;
                // Capture prepared scene
                PreparedRasterScene? sceneResources = preparedScene;
                // Clear prepared scene
                preparedScene = null;
                // Snapshot cached targets
                RasterTargets[] targets = sharedTargets.Values.ToArray();
                // Clear target cache
                sharedTargets.Clear();

                // Check device exists
                if (device != null)
                // Begin device null check
                {
                    // Wait for GPU idle
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice: WaitForIdle"); device.WaitForIdle(); } catch { }
                // End device null check
                }

                // Loop over cached targets
                foreach (RasterTargets target in targets)
                // Begin target loop
                {
                    // Dispose one target safely
                    try { target.Dispose(); } catch { }
                // End target loop
                }
                // Dispose scene resources safely
                try { sceneResources?.Dispose(); } catch { }
                // Dispose shared resources safely
                try { resources?.Dispose(); } catch { }
                // Check device exists
                if (device != null)
                // Begin device null check
                {
                    // Dispose shared device with staging log
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice"); device.Dispose(); } catch { }
                // End device null check
                }
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End DisposeSharedDevice method
    }

    /// <summary>
    /// Releases scene-sized Vulkan buffers while keeping the device, pipelines,
    /// and small render-target cache alive. Call this before replacing a large
    /// scene so the old GPU allocation does not overlap the new scene load.
    /// </summary>
    // Release cached scene GPU buffers
    public static void ReleasePreparedScene()
    // Begin ReleasePreparedScene method
    {
        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Serialize against device lifecycle changes
            lock (DeviceSync)
            // Begin DeviceSync lock
            {
                // Snapshot cached scene resources
                PreparedRasterScene? sceneResources = preparedScene;
                // Clear cached scene reference
                preparedScene = null;
                // Skip release when cache empty
                if (sceneResources == null)
                    // Exit without cached scene
                    return;

                // Drain GPU before releasing buffers
                try { sharedGraphicsDevice?.WaitForIdle(); } catch { }
                // Dispose scene GPU buffers
                try { sceneResources.Dispose(); } catch { }
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End ReleasePreparedScene method
    }

    /// <summary>
    /// Reuploads only vertex positions, normals, UVs and material indices into an
    /// already allocated Vulkan scene. This is the fast path for baked transforms:
    /// textures, pipelines, descriptors, render targets and buffer allocations stay hot.
    /// </summary>
    // Refresh cached geometry in place
    public static bool TryRefreshPreparedGeometry(
        // Scene providing updated geometry
        Scene scene,
        // Cancellation token for refresh
        CancellationToken cancellationToken,
        // Output refresh status message
        out string details)
    // Begin TryRefreshPreparedGeometry method
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Serialize against device lifecycle changes
            lock (DeviceSync)
            // Begin DeviceSync lock
            {
                // Snapshot cached scene
                PreparedRasterScene? prepared = preparedScene;
                // Snapshot shared graphics device
                GraphicsDevice? gd = sharedGraphicsDevice;
                // Require matching cached scene
                if (prepared == null || gd == null || !ReferenceEquals(prepared.Scene, scene))
                // Begin cache mismatch branch
                {
                    // Report missing compatible cache
                    details = "No compatible Vulkan raster scene cache is active.";
                    // Fail without compatible cache
                    return false;
                // End cache mismatch branch
                }
                // Skip refresh when cache current
                if (prepared.CacheStamp.Matches(scene))
                // Begin cache current branch
                {
                    // Report cache already current
                    details = "Vulkan raster geometry cache is already current.";
                    // Succeed without reuploading
                    return true;
                // End cache current branch
                }

                // Count opaque triangles
                int opaqueTriangles = 0;
                // Count transparent triangles
                int transparentTriangles = 0;
                // Scan scene triangles for changes
                foreach (Triangle triangle in scene.Triangles)
                // Begin per-triangle loop
                {
                    // Honor refresh cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    // Skip non-finite triangles
                    if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                        // Skip invalid triangle
                        continue;
                    // Detect new or changed material
                    if (!prepared.MaterialIds.ContainsKey(triangle.Material))
                    // Begin material mismatch branch
                    {
                        // Report material-driven rebuild need
                        details = "A material changed, so the Vulkan raster scene requires a full rebuild.";
                        // Require full rebuild for materials
                        return false;
                    // End material mismatch branch
                    }

                    // Classify triangle transparency
                    if (IsTransparentMaterial(triangle.Material))
                        // Tally transparent triangle
                        transparentTriangles++;
                    // Handle opaque triangle case
                    else
                        // Tally opaque triangle
                        opaqueTriangles++;
                // End per-triangle loop
                }

                // Detect topology count mismatch
                if (opaqueTriangles * 3 != prepared.OpaqueVertexCount ||
                    // Check transparent vertex count
                    transparentTriangles * 3 != prepared.TransparentVertexCount)
                // Begin topology mismatch branch
                {
                    // Report topology-driven rebuild need
                    details = "Scene topology or transparency classification changed, so the Vulkan raster scene requires a full rebuild.";
                    // Require full rebuild for topology
                    return false;
                // End topology mismatch branch
                }

                // Time geometry reupload
                Stopwatch stopwatch = Stopwatch.StartNew();
                // Reupload vertex buffers in place
                UploadRasterGeometry(
                    // Target graphics device
                    gd,
                    // Opaque vertex buffer destination
                    prepared.OpaqueVertexBuffer,
                    // Transparent vertex buffer destination
                    prepared.TransparentVertexBuffer,
                    // Source scene geometry
                    scene,
                    // Material index lookup
                    prepared.MaterialIds,
                    // Cancellation token for upload
                    cancellationToken);
                // Recompute scene bounding sphere
                ComputeSceneBounds(scene, out Vec3 center, out double radius);
                // Store refreshed bounding center
                prepared.BoundingCenter = center;
                // Store refreshed bounding radius
                prepared.BoundingRadius = radius;
                // Stamp cache as current
                prepared.CacheStamp = SceneCacheStamp.Capture(scene);
                // Invalidate cached preview ranges
                prepared.PreviewRanges.Clear();
                // Clear stale mesh edit state
                prepared.ActiveMeshEdit = null;
                // Stop refresh timer
                stopwatch.Stop();
                // Report in-place refresh timing
                details = $"Vulkan raster geometry refreshed in-place in {stopwatch.ElapsedMilliseconds} ms; allocations and textures were reused.";
                // Log refresh stage message
                Stage(details);
                // Succeed after geometry refresh
                return true;
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End TryRefreshPreparedGeometry method
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    // Render frame without previews
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details) =>
        // Forward to full render overload
        Render(scene, cameraPosition, basis, width, height, null, null, cancellationToken, out details);

    /// <summary>
    /// Renders with an optional editor-only transform. The preview updates one small
    /// uniform buffer and changes draw ranges; it does not rebuild or upload vertices.
    /// </summary>
    // Render frame with transform preview
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details) =>
        // Forward to full render overload
        Render(scene, cameraPosition, basis, width, height, preview, null, cancellationToken, out details);

    /// <summary>
    /// Renders with optional whole-object and mesh-component editor previews.
    /// Component moves patch only affected vertices in the existing GPU buffers.
    /// </summary>
    // Render frame with both previews
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Optional live mesh edit preview
        VulkanRasterMeshEditPreview? meshEditPreview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details,
        // Render wireframe view flag
        bool wireframe = false)
    // Begin Render method
    {
        // Serialize the Veldrid/Vulkan raster path against disposal and resize/backend
        // switches. Without this, closing the form or switching renderers while a
        // preview frame is submitting can dispose the shared device underneath the
        // worker thread and crash inside the graphics driver instead of raising a
        // managed exception.
        // Serialize against disposal and resize
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Delegate to locked render path
            return RenderLocked(
                // Forward scene and preview arguments
                scene, cameraPosition, basis, width, height, preview, meshEditPreview,
                // Forward cancellation and details
                cancellationToken, out details, wireframe);
        // End RenderSync lock
        }
    // End Render method
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    // Render frame under render lock
    private static RenderImage RenderLocked(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Optional live mesh edit preview
        VulkanRasterMeshEditPreview? meshEditPreview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details,
        // Render wireframe view flag
        bool wireframe = false)
    // Begin RenderLocked method
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        // Clamp width to valid range
        width = Math.Max(1, width);
        // Clamp height to valid range
        height = Math.Max(1, height);

        // Time total frame
        Stopwatch total = Stopwatch.StartNew();
        // Time individual render phases
        Stopwatch phase = Stopwatch.StartNew();
        // Reset per-frame stage log
        ResetStageLog(width, height);
        // Abort before device creation
        ThrowIfCancellationRequested(cancellationToken, "create Vulkan device");
        // Acquire shared graphics device
        GraphicsDevice gd = GetOrCreateSharedDevice();
        // Record device setup time
        long deviceMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Acquire sized render targets
        RasterTargets targets = GetOrCreateTargets(gd, width, height);
        // Acquire shared raster pipelines
        SharedRasterResources resources = GetOrCreateSharedRasterResources(gd, targets.Framebuffer.OutputDescription);
        // Record target setup time
        long targetMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Acquire prepared GPU scene
        PreparedRasterScene prepared = GetOrCreatePreparedScene(gd, resources, scene, cancellationToken);
        // Record scene prepare time
        long prepareMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Detect active mesh edit preview
        bool hasMeshEditPreview = meshEditPreview != null && !meshEditPreview.IsIdentity;
        // Patch edited vertices on GPU
        UpdateMeshEditPreview(gd, prepared, meshEditPreview, cancellationToken);
        // Record mesh edit upload time
        long meshEditUploadMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Compute far plane from bounds
        double cameraFar = ComputeCachedCameraFarPlane(prepared, cameraPosition);
        // Upload camera constants
        gd.UpdateBuffer(prepared.CameraBuffer, 0, new RasterCameraConstants(
            // Camera and scene count constants
            cameraPosition, basis, width, height, prepared.LightCount, prepared.TextureCount,
            // Debug mode and far plane
            RasterDebugMode, prepared.MaterialCount, cameraFar));
        // Detect active transform preview
        bool hasPreview = preview != null && !preview.IsIdentity && preview.GroupIds.Count > 0;
        // Default to full-buffer draw
        PreviewRangeSet? previewRanges = null;
        // Upload transform when previewing
        if (hasPreview)
        // Begin preview branch
        {
            // Upload preview transform constants
            gd.UpdateBuffer(prepared.PreviewTransformBuffer, 0, new RasterTransformConstants(preview));
            // Resolve preview vertex ranges
            previewRanges = GetOrCreatePreviewRanges(prepared, preview!);
        // End preview branch
        }
        // Record uniform upload time
        long uniformMs = phase.ElapsedMilliseconds;

        // Abort before command recording
        ThrowIfCancellationRequested(cancellationToken, "record Vulkan raster command list");
        // Restart phase timer
        phase.Restart();
        // Reuse cached command list
        CommandList commandList = targets.CommandList;
        // Begin command recording
        commandList.Begin();
        // Bind render targets
        commandList.SetFramebuffer(targets.Framebuffer);
        // Clear color to dark background
        commandList.ClearColorTarget(0, new RgbaFloat(0.035f, 0.040f, 0.050f, 1.0f));
        // Clear depth buffer
        commandList.ClearDepthStencil(1.0f);
        // Branch to wireframe passes
        if (wireframe)
        // Begin wireframe pass branch
        {
            // Fill depth with filled prepass
            if (prepared.OpaqueVertexCount > 0)
            // Begin prepass opaque branch
            {
                // Bind depth prepass pipeline
                commandList.SetPipeline(resources.DepthPrepassPipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque prepass vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End prepass opaque branch
            }
            // Fill depth with transparent prepass
            if (prepared.TransparentVertexCount > 0)
            // Begin prepass transparent branch
            {
                // Bind depth prepass pipeline
                commandList.SetPipeline(resources.DepthPrepassPipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent prepass vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End prepass transparent branch
            }
            // Restore background over prepass
            commandList.ClearColorTarget(0, new RgbaFloat(0.035f, 0.040f, 0.050f, 1.0f));
            // Draw solid lines over depth
            if (prepared.OpaqueVertexCount > 0)
            // Begin solid opaque branch
            {
                // Bind solid wireframe pipeline
                commandList.SetPipeline(resources.WireframePipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque wireframe vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End solid opaque branch
            }
            // Draw transparent solid lines
            if (prepared.TransparentVertexCount > 0)
            // Begin solid transparent branch
            {
                // Bind solid wireframe pipeline
                commandList.SetPipeline(resources.WireframePipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent wireframe vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End solid transparent branch
            }
            // Draw dashed lines behind depth
            if (prepared.OpaqueVertexCount > 0)
            // Begin dashed opaque branch
            {
                // Bind dashed wireframe pipeline
                commandList.SetPipeline(resources.WireframeDashedPipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque dashed vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End dashed opaque branch
            }
            // Draw transparent dashed lines
            if (prepared.TransparentVertexCount > 0)
            // Begin dashed transparent branch
            {
                // Bind dashed wireframe pipeline
                commandList.SetPipeline(resources.WireframeDashedPipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent dashed vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End dashed transparent branch
            }
        // End wireframe pass branch
        }
        // Skip empty opaque pass
        if (!wireframe && prepared.OpaqueVertexCount > 0)
        // Begin opaque pass branch
        {
            // Bind opaque PBR pipeline
            commandList.SetPipeline(resources.OpaquePipeline);
            // Bind opaque vertex buffer
            commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
            // Draw opaque vertices by range
            DrawVertexRanges(
                // Recording command list
                commandList,
                // Total opaque vertex count
                (uint)prepared.OpaqueVertexCount,
                // Opaque preview ranges
                previewRanges?.Opaque,
                // Identity transform resource set
                prepared.IdentityResourceSet,
                // Preview transform resource set
                prepared.PreviewResourceSet);
        // End opaque pass branch
        }
        // Skip empty transparent pass
        if (!wireframe && prepared.TransparentVertexCount > 0)
        // Begin transparent pass branch
        {
            // Bind transparent PBR pipeline
            commandList.SetPipeline(resources.TransparentPipeline);
            // Bind transparent vertex buffer
            commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
            // Draw transparent vertices by range
            DrawVertexRanges(
                // Recording command list
                commandList,
                // Total transparent vertex count
                (uint)prepared.TransparentVertexCount,
                // Transparent preview ranges
                previewRanges?.Transparent,
                // Identity transform resource set
                prepared.IdentityResourceSet,
                // Preview transform resource set
                prepared.PreviewResourceSet);
        // End transparent pass branch
        }
        // Copy render target for readback
        commandList.CopyTexture(targets.ColorTexture, 0, 0, 0, 0, 0,
            // Staging texture copy extents
            targets.StagingTexture, 0, 0, 0, 0, 0, (uint)width, (uint)height, 1, 1);
        // Finish command recording
        commandList.End();
        // Record command recording time
        long recordMs = phase.ElapsedMilliseconds;

        // Abort before GPU submission
        ThrowIfCancellationRequested(cancellationToken, "submit Vulkan raster command list");
        // Restart phase timer
        phase.Restart();
        // Reset frame completion fence
        gd.ResetFence(targets.Fence);
        // Submit commands to GPU
        gd.SubmitCommands(commandList, targets.Fence);
        // Wait for GPU completion
        gd.WaitForFence(targets.Fence);
        // Record GPU wait time
        long gpuWaitMs = phase.ElapsedMilliseconds;

        // Abort before texture readback
        ThrowIfCancellationRequested(cancellationToken, "read back Vulkan raster texture");
        // Restart phase timer
        phase.Restart();
        // Read back rendered image
        RenderImage image = ReadBackImage(gd, targets.StagingTexture, width, height);
        // Record readback time
        long readbackMs = phase.ElapsedMilliseconds;
        // Stop total frame timer
        total.Stop();

        // Describe texture sampling mode
        string textureMode = prepared.UsesGpuTextureSampling
            // GPU texture sampling summary
            ? $"textures={prepared.TextureCount}, pages={prepared.TexturePageCount}"
            // Detect texture fallback case
            : prepared.GpuTextureSamplingRequested && !string.IsNullOrWhiteSpace(prepared.TextureFallbackReason)
                // Texture fallback summary
                ? $"textures=base-color fallback ({prepared.TextureFallbackReason})"
                // Default base color mode
                : "textures=base color";
        // Describe triangle count mode
        string triangleMode = prepared.NearClippedTriangleCount > 0
            // Triangle count with skipped invalid
            ? $"triangles={prepared.TotalTriangleCount}/{prepared.SourceTriangleCount} ({prepared.NearClippedTriangleCount} invalid skipped)"
            // Plain triangle count
            : $"triangles={prepared.TotalTriangleCount}";
        // Describe live transform suffix
        string previewMode = hasPreview ? $", live-transform={preview!.SelectionId}" : string.Empty;
        // Append mesh edit suffix when active
        if (hasMeshEditPreview)
            // Append mesh edit selection id
            previewMode += $", live-mesh-edit={meshEditPreview!.SelectionId}";
        // Build frame timing summary
        details = $"VULKAN RASTER PBR CACHED - {width}x{height}, revision={prepared.CacheStamp.Revision}, {triangleMode}, lights={prepared.LightCount}, {textureMode}, cache={(prepareMs == 0 ? "hot" : "ready")}{previewMode}, device={deviceMs}ms, targets={targetMs}ms, prepare={prepareMs}ms, mesh-edit-upload={meshEditUploadMs}ms, uniform={uniformMs}ms, record={recordMs}ms, gpu+wait={gpuWaitMs}ms, readback={readbackMs}ms, total={total.ElapsedMilliseconds}ms" + (wireframe ? ", wireframe" : string.Empty);
        // Log frame stage summary
        Stage(details);
        // Return rendered image
        return image;
    // End RenderLocked method
    }

    /// <summary>Applies the active mesh edit delta to affected GPU vertices, or restores them when idle.</summary>
    // Patch GPU vertices for mesh edits
    private static void UpdateMeshEditPreview(
        // Target graphics device
        GraphicsDevice gd,
        // Prepared GPU scene
        PreparedRasterScene prepared,
        // Optional mesh edit preview
        VulkanRasterMeshEditPreview? preview,
        // Cancellation token for upload
        CancellationToken cancellationToken)
    // Begin UpdateMeshEditPreview method
    {
        // Restore vertices when idle
        if (preview == null || preview.IsIdentity)
        // Begin identity preview branch
        {
            // Restore patched vertices
            RestoreMeshEditPreview(gd, prepared);
            // Exit after restore
            return;
        // End identity preview branch
        }

        // Snapshot active patch state
        MeshEditPatchState? active = prepared.ActiveMeshEdit;
        // Detect stale patch state
        if (active == null ||
            // Compare preview selection id
            active.SelectionId != preview.SelectionId ||
            // Compare preview group id
            active.GroupId != preview.GroupId)
        // Begin stale patch branch
        {
            // Restore previous patch first
            RestoreMeshEditPreview(gd, prepared);
            // Build fresh patch state
            active = BuildMeshEditPatchState(prepared, preview);
            // Store fresh patch state
            prepared.ActiveMeshEdit = active;
        // End stale patch branch
        }

        // Count uploaded patches
        int patchIndex = 0;
        // Upload each edited triangle
        foreach (RasterTrianglePatch patch in active.Patches)
        // Begin per-patch loop
        {
            // Honor preview cancellation
            cancellationToken.ThrowIfCancellationRequested();
            // Read original triangle
            Triangle source = patch.Source;
            // Offset edited corner A
            Vec3 a = (patch.CornerMask & 1) != 0 ? source.A + preview.WorldDelta : source.A;
            // Offset edited corner B
            Vec3 b = (patch.CornerMask & 2) != 0 ? source.B + preview.WorldDelta : source.B;
            // Offset edited corner C
            Vec3 c = (patch.CornerMask & 4) != 0 ? source.C + preview.WorldDelta : source.C;
            // Build moved triangle
            Triangle moved = new(
                // Moved corner positions
                a, b, c,
                // Preserved texture coordinates
                source.UvA, source.UvB, source.UvC,
                // Preserved material
                source.Material,
                // Preserved group id
                source.GroupId);
            // Resolve material index
            int materialIndex = prepared.MaterialIds[source.Material];
            // Build replacement vertex trio
            RasterVertex[] vertices =
            // Begin vertex array literal
            [
                // Replacement vertex A
                new RasterVertex(moved.A, moved.NormalA, moved.UvA, materialIndex),
                // Replacement vertex B
                new RasterVertex(moved.B, moved.NormalB, moved.UvB, materialIndex),
                // Replacement vertex C
                new RasterVertex(moved.C, moved.NormalC, moved.UvC, materialIndex)
            // End vertex array literal
            ];
            // Upload replacement vertices
            UploadTrianglePatch(gd, prepared, patch, vertices);

            // Tally uploaded patch
            patchIndex++;
            // Check cancellation periodically
            if ((patchIndex & 63) == 0)
                // Abort slow mesh edit upload
                ThrowIfCancellationRequested(cancellationToken, "upload Vulkan mesh edit preview");
        // End per-patch loop
        }
    // End UpdateMeshEditPreview method
    }

    /// <summary>Locates the GPU vertex ranges and original vertices affected by a mesh edit preview.</summary>
    // Locate GPU vertices for edits
    private static MeshEditPatchState BuildMeshEditPatchState(
        // Prepared GPU scene
        PreparedRasterScene prepared,
        // Mesh edit preview
        VulkanRasterMeshEditPreview preview)
    // Begin BuildMeshEditPatchState method
    {
        // Index edits by triangle
        Dictionary<int, byte> editByTriangle = new();
        // Collect valid triangle edits
        foreach (VulkanRasterMeshTriangleEdit edit in preview.TriangleEdits)
        // Begin per-edit loop
        {
            // Validate triangle edit entry
            if (edit.TriangleIndex >= 0 && edit.CornerMask != 0)
                // Store corner mask
                editByTriangle[edit.TriangleIndex] = edit.CornerMask;
        // End per-edit loop
        }

        // Preallocate triangle patch list
        List<RasterTrianglePatch> patches = new(editByTriangle.Count);
        // Track opaque vertex offset
        uint opaqueCursor = 0;
        // Track transparent vertex offset
        uint transparentCursor = 0;
        // Index group-local triangles
        int localTriangleIndex = 0;
        // Scan scene triangles for edits
        foreach (Triangle triangle in prepared.Scene.Triangles)
        // Begin per-triangle loop
        {
            // Match edit target group
            bool targetGroup = triangle.GroupId == preview.GroupId;
            // Assign group-local triangle index
            int targetTriangleIndex = targetGroup ? localTriangleIndex++ : -1;
            // Skip non-finite triangles
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                // Skip invalid triangle
                continue;

            // Classify triangle transparency
            bool transparent = IsTransparentMaterial(triangle.Material);
            // Select buffer vertex offset
            uint vertexStart = transparent ? transparentCursor : opaqueCursor;
            // Match triangle to an edit
            if (targetTriangleIndex >= 0 && editByTriangle.TryGetValue(targetTriangleIndex, out byte cornerMask))
            // Begin patch match branch
            {
                // Resolve material index
                int materialIndex = prepared.MaterialIds[triangle.Material];
                // Snapshot original vertices
                RasterVertex[] original =
                // Begin vertex array literal
                [
                    // Original vertex A
                    new RasterVertex(triangle.A, triangle.NormalA, triangle.UvA, materialIndex),
                    // Original vertex B
                    new RasterVertex(triangle.B, triangle.NormalB, triangle.UvB, materialIndex),
                    // Original vertex C
                    new RasterVertex(triangle.C, triangle.NormalC, triangle.UvC, materialIndex)
                // End vertex array literal
                ];
                // Record triangle patch
                patches.Add(new RasterTrianglePatch(
                    // Target buffer transparency flag
                    transparent,
                    // Patch vertex start offset
                    vertexStart,
                    // Source triangle
                    triangle,
                    // Edited corner mask
                    cornerMask,
                    // Original vertices for restore
                    original));
            // End patch match branch
            }

            // Advance matching buffer cursor
            if (transparent)
                // Advance transparent cursor
                transparentCursor += 3;
            // Handle opaque cursor case
            else
                // Advance opaque cursor
                opaqueCursor += 3;
        // End per-triangle loop
        }

        // Return assembled patch state
        return new MeshEditPatchState
        // Begin patch state initializer
        {
            // Edited selection identifier
            SelectionId = preview.SelectionId,
            // Edited group identifier
            GroupId = preview.GroupId,
            // Edited triangle patches
            Patches = patches
        // End patch state initializer
        };
    // End BuildMeshEditPatchState method
    }

    /// <summary>Restores previously patched GPU vertices to their original values.</summary>
    // Restore patched GPU vertices
    private static void RestoreMeshEditPreview(GraphicsDevice gd, PreparedRasterScene prepared)
    // Begin RestoreMeshEditPreview method
    {
        // Snapshot active patch state
        MeshEditPatchState? active = prepared.ActiveMeshEdit;
        // Skip restore when idle
        if (active == null)
            // Exit without active patch
            return;

        // Restore each patched triangle
        foreach (RasterTrianglePatch patch in active.Patches)
            // Reupload original triangle vertices
            UploadTrianglePatch(gd, prepared, patch, patch.OriginalVertices);
        // Clear active patch state
        prepared.ActiveMeshEdit = null;
    // End RestoreMeshEditPreview method
    }

    /// <summary>Uploads three replacement vertices into the opaque or transparent buffer.</summary>
    // Upload three replacement vertices
    private static void UploadTrianglePatch(
        // Target graphics device
        GraphicsDevice gd,
        // Prepared GPU scene
        PreparedRasterScene prepared,
        // Patch locating target vertices
        RasterTrianglePatch patch,
        // Replacement vertices
        RasterVertex[] vertices)
    // Begin UploadTrianglePatch method
    {
        // Select target vertex buffer
        DeviceBuffer destination = patch.Transparent
            // Transparent buffer destination
            ? prepared.TransparentVertexBuffer
            // Opaque buffer destination
            : prepared.OpaqueVertexBuffer;
        // Compute vertex byte offset
        uint byteOffset = checked(patch.VertexStart * (uint)Marshal.SizeOf<RasterVertex>());
        // Write replacement vertices to GPU
        gd.UpdateBuffer(destination, byteOffset, vertices);
    // End UploadTrianglePatch method
    }

    /// <summary>Draws a vertex buffer, switching between identity and preview resource sets per range.</summary>
    // Draw buffer with preview ranges
    private static void DrawVertexRanges(
        // Recording command list
        CommandList commandList,
        // Total buffer vertex count
        uint totalVertexCount,
        // Optional preview vertex ranges
        IReadOnlyList<VertexRange>? previewRanges,
        // Identity transform resource set
        ResourceSet identitySet,
        // Preview transform resource set
        ResourceSet previewSet)
    // Begin DrawVertexRanges method
    {
        // Detect full-buffer draw case
        if (previewRanges == null || previewRanges.Count == 0)
        // Begin full draw branch
        {
            // Bind identity transform set
            commandList.SetGraphicsResourceSet(0, identitySet);
            // Draw entire vertex buffer
            commandList.Draw(totalVertexCount);
            // Exit after full draw
            return;
        // End full draw branch
        }

        // Track drawn vertex offset
        uint cursor = 0;
        // Draw each preview range
        foreach (VertexRange range in previewRanges)
        // Begin per-range loop
        {
            // Detect leading identity gap
            if (range.Start > cursor)
            // Begin identity gap branch
            {
                // Bind identity transform set
                commandList.SetGraphicsResourceSet(0, identitySet);
                // Draw identity gap vertices
                commandList.Draw(range.Start - cursor, 1, cursor, 0);
            // End identity gap branch
            }

            // Skip empty preview range
            if (range.Count > 0)
            // Begin preview range branch
            {
                // Bind preview transform set
                commandList.SetGraphicsResourceSet(0, previewSet);
                // Draw preview range vertices
                commandList.Draw(range.Count, 1, range.Start, 0);
            // End preview range branch
            }
            // Advance drawn vertex cursor
            cursor = Math.Max(cursor, range.Start + range.Count);
        // End per-range loop
        }

        // Detect trailing identity vertices
        if (cursor < totalVertexCount)
        // Begin trailing gap branch
        {
            // Bind identity transform set
            commandList.SetGraphicsResourceSet(0, identitySet);
            // Draw trailing identity vertices
            commandList.Draw(totalVertexCount - cursor, 1, cursor, 0);
        // End trailing gap branch
        }
    // End DrawVertexRanges method
    }

    /// <summary>Returns cached vertex ranges for a transform preview selection, building them on first use.</summary>
    // Resolve cached preview vertex ranges
    private static PreviewRangeSet GetOrCreatePreviewRanges(
        // Prepared GPU scene
        PreparedRasterScene prepared,
        // Transform preview selection
        VulkanRasterTransformPreview preview)
    // Begin GetOrCreatePreviewRanges method
    {
        // Return cached ranges when available
        if (prepared.PreviewRanges.TryGetValue(preview.SelectionId, out PreviewRangeSet? cached))
            // Return cached range set
            return cached;

        // Collect opaque preview ranges
        List<VertexRange> opaque = new();
        // Collect transparent preview ranges
        List<VertexRange> transparent = new();
        // Gather ranges per group
        foreach (int groupId in preview.GroupIds)
        // Begin per-group loop
        {
            // Skip groups without ranges
            if (!prepared.GroupRanges.TryGetValue(groupId, out PreviewRangeSet? groupRanges))
                // Skip missing group
                continue;
            // Append opaque group ranges
            opaque.AddRange(groupRanges.Opaque);
            // Append transparent group ranges
            transparent.AddRange(groupRanges.Transparent);
        // End per-group loop
        }

        // Merge collected preview ranges
        PreviewRangeSet result = new(MergeRanges(opaque), MergeRanges(transparent));
        // Cache merged range set
        prepared.PreviewRanges[preview.SelectionId] = result;
        // Return merged range set
        return result;
    // End GetOrCreatePreviewRanges method
    }

    /// <summary>Builds per-group opaque and transparent vertex ranges for the scene.</summary>
    // Build per-group vertex ranges
    private static IReadOnlyDictionary<int, PreviewRangeSet> BuildGroupVertexRanges(Scene scene)
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
    private static IReadOnlyList<VertexRange> MergeRanges(List<VertexRange> ranges)
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
    private static void AppendRange(List<VertexRange> ranges, uint start, uint count)
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

    /// <summary>Returns the cached GPU scene, rebuilding buffers, atlas, and descriptors when stale.</summary>
    // Declare cached scene builder
    private static PreparedRasterScene GetOrCreatePreparedScene(GraphicsDevice gd, SharedRasterResources resources, Scene scene, CancellationToken cancellationToken)
    // Begin GetOrCreatePreparedScene method
    {
        // Read texture sampling setting
        bool gpuTextureSamplingRequested = UseGpuTextureSampling;
        // Check cached scene exists
        if (preparedScene != null &&
            // Check scene cache still valid
            preparedScene.CacheStamp.Matches(scene) &&
            // Check sampling mode unchanged
            preparedScene.GpuTextureSamplingRequested == gpuTextureSamplingRequested)
            // Reuse cached prepared scene
            return preparedScene;

        // Dispose stale prepared scene
        preparedScene?.Dispose();
        // Clear stale scene reference
        preparedScene = null;
        // Abort if rebuild cancelled
        ThrowIfCancellationRequested(cancellationToken, "build cached Vulkan scene");

        // Build only lightweight atlas placement metadata first. Pixel data is
        // transferred one texture at a time after the Vulkan texture exists, so
        // a full CPU-side atlas never coexists with the GPU atlas.
        // Build texture atlas placements
        TextureBuildResult textures = BuildTextureAtlas(gd, scene, gpuTextureSamplingRequested);
        // Build geometry and material metadata
        RasterGeometryBuildResult geometry = BuildGeometryMetadata(scene, textures.TexturePlacements, textures.UsesGpuTextureSampling);
        // Build per-group vertex ranges
        IReadOnlyDictionary<int, PreviewRangeSet> groupRanges = BuildGroupVertexRanges(scene);
        // Build GPU light array
        RasterLight[] lights = BuildLightBuffer(scene);
        // Get Veldrid resource factory
        ResourceFactory factory = gd.ResourceFactory;
        // Measure vertex struct size
        int vertexStride = Marshal.SizeOf<RasterVertex>();
        // Measure light struct size
        int lightStride = Marshal.SizeOf<RasterLight>();
        // Measure material struct size
        int materialStride = Marshal.SizeOf<RasterMaterial>();
        // Read opaque vertex count
        int opaqueVertexCount = geometry.OpaqueVertexCount;
        // Read transparent vertex count
        int transparentVertexCount = geometry.TransparentVertexCount;
        // Read material count
        int materialCount = geometry.MaterialCount;
        // Read total triangle count
        int totalTriangleCount = geometry.TotalTriangles;
        // Read near-clipped triangle count
        int nearClippedTriangleCount = geometry.NearClippedTriangles;

        // Validate opaque buffer size
        uint opaqueBytes = CheckedBufferSize((long)opaqueVertexCount * vertexStride, vertexStride, "opaque vertex");
        // Validate transparent buffer size
        uint transparentBytes = CheckedBufferSize((long)transparentVertexCount * vertexStride, vertexStride, "transparent vertex");
        // Validate light buffer size
        uint lightBytes = CheckedBufferSize((long)lights.Length * lightStride, lightStride, "light");
        // Validate material buffer size
        uint materialBytes = CheckedBufferSize((long)materialCount * materialStride, materialStride, "material");

        // Declare opaque vertex buffer
        DeviceBuffer? opaque = null;
        // Declare transparent vertex buffer
        DeviceBuffer? transparent = null;
        // Declare camera uniform buffer
        DeviceBuffer? camera = null;
        // Declare identity transform buffer
        DeviceBuffer? identityTransform = null;
        // Declare preview transform buffer
        DeviceBuffer? previewTransform = null;
        // Declare light storage buffer
        DeviceBuffer? light = null;
        // Declare material storage buffer
        DeviceBuffer? material = null;
        // Declare texture atlas handle
        Texture? atlas = null;
        // Declare atlas sampler handle
        Sampler? sampler = null;
        // Declare identity resource set
        ResourceSet? identitySet = null;
        // Declare preview resource set
        ResourceSet? previewSet = null;
        // Begin GPU resource creation
        try
        // Begin resource creation block
        {
            // Create opaque vertex buffer
            opaque = factory.CreateBuffer(new BufferDescription(opaqueBytes, BufferUsage.VertexBuffer));
            // Create transparent vertex buffer
            transparent = factory.CreateBuffer(new BufferDescription(transparentBytes, BufferUsage.VertexBuffer));
            // Create camera uniform buffer
            camera = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterCameraConstants>(), BufferUsage.UniformBuffer));
            // Create identity transform buffer
            identityTransform = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterTransformConstants>(), BufferUsage.UniformBuffer));
            // Create preview transform buffer
            previewTransform = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterTransformConstants>(), BufferUsage.UniformBuffer));
            // Create read-only light buffer
            light = factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, (uint)lightStride));
            // Create read-only material buffer
            material = factory.CreateBuffer(new BufferDescription(materialBytes, BufferUsage.StructuredBufferReadOnly, (uint)materialStride));
            // Create texture atlas image
            atlas = factory.CreateTexture(TextureDescription.Texture2D(
                // Pass atlas dimensions and pages
                (uint)textures.Width, (uint)textures.Height, 1, (uint)textures.PageCount,
                // Set atlas pixel format sampled
                Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            // Create linear atlas sampler
            sampler = factory.CreateSampler(SamplerDescription.Linear);

            // Upload vertices to GPU buffers
            UploadRasterGeometry(gd, opaque, transparent, scene, geometry.MaterialIds, cancellationToken);
            // Upload materials with fallback default
            gd.UpdateBuffer(material, 0, geometry.Materials.Length == 0 ? new[] { default(RasterMaterial) } : geometry.Materials);
            // Release CPU material array
            geometry.Materials = Array.Empty<RasterMaterial>();
            // Upload lights with fallback default
            gd.UpdateBuffer(light, 0, lights.Length == 0 ? new[] { default(RasterLight) } : lights);
            // Initialize identity transform constants
            gd.UpdateBuffer(identityTransform, 0, new RasterTransformConstants(preview: null));
            // Initialize preview transform constants
            gd.UpdateBuffer(previewTransform, 0, new RasterTransformConstants(preview: null));
            // Upload atlas texture pixels
            UploadTextureAtlas(gd, atlas, textures, cancellationToken);

            // Create identity resource set
            identitySet = factory.CreateResourceSet(new ResourceSetDescription(
                // Bind identity buffers and atlas
                resources.Layout, camera, identityTransform, light, material, atlas, sampler));
            // Create preview resource set
            previewSet = factory.CreateResourceSet(new ResourceSetDescription(
                // Bind preview buffers and atlas
                resources.Layout, camera, previewTransform, light, material, atlas, sampler));
            // Compute scene bounding sphere
            ComputeSceneBounds(scene, out Vec3 center, out double radius);
            // Build cached prepared scene
            preparedScene = new PreparedRasterScene
            // Begin scene initializer block
            {
                // Store source scene reference
                Scene = scene,
                // Capture scene cache stamp
                CacheStamp = SceneCacheStamp.Capture(scene),
                // Store material index map
                MaterialIds = geometry.MaterialIds,
                // Store opaque vertex buffer
                OpaqueVertexBuffer = opaque,
                // Store transparent vertex buffer
                TransparentVertexBuffer = transparent,
                // Store camera buffer
                CameraBuffer = camera,
                // Store identity transform buffer
                IdentityTransformBuffer = identityTransform,
                // Store preview transform buffer
                PreviewTransformBuffer = previewTransform,
                // Store light buffer
                LightBuffer = light,
                // Store material buffer
                MaterialBuffer = material,
                // Store atlas texture
                AtlasTexture = atlas,
                // Store atlas sampler
                AtlasSampler = sampler,
                // Store identity resource set
                IdentityResourceSet = identitySet,
                // Store preview resource set
                PreviewResourceSet = previewSet,
                // Store per-group ranges
                GroupRanges = groupRanges,
                // Store light count
                LightCount = lights.Length,
                // Store opaque vertex count
                OpaqueVertexCount = opaqueVertexCount,
                // Store transparent vertex count
                TransparentVertexCount = transparentVertexCount,
                // Store material count
                MaterialCount = materialCount,
                // Store source triangle count
                SourceTriangleCount = geometry.SourceTriangleCount,
                // Store total triangle count
                TotalTriangleCount = totalTriangleCount,
                // Store near-clipped triangle count
                NearClippedTriangleCount = nearClippedTriangleCount,
                // Store texture count
                TextureCount = textures.TextureCount,
                // Store atlas page count
                TexturePageCount = textures.PageCount,
                // Store requested sampling mode
                GpuTextureSamplingRequested = gpuTextureSamplingRequested,
                // Store effective sampling mode
                UsesGpuTextureSampling = textures.UsesGpuTextureSampling,
                // Store texture fallback reason
                TextureFallbackReason = textures.FallbackReason,
                // Store bounding sphere center
                BoundingCenter = center,
                // Store bounding sphere radius
                BoundingRadius = radius
            // End scene initializer block
            };
            // Return newly prepared scene
            return preparedScene;
        // End resource creation block
        }
        // Handle resource creation failure
        catch
        // Begin cleanup block
        {
            // Dispose preview resource set
            try { previewSet?.Dispose(); } catch { }
            // Dispose identity resource set
            try { identitySet?.Dispose(); } catch { }
            // Dispose atlas sampler
            try { sampler?.Dispose(); } catch { }
            // Dispose atlas texture
            try { atlas?.Dispose(); } catch { }
            // Dispose material buffer
            try { material?.Dispose(); } catch { }
            // Dispose light buffer
            try { light?.Dispose(); } catch { }
            // Dispose preview transform buffer
            try { previewTransform?.Dispose(); } catch { }
            // Dispose identity transform buffer
            try { identityTransform?.Dispose(); } catch { }
            // Dispose camera buffer
            try { camera?.Dispose(); } catch { }
            // Dispose transparent vertex buffer
            try { transparent?.Dispose(); } catch { }
            // Dispose opaque vertex buffer
            try { opaque?.Dispose(); } catch { }
            // Rethrow creation failure
            throw;
        // End cleanup block
        }
    // End GetOrCreatePreparedScene method
    }

    /// <summary>Uploads scene triangles into the opaque and transparent vertex buffers in chunks.</summary>
    // Declare geometry upload helper
    private static void UploadRasterGeometry(
        // Accept target graphics device
        GraphicsDevice gd,
        // Accept opaque vertex buffer
        DeviceBuffer opaqueVertexBuffer,
        // Accept transparent vertex buffer
        DeviceBuffer transparentVertexBuffer,
        // Accept source scene
        Scene scene,
        // Accept material index map
        IReadOnlyDictionary<Material, int> materialIds,
        // Accept cancellation token
        CancellationToken cancellationToken)
    // Begin UploadRasterGeometry method
    {
        // Keep upload memory constant while preserving the separate transparent
        // pass. The previous low-memory rewrite uploaded every triangle into the
        // opaque buffer and hard-coded the transparent count to zero, which made
        // transmission and alpha-blended materials depth-write as solid white.
        // Set triangles per upload chunk
        const int TrianglesPerChunk = 2048;
        // Measure vertex struct size
        int vertexStride = Marshal.SizeOf<RasterVertex>();
        // Allocate opaque chunk staging
        RasterVertex[] opaqueVertices = new RasterVertex[TrianglesPerChunk * 3];
        // Allocate transparent chunk staging
        RasterVertex[] transparentVertices = new RasterVertex[TrianglesPerChunk * 3];
        // Track opaque chunk fill
        int opaqueTrianglesInChunk = 0;
        // Track transparent chunk fill
        int transparentTrianglesInChunk = 0;
        // Track uploaded opaque triangles
        int uploadedOpaqueTriangles = 0;
        // Track uploaded transparent triangles
        int uploadedTransparentTriangles = 0;
        // Track processed triangle count
        int processedTriangles = 0;

        // Iterate scene triangles for upload
        foreach (Triangle triangle in scene.Triangles)
        // Begin per-triangle upload loop
        {
            // Skip triangles with non-finite vertices
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                // Skip invalid triangle
                continue;

            // Resolve triangle material index
            int materialIndex = materialIds[triangle.Material];
            // Classify triangle transparency
            bool transparent = IsTransparentMaterial(triangle.Material);
            // Select chunk staging buffer
            RasterVertex[] target = transparent ? transparentVertices : opaqueVertices;
            // Read active chunk fill
            int trianglesInChunk = transparent ? transparentTrianglesInChunk : opaqueTrianglesInChunk;
            // Compute vertex write offset
            int vertexIndex = trianglesInChunk * 3;
            // Stage first triangle vertex
            target[vertexIndex] = new RasterVertex(triangle.A, triangle.NormalA, triangle.UvA, materialIndex);
            // Stage second triangle vertex
            target[vertexIndex + 1] = new RasterVertex(triangle.B, triangle.NormalB, triangle.UvB, materialIndex);
            // Stage third triangle vertex
            target[vertexIndex + 2] = new RasterVertex(triangle.C, triangle.NormalC, triangle.UvC, materialIndex);

            // Check transparent upload branch
            if (transparent)
            // Begin transparent chunk block
            {
                // Count staged transparent triangle
                transparentTrianglesInChunk++;
                // Check transparent chunk full
                if (transparentTrianglesInChunk == TrianglesPerChunk)
                // Begin transparent flush block
                {
                    // Flush full transparent chunk
                    UploadChunk(transparentVertexBuffer, transparentVertices, transparentTrianglesInChunk, ref uploadedTransparentTriangles);
                    // Reset transparent chunk fill
                    transparentTrianglesInChunk = 0;
                // End transparent flush block
                }
            // End transparent chunk block
            }
            // Handle opaque upload branch
            else
            // Begin opaque chunk block
            {
                // Count staged opaque triangle
                opaqueTrianglesInChunk++;
                // Check opaque chunk full
                if (opaqueTrianglesInChunk == TrianglesPerChunk)
                // Begin opaque flush block
                {
                    // Flush full opaque chunk
                    UploadChunk(opaqueVertexBuffer, opaqueVertices, opaqueTrianglesInChunk, ref uploadedOpaqueTriangles);
                    // Reset opaque chunk fill
                    opaqueTrianglesInChunk = 0;
                // End opaque flush block
                }
            // End opaque chunk block
            }

            // Count processed triangle
            processedTriangles++;
            // Check cancellation poll interval
            if (processedTriangles % (TrianglesPerChunk * 2) == 0)
                // Abort if upload cancelled
                ThrowIfCancellationRequested(cancellationToken, "upload Vulkan raster geometry");
        // End per-triangle upload loop
        }

        // Check leftover opaque vertices
        if (opaqueTrianglesInChunk > 0)
            // Flush final opaque chunk
            UploadChunk(opaqueVertexBuffer, opaqueVertices, opaqueTrianglesInChunk, ref uploadedOpaqueTriangles);
        // Check leftover transparent vertices
        if (transparentTrianglesInChunk > 0)
            // Flush final transparent chunk
            UploadChunk(transparentVertexBuffer, transparentVertices, transparentTrianglesInChunk, ref uploadedTransparentTriangles);

        /// <summary>Uploads one chunk of raster vertices at the running buffer offset.</summary>
        // Declare chunk upload helper
        void UploadChunk(DeviceBuffer destination, RasterVertex[] vertices, int triangleCount, ref int uploadedTriangles)
        // Begin UploadChunk method
        {
            // Default to full staging array
            RasterVertex[] vertexUpload = vertices;
            // Check partial final chunk
            if (triangleCount != TrianglesPerChunk)
            // Begin partial chunk block
            {
                // Allocate exact-size upload array
                vertexUpload = new RasterVertex[triangleCount * 3];
                // Copy staged vertices to upload
                Array.Copy(vertices, vertexUpload, vertexUpload.Length);
            // End partial chunk block
            }

            // Compute GPU buffer byte offset
            uint vertexOffset = checked((uint)((long)uploadedTriangles * 3L * vertexStride));
            // Write chunk to GPU buffer
            gd.UpdateBuffer(destination, vertexOffset, vertexUpload);
            // Advance uploaded triangle count
            uploadedTriangles += triangleCount;
        // End UploadChunk method
        }
    // End UploadRasterGeometry method
    }

    /// <summary>Uploads each texture and its filtering border pixels into the GPU atlas.</summary>
    // Declare atlas upload helper
    private static void UploadTextureAtlas(GraphicsDevice gd, Texture atlas, TextureBuildResult textures, CancellationToken cancellationToken)
    // Begin UploadTextureAtlas method
    {
        // Check atlas has no uploads
        if (textures.Uploads.Count == 0)
        // Begin empty atlas block
        {
            // Write white fallback texel
            gd.UpdateTexture(atlas, new[] { 0xffffffffu }, 0, 0, 0, 1, 1, 1, 0, 0);
            // Finish empty atlas upload
            return;
        // End empty atlas block
        }

        // Iterate atlas texture uploads
        foreach (RasterTextureUpload upload in textures.Uploads)
        // Begin per-texture upload loop
        {
            // Abort if atlas upload cancelled
            ThrowIfCancellationRequested(cancellationToken, "upload Vulkan raster texture atlas");
            // Read upload source texture
            TextureMap texture = upload.Texture;
            // Clamp texture width minimum
            int width = Math.Max(1, texture.Width);
            // Clamp texture height minimum
            int height = Math.Max(1, texture.Height);
            // Copy packed texture pixels
            uint[] source = texture.CopyPackedRgba32Pixels();
            // Compute expected pixel count
            int expectedPixels = checked(width * height);
            // Check pixel count mismatch
            if (source.Length != expectedPixels)
                // Reject corrupt texture pixels
                throw new InvalidOperationException($"Texture '{texture.Name}' returned {source.Length} pixels; expected {expectedPixels}.");

            // Upload texture rect to atlas
            gd.UpdateTexture(atlas, source, (uint)upload.X, (uint)upload.Y, 0, (uint)width, (uint)height, 1, 0, (uint)upload.Page);

            // Upload only the one-pixel borders needed by linear filtering.
            // This avoids allocating a second padded copy of the whole texture.
            // Allocate top border row
            uint[] top = new uint[width];
            // Allocate bottom border row
            uint[] bottom = new uint[width];
            // Copy first row to border
            Array.Copy(source, 0, top, 0, width);
            // Copy last row to border
            Array.Copy(source, (height - 1) * width, bottom, 0, width);
            // Upload top filter border
            gd.UpdateTexture(atlas, top, (uint)upload.X, (uint)(upload.Y - 1), 0, (uint)width, 1, 1, 0, (uint)upload.Page);
            // Upload bottom filter border
            gd.UpdateTexture(atlas, bottom, (uint)upload.X, (uint)(upload.Y + height), 0, (uint)width, 1, 1, 0, (uint)upload.Page);

            // Allocate left border column
            uint[] left = new uint[height];
            // Allocate right border column
            uint[] right = new uint[height];
            // Iterate texture rows for borders
            for (int y = 0; y < height; y++)
            // Begin border column loop
            {
                // Copy left edge texel
                left[y] = source[y * width];
                // Copy right edge texel
                right[y] = source[y * width + width - 1];
            // End border column loop
            }
            // Upload left filter border
            gd.UpdateTexture(atlas, left, (uint)(upload.X - 1), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);
            // Upload right filter border
            gd.UpdateTexture(atlas, right, (uint)(upload.X + width), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);

            // Upload top-left corner texel
            gd.UpdateTexture(atlas, new[] { source[0] }, (uint)(upload.X - 1), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            // Upload top-right corner texel
            gd.UpdateTexture(atlas, new[] { source[width - 1] }, (uint)(upload.X + width), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            // Upload bottom-left corner texel
            gd.UpdateTexture(atlas, new[] { source[(height - 1) * width] }, (uint)(upload.X - 1), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            // Upload bottom-right corner texel
            gd.UpdateTexture(atlas, new[] { source[source.Length - 1] }, (uint)(upload.X + width), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            // Release CPU pixel buffer
            source = Array.Empty<uint>();
        // End per-texture upload loop
        }
    // End UploadTextureAtlas method
    }

    /// <summary>Validates a requested GPU buffer size against the minimum and 4 GB limit.</summary>
    // Declare buffer size validator
    private static uint CheckedBufferSize(long requestedBytes, int minimumBytes, string label)
    // Begin CheckedBufferSize method
    {
        // Enforce minimum buffer size
        long bytes = Math.Max(minimumBytes, requestedBytes);
        // Check four gigabyte limit
        if (bytes > uint.MaxValue)
            // Reject oversized GPU buffer
            throw new InvalidOperationException($"The Vulkan {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, which exceeds the 4 GB buffer limit.");
        // Return validated byte size
        return checked((uint)bytes);
    // End CheckedBufferSize method
    }

    /// <summary>Returns cached render targets for a resolution, evicting the least recently used size.</summary>
    // Declare render target cache
    private static RasterTargets GetOrCreateTargets(GraphicsDevice gd, int width, int height)
    // Begin GetOrCreateTargets method
    {
        // Build target cache key
        (int Width, int Height) key = (width, height);
        // Look up cached render targets
        if (sharedTargets.TryGetValue(key, out RasterTargets? cached))
        // Begin cache hit block
        {
            // Refresh target use stamp
            cached.LastUseSerial = ++targetUseSerial;
            // Reuse cached render targets
            return cached;
        // End cache hit block
        }

        // Check target cache full
        if (sharedTargets.Count >= MaxCachedTargetSizes)
        // Begin cache eviction block
        {
            // Declare oldest cache entry
            KeyValuePair<(int Width, int Height), RasterTargets> oldest =
                // Find least recently used targets
                sharedTargets.MinBy(pair => pair.Value.LastUseSerial);
            // Remove oldest cache entry
            sharedTargets.Remove(oldest.Key);
            // Dispose evicted render targets
            oldest.Value.Dispose();
            // Log target cache eviction
            Stage($"Evict Vulkan raster target cache: {oldest.Key.Width}x{oldest.Key.Height}");
        // End cache eviction block
        }

        // Get Veldrid resource factory
        ResourceFactory factory = gd.ResourceFactory;
        // Create color render target
        Texture color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        // Create depth stencil target
        Texture depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
        // Create staging readback texture
        Texture staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        // Build new target record
        RasterTargets created = new()
        // Begin target initializer block
        {
            // Store target width
            Width = width,
            // Store target height
            Height = height,
            // Store color texture
            ColorTexture = color,
            // Store depth texture
            DepthTexture = depth,
            // Store staging texture
            StagingTexture = staging,
            // Create target framebuffer
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color)),
            // Create target command list
            CommandList = factory.CreateCommandList(),
            // Create target GPU fence
            Fence = factory.CreateFence(false),
            // Stamp target use serial
            LastUseSerial = ++targetUseSerial
        // End target initializer block
        };
        // Cache new render targets
        sharedTargets.Add(key, created);
        // Log target cache insert
        Stage($"Cache Vulkan raster targets: {width}x{height} ({sharedTargets.Count}/{MaxCachedTargetSizes})");
        // Return new render targets
        return created;
    // End GetOrCreateTargets method
    }

    /// <summary>Computes the scene bounding center and radius from all triangle vertices.</summary>
    // Declare scene bounds helper
    private static void ComputeSceneBounds(Scene scene, out Vec3 center, out double radius)
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
    private static double ComputeCachedCameraFarPlane(PreparedRasterScene scene, Vec3 cameraPosition) =>
        // Clamp far plane to bounds
        Math.Clamp((scene.BoundingCenter - cameraPosition).Length() + scene.BoundingRadius * 1.1 + 1.0, 10.0, CameraFar);

    /// <summary>Returns the shared Vulkan graphics device, creating it after a child-process preflight.</summary>
    // Declare shared device accessor
    private static GraphicsDevice GetOrCreateSharedDevice()
    // Begin GetOrCreateSharedDevice method
    {
        // Lock device creation guard
        lock (DeviceSync)
        // Begin device lock block
        {
            // Check shared device exists
            if (sharedGraphicsDevice != null)
            // Begin device reuse block
            {
                // Log device reuse
                Stage("Reuse shared Veldrid Vulkan raster GraphicsDevice");
                // Return shared graphics device
                return sharedGraphicsDevice;
            // End device reuse block
            }

            // Check preflight still pending
            if (!preflightCompleted)
            // Begin preflight block
            {
                // Log preflight start
                Stage("Preflight Veldrid Vulkan raster GraphicsDevice in child process");
                // Verify Vulkan in child process
                VeldridVulkanDevicePreflight.VerifyInChildProcess(Stage);
                // Mark preflight completed
                preflightCompleted = true;
            // End preflight block
            }

            // Log device creation
            Stage("Create shared Veldrid Vulkan raster GraphicsDevice");
            // Create Vulkan graphics device
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            // Begin device options block
            {
                // Set Vulkan debug flag
                Debug = IsVulkanDebugEnabled(),
                // Use Vulkan-native clip-space Y and flip Y explicitly in the vertex shader.
                // This avoids depending on backend-specific viewport normalization while
                // keeping the projection math easy to audit.
                // Use Vulkan clip-space Y
                PreferStandardClipSpaceYDirection = false,
                // Use zero-to-one depth
                PreferDepthRangeZeroToOne = true,
                // Disable vertical sync wait
                SyncToVerticalBlank = false
            // End device options block
            });

            // Verify Vulkan backend selected
            if (gd.BackendType != GraphicsBackend.Vulkan)
            // Begin backend check block
            {
                // Dispose wrong-backend device
                gd.Dispose();
                // Reject non-Vulkan device
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device for the raster backend.");
            // End backend check block
            }

            // Cache shared graphics device
            sharedGraphicsDevice = gd;
            // Return new graphics device
            return gd;
        // End device lock block
        }
    // End GetOrCreateSharedDevice method
    }

    /// <summary>Returns the shared raster layout, shaders, and opaque and transparent pipelines.</summary>
    // Declare shared resources accessor
    private static SharedRasterResources GetOrCreateSharedRasterResources(GraphicsDevice gd, OutputDescription outputDescription)
    // Begin GetOrCreateSharedRasterResources method
    {
        // Lock resource creation guard
        lock (DeviceSync)
        // Begin resource lock block
        {
            // Check shared resources exist
            if (sharedRasterResources != null)
                // Reuse shared raster resources
                return sharedRasterResources;

            // Get Veldrid resource factory
            ResourceFactory factory = gd.ResourceFactory;
            // Log resource layout creation
            Stage("Create Vulkan raster resource layout");
            // Create shader resource layout
            ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                // Bind camera uniform slot
                new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                // Bind transform uniform slot
                new ResourceLayoutElementDescription("TransformConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                // Bind light buffer slot
                new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                // Bind material buffer slot
                new ResourceLayoutElementDescription("MaterialBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                // Bind atlas texture slot
                new ResourceLayoutElementDescription("AtlasTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                // Bind atlas sampler slot
                new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Begin pipeline creation guard
            try
            // Begin pipeline creation block
            {
                // Compile shared vertex shader
                Shader vertexShader = CreateRasterVertexShader(factory);
                // Compile shaded fragment shader
                Shader fragmentShader = CreateRasterFragmentShader(factory, null);
                // Compile wireframe fragment shader
                Shader wireframeFragmentShader = CreateRasterFragmentShader(factory, "WIREFRAME_SOLID");
                // Compile dashed fragment shader
                Shader dashedFragmentShader = CreateRasterFragmentShader(factory, "WIREFRAME_DASHED");
                // Collect shaders for disposal
                Shader[] shaders = [vertexShader, fragmentShader, wireframeFragmentShader, dashedFragmentShader];
                // Begin shader use guard
                try
                // Begin shader use block
                {
                    // Declare vertex input layout
                    VertexLayoutDescription vertexLayout = new(
                        // Map position vertex element
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        // Map normal vertex element
                        new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        // Map UV vertex element
                        new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        // Map material index element
                        new VertexElementDescription("MaterialIndex", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));

                    // Declare rasterizer state
                    RasterizerStateDescription rasterizerState = new(
                        // Disable face culling
                        cullMode: FaceCullMode.None,
                        // Use solid fill mode
                        fillMode: PolygonFillMode.Solid,
                        // Set clockwise front faces
                        frontFace: FrontFace.Clockwise,
                        // Enable depth clipping
                        depthClipEnabled: true,
                        // Disable scissor test
                        scissorTestEnabled: false);

                    // Combine layout with shaders
                    ShaderSetDescription shaderSet = new(new[] { vertexLayout }, [vertexShader, fragmentShader]);
                    // Combine layout with wireframe shaders
                    ShaderSetDescription wireframeShaderSet = new(new[] { vertexLayout }, [vertexShader, wireframeFragmentShader]);
                    // Combine layout with dashed shaders
                    ShaderSetDescription dashedShaderSet = new(new[] { vertexLayout }, [vertexShader, dashedFragmentShader]);
                    // Clone rasterizer state for wireframe
                    RasterizerStateDescription wireframeRasterizerState = rasterizerState;
                    // Switch clone to wireframe fill
                    wireframeRasterizerState.FillMode = PolygonFillMode.Wireframe;

                    // Declare opaque pipeline description
                    GraphicsPipelineDescription opaquePipelineDescription = new()
                    // Begin opaque pipeline initializer block
                    {
                        // Disable blending for opaque pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin opaque depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable opaque depth testing
                            depthTestEnabled: true,
                            // Enable opaque depth writes
                            depthWriteEnabled: true,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign raster shader set
                        ShaderSet = shaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End opaque pipeline initializer
                    };

                    // Declare transparent pipeline description
                    GraphicsPipelineDescription transparentPipelineDescription = new()
                    // Begin transparent pipeline initializer block
                    {
                        // Enable alpha blending for transparency
                        BlendState = BlendStateDescription.SingleAlphaBlend,
                        // Begin transparent depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable transparent depth testing
                            depthTestEnabled: true,
                            // Disable transparent depth writes
                            depthWriteEnabled: false,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign raster shader set
                        ShaderSet = shaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End transparent pipeline initializer
                    };

                    // Declare depth prepass pipeline description
                    GraphicsPipelineDescription depthPrepassDescription = new()
                    // Begin prepass pipeline initializer block
                    {
                        // Disable blending for prepass pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin prepass depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable prepass depth testing
                            depthTestEnabled: true,
                            // Enable prepass depth writes
                            depthWriteEnabled: true,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign wireframe shader set
                        ShaderSet = wireframeShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End prepass pipeline initializer
                    };

                    // Declare wireframe pipeline description
                    GraphicsPipelineDescription wireframePipelineDescription = new()
                    // Begin wireframe pipeline initializer block
                    {
                        // Disable blending for wireframe pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin wireframe depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable wireframe depth testing
                            depthTestEnabled: true,
                            // Enable wireframe depth writes
                            depthWriteEnabled: true,
                            // Use less-equal depth comparison
                            comparisonKind: ComparisonKind.LessEqual),
                        // Assign wireframe rasterizer state
                        RasterizerState = wireframeRasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign wireframe shader set
                        ShaderSet = wireframeShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End wireframe pipeline initializer
                    };

                    // Declare dashed pipeline description
                    GraphicsPipelineDescription dashedPipelineDescription = new()
                    // Begin dashed pipeline initializer block
                    {
                        // Disable blending for dashed pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin dashed depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable dashed depth testing
                            depthTestEnabled: true,
                            // Disable dashed depth writes
                            depthWriteEnabled: false,
                            // Use greater depth comparison
                            comparisonKind: ComparisonKind.Greater),
                        // Assign wireframe rasterizer state
                        RasterizerState = wireframeRasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign dashed shader set
                        ShaderSet = dashedShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End dashed pipeline initializer
                    };

                    // Create opaque graphics pipeline
                    Pipeline opaquePipeline = factory.CreateGraphicsPipeline(opaquePipelineDescription);
                    // Declare transparent pipeline variable
                    Pipeline transparentPipeline;
                    // Guard transparent pipeline creation
                    try
                    // Begin pipeline creation guard
                    {
                        // Create transparent graphics pipeline
                        transparentPipeline = factory.CreateGraphicsPipeline(transparentPipelineDescription);
                    // End pipeline creation guard
                    }
                    // Handle transparent pipeline failure
                    catch
                    // Begin pipeline cleanup handler
                    {
                        // Dispose opaque pipeline on failure
                        try { opaquePipeline.Dispose(); } catch { }
                        // Rethrow pipeline creation error
                        throw;
                    // End pipeline cleanup handler
                    }

                    // Declare wireframe pipeline variables
                    Pipeline depthPrepassPipeline;
                    // Declare solid wireframe pipeline
                    Pipeline wireframePipeline;
                    // Declare dashed wireframe pipeline
                    Pipeline wireframeDashedPipeline;
                    // Guard wireframe pipeline creation
                    try
                    // Begin wireframe creation guard
                    {
                        // Create depth prepass pipeline
                        depthPrepassPipeline = factory.CreateGraphicsPipeline(depthPrepassDescription);
                        // Create solid wireframe pipeline
                        wireframePipeline = factory.CreateGraphicsPipeline(wireframePipelineDescription);
                        // Create dashed wireframe pipeline
                        wireframeDashedPipeline = factory.CreateGraphicsPipeline(dashedPipelineDescription);
                    // End wireframe creation guard
                    }
                    // Handle wireframe creation failure
                    catch
                    // Begin wireframe cleanup handler
                    {
                        // Dispose opaque pipeline on failure
                        try { opaquePipeline.Dispose(); } catch { }
                        // Dispose transparent pipeline on failure
                        try { transparentPipeline.Dispose(); } catch { }
                        // Rethrow pipeline creation error
                        throw;
                    // End wireframe cleanup handler
                    }

                    // Assign new shared raster resources
                    sharedRasterResources = new SharedRasterResources
                    // Begin shared resources block
                    {
                        // Store shared resource layout
                        Layout = layout,
                        // Store raster shaders
                        Shaders = shaders,
                        // Store opaque pipeline
                        OpaquePipeline = opaquePipeline,
                        // Store transparent pipeline
                        TransparentPipeline = transparentPipeline,
                        // Store depth prepass pipeline
                        DepthPrepassPipeline = depthPrepassPipeline,
                        // Store solid wireframe pipeline
                        WireframePipeline = wireframePipeline,
                        // Store dashed wireframe pipeline
                        WireframeDashedPipeline = wireframeDashedPipeline
                    // End shared resources initializer
                    };
                    // Log pipeline creation stage
                    Stage("Vulkan raster graphics pipelines created: CPU-raster parity opaque z-buffer path");
                    // Return shared raster resources
                    return sharedRasterResources;
                // End shader setup try block
                }
                // Handle shader setup failure
                catch
                // Begin shader cleanup handler
                {
                    // Loop over created shaders
                    foreach (Shader shader in shaders)
                    // Begin shader disposal loop
                    {
                        // Dispose shader ignoring errors
                        try { shader.Dispose(); } catch { }
                    // End shader disposal loop
                    }
                    // Rethrow shader setup error
                    throw;
                // End shader cleanup handler
                }
            // End resource try block
            }
            // Handle resource creation failure
            catch
            // Begin layout cleanup handler
            {
                // Dispose resource layout
                layout.Dispose();
                // Rethrow resource creation error
                throw;
            // End layout cleanup handler
            }
        // End device lock block
        }
    // End GetOrCreateSharedRasterResources method
    }

    /// <summary>Creates the shared raster vertex shader from the embedded source.</summary>
    // Declare raster vertex shader factory
    private static Shader CreateRasterVertexShader(ResourceFactory factory)
    // Begin CreateRasterVertexShader method
    {
        // Begin vertex shader description
        ShaderDescription vertex = new(
            // Select vertex shader stage
            ShaderStages.Vertex,
            // Encode vertex source as UTF8
            Encoding.UTF8.GetBytes(VertexShaderSource),
            // Set vertex entry point
            "main");
        // Guard vertex shader creation
        try
        // Begin vertex creation guard
        {
            // Log vertex compile stage
            Stage("Compile/create Vulkan raster vertex shader");
            // Create vertex shader from SPIRV
            return factory.CreateFromSpirv(vertex);
        // End vertex creation guard
        }
        // Handle vertex compile failure
        catch (Exception ex)
        // Begin vertex failure handler
        {
            // Log vertex compile failure
            Stage("Vulkan raster vertex shader compile/create failed");
            // Log vertex exception details
            Stage(ex.ToString());
            // Throw vertex compilation failure
            throw new InvalidOperationException("Vulkan raster vertex shader compilation failed. See " + StageLogPath + ".", ex);
        // End vertex failure handler
        }
    // End CreateRasterVertexShader method
    }

    /// <summary>Creates a raster fragment shader variant, optionally with a compile define.</summary>
    // Declare raster fragment shader factory
    private static Shader CreateRasterFragmentShader(ResourceFactory factory, string? define)
    // Begin CreateRasterFragmentShader method
    {
        // Inject compile define after version
        string source = WithFragmentDefine(FragmentShaderSource, define);
        // Begin fragment shader description
        ShaderDescription fragment = new(
            // Select fragment shader stage
            ShaderStages.Fragment,
            // Encode fragment source as UTF8
            Encoding.UTF8.GetBytes(source),
            // Set fragment entry point
            "main");
        // Guard fragment shader creation
        try
        // Begin fragment creation guard
        {
            // Log fragment compile stage
            Stage("Compile/create Vulkan raster fragment shader " + (define ?? "default"));
            // Create fragment shader from SPIRV
            return factory.CreateFromSpirv(fragment);
        // End fragment creation guard
        }
        // Handle fragment compile failure
        catch (Exception ex)
        // Begin fragment failure handler
        {
            // Log fragment compile failure
            Stage("Vulkan raster fragment shader compile/create failed");
            // Log fragment exception details
            Stage(ex.ToString());
            // Guard debug fragment dump
            try
            // Begin fragment dump guard
            {
                // Dump failed fragment shader source
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-failed.frag"), source);
            // End fragment dump guard
            }
            // Ignore fragment dump errors
            catch { }
            // Throw fragment compilation failure
            throw new InvalidOperationException("Vulkan raster fragment shader compilation failed. See " + StageLogPath + " and %TEMP%\\LightingShowcase-vulkan-raster-failed.frag.", ex);
        // End fragment failure handler
        }
    // End CreateRasterFragmentShader method
    }

    /// <summary>Inserts a compile define after the GLSL version line.</summary>
    // Declare shader define injector
    private static string WithFragmentDefine(string source, string? define)
    // Begin WithFragmentDefine method
    {
        // Return source unchanged without define
        if (string.IsNullOrEmpty(define))
            // Pass source through unchanged
            return source;
        // Scan source lines for version directive
        int lineStart = 0;
        // Walk lines until version found
        while (lineStart < source.Length)
        // Begin version search loop
        {
            // Find end of current line
            int newline = source.IndexOf('\n', lineStart);
            // Stop scanning at missing newline
            if (newline < 0)
                // Abort search at end of source
                break;
            // Read current source line
            string line = source.Substring(lineStart, newline - lineStart);
            // Insert after version directive
            if (line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
                // Inject define after version line
                return source.Insert(newline + 1, "#define " + define + " 1\n");
            // Advance past current line
            lineStart = newline + 1;
        // End version search loop
        }
        // Fail loudly when version missing
        throw new InvalidOperationException("Cannot inject '" + define + "': no #version line in fragment shader source.");
    // End WithFragmentDefine method
    }

    /// <summary>Counts opaque and transparent triangles and assigns material ids with texture placements.</summary>
    // Declare geometry metadata builder method
    private static RasterGeometryBuildResult BuildGeometryMetadata(
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
    private static double TriangleViewDepth(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
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
    private static bool IsRenderableTriangle(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
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
    private static bool IsFinite(Vec3 value) =>
        // Test all components for finiteness
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>Returns true when all three triangle corners are beyond the near plane.</summary>
    // Declare near plane test method
    private static bool IsTriangleInFrontOfCamera(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
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
    private static double CameraDepth(Vec3 point, Vec3 cameraPosition, CameraBasis basis)
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
    private static Vector4 MaterialColorAlpha(Material material)
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
    private static Vector4 BakedMaterialColorAlpha(Material material, Vec2 uv)
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
    private static Vector4 MaterialEmission(Material material)
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
    private static Vector4 BakedMaterialEmission(Material material, Vec2 uv)
    // Begin BakedMaterialEmission method
    {
        // Return unbaked material emission
        return MaterialEmission(material);
    // End BakedMaterialEmission method
    }

    /// <summary>Returns true when a material uses alpha blend mode.</summary>
    // Declare transparency test method
    private static bool IsTransparentMaterial(Material material)
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
    private static double EffectiveMaterialAlpha(Material material)
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
    private static RasterLight[] BuildLightBuffer(Scene scene)
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
    private static double ComputeCameraFarPlane(Scene scene, Vec3 cameraPosition, CameraBasis basis)
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

    /// <summary>Packs scene textures into a GPU texture array and computes their placements.</summary>
    // Declare texture atlas builder method
    private static TextureBuildResult BuildTextureAtlas(GraphicsDevice gd, Scene scene, bool gpuTextureSamplingRequested)
    // Begin BuildTextureAtlas method
    {
        // Test for baked texture path
        if (!gpuTextureSamplingRequested)
            // Return baked texture result
            return CreateBakedTextureResult(null);

        // Create atlas texture list
        List<TextureMap> textures = new();
        // Create deduplication set
        HashSet<TextureMap> seen = new();
        // Loop over scene triangles
        foreach (Triangle triangle in scene.Triangles)
        // Begin texture collection loop
        {
            // Collect base color texture
            AddTexture(triangle.Material.Texture);
            // Collect metallic roughness texture
            AddTexture(triangle.Material.MetallicRoughnessTexture);
            // Collect normal texture
            AddTexture(triangle.Material.NormalTexture);
            // Collect emissive texture
            AddTexture(triangle.Material.EmissiveTexture);
            // Collect occlusion texture
            AddTexture(triangle.Material.OcclusionTexture);
            // Collect transmission texture
            AddTexture(triangle.Material.TransmissionTexture);
        // End texture collection loop
        }

        /// <summary>Adds a texture to the atlas list unless it is null or already seen.</summary>
        // Declare local texture collector
        void AddTexture(TextureMap? texture)
        // Begin AddTexture local function
        {
            // Deduplicate added texture
            if (texture != null && seen.Add(texture))
                // Append unseen texture
                textures.Add(texture);
        // End AddTexture local function
        }

        // Test for empty texture set
        if (textures.Count == 0)
        // Begin empty atlas handler
        {
            // Return new empty atlas result
            return new TextureBuildResult
            // Begin empty result initializer
            {
                // Store empty placement map
                TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
                // Store empty upload list
                Uploads = Array.Empty<RasterTextureUpload>(),
                // Set unit atlas width
                Width = 1,
                // Set unit atlas height
                Height = 1,
                // Set zero texture count
                TextureCount = 0,
                // Set single atlas page
                PageCount = 1,
                // Flag GPU sampling enabled
                UsesGpuTextureSampling = true
            // End empty atlas result
            };
        // End empty atlas handler
        }

        // Define atlas border padding
        const int padding = 1;
        // Define RGBA8 atlas format
        const Veldrid.PixelFormat atlasFormat = Veldrid.PixelFormat.R8_G8_B8_A8_UNorm;
        // Query RGBA8 array support
        if (!gd.GetPixelFormatSupport(atlasFormat, TextureType.Texture2D, TextureUsage.Sampled, out PixelFormatProperties support))
            // Reject unsupported texture arrays
            throw new InvalidOperationException("The Vulkan device does not support sampled RGBA8 texture arrays.");

        // Clamp maximum page width
        int maxPageWidth = checked((int)Math.Min((uint)int.MaxValue, support.MaxWidth));
        // Clamp maximum page height
        int maxPageHeight = checked((int)Math.Min((uint)int.MaxValue, support.MaxHeight));
        // Clamp maximum page count
        int maxPageCount = checked((int)Math.Min((uint)int.MaxValue, support.MaxArrayLayers));
        // Validate reported texture limits
        if (maxPageWidth <= 0 || maxPageHeight <= 0 || maxPageCount <= 0)
            // Reject invalid texture limits
            throw new InvalidOperationException("The Vulkan device reported invalid sampled-texture array limits.");

        // Begin largest first texture sort
        List<TextureMap> sorted = textures
            // Sort by largest side first
            .OrderByDescending(texture => Math.Max(texture.Width, texture.Height))
            // Break ties by pixel area
            .ThenByDescending(texture => checked((long)Math.Max(1, texture.Width) * Math.Max(1, texture.Height)))
            // Materialize sorted texture list
            .ToList();

        // Initialize total texel area
        long totalArea = 0;
        // Initialize widest entry tracker
        int largestEntryWidth = 1;
        // Initialize tallest entry tracker
        int largestEntryHeight = 1;
        // Loop over sorted textures
        foreach (TextureMap texture in sorted)
        // Begin atlas measurement loop
        {
            // Compute padded entry width
            int entryWidth = checked(Math.Max(1, texture.Width) + padding * 2);
            // Compute padded entry height
            int entryHeight = checked(Math.Max(1, texture.Height) + padding * 2);
            // Test entry against page limits
            if (entryWidth > maxPageWidth || entryHeight > maxPageHeight)
            // Begin oversize texture handler
            {
                // Throw oversize texture error
                throw new InvalidOperationException(
                    // Name offending texture size
                    $"Texture '{texture.Name}' is {texture.Width}x{texture.Height}; with filtering borders it exceeds " +
                    // Name device texture limit
                    $"this Vulkan device's {maxPageWidth}x{maxPageHeight} sampled-texture limit.");
            // End oversize texture handler
            }

            // Accumulate padded texel area
            totalArea = checked(totalArea + checked((long)entryWidth * entryHeight));
            // Track widest atlas entry
            largestEntryWidth = Math.Max(largestEntryWidth, entryWidth);
            // Track tallest atlas entry
            largestEntryHeight = Math.Max(largestEntryHeight, entryHeight);
        // End atlas measurement loop
        }

        // Build width candidates
        List<int> candidateWidths = BuildDimensionCandidates(largestEntryWidth, maxPageWidth, totalArea);
        // Build height candidates
        List<int> candidateHeights = BuildDimensionCandidates(largestEntryHeight, maxPageHeight, totalArea);

        // Initialize best packing result
        List<RasterTextureUpload>? bestUploads = null;
        // Initialize best width
        int bestWidth = 0;
        // Initialize best height
        int bestHeight = 0;
        // Initialize best page count
        int bestPageCount = 0;
        // Initialize best allocation score
        long bestAllocatedPixels = long.MaxValue;
        // Initialize best page tiebreaker
        long bestLargestPage = long.MaxValue;

        // Loop over width candidates
        foreach (int candidateWidth in candidateWidths)
        // Begin width search loop
        {
            // Loop over height candidates
            foreach (int candidateHeight in candidateHeights)
            // Begin height search loop
            {
                // Attempt row packing at size
                if (!TryPackPages(candidateWidth, candidateHeight, out List<RasterTextureUpload> uploads, out int pageCount))
                    // Skip unpackable dimensions
                    continue;

                // Compute single page pixels
                long pagePixels = checked((long)candidateWidth * candidateHeight);
                // Compute total allocated pixels
                long allocatedPixels = checked(pagePixels * pageCount);
                // Begin worse packing test
                if (allocatedPixels > bestAllocatedPixels ||
                    // Reject ties with larger pages
                    (allocatedPixels == bestAllocatedPixels && pagePixels >= bestLargestPage))
                // Begin worse packing handler
                {
                    // Keep current best packing
                    continue;
                // End worse packing handler
                }

                // Record best allocation score
                bestAllocatedPixels = allocatedPixels;
                // Record best page size
                bestLargestPage = pagePixels;
                // Record best width
                bestWidth = candidateWidth;
                // Record best height
                bestHeight = candidateHeight;
                // Record best page count
                bestPageCount = pageCount;
                // Record best packing uploads
                bestUploads = uploads;
            // End height search loop
            }
        // End width search loop
        }

        // Test for packing failure
        if (bestUploads == null)
        // Begin packing failure handler
        {
            // Throw unpackable textures error
            throw new InvalidOperationException(
                // Name unpackable texture count
                $"The scene's {textures.Count} textures cannot be packed into the Vulkan device's " +
                // Name device array limits
                $"{maxPageWidth}x{maxPageHeight}, {maxPageCount}-layer sampled-texture array limits.");
        // End packing failure handler
        }

        // Create placement map
        Dictionary<TextureMap, RasterTexturePlacement> texturePlacements = new(bestUploads.Count);
        // Loop over packed uploads
        foreach (RasterTextureUpload upload in bestUploads)
        // Begin placement computation loop
        {
            // Read uploaded texture reference
            TextureMap texture = upload.Texture;
            // Compute normalized X offset
            float offsetX = (upload.X + 0.5f) / bestWidth;
            // Compute normalized Y offset
            float offsetY = (upload.Y + 0.5f) / bestHeight;
            // Compute normalized X scale
            float scaleX = texture.Width <= 1 ? 0.0f : (texture.Width - 1.0f) / bestWidth;
            // Compute normalized Y scale
            float scaleY = texture.Height <= 1 ? 0.0f : (texture.Height - 1.0f) / bestHeight;
            // Assign new texture placement record
            texturePlacements[texture] = new RasterTexturePlacement(
                // Pack offset scale page placement
                offsetX, offsetY, scaleX, scaleY, upload.Page, texture);
        // End placement computation loop
        }

        // Log texture atlas summary
        Stage(
            // Log packed texture count
            $"Vulkan raster texture pages: {textures.Count} textures -> " +
            // Log atlas page dimensions
            $"{bestPageCount} layer(s) of {bestWidth}x{bestHeight}; " +
            // Log atlas memory size
            $"allocated={bestAllocatedPixels * 4.0 / (1024.0 * 1024.0):F1} MB");

        // Return new atlas build result
        return new TextureBuildResult
        // Begin result initializer block
        {
            // Store texture placement map
            TexturePlacements = texturePlacements,
            // Store texture upload list
            Uploads = bestUploads,
            // Store atlas width
            Width = bestWidth,
            // Store atlas height
            Height = bestHeight,
            // Store texture count
            TextureCount = texturePlacements.Count,
            // Store page count
            PageCount = bestPageCount,
            // Flag GPU sampling enabled
            UsesGpuTextureSampling = true
        // End atlas result initializer
        };

        /// <summary>Builds candidate atlas dimensions from minimum, square estimate, and power-of-two sizes.</summary>
        // Declare dimension candidate builder
        List<int> BuildDimensionCandidates(int minimum, int maximum, long area)
        // Begin BuildDimensionCandidates function
        {
            // Create candidate dimension set
            SortedSet<int> candidates = new();
            // Seed minimum dimension candidate
            AddCandidate(minimum);
            // Seed rounded minimum candidate
            AddCandidate(NextPowerOfTwo(minimum));

            // Estimate square root dimension
            int squareEstimate = checked((int)Math.Min(int.MaxValue, Math.Ceiling(Math.Sqrt(Math.Max(1L, area)))));
            // Seed square estimate candidate
            AddCandidate(squareEstimate);
            // Seed rounded square candidate
            AddCandidate(NextPowerOfTwo(squareEstimate));

            // Start power of two walk
            long power = NextPowerOfTwo(minimum);
            // Loop over powers of two
            while (power > 0 && power <= maximum)
            // Begin power walk loop
            {
                // Seed power of two candidate
                AddCandidate((int)power);
                // Test for doubling overflow
                if (power > int.MaxValue / 2)
                    // Stop before integer overflow
                    break;
                // Double candidate dimension
                power *= 2;
            // End power walk loop
            }

            // The physical device limit is included as a last-resort candidate;
            // the score still chooses the smallest total allocation that fits.
            // Seed device limit candidate
            AddCandidate(maximum);
            // Return sorted candidate list
            return candidates.ToList();

            /// <summary>Adds a clamped dimension candidate to the set.</summary>
            // Declare local candidate clamp
            void AddCandidate(int value)
            // Begin AddCandidate local function
            {
                // Test below minimum bound
                if (value < minimum)
                    // Clamp candidate to minimum
                    value = minimum;
                // Test above maximum bound
                if (value > maximum)
                    // Clamp candidate to maximum
                    value = maximum;
                // Insert clamped candidate value
                candidates.Add(value);
            // End AddCandidate local function
            }
        // End BuildDimensionCandidates function
        }

        /// <summary>Packs textures into atlas pages with a row-based layout, returning false when they overflow.</summary>
        // Declare row packing helper
        bool TryPackPages(int pageWidth, int pageHeight, out List<RasterTextureUpload> uploads, out int pageCount)
        // Begin TryPackPages function
        {
            // Create upload list
            uploads = new List<RasterTextureUpload>(sorted.Count);
            // Start on first page
            int page = 0;
            // Reset horizontal cursor
            int cursorX = 0;
            // Reset vertical cursor
            int cursorY = 0;
            // Reset current row height
            int rowHeight = 0;

            // Pack each sorted texture into atlas
            foreach (TextureMap texture in sorted)
            // Begin per-texture packing loop
            {
                // Compute padded entry width
                int entryWidth = checked(Math.Max(1, texture.Width) + padding * 2);
                // Compute padded entry height
                int entryHeight = checked(Math.Max(1, texture.Height) + padding * 2);

                // Wrap row when entry overflows width
                if (cursorX > 0 && cursorX + entryWidth > pageWidth)
                // Begin row wrap block
                {
                    // Advance cursor past finished row
                    cursorY = checked(cursorY + rowHeight);
                    // Reset horizontal cursor to origin
                    cursorX = 0;
                    // Reset current row height
                    rowHeight = 0;
                // End row wrap block
                }

                // Start new page when full
                if (cursorY + entryHeight > pageHeight)
                // Begin page overflow block
                {
                    // Advance to next atlas page
                    page++;
                    // Fail when exceeding page limit
                    if (page >= maxPageCount)
                    // Begin overflow failure block
                    {
                        // Clear page count on failure
                        pageCount = 0;
                        // Discard partial upload list
                        uploads.Clear();
                        // Report packing failure to caller
                        return false;
                    // End overflow failure block
                    }
                    // Reset horizontal cursor for page
                    cursorX = 0;
                    // Reset vertical cursor for page
                    cursorY = 0;
                    // Reset row height for page
                    rowHeight = 0;
                // End page overflow block
                }

                // Record upload placement for texture
                uploads.Add(new RasterTextureUpload
                // Begin upload initializer block
                {
                    // Store source texture reference
                    Texture = texture,
                    // Store padded horizontal offset
                    X = cursorX + padding,
                    // Store padded vertical offset
                    Y = cursorY + padding,
                    // Store destination page index
                    Page = page
                // End upload initializer block
                });
                // Advance cursor past placed entry
                cursorX = checked(cursorX + entryWidth);
                // Track tallest entry in row
                rowHeight = Math.Max(rowHeight, entryHeight);
            // End per-texture packing loop
            }

            // Count used pages from index
            pageCount = page + 1;
            // Report whether pages fit limit
            return pageCount <= maxPageCount;
        // End page packing helper
        }

        /// <summary>Returns the smallest power of two greater than or equal to the value.</summary>
        // Declare power of two helper
        static int NextPowerOfTwo(int value)
        // Begin power of two block
        {
            // Clamp tiny values to one
            if (value <= 1)
                // Return minimum power of two
                return 1;
            // Seed bit spread below value
            uint v = checked((uint)value - 1u);
            // Spread highest set bit downward
            v |= v >> 1;
            // Spread bits across nibble
            v |= v >> 2;
            // Spread bits across byte
            v |= v >> 4;
            // Spread bits across half word
            v |= v >> 8;
            // Spread bits across full word
            v |= v >> 16;
            // Round up with overflow clamp
            return v >= 0x7fffffffu ? int.MaxValue : checked((int)(v + 1u));
        // End power of two block
        }
    // End texture atlas helper scope
    }

    /// <summary>Creates an empty texture result that falls back to base-color sampling.</summary>
    // Declare fallback texture result factory
    private static TextureBuildResult CreateBakedTextureResult(string? reason) => new()
    // Begin fallback result initializer
    {
        // Initialize empty texture placement map
        TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
        // Initialize empty upload list
        Uploads = Array.Empty<RasterTextureUpload>(),
        // Set fallback atlas width
        Width = 1,
        // Set fallback atlas height
        Height = 1,
        // Report zero baked textures
        TextureCount = 0,
        // Keep single fallback page
        PageCount = 1,
        // Disable GPU texture sampling flag
        UsesGpuTextureSampling = false,
        // Record fallback reason string
        FallbackReason = reason
    // End fallback result initializer
    };

    /// <summary>Returns the atlas placement for a texture, or none when absent.</summary>
    // Declare texture placement lookup helper
    private static RasterTexturePlacement TexturePlacement(IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, TextureMap? texture)
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

    /// <summary>Reads staging pixels into a render image, forcing alpha to opaque.</summary>
    // Declare staging readback helper
    private static unsafe RenderImage ReadBackImage(GraphicsDevice gd, Texture stagingTexture, int width, int height)
    // Begin readback block
    {
        // Allocate pixel output buffer
        uint[] pixels = new uint[checked(width * height)];
        // Map staging texture for reading
        MappedResource mapped = gd.Map(stagingTexture, MapMode.Read);
        // Begin mapped read guard
        try
        // Begin pinned copy block
        {
            // Pin pixel buffer destination
            fixed (uint* destinationBase = pixels)
            // Begin row copy scope
            {
                // Compute bytes per pixel row
                nuint rowBytes = checked((nuint)(width * sizeof(uint)));
                // Copy each image row
                for (int y = 0; y < height; y++)
                // Begin per-row copy block
                {
                    // Locate pitched source row pointer
                    byte* source = (byte*)mapped.Data + checked((nint)(y * mapped.RowPitch));
                    // Locate destination row pointer
                    uint* destination = destinationBase + y * width;
                    // Copy one row of pixels
                    Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                    // Force every pixel fully opaque
                    for (int x = 0; x < width; x++)
                        // Set alpha channel to opaque
                        destination[x] |= 0xff000000u;
                // End per-row copy block
                }
            // End row copy scope
            }
        // End pinned copy block
        }
        // Ensure staging texture unmapped
        finally
        // Begin unmap cleanup block
        {
            // Release staging texture mapping
            gd.Unmap(stagingTexture);
        // End unmap cleanup block
        }
        // Return completed render image
        return new RenderImage(width, height, pixels);
    // End readback block
    }

    // Detect stage logging enablement flag
    private static bool StageLoggingEnabled => IsVulkanDebugEnabled() || string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_STAGE_LOG"), "1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resets the raster stage log with the current resolution when logging is enabled.</summary>
    // Declare stage log reset helper
    private static void ResetStageLog(int width, int height)
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
    private static void Stage(string name)
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
    private static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
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
    private static bool IsVulkanDebugEnabled()
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
    private static string DebugModeName(int mode) => mode switch
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
    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
    /// <summary>Converts a Vec3 and w component to a single-precision Vector4.</summary>
    // Convert Vec3 plus w component
    private static Vector4 ToVector4(Vec3 value, float w) => new((float)value.X, (float)value.Y, (float)value.Z, w);

    // Begin embedded vertex shader string
    private const string VertexShaderSource = @"
#version 450

// Declare vertex position input
layout(location = 0) in vec3 Position;
// Declare vertex normal input
layout(location = 1) in vec3 Normal;
// Declare vertex UV input
layout(location = 2) in vec2 Uv;
// Declare material index input
layout(location = 3) in float MaterialIndex;

// Output world position to fragment
layout(location = 0) out vec3 fsWorldPos;
// Output world normal to fragment
layout(location = 1) out vec3 fsNormal;
// Output UV to fragment stage
layout(location = 2) out vec2 fsUv;
// Output flat material index
layout(location = 3) flat out int fsMaterialIndex;

// Declare camera uniform block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin camera uniform block
{
    // Store camera world position
    vec4 CameraPosition;
    // Store camera forward vector
    vec4 CameraForward;
    // Store camera right vector
    vec4 CameraRight;
    // Store camera up vector
    vec4 CameraUp;
    // Store projection parameters
    vec4 Projection;
    // Store light and material counts
    vec4 Counts;
// End camera uniform block
} Camera;

// Declare transform uniform block
layout(std140, set = 0, binding = 1) uniform TransformConstants
// Begin transform uniform block
{
    // Store pivot and enable flag
    vec4 PivotEnabled;
    // Store translation offset
    vec4 Translation;
    // Store Euler rotation angles
    vec4 Rotation;
    // Store scale factors
    vec4 Scale;
// End transform uniform block
} Transform;

// Declare scale sanitizer function
float sanitizeScale(float value)
// Begin scale sanitizer block
{
    // Replace near zero scale
    return abs(value) < 0.00000001 ? 1.0 : value;
// End scale sanitizer block
}

// Declare Euler rotation helper
vec3 rotateEuler(vec3 value, vec3 rotation)
// Begin Euler rotation block
{
    // Compute X axis rotation factors
    float cx = cos(rotation.x); float sx = sin(rotation.x);
    // Apply rotation about X
    value = vec3(value.x, value.y * cx - value.z * sx, value.y * sx + value.z * cx);
    // Compute Y axis rotation factors
    float cy = cos(rotation.y); float sy = sin(rotation.y);
    // Apply rotation about Y
    value = vec3(value.x * cy + value.z * sy, value.y, -value.x * sy + value.z * cy);
    // Compute Z axis rotation factors
    float cz = cos(rotation.z); float sz = sin(rotation.z);
    // Apply rotation about Z
    return vec3(value.x * cz - value.y * sz, value.x * sz + value.y * cz, value.z);
// End Euler rotation block
}

// Declare vertex shader entry
void main()
// Begin vertex main block
{
    // Initialize world position from input
    vec3 worldPosition = Position;
    // Initialize world normal from input
    vec3 worldNormal = Normal;
    // Apply pivot transform when enabled
    if (Transform.PivotEnabled.w > 0.5)
    // Begin pivot transform block
    {
        // Start sanitized scale vector
        vec3 safeScale = vec3(
            // Sanitize X scale component
            sanitizeScale(Transform.Scale.x),
            // Sanitize Y scale component
            sanitizeScale(Transform.Scale.y),
            // Sanitize Z scale component
            sanitizeScale(Transform.Scale.z));
        // Scale position around pivot point
        vec3 local = (Position - Transform.PivotEnabled.xyz) * safeScale;
        // Rotate and translate pivot position
        worldPosition = Transform.PivotEnabled.xyz + Transform.Translation.xyz + rotateEuler(local, Transform.Rotation.xyz);
        // Rotate normal with inverse scale
        worldNormal = normalize(rotateEuler(Normal / safeScale, Transform.Rotation.xyz));
    // End pivot transform block
    }

    // Compute position relative to camera
    vec3 rel = worldPosition - Camera.CameraPosition.xyz;
    // Project onto camera right axis
    float vx = dot(rel, Camera.CameraRight.xyz);
    // Project onto camera up axis
    float vy = dot(rel, Camera.CameraUp.xyz);
    // Project onto camera forward axis
    float vz = dot(rel, Camera.CameraForward.xyz);

    // Clamp aspect ratio minimum
    float aspect = max(Camera.Projection.x, 0.0001);
    // Clamp field of view factor
    float tanHalfFov = max(Camera.Projection.y, 0.0001);
    // Clamp near plane minimum
    float nearPlane = max(Camera.Projection.z, 0.0001);
    // Ensure far plane beyond near
    float farPlane = max(Camera.Projection.w, nearPlane + 1.0);
    // Use forward depth as w
    float z = vz;
    // Normalize depth between planes
    float ndcDepth = (z - nearPlane) / (farPlane - nearPlane);

    // Start clip position constructor
    gl_Position = vec4(
        // Compute horizontal clip coordinate
        -vx / (aspect * tanHalfFov),
         // Compute vertical clip coordinate
         vy / tanHalfFov,
         // Scale depth by view distance
         ndcDepth * z,
         // Store view depth as w
         z);
    // Flip Y for Vulkan coordinates
    gl_Position.y = -gl_Position.y;

    // Pass world position onward
    fsWorldPos = worldPosition;
    // Pass world normal onward
    fsNormal = worldNormal;
    // Pass texture coordinates onward
    fsUv = Uv;
    // Pass rounded material index onward
    fsMaterialIndex = int(MaterialIndex + 0.5);
// End vertex main block
}
// End embedded vertex shader string
";

    // Begin embedded fragment shader string
    private const string FragmentShaderSource = @"
#version 450

// Define PI constant for BRDF
const float PI = 3.14159265358979323846;

// Declare raster light structure
struct RasterLight
// Begin light structure block
{
    // Store light position and kind
    vec4 PositionKind;
    // Store light direction and range
    vec4 DirectionRange;
    // Store light color and intensity
    vec4 ColorIntensity;
    // Store cone and enable flags
    vec4 ConeEnabled;
// End light structure block
};

// Declare raster material structure
struct RasterMaterial
// Begin material structure block
{
    // Store base color and alpha
    vec4 BaseColorAlpha;
    // Store emissive factor
    vec4 EmissiveFactor;
    // Store metallic roughness factors
    vec4 PbrFactors;
    // Store alpha test flags
    vec4 AlphaFlags;
    // Store clearcoat and optical factors
    vec4 OpticalFactors;
    // Store attenuation color and distance
    vec4 AttenuationColorDistance;
    // Store primary texture presence flags
    vec4 TextureFlags;
    // Store extra texture presence flags
    vec4 TextureFlags2;

    // Store base atlas transform
    vec4 BaseAtlasTransform;
    // Store base texture addressing
    vec4 BaseTextureAddress;
    // Store base UV transform
    vec4 BaseTextureTransform;

    // Store metallic roughness atlas transform
    vec4 MrAtlasTransform;
    // Store metallic roughness addressing
    vec4 MrTextureAddress;
    // Store metallic roughness UV transform
    vec4 MrTextureTransform;

    // Store normal atlas transform
    vec4 NormalAtlasTransform;
    // Store normal texture addressing
    vec4 NormalTextureAddress;
    // Store normal UV transform
    vec4 NormalTextureTransform;

    // Store emissive atlas transform
    vec4 EmissiveAtlasTransform;
    // Store emissive texture addressing
    vec4 EmissiveTextureAddress;
    // Store emissive UV transform
    vec4 EmissiveTextureTransform;

    // Store occlusion atlas transform
    vec4 OcclusionAtlasTransform;
    // Store occlusion texture addressing
    vec4 OcclusionTextureAddress;
    // Store occlusion UV transform
    vec4 OcclusionTextureTransform;

    // Store transmission atlas transform
    vec4 TransmissionAtlasTransform;
    // Store transmission texture addressing
    vec4 TransmissionTextureAddress;
    // Store transmission UV transform
    vec4 TransmissionTextureTransform;
// End material structure block
};

// Receive interpolated world position
layout(location = 0) in vec3 fsWorldPos;
// Receive interpolated world normal
layout(location = 1) in vec3 fsNormal;
// Receive interpolated texture coordinates
layout(location = 2) in vec2 fsUv;
// Receive flat material index
layout(location = 3) flat in int fsMaterialIndex;

// Declare fragment color output
layout(location = 0) out vec4 outColor;

// Declare fragment camera block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin fragment camera block
{
    // Store camera world position
    vec4 CameraPosition;
    // Store camera forward vector
    vec4 CameraForward;
    // Store camera right vector
    vec4 CameraRight;
    // Store camera up vector
    vec4 CameraUp;
    // Store projection parameters
    vec4 Projection;
    // Store light and material counts
    vec4 Counts;
// End fragment camera block
} Camera;

// Declare light storage buffer
layout(std430, set = 0, binding = 2) readonly buffer LightBuffer
// Begin light buffer block
{
    // Store variable light array
    RasterLight Lights[];
// End light buffer block
};

// Declare material storage buffer
layout(std430, set = 0, binding = 3) readonly buffer MaterialBuffer
// Begin material buffer block
{
    // Store variable material array
    RasterMaterial Materials[];
// End material buffer block
};

// Declare atlas texture array
layout(set = 0, binding = 4) uniform texture2DArray AtlasTexture;
// Declare atlas texture sampler
layout(set = 0, binding = 5) uniform sampler AtlasSampler;

// Declare texture addressing helper
float addressTexture(float v, float mode)
// Begin addressing helper block
{
    // Detect clamp addressing mode
    if (mode > 0.5 && mode < 1.5)
        // Clamp coordinate into range
        return clamp(v, 0.0, 1.0);
    // Detect mirrored repeat mode
    if (mode > 1.5)
    // Begin mirror addressing block
    {
        // Find integer repeat count
        float floorValue = floor(v);
        // Find fractional local coordinate
        float local = fract(v);
        // Mirror alternate repeats
        return mod(floorValue, 2.0) == 0.0 ? local : 1.0 - local;
    // End mirror addressing block
    }
    // Pass through in range coordinates
    if (v >= 0.0 && v <= 1.0)
        // Return already valid coordinate
        return v;
    // Wrap coordinate with fraction
    return fract(v);
// End addressing helper block
}

// Declare UV transform helper
vec2 addressUv(vec2 uv, vec4 textureAddress, vec4 textureTransform)
// Begin UV transform block
{
    // Scale UV by transform factor
    vec2 transformed = uv * textureTransform.zw;
    // Read texture rotation angle
    float rotation = textureAddress.z;
    // Skip rotation when nearly zero
    if (abs(rotation) > 0.000001)
    // Begin UV rotation block
    {
        // Compute rotation cosine factor
        float c = cos(rotation);
        // Compute rotation sine factor
        float s = sin(rotation);
        // Rotate scaled UV coordinates
        transformed = vec2(transformed.x * c - transformed.y * s, transformed.x * s + transformed.y * c);
    // End UV rotation block
    }
    // Add UV offset translation
    transformed += textureTransform.xy;
    // Address both transformed axes
    return vec2(addressTexture(transformed.x, textureAddress.x), addressTexture(transformed.y, textureAddress.y));
// End UV transform block
}

// Declare atlas sampling helper
vec4 sampleAtlasTexture(vec2 uv, vec4 atlasTransform, vec4 textureAddress, vec4 textureTransform)
// Begin atlas sampling block
{
    // Resolve addressed texture coordinates
    vec2 addressed = addressUv(uv, textureAddress, textureTransform);
    // Map coordinates into atlas region
    vec2 atlasUv = atlasTransform.xy + addressed * atlasTransform.zw;
    // Sample atlas array layer
    return texture(sampler2DArray(AtlasTexture, AtlasSampler), vec3(atlasUv, textureAddress.w));
// End atlas sampling block
}

// Declare sRGB to linear helper
float srgbChannelToLinear(float value)
// Begin sRGB conversion block
{
    // Convert one channel to linear
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
// End sRGB conversion block
}

// Declare vector sRGB converter
vec3 srgbToLinear(vec3 value)
// Begin vector sRGB block
{
    // Start linear color constructor
    return vec3(
        // Convert red channel value
        srgbChannelToLinear(value.r),
        // Convert green channel value
        srgbChannelToLinear(value.g),
        // Convert blue channel value
        srgbChannelToLinear(value.b));
// End vector sRGB block
}

// Declare linear to sRGB helper
float linearChannelToSrgb(float value)
// Begin linear conversion block
{
    // Clamp negative linear values
    value = max(value, 0.0);
    // Convert one channel to sRGB
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
// End linear conversion block
}

// Declare vector linear converter
vec3 linearToSrgb(vec3 value)
// Begin vector linear block
{
    // Start sRGB color constructor
    return vec3(
        // Convert red channel value
        linearChannelToSrgb(value.r),
        // Convert green channel value
        linearChannelToSrgb(value.g),
        // Convert blue channel value
        linearChannelToSrgb(value.b));
// End vector linear block
}

// Declare neutral tone mapping helper
vec3 pbrNeutralToneMap(vec3 color)
// Begin tone mapping block
{
    // Set highlight compression start
    const float startCompression = 0.76;
    // Set highlight desaturation amount
    const float desaturation = 0.15;
    // Find minimum color channel
    float x = min(color.r, min(color.g, color.b));
    // Compute shadow toe offset
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    // Subtract toe offset value
    color -= offset;

    // Find peak color channel
    float peak = max(color.r, max(color.g, color.b));
    // Skip compression below threshold
    if (peak < startCompression)
        // Return dark colors directly
        return max(color, vec3(0.0));

    // Compute remaining headroom range
    float d = 1.0 - startCompression;
    // Compress peak toward white
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    // Scale color by compressed peak
    color *= newPeak / max(peak, 0.000001);
    // Compute desaturation blend factor
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    // Return desaturated safe color
    return max(mix(color, newPeak * vec3(1.0), g), vec3(0.0));
// End tone mapping block
}

// Declare GGX distribution helper
float distributionGgx(float nDotH, float alphaRoughness)
// Begin GGX distribution block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute denominator shape term
    float f = nDotH * nDotH * (a2 - 1.0) + 1.0;
    // Return normalized facet distribution
    return a2 / max(PI * f * f, 0.000001);
// End GGX distribution block
}

// Declare Smith visibility helper
float visibilitySmithGgxCorrelated(float nDotV, float nDotL, float alphaRoughness)
// Begin Smith visibility block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute view masking term
    float gv = nDotL * sqrt(max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
    // Compute light shadowing term
    float gl = nDotV * sqrt(max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
    // Return correlated visibility factor
    return 0.5 / max(gv + gl, 0.000001);
// End Smith visibility block
}

// Declare Schlick fresnel helper
vec3 fresnelSchlick(float vDotH, vec3 f0)
// Begin fresnel helper block
{
    // Compute fifth power falloff
    float f = pow(1.0 - clamp(vDotH, 0.0, 1.0), 5.0);
    // Blend base reflectance upward
    return f0 + (1.0 - f0) * f;
// End fresnel helper block
}

// Declare cotangent frame helper
mat3 cotangentFrame(vec3 normal, vec3 position, vec2 uv)
// Begin cotangent frame block
{
    // Compute X position derivative
    vec3 dp1 = dFdx(position);
    // Compute Y position derivative
    vec3 dp2 = dFdy(position);
    // Compute X UV derivative
    vec2 duv1 = dFdx(uv);
    // Compute Y UV derivative
    vec2 duv2 = dFdy(uv);
    // Build first perpendicular vector
    vec3 dp2Perp = cross(dp2, normal);
    // Build second perpendicular vector
    vec3 dp1Perp = cross(normal, dp1);
    // Accumulate tangent direction
    vec3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
    // Accumulate bitangent direction
    vec3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
    // Measure worst frame length
    float maxLength = max(dot(tangent, tangent), dot(bitangent, bitangent));
    // Handle degenerate UV mapping
    if (maxLength < 0.00000001)
    // Begin degenerate fallback block
    {
        // Pick axis away from normal
        vec3 axis = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
        // Build orthonormal tangent fallback
        tangent = normalize(cross(axis, normal));
        // Build orthonormal bitangent fallback
        bitangent = cross(normal, tangent);
        // Return fallback tangent frame
        return mat3(tangent, bitangent, normal);
    // End degenerate fallback block
    }
    // Compute inverse frame length
    float inverseLength = inversesqrt(maxLength);
    // Return normalized tangent frame
    return mat3(tangent * inverseLength, bitangent * inverseLength, normal);
// End cotangent frame block
}

// Declare studio environment helper
vec3 studioEnvironment(vec3 direction)
// Begin environment lighting block
{
    // Normalize sample direction vector
    direction = normalize(direction);
    // Blend ground to sky factor
    float skyAmount = smoothstep(-0.25, 0.65, direction.y);
    // Define ground bounce color
    vec3 ground = vec3(0.035, 0.040, 0.050);
    // Define sky ambient color
    vec3 sky = vec3(0.32, 0.39, 0.49);
    // Mix ground and sky radiance
    vec3 radiance = mix(ground, sky, skyAmount);

    // Define key light direction
    vec3 keyDirection = normalize(vec3(-0.55, 0.62, -0.56));
    // Define rim light direction
    vec3 rimDirection = normalize(vec3(0.78, 0.28, 0.56));
    // Compute key highlight lobe
    float key = pow(max(dot(direction, keyDirection), 0.0), 96.0);
    // Compute rim highlight lobe
    float rim = pow(max(dot(direction, rimDirection), 0.0), 180.0);
    // Add key light contribution
    radiance += vec3(5.2, 4.8, 4.2) * key;
    // Add blue rim highlight to environment radiance
    radiance += vec3(2.0, 2.7, 3.8) * rim;
    // Return studio environment radiance
    return radiance;
// End studioEnvironment function
}

// Declare diffuse environment lookup function
vec3 diffuseEnvironment(vec3 normal)
// Begin diffuseEnvironment function
{
    // Blend ground to sky by normal height
    float skyAmount = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    // Return ground sky gradient color
    return mix(vec3(0.055, 0.060, 0.070), vec3(0.42, 0.47, 0.54), skyAmount);
// End diffuseEnvironment function
}

// Declare fragment shader entry point
void main()
// Begin main function
{
    // Mark solid wireframe override block
#ifdef WIREFRAME_SOLID
    // Output flat wireframe line color
    outColor = vec4(0.92, 0.96, 1.0, 1.0);
    // Exit after wireframe output
    return;
#endif
    // Mark dashed wireframe override block
#ifdef WIREFRAME_DASHED
    // Discard dashed gap fragments
    if (mod(gl_FragCoord.x + gl_FragCoord.y * 2.0, 8.0) >= 4.0) discard;
    // Output flat hidden line color
    outColor = vec4(0.42, 0.52, 0.66, 1.0);
    // Exit after dashed output
    return;
#endif
    // Read debug visualization mode selector
    int debugMode = int(Camera.Counts.z + 0.5);
    // Fetch fragment material record
    RasterMaterial material = Materials[fsMaterialIndex];

    // Flag base color texture presence
    bool hasBase = material.TextureFlags.x > 0.5;
    // Flag metallic roughness texture presence
    bool hasMr = material.TextureFlags.y > 0.5;
    // Flag normal map presence
    bool hasNormal = material.TextureFlags.z > 0.5;
    // Flag emissive texture presence
    bool hasEmissive = material.TextureFlags.w > 0.5;
    // Flag occlusion texture presence
    bool hasOcclusion = material.TextureFlags2.x > 0.5;
    // Flag transmission texture presence
    bool hasTransmissionTexture = material.TextureFlags2.y > 0.5;

    // Sample base color texel when present
    vec4 baseTexel = hasBase
        // Fetch base texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.BaseAtlasTransform, material.BaseTextureAddress, material.BaseTextureTransform)
        // Default base texel to white
        : vec4(1.0);
    // Compute linear base color
    vec3 baseColor = material.BaseColorAlpha.rgb * (hasBase ? srgbToLinear(baseTexel.rgb) : vec3(1.0));
    // Combine material and texture alpha
    float sourceAlpha = material.BaseColorAlpha.a * baseTexel.a;
    // Initialize coverage alpha
    float alpha = sourceAlpha;
    // Default sampled transmission to opaque
    float sampledTransmission = 1.0;
    // Clamp material transmission factor
    float transmission = clamp(material.AlphaFlags.w, 0.0, 1.0);
    // Test for transmission texture
    if (hasTransmissionTexture)
    // Begin transmission sampling block
    {
        // Sample transmission mask red channel
        sampledTransmission = sampleAtlasTexture(
            // Pass fragment UVs to sampler
            fsUv,
            // Pass transmission atlas transform
            material.TransmissionAtlasTransform,
            // Pass transmission address mode
            material.TransmissionTextureAddress,
            // Read mask value from transform result
            material.TransmissionTextureTransform).r;
        // Modulate transmission by sampled mask
        transmission *= sampledTransmission;
    // End transmission sampling block
    }

    // Clamp index of refraction
    float ior = clamp(material.OpticalFactors.x, 1.0, 2.333);
    // Clamp volume thickness nonnegative
    float thickness = max(material.OpticalFactors.y, 0.0);
    // Clamp clearcoat layer weight
    float clearcoat = clamp(material.OpticalFactors.z, 0.0, 1.0);
    // Test for shared clearcoat mask
    if (material.TextureFlags2.z > 0.5)
        // Reuse transmission mask for clearcoat
        clearcoat *= sampledTransmission;
    // Clamp clearcoat roughness
    float clearcoatRoughness = clamp(material.OpticalFactors.w, 0.045, 1.0);
    // Decode alpha blend mode
    int alphaMode = int(material.AlphaFlags.x + 0.5);
    // Test for opaque alpha mode
    if (alphaMode == 0)
        // Force full opacity
        alpha = 1.0;
    // Test mask cutoff threshold
    else if (alphaMode == 1 && alpha < material.AlphaFlags.y)
        // Discard fragment below cutoff
        discard;

    // The shared rasterizer disables fixed-function culling so double-sided
    // materials remain possible. Respect glTF single-sided semantics here for
    // transparent surfaces; otherwise every back face is alpha blended too and
    // thin stained-glass meshes quickly accumulate toward white.
    // Flag blended or transmissive surface
    bool transparentSurface = alphaMode == 2 || transmission > 0.001;
    // Test single sided back face
    if (transparentSurface && material.AlphaFlags.z < 0.5 && !gl_FrontFacing)
        // Cull single sided back face
        discard;

    // Sample metallic roughness texel when present
    vec4 mrTexel = hasMr
        // Fetch MR texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.MrAtlasTransform, material.MrTextureAddress, material.MrTextureTransform)
        // Default MR texel to white
        : vec4(1.0);
    // Compute roughness from green channel
    float roughness = clamp(material.PbrFactors.y * (hasMr ? mrTexel.g : 1.0), 0.045, 1.0);
    // Compute metallic from blue channel
    float metallic = clamp(material.PbrFactors.x * (hasMr ? mrTexel.b : 1.0), 0.0, 1.0);

    // Default occlusion to unshadowed
    float occlusion = 1.0;
    // Test for occlusion texture
    if (hasOcclusion)
    // Begin occlusion sampling block
    {
        // Sample occlusion red channel
        float sampledOcclusion = sampleAtlasTexture(
            // Fetch occlusion texel from atlas
            fsUv, material.OcclusionAtlasTransform, material.OcclusionTextureAddress, material.OcclusionTextureTransform).r;
        // Blend occlusion by material strength
        occlusion = mix(1.0, sampledOcclusion, clamp(material.PbrFactors.w, 0.0, 1.0));
    // End occlusion sampling block
    }

    // Compute view direction to camera
    vec3 viewDir = normalize(Camera.CameraPosition.xyz - fsWorldPos);
    // Normalize interpolated surface normal
    vec3 normal = normalize(fsNormal);
    // Test double sided material flag
    if (material.AlphaFlags.z > 0.5)
    // Begin double sided normal block
    {
        // Test back facing normal
        if (dot(normal, viewDir) < 0.0)
            // Flip normal toward viewer
            normal = -normal;
    // End double sided normal block
    }
    // Test single sided back face
    else if (dot(normal, viewDir) < 0.0)
    // Begin normal flip block
    {
        // Flip normal toward viewer
        normal = -normal;
    // End normal flip block
    }

    // Test for normal map
    if (hasNormal)
    // Begin normal mapping block
    {
        // Compute addressed normal map UVs
        vec2 normalUv = addressUv(fsUv, material.NormalTextureAddress, material.NormalTextureTransform);
        // Sample tangent space normal
        vec3 tangentNormal = sampleAtlasTexture(
            // Decode normal from unsigned range
            fsUv, material.NormalAtlasTransform, material.NormalTextureAddress, material.NormalTextureTransform).xyz * 2.0 - 1.0;
        // Scale normal map strength
        tangentNormal.xy *= material.PbrFactors.z;
        // Normalize perturbed tangent normal
        tangentNormal = normalize(tangentNormal);
        // Transform normal to world space
        normal = normalize(cotangentFrame(normal, fsWorldPos, normalUv) * tangentNormal);
    // End normal mapping block
    }

    // Default emissive to black
    vec3 emissive = vec3(0.0);
    // Test for emissive contribution
    if (material.EmissiveFactor.a > 0.0)
    // Begin emissive block
    {
        // Select emissive source color
        vec3 emissionSource = hasEmissive
            // Sample emissive texel from atlas
            ? srgbToLinear(sampleAtlasTexture(
                // Convert emissive texel to linear
                fsUv, material.EmissiveAtlasTransform, material.EmissiveTextureAddress, material.EmissiveTextureTransform).rgb)
            // Fall back to base color
            : baseColor;
        // Scale emissive by factor and strength
        emissive = emissionSource * material.EmissiveFactor.rgb * material.EmissiveFactor.a;
    // End emissive block
    }

    // Test UV debug view
    if (debugMode == 1)
    // Begin UV debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Output UV gradient debug color
        outColor = vec4(uv.x, uv.y, 0.0, 1.0);
        // Exit after UV debug output
        return;
    // End UV debug block
    }
    // Test atlas debug view
    if (debugMode == 2)
    // Begin atlas debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Map UVs into atlas region
        vec2 atlasUv = material.BaseAtlasTransform.xy + uv * material.BaseAtlasTransform.zw;
        // Output atlas grid debug color
        outColor = vec4(fract(atlasUv.x * 8.0), fract(atlasUv.y * 8.0), hasBase ? 1.0 : 0.0, 1.0);
        // Exit after atlas debug output
        return;
    // End atlas debug block
    }
    // Test base texture debug view
    if (debugMode == 3)
    // Begin base texture debug block
    {
        // Output base texel debug color
        outColor = vec4(hasBase ? baseTexel.rgb : linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after base debug output
        return;
    // End base texture debug block
    }
    // Test base color debug view
    if (debugMode == 4)
    // Begin base color debug block
    {
        // Output base color debug swatch
        outColor = vec4(linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after color debug output
        return;
    // End base color debug block
    }
    // Test normal debug view
    if (debugMode == 5)
    // Begin normal debug block
    {
        // Output normal vector debug color
        outColor = vec4(normal * 0.5 + 0.5, 1.0);
        // Exit after normal debug output
        return;
    // End normal debug block
    }
    // Test metallic debug view
    if (debugMode == 6)
    // Begin metallic debug block
    {
        // Output metallic debug grayscale
        outColor = vec4(vec3(metallic), 1.0);
        // Exit after metallic debug output
        return;
    // End metallic debug block
    }
    // Test roughness debug view
    if (debugMode == 7)
    // Begin roughness debug block
    {
        // Output roughness debug grayscale
        outColor = vec4(vec3(roughness), 1.0);
        // Exit after roughness debug output
        return;
    // End roughness debug block
    }
    // Test occlusion debug view
    if (debugMode == 8)
    // Begin occlusion debug block
    {
        // Output occlusion debug grayscale
        outColor = vec4(vec3(occlusion), 1.0);
        // Exit after occlusion debug output
        return;
    // End occlusion debug block
    }
    // Test emissive debug view
    if (debugMode == 9)
    // Begin emissive debug block
    {
        // Output tone mapped emissive debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(emissive)), 1.0);
        // Exit after emissive debug output
        return;
    // End emissive debug block
    }

    // Clamp normal view cosine
    float nDotV = max(dot(normal, viewDir), 0.0001);
    // Compute dielectric Fresnel reflectance
    float dielectricF0 = pow((ior - 1.0) / max(ior + 1.0, 0.0001), 2.0);
    // Blend Fresnel base by metallic
    vec3 f0 = mix(vec3(dielectricF0), baseColor, metallic);
    // Initialize direct light accumulator
    vec3 directLighting = vec3(0.0);
    // Initialize clearcoat light accumulator
    vec3 clearcoatDirect = vec3(0.0);
    // Clamp active light count
    int lightCount = min(32, int(Camera.Counts.x + 0.5));
    // Loop over active lights
    for (int i = 0; i < lightCount; i++)
    // Begin per light loop
    {
        // Fetch current light record
        RasterLight light = Lights[i];
        // Test disabled light flag
        if (light.ConeEnabled.w < 0.5)
            // Skip disabled light
            continue;

        // Read light type selector
        float kind = light.PositionKind.w;
        // Declare light direction variable
        vec3 lightDirection;
        // Default attenuation to none
        float attenuation = 1.0;
        // Default spotlight cone to full
        float cone = 1.0;

        // Test directional light type
        if (kind > 0.5 && kind < 1.5)
        // Begin directional light block
        {
            // Aim along directional light vector
            lightDirection = normalize(-light.DirectionRange.xyz);
        // End directional light block
        }
        // Handle positional light types
        else
        // Begin positional light block
        {
            // Compute vector to light
            vec3 toLight = light.PositionKind.xyz - fsWorldPos;
            // Measure distance to light
            float distanceToLight = length(toLight);
            // Test degenerate light distance
            if (distanceToLight < 0.0001)
                // Skip coincident light
                continue;

            // Normalize direction to light
            lightDirection = toLight / distanceToLight;
            // Read light range cutoff
            float range = light.DirectionRange.w;
            // Apply inverse square falloff
            attenuation = 1.0 / (1.0 + 0.11 * distanceToLight * distanceToLight);
            // Test finite range cutoff
            if (range > 0.0001)
            // Begin range fade block
            {
                // Normalize distance by range
                float normalizedDistance = clamp(distanceToLight / range, 0.0, 1.0);
                // Compute range edge fade
                float edgeFade = 1.0 - normalizedDistance * normalizedDistance;
                // Fade attenuation at range edge
                attenuation *= edgeFade * edgeFade;
            // End range fade block
            }

            // Test spotlight type
            if (kind >= 1.5)
            // Begin spotlight cone block
            {
                // Normalize spotlight forward vector
                vec3 spotForward = normalize(light.DirectionRange.xyz);
                // Measure spot alignment cosine
                float coneDot = dot(normalize(fsWorldPos - light.PositionKind.xyz), spotForward);
                // Read inner cone angle
                float inner = light.ConeEnabled.x;
                // Read outer cone angle
                float outer = light.ConeEnabled.y;
                // Interpolate cone falloff factor
                cone = clamp((coneDot - outer) / max(0.0001, inner - outer), 0.0, 1.0);
                // Soften cone falloff curve
                cone *= cone;
            // End spotlight cone block
            }
        // End positional light block
        }

        // Clamp normal light cosine
        float nDotL = max(dot(normal, lightDirection), 0.0);
        // Test back facing light
        if (nDotL <= 0.0)
            // Skip unlit contribution
            continue;

        // Compute Blinn Phong half vector
        vec3 halfVector = normalize(lightDirection + viewDir);
        // Clamp normal half cosine
        float nDotH = max(dot(normal, halfVector), 0.0);
        // Clamp view half cosine
        float vDotH = max(dot(viewDir, halfVector), 0.0);
        // Square perceptual roughness value
        float alphaRoughness = roughness * roughness;
        // Evaluate GGX normal distribution
        float distribution = distributionGgx(nDotH, alphaRoughness);
        // Evaluate Smith geometry term
        float visibility = visibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        // Evaluate Schlick Fresnel term
        vec3 fresnel = fresnelSchlick(vDotH, f0);
        // Combine specular BRDF terms
        vec3 specular = distribution * visibility * fresnel;
        // Compute energy conserving diffuse
        vec3 diffuse = (vec3(1.0) - fresnel) * (1.0 - metallic) * baseColor / PI;
        // Scale light color by falloff
        vec3 radiance = light.ColorIntensity.rgb * light.ColorIntensity.w * attenuation * cone * 0.18;
        // Accumulate diffuse plus specular lighting
        directLighting += (diffuse + specular) * radiance * nDotL;

        // KHR_materials_clearcoat adds a dielectric layer. Reuse the already
        // sampled transmission mask when the asset shares that texture, so the
        // StainedGlassLamp path adds ALU only and no extra texture fetch.
        // Test clearcoat layer presence
        if (clearcoat > 0.001)
        // Begin clearcoat direct block
        {
            // Square clearcoat roughness value
            float clearcoatAlpha = clearcoatRoughness * clearcoatRoughness;
            // Evaluate clearcoat GGX distribution
            float clearcoatDistribution = distributionGgx(nDotH, clearcoatAlpha);
            // Evaluate clearcoat geometry term
            float clearcoatVisibility = visibilitySmithGgxCorrelated(nDotV, nDotL, clearcoatAlpha);
            // Evaluate clearcoat Fresnel term
            vec3 clearcoatFresnel = fresnelSchlick(vDotH, vec3(0.04));
            // Accumulate clearcoat specular direct light
            clearcoatDirect += clearcoat * clearcoatDistribution * clearcoatVisibility *
                // Scale clearcoat by radiance and cosine
                clearcoatFresnel * radiance * nDotL;
        // End clearcoat direct block
        }
    // End per light loop
    }

    // Compute mirror reflection vector
    vec3 reflection = reflect(-viewDir, normal);
    // Approximate image based Fresnel
    vec3 fresnelIbl = f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nDotV, 5.0);
    // Compute diffuse image based lighting
    vec3 diffuseIbl = diffuseEnvironment(normal) * baseColor * (vec3(1.0) - fresnelIbl) * (1.0 - metallic);
    // Sample sharp environment reflection
    vec3 sharpSpecularIbl = studioEnvironment(reflection);
    // Sample broad environment reflection
    vec3 broadSpecularIbl = diffuseEnvironment(normal);
    // Blend reflections by roughness
    vec3 specularIbl = mix(sharpSpecularIbl, broadSpecularIbl, roughness * roughness) * fresnelIbl;
    // Combine indirect light with occlusion
    vec3 indirectLighting = (diffuseIbl + specularIbl) * occlusion * 0.72;

    // Test direct light debug view
    if (debugMode == 10)
    // Begin direct debug block
    {
        // Output tone mapped direct debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(directLighting)), 1.0);
        // Exit after direct debug output
        return;
    // End direct debug block
    }
    // Test indirect light debug view
    if (debugMode == 11)
    // Begin indirect debug block
    {
        // Output tone mapped indirect debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(indirectLighting)), 1.0);
        // Exit after indirect debug output
        return;
    // End indirect debug block
    }

    // Sum direct and indirect lighting
    vec3 surfaceLighting = directLighting + indirectLighting;

    // Keep the fast single-pass raster path: transmission changes the shaded
    // light, never surface coverage. This follows glTF's separation of optical
    // transmission from alpha while avoiding a framebuffer resolve, mip chain,
    // additional render pass, or any extra texture sample for StainedGlassLamp.
    // Test transmissive material
    if (transmission > 0.001)
    // Begin transmission block
    {
        // Compute Snell refraction vector
        vec3 refractedDirection = refract(-viewDir, normal, 1.0 / ior);
        // Test total internal reflection
        if (dot(refractedDirection, refractedDirection) < 0.000001)
            // Fall back to mirror reflection
            refractedDirection = reflection;

        // Square roughness for blur
        float transmissionBlur = roughness * roughness;
        // Blend transmitted environment samples
        vec3 transmittedEnvironment = mix(
            // Sample sharp refracted environment
            studioEnvironment(refractedDirection),
            // Sample boosted backside diffuse
            diffuseEnvironment(-normal) * 1.35,
            // Blur by roughness factor
            transmissionBlur);

        // Tint glass by base color
        vec3 glassTint = mix(vec3(1.0), max(baseColor, vec3(0.001)), 0.72);
        // Default volume attenuation to none
        vec3 volumeAttenuation = vec3(1.0);
        // Read attenuation distance parameter
        float attenuationDistance = material.AttenuationColorDistance.w;
        // Test volumetric absorption enabled
        if (thickness > 0.0 && attenuationDistance > 0.0)
        // Begin absorption block
        {
            // Compute view dependent path length
            float pathLength = thickness / max(abs(dot(normal, viewDir)), 0.15);
            // Apply Beer Lambert absorption
            volumeAttenuation = pow(
                // Clamp attenuation color positive
                max(material.AttenuationColorDistance.rgb, vec3(0.0001)),
                // Scale absorption by normalized path
                vec3(pathLength / attenuationDistance));
        // End absorption block
        }

        // Tint transmitted environment light
        vec3 transmittedLight = transmittedEnvironment * glassTint * volumeAttenuation;
        // Evaluate transmission Fresnel term
        vec3 transmissionFresnel = fresnelSchlick(nDotV, vec3(dielectricF0));
        // Blend transmitted and surface light
        vec3 opticalResult = mix(transmittedLight, surfaceLighting, transmissionFresnel);
        // Mix surface toward optical result
        surfaceLighting = mix(surfaceLighting, opticalResult, transmission);
    // End transmission block
    }

    // Add emissive to surface lighting
    vec3 linearColor = surfaceLighting + emissive;
    // Test clearcoat layer presence
    if (clearcoat > 0.001)
    // Begin clearcoat indirect block
    {
        // Compute view Fresnel for clearcoat
        float clearcoatFresnelView = 0.04 + 0.96 * pow(1.0 - nDotV, 5.0);
        // Blend clearcoat environment reflections
        vec3 clearcoatIbl = mix(
            // Use sharp reflection sample
            sharpSpecularIbl,
            // Use broad reflection sample
            broadSpecularIbl,
            // Weight clearcoat IBL by Fresnel
            clearcoatRoughness * clearcoatRoughness) * clearcoatFresnelView * clearcoat;
        // Attenuate base by clearcoat layer
        linearColor *= 1.0 - clearcoat * clearcoatFresnelView;
        // Add clearcoat direct and indirect
        linearColor += clearcoatDirect + clearcoatIbl;
    // End clearcoat indirect block
    }

    // Alpha remains geometric coverage. OPAQUE and MASK transmission materials
    // therefore stay visibly present instead of becoming ordinary transparency.
    // Select output alpha by mode
    float outputAlpha = alphaMode == 2 ? alpha : 1.0;

    // Tone map and encode output
    vec3 outputColor = linearToSrgb(pbrNeutralToneMap(linearColor));
    // Write final fragment color
    outColor = vec4(outputColor, outputAlpha);
// End main function
}
// End fragment shader source string
";
// End VulkanRasterRenderer class
}
