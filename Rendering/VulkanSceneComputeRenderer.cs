// -----------------------------------------------------------------------------
// File: Rendering/VulkanSceneComputeRenderer.cs
// Purpose: Vulkan compute scene ray/path tracer.
//
// This renderer uses Vulkan compute through Veldrid rather than Vulkan RT
// BLAS/TLAS. It traces the current Scene.Triangles buffer on the GPU, applies
// scene lights, simple material properties, hard shadows, emissive surfaces, and
// optional stochastic bounces selected from the Render pane.
// -----------------------------------------------------------------------------

// Import vector math types
using System.Numerics;
// Import struct layout attributes
using System.Runtime.InteropServices;
// Import text encoding utilities
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
// Import Veldrid graphics device API
using Veldrid;
// Import SPIRV shader compilation support
using Veldrid.SPIRV;

// Declare rendering namespace scope
namespace LightingShowcase.Rendering;

/// <summary>Vulkan compute ray/path tracer for the current triangle scene.</summary>
// Define Vulkan compute renderer class
public static class VulkanSceneComputeRenderer
// Begin renderer class block
{
    // Expose Vulkan stage log path
    public static string StageLogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-stage-log.txt");

    /// <summary>Resets the Vulkan stage diagnostic log.</summary>
    // Declare stage log reset method
    private static void ResetStageLog(int width, int height, int bounceCount, int sampleIndex)
    // Begin ResetStageLog method block
    {
        // Begin guarded log write
        try
        // Begin try block
        {
            // Write fresh stage log header
            File.WriteAllText(StageLogPath, $"LightingShowcase Vulkan stage log{Environment.NewLine}Resolution: {width}x{height}{Environment.NewLine}Bounces: {bounceCount}{Environment.NewLine}Sample: {sampleIndex}{Environment.NewLine}Started: {DateTime.Now:O}{Environment.NewLine}");
        // End try block
        }
        // Swallow logging failures silently
        catch { }
    // End ResetStageLog method
    }

    /// <summary>Appends a stage entry to the Vulkan diagnostic log.</summary>
    // Declare stage log append method
    private static void Stage(string name)
    // Begin Stage method block
    {
        // Begin guarded log append
        try
        // Begin try block
        {
            // Append timestamped stage entry
            File.AppendAllText(StageLogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}");
        // End try block
        }
        // Swallow logging failures silently
        catch { }
    // End Stage method
    }

    /// <summary>Logs current managed memory usage to the stage log.</summary>
    // Declare memory logging helper
    private static void StageMemory(string label)
    // Begin StageMemory method block
    {
        // Begin guarded memory probe
        try
        // Begin try block
        {
            // Query current managed heap size
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            // Log managed memory in megabytes
            Stage($"Memory {label}: managed={managed / (1024.0 * 1024.0):F1} MB");
        // End try block
        }
        // Swallow logging failures silently
        catch { }
    // End StageMemory method
    }

    // This simple compute renderer is O(pixels * triangles * bounces), but the
    // Vulkan path must not silently truncate geometry. Older builds capped this
    // at 65,535 triangles, which caused partial geometry for larger scenes.
    // Keep all scene triangles unless the buffer byte-size check below overflows.
    // Cap GPU light buffer size
    private const int MaxGpuLights = 32;

    // Veldrid/Vulkan device creation is expensive and, on some drivers, unsafe
    // when repeated rapidly during progressive sampling. Keep one device alive
    // for the app lifetime and only recreate it after an explicit cleanup.
    // Serialize renderer entry points
    private static readonly object RenderSync = new();
    // Serialize device creation disposal
    private static readonly object DeviceSync = new();
    // Cache shared Vulkan graphics device
    private static GraphicsDevice? sharedGraphicsDevice;
    // Cache shared compute pipeline resources
    private static SharedComputeResources? sharedComputeResources;
    // Cache prepared GPU scene buffers
    private static PreparedComputeScene? preparedScene;
    // Track Vulkan preflight completion flag
    private static bool preflightCompleted;

    // Define shared compute resource container
    private sealed class SharedComputeResources : IDisposable
    // Begin SharedComputeResources class block
    {
        // Hold compute resource layout
        public required ResourceLayout Layout { get; init; }
        // Hold compute shader handle
        public required Shader Shader { get; init; }
        // Hold compute pipeline handle
        public required Pipeline Pipeline { get; init; }

        /// <summary>Disposes the shared compute pipeline resources.</summary>
        // Declare shared resource cleanup method
        public void Dispose()
        // Begin Dispose method block
        {
            // Release compute pipeline safely
            try { Pipeline.Dispose(); } catch { }
            // Release compute shader safely
            try { Shader.Dispose(); } catch { }
            // Release resource layout safely
            try { Layout.Dispose(); } catch { }
        // End Dispose method
        }
    // End SharedComputeResources class
    }

    // Define prepared GPU scene container
    private sealed class PreparedComputeScene : IDisposable
    // Begin PreparedComputeScene class block
    {
        // Reference source scene object
        public required Scene Scene { get; init; }
        // Track scene cache validity stamp
        public required SceneCacheStamp CacheStamp { get; init; }
        // Hold GPU triangle buffer
        public required DeviceBuffer TriangleBuffer { get; init; }
        // Hold GPU BVH node buffer
        public required DeviceBuffer BvhBuffer { get; init; }
        // Hold GPU light buffer
        public required DeviceBuffer LightBuffer { get; init; }
        // Hold GPU texture pixel buffer
        public required DeviceBuffer TexturePixelBuffer { get; init; }
        // Hold GPU texture metadata buffer
        public required DeviceBuffer TextureInfoBuffer { get; init; }
        // Count original scene triangles
        public required int SourceTriangleCount { get; init; }
        // Count uploaded GPU triangles
        public required int TriangleCount { get; init; }
        // Count uploaded BVH nodes
        public required int BvhNodeCount { get; init; }
        // Count original scene lights
        public required int SourceLightCount { get; init; }
        // Count uploaded GPU lights
        public required int LightCount { get; init; }
        // Count uploaded GPU textures
        public required int TextureCount { get; init; }
        // Track total scene buffer bytes
        public required long SceneBufferBytes { get; init; }

        /// <summary>Disposes the prepared scene GPU buffers.</summary>
        // Declare scene buffer cleanup method
        public void Dispose()
        // Begin Dispose method block
        {
            // Release texture metadata buffer safely
            try { TextureInfoBuffer.Dispose(); } catch { }
            // Release texture pixel buffer safely
            try { TexturePixelBuffer.Dispose(); } catch { }
            // Release light buffer safely
            try { LightBuffer.Dispose(); } catch { }
            // Release BVH buffer safely
            try { BvhBuffer.Dispose(); } catch { }
            // Release triangle buffer safely
            try { TriangleBuffer.Dispose(); } catch { }
        // End Dispose method
        }
    // End PreparedComputeScene class
    }

    /// <summary>Disposes the shared Vulkan device and its cached resources.</summary>
    // Declare shared device teardown method
    public static void DisposeSharedDevice()
    // Begin DisposeSharedDevice method block
    {
        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin render lock block
        {
            // Serialize against device creation
            lock (DeviceSync)
            // Begin device lock block
            {
                // Snapshot shared device handle
                GraphicsDevice? device = sharedGraphicsDevice;
                // Clear shared device cache
                sharedGraphicsDevice = null;
                // Snapshot compute resources handle
                SharedComputeResources? computeResources = sharedComputeResources;
                // Clear compute resource cache
                sharedComputeResources = null;
                // Snapshot prepared scene handle
                PreparedComputeScene? sceneResources = preparedScene;
                // Clear prepared scene cache
                preparedScene = null;
                // Check for missing device
                if (device == null)
                // Begin null device block
                {
                    // Dispose orphaned scene resources
                    sceneResources?.Dispose();
                    // Dispose orphaned compute resources
                    computeResources?.Dispose();
                    // Exit without device teardown
                    return;
                // End null device block
                }

                // Begin device idle guard
                try
                // Begin try block
                {
                    // Log device idle wait
                    Stage("Dispose shared Vulkan GraphicsDevice: WaitForIdle");
                    // Wait for GPU work completion
                    device.WaitForIdle();
                // End try block
                }
                // Catch device idle failures
                catch (Exception ex)
                // Begin catch block
                {
                    // Log device idle failure
                    Stage($"Dispose shared Vulkan GraphicsDevice: WaitForIdle failed: {ex.GetType().Name}: {ex.Message}");
                // End catch block
                }

                // Begin scene dispose guard
                try
                // Begin try block
                {
                    // Log scene buffer disposal
                    Stage("Dispose prepared Vulkan compute scene");
                    // Dispose prepared scene buffers
                    sceneResources?.Dispose();
                // End try block
                }
                // Catch scene dispose failures
                catch (Exception ex)
                // Begin catch block
                {
                    // Log scene dispose failure
                    Stage($"Dispose prepared Vulkan compute scene failed: {ex.GetType().Name}: {ex.Message}");
                // End catch block
                }

                // Begin compute dispose guard
                try
                // Begin try block
                {
                    // Log compute resource disposal
                    Stage("Dispose shared Vulkan compute resources");
                    // Dispose shared compute resources
                    computeResources?.Dispose();
                // End try block
                }
                // Catch compute dispose failures
                catch (Exception ex)
                // Begin catch block
                {
                    // Log compute dispose failure
                    Stage($"Dispose shared Vulkan compute resources failed: {ex.GetType().Name}: {ex.Message}");
                // End catch block
                }

                // Begin device dispose guard
                try
                // Begin try block
                {
                    // Log graphics device disposal
                    Stage("Dispose shared Vulkan GraphicsDevice");
                    // Dispose shared graphics device
                    device.Dispose();
                // End try block
                }
                // Catch device dispose failures
                catch (Exception ex)
                // Begin catch block
                {
                    // Log device dispose failure
                    Stage($"Dispose shared Vulkan GraphicsDevice failed: {ex.GetType().Name}: {ex.Message}");
                // End catch block
                }
            // End device lock block
            }
        // End render lock block
        }
    // End DisposeSharedDevice method
    }


    /// <summary>
    /// Releases scene-sized Vulkan buffers while preserving the shared device
    /// and pipeline. This avoids overlapping the old GPU scene with a newly
    /// loaded model.
    /// </summary>
    // Declare scene buffer release method
    public static void ReleasePreparedScene()
    // Begin ReleasePreparedScene method block
    {
        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin render lock block
        {
            // Serialize against device creation
            lock (DeviceSync)
            // Begin device lock block
            {
                // Snapshot prepared scene handle
                PreparedComputeScene? sceneResources = preparedScene;
                // Clear prepared scene cache
                preparedScene = null;
                // Check for missing scene
                if (sceneResources == null)
                    // Exit when nothing cached
                    return;

                // Idle GPU before buffer release
                try { sharedGraphicsDevice?.WaitForIdle(); } catch { }
                // Dispose scene buffers safely
                try { sceneResources.Dispose(); } catch { }
            // End device lock block
            }
        // End render lock block
        }
    // End ReleasePreparedScene method
    }


    /// <summary>Initializes and validates the shared Vulkan device for command-line readiness checks.</summary>
    // Declare device readiness check method
    public static string EnsureDeviceReady()
    // Begin EnsureDeviceReady method block
    {
        // Create or reuse shared device
        GraphicsDevice device = GetOrCreateSharedDevice();
        // Return graphics backend name
        return device.BackendType.ToString();
    // End EnsureDeviceReady method
    }

    /// <summary>Returns the shared Vulkan graphics device, creating it when needed.</summary>
    // Declare shared device factory method
    private static GraphicsDevice GetOrCreateSharedDevice()
    // Begin GetOrCreateSharedDevice method block
    {
        // Serialize device creation
        lock (DeviceSync)
        // Begin device lock block
        {
            // Check for cached device
            if (sharedGraphicsDevice != null)
            // Begin cache hit block
            {
                // Log device reuse
                Stage("Reuse shared Veldrid Vulkan GraphicsDevice");
                // Return cached graphics device
                return sharedGraphicsDevice;
            // End cache hit block
            }

            // Check Vulkan preflight status
            if (!preflightCompleted)
            // Begin preflight block
            {
                // Log Vulkan preflight start
                Stage("Preflight Veldrid Vulkan GraphicsDevice in child process");
                // Verify Vulkan in child process
                VeldridVulkanDevicePreflight.VerifyInChildProcess(Stage);
                // Mark preflight as completed
                preflightCompleted = true;
            // End preflight block
            }

            // Log device creation start
            Stage("Create shared Veldrid Vulkan GraphicsDevice in main process");
            // Create Vulkan graphics device
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            // Begin device options block
            {
                // Disable graphics debug layer
                Debug = false,
                // Request standard clip Y direction
                PreferStandardClipSpaceYDirection = true,
                // Request zero to one depth
                PreferDepthRangeZeroToOne = true,
                // Disable vsync for compute
                SyncToVerticalBlank = false
            // End device creation call
            });

            // Log created backend type
            Stage($"Shared GraphicsDevice created: backend={gd.BackendType}");
            // Verify Vulkan backend selected
            if (gd.BackendType != GraphicsBackend.Vulkan)
            // Begin backend mismatch block
            {
                // Dispose non Vulkan device
                gd.Dispose();
                // Reject non Vulkan device
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device.");
            // End backend mismatch block
            }

            // Cache new graphics device
            sharedGraphicsDevice = gd;
            // Return new graphics device
            return gd;
        // End device lock block
        }
    // End GetOrCreateSharedDevice method
    }

    /// <summary>Throws when cancellation was requested at the given stage.</summary>
    // Declare cancellation check helper
    private static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    // Begin ThrowIfCancellationRequested method block
    {
        // Check for pending cancellation
        if (!cancellationToken.IsCancellationRequested)
            // Continue when not cancelled
            return;

        // Log cancellation stage name
        Stage($"Cancellation requested before/at: {stageName}");
        // Throw cancellation exception
        throw new OperationCanceledException(cancellationToken);
    // End ThrowIfCancellationRequested method
    }

    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define GPU triangle record
    private readonly struct GpuTriangle
    // Begin GpuTriangle struct block
    {
        // Position and UV padding components carry the three authored vertex
        // normals. This preserves the original 11-vec4 record size while giving
        // the ray tracer smooth glTF shading without another scene-sized buffer.
        public readonly Vector4 A; // xyz=position A, w=normal A.x
        public readonly Vector4 B; // xyz=position B, w=normal A.y
        public readonly Vector4 C; // xyz=position C, w=normal A.z
        public readonly Vector4 BaseColor; // rgb=linear factor, a=alpha factor
        public readonly Vector4 Emission; // rgb=linear factor, a=strength
        public readonly Vector4 MaterialParams; // x=metallic, y=roughness, z=transmission, w=normal scale
        public readonly Vector4 UvA; // xy=UV A, zw=normal B.xy
        public readonly Vector4 UvB; // xy=UV B, z=normal B.z, w=normal C.x
        public readonly Vector4 UvC; // xy=UV C, zw=normal C.yz
        public readonly Vector4 TextureInfo; // x=base index, y=emissive index, z=alpha/double-sided flags, w=alpha cutoff
        public readonly Vector4 TextureInfo2; // x=MR index, y=normal index, z=occlusion index, w=occlusion strength

        /// <summary>Packs a scene triangle and its material into GPU format.</summary>
        // Declare triangle packing constructor
        public GpuTriangle(Triangle triangle, IReadOnlyDictionary<TextureMap, int> textureIds)
        // Begin constructor block
        {
            // Normalize first vertex normal
            Vec3 normalA = triangle.NormalA.Normalize();
            // Normalize second vertex normal
            Vec3 normalB = triangle.NormalB.Normalize();
            // Normalize third vertex normal
            Vec3 normalC = triangle.NormalC.Normalize();
            // Pack position A plus normal
            A = ToVector4(triangle.A, (float)normalA.X);
            // Pack position B plus normal
            B = ToVector4(triangle.B, (float)normalA.Y);
            // Pack position C plus normal
            C = ToVector4(triangle.C, (float)normalA.Z);
            // Pack UV A plus normals
            UvA = new Vector4((float)triangle.UvA.U, (float)triangle.UvA.V, (float)normalB.X, (float)normalB.Y);
            // Pack UV B plus normals
            UvB = new Vector4((float)triangle.UvB.U, (float)triangle.UvB.V, (float)normalB.Z, (float)normalC.X);
            // Pack UV C plus normals
            UvC = new Vector4((float)triangle.UvC.U, (float)triangle.UvC.V, (float)normalC.Y, (float)normalC.Z);

            // Fetch triangle material reference
            Material material = triangle.Material;
            // Fetch base color factor
            Vec3 colorFactor = material.Color;
            // Begin base color packing
            BaseColor = new Vector4(
                // Clamp red channel value
                (float)Math.Clamp(colorFactor.X, 0.0, 64.0),
                // Clamp green channel value
                (float)Math.Clamp(colorFactor.Y, 0.0, 64.0),
                // Clamp blue channel value
                (float)Math.Clamp(colorFactor.Z, 0.0, 64.0),
                // Clamp alpha factor value
                (float)Math.Clamp(material.Alpha, 0.0, 1.0));

            // Begin emission packing
            Emission = new Vector4(
                // Clamp emission red nonnegative
                (float)Math.Max(0.0, material.EmissionColor.X),
                // Clamp emission green nonnegative
                (float)Math.Max(0.0, material.EmissionColor.Y),
                // Clamp emission blue nonnegative
                (float)Math.Max(0.0, material.EmissionColor.Z),
                // Clamp emission strength nonnegative
                (float)Math.Max(0.0, material.Emission));

            // Begin material parameter packing
            MaterialParams = new Vector4(
                // Clamp metallic factor value
                (float)Math.Clamp(material.Metallic, 0.0, 1.0),
                // Clamp roughness above minimum
                (float)Math.Clamp(material.Roughness, 0.02, 1.0),
                // Clamp transmission factor value
                (float)Math.Clamp(material.Transmission, 0.0, 1.0),
                // Copy normal map scale
                (float)material.NormalScale);

            // Resolve base texture index
            int baseTextureIndex = TextureIndex(textureIds, material.Texture);
            // Resolve emissive texture index
            int emissiveTextureIndex = TextureIndex(textureIds, material.EmissiveTexture);
            // Resolve metallic roughness texture index
            int metallicRoughnessTextureIndex = TextureIndex(textureIds, material.MetallicRoughnessTexture);
            // Resolve normal texture index
            int normalTextureIndex = TextureIndex(textureIds, material.NormalTexture);
            // Resolve occlusion texture index
            int occlusionTextureIndex = TextureIndex(textureIds, material.OcclusionTexture);
            // Combine alpha mode plus sidedness
            int alphaFlags = (int)material.AlphaMode | (material.DoubleSided ? 4 : 0);
            // Begin primary texture packing
            TextureInfo = new Vector4(
                // Store base texture index
                baseTextureIndex,
                // Store emissive texture index
                emissiveTextureIndex,
                // Store alpha mode flags
                alphaFlags,
                // Store alpha cutoff threshold
                (float)material.AlphaCutoff);
            // Begin secondary texture packing
            TextureInfo2 = new Vector4(
                // Store metallic roughness index
                metallicRoughnessTextureIndex,
                // Store normal texture index
                normalTextureIndex,
                // Store occlusion texture index
                occlusionTextureIndex,
                // Store occlusion strength value
                (float)material.OcclusionStrength);
        // End constructor block
        }

        /// <summary>Returns the GPU texture index for a texture map.</summary>
        // Declare texture index lookup
        private static int TextureIndex(IReadOnlyDictionary<TextureMap, int> textureIds, TextureMap? texture)
        // Begin TextureIndex method block
        {
            // Check for missing texture
            if (texture == null)
                // Return missing texture sentinel
                return -1;
            // Lookup cached texture index
            return textureIds.TryGetValue(texture, out int textureIndex) ? textureIndex : -1;
        // End TextureIndex method
        }
    // End GpuTriangle struct
    }

    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define GPU texture metadata record
    private readonly struct GpuTextureInfo
    // Begin GpuTextureInfo struct block
    {
        public readonly Vector4 Data; // x=pixel offset, y=width, z=height, w=reserved

        /// <summary>Initializes GPU texture location metadata.</summary>
        // Declare texture metadata constructor
        public GpuTextureInfo(int pixelOffset, int width, int height)
        // Begin constructor block
        {
            // Pack texture offset plus dimensions
            Data = new Vector4(pixelOffset, width, height, 0.0f);
        // End constructor block
        }
    // End GpuTextureInfo struct
    }

    // Define texture upload descriptor
    private sealed class TextureUpload
    // Begin TextureUpload class block
    {
        // Reference source texture map
        public required TextureMap Texture { get; init; }
        // Store pixel buffer offset
        public required int PixelOffset { get; init; }
    // End TextureUpload class
    }

    // Define texture build result
    private sealed class TextureBuildResult
    // Begin TextureBuildResult class block
    {
        // Map textures to indices
        public required Dictionary<TextureMap, int> TextureIds { get; init; }
        // List pending texture uploads
        public required IReadOnlyList<TextureUpload> Uploads { get; init; }
        // Hold GPU texture metadata array
        public required GpuTextureInfo[] Infos { get; set; }
        // Count total texture pixels
        public required long PixelCount { get; init; }
        // Count built textures
        public required int TextureCount { get; init; }
    // End TextureBuildResult class
    }




    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define GPU BVH node
    private readonly struct GpuBvhNode
    // Begin GpuBvhNode struct block
    {
        // Hold node minimum bounds
        public readonly Vector4 BoundsMin;
        // Hold node maximum bounds
        public readonly Vector4 BoundsMax;
        // x=left child or first triangle, y=triangle count (0 for internal), z=right child, w=reserved
        // Hold child indices plus count
        public readonly Vector4 Data;

        /// <summary>Initializes a GPU bounding volume hierarchy node.</summary>
        // Declare BVH node constructor
        public GpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
        // Begin constructor block
        {
            // Pack minimum bounds vector
            BoundsMin = new Vector4(boundsMin, 0.0f);
            // Pack maximum bounds vector
            BoundsMax = new Vector4(boundsMax, 0.0f);
            // Pack child links plus count
            Data = new Vector4(leftOrFirst, triangleCount, rightChild, 0.0f);
        // End constructor block
        }
    // End GpuBvhNode struct
    }

    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define CPU BVH node
    private readonly struct CpuBvhNode
    // Begin CpuBvhNode struct block
    {
        // Hold node minimum bounds
        public readonly Vector3 BoundsMin;
        // Hold node maximum bounds
        public readonly Vector3 BoundsMax;
        // Hold left child or first index
        public readonly int LeftOrFirst;
        // Hold leaf triangle count
        public readonly int TriangleCount;
        // Hold right child index
        public readonly int RightChild;

        /// <summary>Initializes a CPU bounding volume hierarchy node.</summary>
        // Declare CPU node constructor
        public CpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
        // Begin constructor block
        {
            // Copy minimum bounds value
            BoundsMin = boundsMin;
            // Copy maximum bounds value
            BoundsMax = boundsMax;
            // Copy left or first index
            LeftOrFirst = leftOrFirst;
            // Copy triangle count value
            TriangleCount = triangleCount;
            // Copy right child index
            RightChild = rightChild;
        // End constructor block
        }

        /// <summary>Converts this CPU node into its GPU representation.</summary>
        // Convert CPU node to GPU
        public GpuBvhNode ToGpu() => new(BoundsMin, BoundsMax, LeftOrFirst, TriangleCount, RightChild);
    // End CpuBvhNode struct
    }

    // Define centroid sort comparer
    private sealed class TriangleCentroidComparer : IComparer<int>
    // Begin comparer class block
    {
        // Reference triangles for sorting
        private readonly IReadOnlyList<Triangle> triangles;
        // Select active sort axis
        public int Axis { get; set; }

        /// <summary>Initializes the comparer with the scene triangles.</summary>
        // Declare comparer constructor
        public TriangleCentroidComparer(IReadOnlyList<Triangle> triangles)
        // Begin constructor block
        {
            // Store triangle list reference
            this.triangles = triangles;
        // End constructor block
        }

        /// <summary>Compares two triangles by centroid along the active axis.</summary>
        // Declare centroid comparison method
        public int Compare(int leftIndex, int rightIndex)
        // Begin Compare method block
        {
            // Fetch left triangle centroid
            Vec3 left = triangles[leftIndex].Centroid;
            // Fetch right triangle centroid
            Vec3 right = triangles[rightIndex].Centroid;
            // Select left axis coordinate
            double leftValue = Axis == 0 ? left.X : Axis == 1 ? left.Y : left.Z;
            // Select right axis coordinate
            double rightValue = Axis == 0 ? right.X : Axis == 1 ? right.Y : right.Z;
            // Compare axis coordinates
            return leftValue.CompareTo(rightValue);
        // End Compare method
        }
    // End comparer class
    }

    // Define BVH build result
    private sealed class BvhBuildResult
    // Begin BvhBuildResult class block
    {
        // Hold reordered triangle indices
        public required int[] TriangleIndices { get; set; }
        // Hold built BVH nodes
        public required CpuBvhNode[] Nodes { get; set; }
    // End BvhBuildResult class
    }

    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define GPU light record
    private readonly struct GpuLight
    // Begin GpuLight struct block
    {
        public readonly Vector4 PositionKind;   // xyz=position, w=0 point / 1 directional / 2 spot
        public readonly Vector4 DirectionRange; // xyz=direction light travels, w=range
        public readonly Vector4 ColorIntensity; // rgb=color, w=intensity
        public readonly Vector4 ConeShadow;     // x=cos inner, y=cos outer, z=castsShadow, w=enabled

        /// <summary>Packs a scene light into GPU format.</summary>
        // Declare light packing constructor
        public GpuLight(SceneLight light)
        // Begin constructor block
        {
            // Begin light kind mapping
            float kind = light.Kind switch
            // Begin switch expression block
            {
                // Map directional light kind
                SceneLightKind.Directional => 1.0f,
                // Map spot light kind
                SceneLightKind.Spot => 2.0f,
                // Default to point light
                _ => 0.0f
            // End light kind mapping
            };

            // Pack position plus kind
            PositionKind = ToVector4(light.Position, kind);
            // Pack direction plus range
            DirectionRange = ToVector4(light.Direction.Normalize(), (float)light.Range);
            // Pack color plus intensity
            ColorIntensity = ToVector4(light.Color, (float)light.Intensity);
            // Begin cone shadow packing
            ConeShadow = new Vector4(
                // Compute inner cone cosine
                (float)Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)),
                // Compute outer cone cosine
                (float)Math.Cos(light.OuterConeAngle),
                // Encode shadow casting flag
                light.CastsShadow ? 1.0f : 0.0f,
                // Encode light enabled flag
                light.Enabled ? 1.0f : 0.0f);
        // End constructor block
        }
    // End GpuLight struct
    }

    // Enforce sequential GPU struct layout
    [StructLayout(LayoutKind.Sequential)]
    // Define shader camera constants
    private readonly struct CameraConstants
    // Begin CameraConstants struct block
    {
        // Hold camera position vector
        public readonly Vector4 CameraPosition;
        // Hold camera forward vector
        public readonly Vector4 CameraForward;
        // Hold camera right vector
        public readonly Vector4 CameraRight;
        // Hold camera up vector
        public readonly Vector4 CameraUp;
        // Hold render width pixels
        public readonly uint Width;
        // Hold render height pixels
        public readonly uint Height;
        // Hold triangle count value
        public readonly uint TriangleCount;
        // Hold light count value
        public readonly uint LightCount;
        // Hold bounce count value
        public readonly uint BounceCount;
        // Hold current sample index
        public readonly uint SampleIndex;
        // Hold tile vertical offset
        public readonly uint TileYOffset;
        // Hold tile height pixels
        public readonly uint TileHeight;
        // Hold BVH node count
        public readonly uint BvhNodeCount;
        // Hold total sample count
        public readonly uint SampleCount;
        // Reserve padding field one
        public readonly uint Reserved1;
        // Reserve padding field two
        public readonly uint Reserved2;
        // Hold top background plus FOV
        public readonly Vector4 BackgroundTopFov;
        // Hold bottom background plus exposure
        public readonly Vector4 BackgroundBottomExposure;
        // Hold ambient plus shadow options
        public readonly Vector4 LightingOptions;

        /// <summary>Packs the camera and tile constants for the compute shader.</summary>
        // Declare constants packing constructor
        public CameraConstants(
            // Accept camera position input
            Vec3 position,
            // Accept camera orientation basis
            CameraBasis basis,
            // Accept render width input
            int width,
            // Accept render height input
            int height,
            // Accept triangle count input
            int triangleCount,
            // Accept light count input
            int lightCount,
            // Accept bounce count input
            int bounceCount,
            // Accept sample index input
            int sampleIndex,
            // Accept tile offset input
            int tileYOffset,
            // Accept tile height input
            int tileHeight,
            // Accept BVH count input
            int bvhNodeCount,
            // Accept sample count input
            int sampleCount,
            // Accept render settings input
            RenderSettings settings,
            // Accept field of view input
            double fieldOfViewDegrees)
        // Begin constructor block
        {
            // Pack camera position vector
            CameraPosition = ToVector4(position, 1.0f);
            // Pack camera forward vector
            CameraForward = ToVector4(basis.Forward, 0.0f);
            // Pack camera right vector
            CameraRight = ToVector4(basis.Right, 0.0f);
            // Pack camera up vector
            CameraUp = ToVector4(basis.Up, 0.0f);
            // Convert width to unsigned
            Width = checked((uint)width);
            // Convert height to unsigned
            Height = checked((uint)height);
            // Convert triangle count unsigned
            TriangleCount = checked((uint)triangleCount);
            // Convert light count unsigned
            LightCount = checked((uint)lightCount);
            // Clamp bounce count range
            BounceCount = checked((uint)Math.Clamp(bounceCount, 0, 8));
            // Clamp sample index nonnegative
            SampleIndex = checked((uint)Math.Max(0, sampleIndex));
            // Clamp tile offset nonnegative
            TileYOffset = checked((uint)Math.Max(0, tileYOffset));
            // Clamp tile height positive
            TileHeight = checked((uint)Math.Max(1, tileHeight));
            // Store clamped BVH node count
            BvhNodeCount = checked((uint)Math.Max(0, bvhNodeCount));
            // Store clamped sample count
            SampleCount = checked((uint)Math.Clamp(sampleCount, 1, 4096));
            // Clear first reserved field
            Reserved1 = 0;
            // Clear second reserved field
            Reserved2 = 0;

            // Clamp field of view degrees
            double clampedFov = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
            // Compute perspective scale from FOV
            float fovScale = (float)Math.Tan(clampedFov * Math.PI / 360.0);
            // Begin top background color packing
            BackgroundTopFov = new Vector4(
                // Pack top background red channel
                (float)Math.Max(0.0, settings.BackgroundTop.X),
                // Pack top background green channel
                (float)Math.Max(0.0, settings.BackgroundTop.Y),
                // Pack top background blue channel
                (float)Math.Max(0.0, settings.BackgroundTop.Z),
                // Store FOV scale in vector
                fovScale);
            // Begin bottom background color packing
            BackgroundBottomExposure = new Vector4(
                // Pack bottom background red channel
                (float)Math.Max(0.0, settings.BackgroundBottom.X),
                // Pack bottom background green channel
                (float)Math.Max(0.0, settings.BackgroundBottom.Y),
                // Pack bottom background blue channel
                (float)Math.Max(0.0, settings.BackgroundBottom.Z),
                // Store clamped exposure value
                (float)Math.Clamp(settings.Exposure, 0.01, 100.0));
            // Begin lighting options packing
            LightingOptions = new Vector4(
                // Store clamped ambient strength
                (float)Math.Clamp(settings.AmbientStrength, 0.0, 100.0),
                // Encode shadow toggle flag
                settings.UseShadows ? 1.0f : 0.0f,
                // Reserve third lighting slot
                0.0f,
                // Reserve fourth lighting slot
                0.0f);
        // End CameraConstants constructor
        }
    // End CameraConstants struct
    }

    /// <summary>Tries to render the scene with Vulkan compute. Throws on unsupported systems; this renderer never falls back to CPU rendering.</summary>
    // Begin public Vulkan render entry
    public static RenderImage Render(
        // Accept scene to render
        Scene scene,
        // Accept camera position
        Vec3 cameraPosition,
        // Accept camera orientation basis
        CameraBasis basis,
        // Accept output image width
        int width,
        // Accept output image height
        int height,
        // Accept path bounce count
        int bounceCount,
        // Accept starting sample index
        int sampleIndex,
        // Accept samples per pixel batch
        int sampleCount,
        // Accept cancellation token
        CancellationToken cancellationToken,
        // Output render details string
        out string details,
        // Accept optional progress callback
        Action<RenderImage, string>? progressCallback = null,
        // Accept optional render settings
        RenderSettings? settings = null,
        // Accept field of view degrees
        double fieldOfViewDegrees = 72.0)
    // Begin Render method
    {
        // Serialize renders on shared lock
        lock (RenderSync)
        // Begin locked render block
        {
            // Delegate to locked render path
            return RenderLocked(
                // Forward scene argument
                scene,
                // Forward camera position
                cameraPosition,
                // Forward camera basis
                basis,
                // Forward image width
                width,
                // Forward image height
                height,
                // Forward bounce count
                bounceCount,
                // Forward sample index
                sampleIndex,
                // Forward sample count
                sampleCount,
                // Forward cancellation token
                cancellationToken,
                // Forward details output
                out details,
                // Forward progress callback
                progressCallback,
                // Forward render settings
                settings,
                // Forward field of view
                fieldOfViewDegrees);
        // End locked render block
        }
    // End Render method
    }

    /// <summary>Renders the scene with Vulkan compute under the render lock.</summary>
    // Begin locked Vulkan render implementation
    private static RenderImage RenderLocked(
        // Accept scene under lock
        Scene scene,
        // Accept locked camera position
        Vec3 cameraPosition,
        // Accept locked camera basis
        CameraBasis basis,
        // Accept locked image width
        int width,
        // Accept locked image height
        int height,
        // Accept locked bounce count
        int bounceCount,
        // Accept locked sample index
        int sampleIndex,
        // Accept locked sample count
        int sampleCount,
        // Accept locked cancellation token
        CancellationToken cancellationToken,
        // Output locked render details
        out string details,
        // Accept locked progress callback
        Action<RenderImage, string>? progressCallback = null,
        // Accept locked render settings
        RenderSettings? settings = null,
        // Accept locked field of view
        double fieldOfViewDegrees = 72.0)
    // Begin RenderLocked method
    {
        // Validate render target dimensions
        if (width <= 0 || height <= 0)
            // Reject nonpositive render dimensions
            throw new ArgumentOutOfRangeException(nameof(width), "Vulkan render target dimensions must be positive.");
        // Validate sample count positive
        if (sampleCount <= 0)
            // Reject nonpositive sample count
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Vulkan sample count must be positive.");

        // Clamp sample count range
        sampleCount = Math.Clamp(sampleCount, 1, 4096);
        // Default missing render settings
        settings ??= new RenderSettings
        // Begin settings initializer
        {
            // Set default settings width
            Width = width,
            // Set default settings height
            Height = height,
            // Set default bounce count
            PathBounceCount = bounceCount,
            // Select Vulkan GPU backend
            Backend = RenderBackend.VulkanGpu
        // End settings initializer
        };
        // Clamp field of view range
        fieldOfViewDegrees = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
        // Reset render stage log
        ResetStageLog(width, height, bounceCount, sampleIndex);
        // Log sample batch range
        Stage($"Sample batch: start={sampleIndex}, count={sampleCount}");
        // Log render entry
        Stage("Render entered");
        // Log starting memory usage
        StageMemory("start");

        // Check cancellation before device setup
        ThrowIfCancellationRequested(cancellationToken, "before shared Vulkan device");
        // Get shared Vulkan device
        GraphicsDevice gd = GetOrCreateSharedDevice();
        // Get device resource factory
        ResourceFactory factory = gd.ResourceFactory;

        // Log cached scene lookup
        Stage("Get or build cached Vulkan compute scene");
        // Get cached prepared scene
        PreparedComputeScene prepared = GetOrCreatePreparedScene(gd, scene, cancellationToken);
        // Reference cached triangle buffer
        DeviceBuffer triangleBuffer = prepared.TriangleBuffer;
        // Reference cached BVH buffer
        DeviceBuffer bvhBuffer = prepared.BvhBuffer;
        // Reference cached light buffer
        DeviceBuffer lightBuffer = prepared.LightBuffer;
        // Reference cached texture pixels
        DeviceBuffer texturePixelBuffer = prepared.TexturePixelBuffer;
        // Reference cached texture metadata
        DeviceBuffer textureInfoBuffer = prepared.TextureInfoBuffer;
        // Read source triangle count
        int sourceTriangleCount = prepared.SourceTriangleCount;
        // Read uploaded triangle count
        int uploadedTriangleCount = prepared.TriangleCount;
        // Read uploaded BVH node count
        int uploadedBvhNodeCount = prepared.BvhNodeCount;
        // Read source light count
        int sourceLightCount = prepared.SourceLightCount;
        // Read uploaded light count
        int uploadedLightCount = prepared.LightCount;

        // Compute output buffer byte size
        uint outputBytes = checked((uint)((long)width * height * sizeof(uint)));
        // Compute constants struct byte size
        uint constantBytes = checked((uint)Marshal.SizeOf<CameraConstants>());
        // Log scene buffer sizes
        Stage($"Cached scene buffers={prepared.SceneBufferBytes / (1024.0 * 1024.0):F1} MB; per-frame output+readback={(outputBytes * 2.0) / (1024.0 * 1024.0):F1} MB");

        // Log output buffer creation
        Stage("Create output buffer");
        // Check cancellation before output buffer
        ThrowIfCancellationRequested(cancellationToken, "Create output buffer");
        // Begin output buffer creation
        using DeviceBuffer outputBuffer = factory.CreateBuffer(new BufferDescription(
            // Size output buffer in bytes
            outputBytes,
            // Mark output buffer read write
            BufferUsage.StructuredBufferReadWrite,
            // Set output stride to pixel
            structureByteStride: sizeof(uint)));

        // Log constants buffer creation
        Stage("Create constants buffer");
        // Begin constants buffer creation
        using DeviceBuffer constantsBuffer = factory.CreateBuffer(new BufferDescription(
            // Size constants buffer in bytes
            constantBytes,
            // Mark buffer as uniform storage
            BufferUsage.UniformBuffer));

        // Log staging buffer creation
        Stage("Create staging readback buffer");
        // Begin staging buffer creation
        using DeviceBuffer stagingBuffer = factory.CreateBuffer(new BufferDescription(
            // Size staging buffer in bytes
            outputBytes,
            // Mark buffer for CPU readback
            BufferUsage.Staging));

        // Log output buffer initialization
        Stage("Initialize output buffer");
        // Clear output buffer contents
        ZeroOutputBuffer(gd, outputBuffer, outputBytes);

        // Log pipeline lookup
        Stage("Get shared compute pipeline");
        // Check cancellation before pipeline setup
        ThrowIfCancellationRequested(cancellationToken, "Get shared compute pipeline");
        // Get shared compute resources
        SharedComputeResources computeResources = GetOrCreateSharedComputeResources(gd);
        // Reference compute resource layout
        ResourceLayout layout = computeResources.Layout;
        // Reference compute pipeline
        Pipeline pipeline = computeResources.Pipeline;

        // Log resource set creation
        Stage("Create resource set");
        // Begin resource set creation
        using ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            // Bind resource layout
            layout,
            // Bind output buffer
            outputBuffer,
            // Bind triangle buffer
            triangleBuffer,
            // Bind BVH buffer
            bvhBuffer,
            // Bind light buffer
            lightBuffer,
            // Bind constants buffer
            constantsBuffer,
            // Bind texture pixel buffer
            texturePixelBuffer,
            // Bind texture info buffer
            textureInfoBuffer));

        // Submit the compute shader in row tiles.  When no per-tile preview is
        // needed, batch all tile dispatches into one command-list submission and
        // wait only once.  The previous implementation called WaitForIdle after
        // every tile, which serialized the GPU and made the Vulkan path slower
        // than the CPU path on many scenes.
        // Choose compute tile row count
        int tileRows = ChooseTileRows(width, height, uploadedTriangleCount, bounceCount, uploadedLightCount, sampleCount);
        // Log tiled dispatch plan
        Stage($"Dispatch compute shader in row tiles: tileRows={tileRows}");

        // Check for preview callback
        if (progressCallback == null)
        // Begin batched dispatch branch
        {
            // Begin batched tile dispatch call
            DispatchTilesBatched(
                // Pass graphics device
                gd,
                // Pass resource factory
                factory,
                // Pass compute pipeline
                pipeline,
                // Pass resource layout
                layout,
                // Pass output buffer
                outputBuffer,
                // Pass triangle buffer
                triangleBuffer,
                // Pass BVH buffer
                bvhBuffer,
                // Pass light buffer
                lightBuffer,
                // Pass texture pixels
                texturePixelBuffer,
                // Pass texture metadata
                textureInfoBuffer,
                // Pass image width
                width,
                // Pass image height
                height,
                // Pass uploaded triangle count
                uploadedTriangleCount,
                // Pass uploaded BVH count
                uploadedBvhNodeCount,
                // Pass uploaded light count
                uploadedLightCount,
                // Pass tile row count
                tileRows,
                // Pass camera position
                cameraPosition,
                // Pass camera basis
                basis,
                // Pass bounce count
                bounceCount,
                // Pass sample index
                sampleIndex,
                // Pass sample count
                sampleCount,
                // Pass constants byte size
                constantBytes,
                // Pass render settings
                settings,
                // Pass field of view
                fieldOfViewDegrees,
                // Pass cancellation token
                cancellationToken);
        // End batched dispatch branch
        }
        // Handle preview callback present
        else
        // Begin preview dispatch branch
        {
            // Begin preview tile dispatch call
            DispatchTilesWithPreview(
                // Pass graphics device
                gd,
                // Pass resource factory
                factory,
                // Pass compute pipeline
                pipeline,
                // Pass shared resource set
                resourceSet,
                // Pass constants buffer
                constantsBuffer,
                // Pass output buffer
                outputBuffer,
                // Pass staging buffer
                stagingBuffer,
                // Pass output byte size
                outputBytes,
                // Pass image width
                width,
                // Pass image height
                height,
                // Pass uploaded triangle count
                uploadedTriangleCount,
                // Pass uploaded BVH count
                uploadedBvhNodeCount,
                // Pass uploaded light count
                uploadedLightCount,
                // Pass tile row count
                tileRows,
                // Pass camera position
                cameraPosition,
                // Pass camera basis
                basis,
                // Pass bounce count
                bounceCount,
                // Pass sample index
                sampleIndex,
                // Pass sample count
                sampleCount,
                // Pass render settings
                settings,
                // Pass field of view
                fieldOfViewDegrees,
                // Pass cancellation token
                cancellationToken,
                // Pass progress callback
                progressCallback);
        // End preview dispatch branch
        }

        // Log tile completion
        Stage("All compute tiles completed");

        // Log final readback start
        Stage("Copy output buffer to staging buffer");
        // Read back final output image
        RenderImage image = ReadBackOutputImage(gd, factory, outputBuffer, stagingBuffer, outputBytes, width, height, cancellationToken, "final readback");
        // Build triangle truncation note
        string triangleTruncated = sourceTriangleCount > uploadedTriangleCount ? $", triangles truncated from {sourceTriangleCount}" : string.Empty;
        // Build light truncation note
        string lightTruncated = sourceLightCount > uploadedLightCount ? $", lights truncated from {sourceLightCount}" : string.Empty;
        // Compose render details string
        details = $"VULKAN GPU COMPUTE BVH TRACE - {width}x{height}, revision={prepared.CacheStamp.Revision}, {uploadedTriangleCount} triangles{triangleTruncated}, {uploadedBvhNodeCount} BVH nodes, {uploadedLightCount} lights{lightTruncated}, textures={prepared.TextureCount}, material=linear-srgb+pbr+smooth-normal+normal-map+occlusion+ibl, bounces={Math.Clamp(bounceCount, 0, 8)}, samples={sampleIndex + 1}-{sampleIndex + sampleCount}, fov={fieldOfViewDegrees:0.##}, exposure={settings.Exposure:0.###}, ambient={settings.AmbientStrength:0.###}, shadows={settings.UseShadows}, tileRows={tileRows}";
        // Log successful render completion
        Stage("Render completed successfully");
        // Return rendered image
        return image;
    // End RenderLocked method
    }

    /// <summary>Dispatches compute tiles in bounded batched submissions.</summary>
    // Begin batched tile dispatcher
    private static void DispatchTilesBatched(
        // Accept graphics device
        GraphicsDevice gd,
        // Accept resource factory
        ResourceFactory factory,
        // Accept compute pipeline
        Pipeline pipeline,
        // Accept resource layout
        ResourceLayout layout,
        // Accept output buffer
        DeviceBuffer outputBuffer,
        // Accept triangle buffer
        DeviceBuffer triangleBuffer,
        // Accept BVH buffer
        DeviceBuffer bvhBuffer,
        // Accept light buffer
        DeviceBuffer lightBuffer,
        // Accept texture pixel buffer
        DeviceBuffer texturePixelBuffer,
        // Accept texture info buffer
        DeviceBuffer textureInfoBuffer,
        // Accept image width
        int width,
        // Accept image height
        int height,
        // Accept triangle count
        int triangleCount,
        // Accept BVH node count
        int bvhNodeCount,
        // Accept light count
        int lightCount,
        // Accept tile row count
        int tileRows,
        // Accept camera position
        Vec3 cameraPosition,
        // Accept camera basis
        CameraBasis basis,
        // Accept bounce count
        int bounceCount,
        // Accept sample index
        int sampleIndex,
        // Accept sample count
        int sampleCount,
        // Accept constants byte size
        uint constantBytes,
        // Accept render settings
        RenderSettings settings,
        // Accept field of view
        double fieldOfViewDegrees,
        // Accept cancellation token
        CancellationToken cancellationToken)
    // Begin DispatchTilesBatched method
    {
        // Choose tiles per submission
        int tilesPerSubmit = ChooseTilesPerSubmit(tileRows, bounceCount, sampleCount, triangleCount, width, lightCount);
        // Log batched submission plan
        Stage($"Batch Vulkan tile dispatches in bounded submissions: tileRows={tileRows}, tilesPerSubmit={tilesPerSubmit}");

        // Loop over image tile rows
        for (int tileY = 0; tileY < height;)
        // Begin tile batch loop
        {
            // Create per batch resource list
            List<(DeviceBuffer Constants, ResourceSet Set, int TileY, int Rows)> tileResources = new();
            // Guard batch resource cleanup
            try
            // Begin batch try block
            {
                // Loop tiles within one batch
                for (int tileIndex = 0; tileIndex < tilesPerSubmit && tileY < height; tileIndex++, tileY += tileRows)
                // Begin tile preparation loop
                {
                    // Check cancellation before tile prep
                    ThrowIfCancellationRequested(cancellationToken, $"Prepare batched tile y={tileY}");
                    // Clamp current tile row count
                    int currentTileRows = Math.Min(tileRows, height - tileY);
                    // Create per tile constants buffer
                    DeviceBuffer tileConstants = factory.CreateBuffer(new BufferDescription(constantBytes, BufferUsage.UniformBuffer));
                    // Upload per tile camera constants
                    gd.UpdateBuffer(tileConstants, 0, new CameraConstants(cameraPosition, basis, width, height, triangleCount, lightCount, bounceCount, sampleIndex, tileY, currentTileRows, bvhNodeCount, sampleCount, settings, fieldOfViewDegrees));
                    // Begin per tile resource set
                    ResourceSet tileSet = factory.CreateResourceSet(new ResourceSetDescription(
                        // Bind shared layout
                        layout,
                        // Bind output buffer
                        outputBuffer,
                        // Bind triangle buffer
                        triangleBuffer,
                        // Bind BVH buffer
                        bvhBuffer,
                        // Bind light buffer
                        lightBuffer,
                        // Bind per tile constants
                        tileConstants,
                        // Bind texture pixels
                        texturePixelBuffer,
                        // Bind texture metadata
                        textureInfoBuffer));
                    // Record tile batch resources
                    tileResources.Add((tileConstants, tileSet, tileY, currentTileRows));
                // End tile preparation loop
                }

                // Create batch command list
                using CommandList commandList = factory.CreateCommandList();
                // Begin batch command recording
                commandList.Begin();
                // Bind compute pipeline
                commandList.SetPipeline(pipeline);
                // Loop prepared batch tiles
                foreach (var tile in tileResources)
                // Begin dispatch recording loop
                {
                    // Check cancellation before tile record
                    ThrowIfCancellationRequested(cancellationToken, $"Record batched tile y={tile.TileY}");
                    // Bind tile resource set
                    commandList.SetComputeResourceSet(0, tile.Set);
                    // Dispatch compute workgroups for tile
                    commandList.Dispatch(((uint)width + 7u) / 8u, ((uint)tile.Rows + 7u) / 8u, 1u);
                // End dispatch recording loop
                }
                // End batch command recording
                commandList.End();

                // Find first batch tile offset
                int firstTileY = tileResources.Count == 0 ? tileY : tileResources[0].TileY;
                // Find last batch tile offset
                int lastTileY = tileResources.Count == 0 ? tileY : tileResources[^1].TileY;
                // Log batch submission range
                Stage($"Submit bounded Vulkan tile batch: count={tileResources.Count}, y={firstTileY}-{lastTileY}");
                // Submit batch command list
                gd.SubmitCommands(commandList);
                // Wait for batch completion
                gd.WaitForIdle();
                // Log batch completion
                Stage("Bounded Vulkan tile batch idle returned");
            // End batch try block
            }
            // Ensure batch resources release
            finally
            // Begin batch cleanup block
            {
                // Loop batch resources in reverse
                for (int i = tileResources.Count - 1; i >= 0; i--)
                // Begin resource disposal loop
                {
                    // Dispose tile resource set
                    try { tileResources[i].Set.Dispose(); } catch { }
                    // Dispose tile constants buffer
                    try { tileResources[i].Constants.Dispose(); } catch { }
                // End resource disposal loop
                }
            // End batch cleanup block
            }
        // End tile batch loop
        }
    // End DispatchTilesBatched method
    }

    /// <summary>Chooses how many tiles to submit per command list.</summary>
    // Begin tiles per submit chooser
    private static int ChooseTilesPerSubmit(int tileRows, int bounceCount, int sampleCount, int triangleCount, int width, int lightCount)
    // Begin ChooseTilesPerSubmit method
    {
        // tileRows limits the size of one dispatch.  It does not limit the size
        // of a command-list submission when every tile is recorded into a single
        // command list.  Deep-bounce scenes can therefore still hit a driver
        // watchdog and come back black even though each individual dispatch is
        // small.  Bound the number of tile dispatches per submit as well.
        // Estimate direct shadow rays
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        // Estimate rays per pixel
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        // Estimate BVH visits per ray
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        // Estimate work per tile
        long workPerTile = Math.Max(1L, width) * Math.Max(1L, tileRows) * approximateNodeVisits * raysPerPixel * Math.Max(1L, sampleCount);

        // Define target work per submit
        const long targetWorkPerSubmit = 96_000_000L;
        // Compute tiles per submit count
        int computed = (int)Math.Clamp(targetWorkPerSubmit / Math.Max(1L, workPerTile), 1L, 8L);

        // Check heavy bounce workload
        if (bounceCount >= 6)
            // Limit heavy bounces to one
            computed = Math.Min(computed, 1);
        // Check moderate workload case
        else if (sampleCount > 1 || bounceCount >= 3)
            // Limit moderate work to two
            computed = Math.Min(computed, 2);

        // Return at least one tile
        return Math.Max(1, computed);
    // End ChooseTilesPerSubmit method
    }

    /// <summary>Dispatches compute tiles while publishing progress previews.</summary>
    // Begin preview tile dispatcher
    private static void DispatchTilesWithPreview(
        // Accept graphics device
        GraphicsDevice gd,
        // Accept resource factory
        ResourceFactory factory,
        // Accept compute pipeline
        Pipeline pipeline,
        // Accept shared resource set
        ResourceSet resourceSet,
        // Accept constants buffer
        DeviceBuffer constantsBuffer,
        // Accept output buffer
        DeviceBuffer outputBuffer,
        // Accept staging buffer
        DeviceBuffer stagingBuffer,
        // Accept output byte size
        uint outputBytes,
        // Accept image width
        int width,
        // Accept image height
        int height,
        // Accept triangle count
        int triangleCount,
        // Accept BVH node count
        int bvhNodeCount,
        // Accept light count
        int lightCount,
        // Accept tile row count
        int tileRows,
        // Accept camera position
        Vec3 cameraPosition,
        // Accept camera basis
        CameraBasis basis,
        // Accept bounce count
        int bounceCount,
        // Accept sample index
        int sampleIndex,
        // Accept sample count
        int sampleCount,
        // Accept render settings
        RenderSettings settings,
        // Accept field of view
        double fieldOfViewDegrees,
        // Accept cancellation token
        CancellationToken cancellationToken,
        // Accept progress callback
        Action<RenderImage, string> progressCallback)
    // Begin DispatchTilesWithPreview method
    {
        // Loop image tiles for preview
        for (int tileY = 0; tileY < height; tileY += tileRows)
        // Begin preview tile loop
        {
            // Check cancellation before tile dispatch
            ThrowIfCancellationRequested(cancellationToken, $"Dispatch tile y={tileY}");
            // Clamp preview tile rows
            int currentTileRows = Math.Min(tileRows, height - tileY);
            // Upload preview tile constants
            gd.UpdateBuffer(constantsBuffer, 0, new CameraConstants(cameraPosition, basis, width, height, triangleCount, lightCount, bounceCount, sampleIndex, tileY, currentTileRows, bvhNodeCount, sampleCount, settings, fieldOfViewDegrees));

            // Create preview tile command list
            using CommandList tileCommandList = factory.CreateCommandList();
            // Begin preview command recording
            tileCommandList.Begin();
            // Bind compute pipeline
            tileCommandList.SetPipeline(pipeline);
            // Bind shared resource set
            tileCommandList.SetComputeResourceSet(0, resourceSet);
            // Dispatch preview tile workgroups
            tileCommandList.Dispatch(((uint)width + 7u) / 8u, ((uint)currentTileRows + 7u) / 8u, 1u);
            // End preview command recording
            tileCommandList.End();

            // Submit preview tile commands
            gd.SubmitCommands(tileCommandList);
            // Wait for preview tile completion
            gd.WaitForIdle();

            // Decide whether to publish preview
            bool shouldPublishTile = ShouldPublishTileProgress(tileY, currentTileRows, tileRows, height, enabled: true, width, triangleCount);
            // Throttle tile completion logging
            if ((tileY / Math.Max(1, tileRows)) % 8 == 0)
                // Log tile completion
                Stage($"Tile completed: y={tileY}, rows={currentTileRows}");

            // Check preview publish decision
            if (shouldPublishTile)
            // Begin preview publish block
            {
                // Log preview publication
                Stage($"Publish partial Vulkan tile preview: y={tileY}, rows={currentTileRows}");
                // Read back partial preview image
                RenderImage partial = ReadBackOutputImage(gd, factory, outputBuffer, stagingBuffer, outputBytes, width, height, cancellationToken, "partial tile readback");
                // Publish partial preview image
                progressCallback(partial, $"VULKAN GPU - tile {Math.Min(height, tileY + currentTileRows)}/{height}, samples {sampleIndex + 1}-{sampleIndex + sampleCount}");
            // End preview publish block
            }
        // End preview tile loop
        }
    // End DispatchTilesWithPreview method
    }

    /// <summary>Returns true when the current tile should publish a progress preview.</summary>
    // Begin preview publish decider
    private static bool ShouldPublishTileProgress(int tileY, int currentTileRows, int tileRows, int height, bool enabled, int width, int triangleCount)
    // Begin ShouldPublishTileProgress method
    {
        // Check preview publishing enabled
        if (!enabled)
            // Skip disabled preview publish
            return false;

        // Compute completed row count
        int completedRows = tileY + currentTileRows;
        // Check final tile reached
        if (completedRows >= height)
            // Publish final tile preview
            return true;

        // Large scenes/resolutions caused very high apparent memory usage because
        // every partial preview creates a full Bitmap and hands it to the UI.
        // For heavy scenes, only publish the final tile for each sample.
        // Compute total pixel count
        long pixelCount = (long)Math.Max(1, width) * Math.Max(1, height);
        // Check heavy scene size
        if (triangleCount > 50000 || pixelCount > 1_000_000)
            // Skip previews for heavy scenes
            return false;

        // Check first tile
        if (tileY == 0)
            // Publish first tile preview
            return true;

        // Publish about 8 partial images per sample for modest scenes.
        // Compute preview row interval
        int publishEveryRows = Math.Max(Math.Max(1, tileRows), Math.Max(1, height / 8));
        // Publish when interval crossed
        return completedRows / publishEveryRows != tileY / publishEveryRows;
    // End ShouldPublishTileProgress method
    }

    /// <summary>Clears the GPU output buffer in small chunks.</summary>
    // Begin output buffer clearer
    private static void ZeroOutputBuffer(GraphicsDevice gd, DeviceBuffer outputBuffer, uint outputBytes)
    // Begin ZeroOutputBuffer method
    {
        // Avoid allocating one giant byte[width * height * 4] just to clear the
        // output.  Update the GPU buffer in small reusable chunks instead.
        // Define one megabyte chunk size
        const int ChunkBytes = 1024 * 1024;
        // Allocate zero filled chunk
        byte[] zeroChunk = new byte[Math.Min(ChunkBytes, checked((int)Math.Min(outputBytes, int.MaxValue)))];
        // Initialize buffer write offset
        uint offset = 0;
        // Loop over output buffer chunks
        while (offset < outputBytes)
        // Begin buffer clear loop
        {
            // Compute current chunk byte count
            int count = checked((int)Math.Min((uint)zeroChunk.Length, outputBytes - offset));
            // Check full sized chunk
            if (count == zeroChunk.Length)
            // Begin full chunk branch
            {
                // Upload full zero chunk
                gd.UpdateBuffer(outputBuffer, offset, zeroChunk);
            // End full chunk branch
            }
            // Handle final partial chunk
            else
            // Begin partial chunk branch
            {
                // Last chunk only; keep this allocation below 1 MB.
                // Upload final partial zeros
                gd.UpdateBuffer(outputBuffer, offset, new byte[count]);
            // End partial chunk branch
            }
            // Advance buffer write offset
            offset += (uint)count;
        // End buffer clear loop
        }
    // End ZeroOutputBuffer method
    }

    /// <summary>Reads the GPU output buffer back into a render image.</summary>
    // Begin output readback helper
    private static RenderImage ReadBackOutputImage(GraphicsDevice gd, ResourceFactory factory, DeviceBuffer outputBuffer, DeviceBuffer stagingBuffer, uint outputBytes, int width, int height, CancellationToken cancellationToken, string stageName)
    // Begin ReadBackOutputImage method
    {
        // Log staging copy start
        Stage($"Copy output buffer to staging buffer ({stageName})");
        // Create copy command list
        using (CommandList copyCommandList = factory.CreateCommandList())
        // Begin copy command block
        {
            // Begin copy command recording
            copyCommandList.Begin();
            // Copy output to staging buffer
            copyCommandList.CopyBuffer(outputBuffer, 0, stagingBuffer, 0, outputBytes);
            // End copy command recording
            copyCommandList.End();
            // Submit copy commands
            gd.SubmitCommands(copyCommandList);
            // Wait for copy completion
            gd.WaitForIdle();
        // End copy command block
        }
        // Log copy completion
        Stage($"GPU copy/readback idle returned ({stageName})");

        // Check cancellation before buffer map
        ThrowIfCancellationRequested(cancellationToken, "Map staging buffer for readback: " + stageName);
        // Map staging buffer for read
        MappedResourceView<uint> mapped = gd.Map<uint>(stagingBuffer, MapMode.Read);
        // Guard staging buffer unmap
        try
        // Begin readback conversion block
        {
            // Log bitmap conversion start
            Stage("Convert readback to bitmap: " + stageName);
            // Convert mapped data to image
            return ToRenderImage(mapped, width, height);
        // End readback conversion block
        }
        // Ensure staging buffer unmapped
        finally
        // Begin readback cleanup block
        {
            // Unmap staging buffer after readback
            gd.Unmap(stagingBuffer);
        // End readback finally block
        }
    // End ReadBackOutputImage method
    }

    /// <summary>Returns the cached GPU scene buffers, rebuilding them when stale.</summary>
    // Declare cached scene getter
    private static PreparedComputeScene GetOrCreatePreparedScene(GraphicsDevice gd, Scene scene, CancellationToken cancellationToken)
    // Begin GetOrCreatePreparedScene method
    {
        // Lock device for scene cache
        lock (DeviceSync)
        // Begin device lock block
        {
            // Check cached scene still matches
            if (preparedScene != null && preparedScene.CacheStamp.Matches(scene))
            // Begin cache hit block
            {
                // Log cached scene reuse
                Stage("Reuse cached Vulkan compute scene buffers");
                // Return cached prepared scene
                return preparedScene;
            // End cache hit block
            }

            // Check stale scene needs disposal
            if (preparedScene != null)
            // Begin stale scene cleanup
            {
                // Wait for GPU idle quietly
                try { gd.WaitForIdle(); } catch { }
                // Dispose stale scene quietly
                try { preparedScene.Dispose(); } catch { }
                // Clear stale scene reference
                preparedScene = null;
            // End stale scene cleanup
            }

            // Log texture metadata build
            Stage("Build cached texture metadata");
            // Build texture upload metadata
            TextureBuildResult textureBuild = BuildTextureBuffers(scene);
            // Log texture build summary
            Stage($"Texture metadata built: textures={textureBuild.TextureCount}, pixels={textureBuild.PixelCount}, infos={textureBuild.Infos.Length}");

            // Count source scene triangles
            int sourceTriangleCount = scene.Triangles.Count;
            // Check scene has triangles
            if (sourceTriangleCount == 0)
                // Reject empty triangle scene
                throw new InvalidOperationException("Vulkan scene render requires at least one triangle.");

            // The BVH keeps only an integer triangle-order array and compact CPU
            // nodes. GPU records are generated directly into small upload chunks.
            // Log BVH build start
            Stage("Build cached traversal BVH");
            // Build GPU traversal BVH
            BvhBuildResult bvh = BuildGpuBvh(scene);
            // Build GPU light buffer
            GpuLight[] lights = BuildLightBuffer(scene, out int sourceLightCount);

            // Measure GPU triangle stride
            int triangleStride = Marshal.SizeOf<GpuTriangle>();
            // Measure GPU BVH node stride
            int bvhStride = Marshal.SizeOf<GpuBvhNode>();
            // Measure GPU light stride
            int lightStride = Marshal.SizeOf<GpuLight>();
            // Measure texture info stride
            int textureInfoStride = Marshal.SizeOf<GpuTextureInfo>();
            // Size triangle buffer bytes
            uint triangleBytes = CheckedSceneBufferSize((long)sourceTriangleCount * triangleStride, triangleStride, "triangle");
            // Size BVH buffer bytes
            uint bvhBytes = CheckedSceneBufferSize((long)bvh.Nodes.Length * bvhStride, bvhStride, "BVH");
            // Size light buffer bytes
            uint lightBytes = CheckedSceneBufferSize((long)lights.Length * lightStride, lightStride, "light");
            // Size texture pixel buffer
            uint texturePixelBytes = CheckedSceneBufferSize(textureBuild.PixelCount * sizeof(uint), sizeof(uint), "texture pixel");
            // Size texture info buffer
            uint textureInfoBytes = CheckedSceneBufferSize((long)textureBuild.Infos.Length * textureInfoStride, textureInfoStride, "texture info");
            // Total scene buffer bytes
            long sceneBufferBytes = (long)triangleBytes + bvhBytes + lightBytes + texturePixelBytes + textureInfoBytes;
            // Log scene buffer summary
            Stage($"Create cached Vulkan compute scene: {sceneBufferBytes / (1024.0 * 1024.0):F1} MB, triangles={sourceTriangleCount}, BVH nodes={bvh.Nodes.Length}, textures={textureBuild.TextureCount}");

            // Get device resource factory
            ResourceFactory factory = gd.ResourceFactory;
            // Declare triangle buffer handle
            DeviceBuffer? triangleBuffer = null;
            // Declare BVH buffer handle
            DeviceBuffer? bvhBuffer = null;
            // Declare light buffer handle
            DeviceBuffer? lightBuffer = null;
            // Declare pixel buffer handle
            DeviceBuffer? texturePixelBuffer = null;
            // Declare texture info handle
            DeviceBuffer? textureInfoBuffer = null;
            // Guard scene buffer creation
            try
            // Begin buffer creation block
            {
                // Create GPU triangle buffer
                triangleBuffer = factory.CreateBuffer(new BufferDescription(triangleBytes, BufferUsage.StructuredBufferReadOnly, (uint)triangleStride));
                // Create GPU BVH buffer
                bvhBuffer = factory.CreateBuffer(new BufferDescription(bvhBytes, BufferUsage.StructuredBufferReadOnly, (uint)bvhStride));
                // Create GPU light buffer
                lightBuffer = factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, (uint)lightStride));
                // Create texture pixel buffer
                texturePixelBuffer = factory.CreateBuffer(new BufferDescription(texturePixelBytes, BufferUsage.StructuredBufferReadOnly, sizeof(uint)));
                // Create texture info buffer
                textureInfoBuffer = factory.CreateBuffer(new BufferDescription(textureInfoBytes, BufferUsage.StructuredBufferReadOnly, (uint)textureInfoStride));

                // Abort if upload cancelled
                ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan triangle buffer");
                // Upload triangles to GPU
                UploadGpuTriangles(gd, triangleBuffer, scene.Triangles, bvh.TriangleIndices, textureBuild.TextureIds, cancellationToken);
                // Release CPU triangle order
                bvh.TriangleIndices = Array.Empty<int>();
                // Release texture id map
                textureBuild.TextureIds.Clear();

                // Upload BVH nodes to GPU
                UploadGpuBvhNodes(gd, bvhBuffer, bvh.Nodes, bvhStride, cancellationToken);
                // Record BVH node count
                int bvhNodeCount = bvh.Nodes.Length;
                // Release CPU BVH nodes
                bvh.Nodes = Array.Empty<CpuBvhNode>();

                // Upload lights to GPU
                gd.UpdateBuffer(lightBuffer, 0, lights.Length == 0 ? new[] { default(GpuLight) } : lights);
                // Upload texture pixels to GPU
                UploadTexturePixels(gd, texturePixelBuffer, textureBuild, cancellationToken);
                // Upload texture infos to GPU
                gd.UpdateBuffer(textureInfoBuffer, 0, textureBuild.Infos.Length == 0 ? new[] { new GpuTextureInfo(0, 1, 1) } : textureBuild.Infos);
                // Release CPU texture infos
                textureBuild.Infos = Array.Empty<GpuTextureInfo>();

                // Create cached prepared scene
                preparedScene = new PreparedComputeScene
                // Begin scene initializer block
                {
                    // Store source scene reference
                    Scene = scene,
                    // Capture scene cache stamp
                    CacheStamp = SceneCacheStamp.Capture(scene),
                    // Store triangle buffer
                    TriangleBuffer = triangleBuffer,
                    // Store BVH buffer
                    BvhBuffer = bvhBuffer,
                    // Store light buffer
                    LightBuffer = lightBuffer,
                    // Store pixel buffer
                    TexturePixelBuffer = texturePixelBuffer,
                    // Store texture info buffer
                    TextureInfoBuffer = textureInfoBuffer,
                    // Store source triangle count
                    SourceTriangleCount = sourceTriangleCount,
                    // Store GPU triangle count
                    TriangleCount = sourceTriangleCount,
                    // Store BVH node count
                    BvhNodeCount = bvhNodeCount,
                    // Store source light count
                    SourceLightCount = sourceLightCount,
                    // Store GPU light count
                    LightCount = lights.Length,
                    // Store texture count
                    TextureCount = textureBuild.TextureCount,
                    // Store scene buffer bytes
                    SceneBufferBytes = sceneBufferBytes
                // End scene initializer block
                };
                // Log memory after upload
                StageMemory("after cached Vulkan scene upload");
                // Return newly prepared scene
                return preparedScene;
            // End buffer creation block
            }
            // Catch scene upload failure
            catch
            // Begin upload cleanup block
            {
                // Dispose texture info buffer
                try { textureInfoBuffer?.Dispose(); } catch { }
                // Dispose texture pixel buffer
                try { texturePixelBuffer?.Dispose(); } catch { }
                // Dispose light buffer
                try { lightBuffer?.Dispose(); } catch { }
                // Dispose BVH buffer
                try { bvhBuffer?.Dispose(); } catch { }
                // Dispose triangle buffer
                try { triangleBuffer?.Dispose(); } catch { }
                // Rethrow upload failure
                throw;
            // End upload cleanup block
            }
        // End device lock block
        }
    // End GetOrCreatePreparedScene method
    }

    /// <summary>Uploads triangle records to the GPU in traversal order.</summary>
    // Declare triangle upload helper
    private static void UploadGpuTriangles(
        // Graphics device parameter
        GraphicsDevice gd,
        // Destination buffer parameter
        DeviceBuffer destination,
        // Source triangles parameter
        IReadOnlyList<Triangle> sourceTriangles,
        // Ordered indices parameter
        IReadOnlyList<int> orderedTriangleIndices,
        // Texture id map parameter
        IReadOnlyDictionary<TextureMap, int> textureIds,
        // Cancellation token parameter
        CancellationToken cancellationToken)
    // Begin UploadGpuTriangles method
    {
        // Define triangles per chunk
        const int TrianglesPerChunk = 2048;
        // Measure GPU triangle stride
        int stride = Marshal.SizeOf<GpuTriangle>();
        // Allocate triangle chunk buffer
        GpuTriangle[] chunk = new GpuTriangle[TrianglesPerChunk];
        // Reset pending chunk count
        int chunkCount = 0;
        // Reset uploaded triangle count
        int uploaded = 0;

        // Loop ordered triangle indices
        for (int i = 0; i < orderedTriangleIndices.Count; i++)
        // Begin triangle loop
        {
            // Convert triangle into chunk
            chunk[chunkCount++] = new GpuTriangle(sourceTriangles[orderedTriangleIndices[i]], textureIds);
            // Check chunk is full
            if (chunkCount == chunk.Length)
            // Begin chunk flush block
            {
                // Upload full triangle chunk
                gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), chunk);
                // Advance uploaded triangle count
                uploaded += chunkCount;
                // Reset pending chunk count
                chunkCount = 0;
                // Abort if upload cancelled
                ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan triangle buffer");
            // End chunk flush block
            }
        // End triangle loop
        }

        // Check leftover triangles remain
        if (chunkCount > 0)
        // Begin final chunk block
        {
            // Allocate final partial chunk
            GpuTriangle[] finalChunk = new GpuTriangle[chunkCount];
            // Copy leftover triangles
            Array.Copy(chunk, finalChunk, chunkCount);
            // Upload final triangle chunk
            gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), finalChunk);
        // End final chunk block
        }
    // End UploadGpuTriangles method
    }

    /// <summary>Uploads bounding volume hierarchy nodes to the GPU.</summary>
    // Declare BVH upload helper
    private static void UploadGpuBvhNodes(
        // Graphics device parameter
        GraphicsDevice gd,
        // Destination buffer parameter
        DeviceBuffer destination,
        // Source CPU nodes parameter
        CpuBvhNode[] source,
        // GPU node stride parameter
        int gpuStride,
        // Cancellation token parameter
        CancellationToken cancellationToken)
    // Begin UploadGpuBvhNodes method
    {
        // Check BVH nodes exist
        if (source.Length == 0)
        // Begin empty BVH block
        {
            // Upload default BVH placeholder
            gd.UpdateBuffer(destination, 0, new[] { default(GpuBvhNode) });
            // Exit empty BVH upload
            return;
        // End empty BVH block
        }

        // Define nodes per chunk
        const int NodesPerChunk = 4096;
        // Allocate BVH chunk buffer
        GpuBvhNode[] chunk = new GpuBvhNode[Math.Min(NodesPerChunk, source.Length)];
        // Reset node upload offset
        int offset = 0;
        // Loop remaining BVH nodes
        while (offset < source.Length)
        // Begin node chunk loop
        {
            // Clamp chunk node count
            int count = Math.Min(chunk.Length, source.Length - offset);
            // Loop nodes in chunk
            for (int i = 0; i < count; i++)
                // Convert CPU node to GPU
                chunk[i] = source[offset + i].ToGpu();

            // Default upload to chunk
            GpuBvhNode[] upload = chunk;
            // Check partial final chunk
            if (count != chunk.Length)
            // Begin partial chunk block
            {
                // Allocate trimmed upload array
                upload = new GpuBvhNode[count];
                // Copy trimmed node chunk
                Array.Copy(chunk, upload, count);
            // End partial chunk block
            }

            // Upload BVH node chunk
            gd.UpdateBuffer(destination, checked((uint)((long)offset * gpuStride)), upload);
            // Advance node upload offset
            offset += count;
            // Abort if upload cancelled
            ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan BVH buffer");
        // End node chunk loop
        }
    // End UploadGpuBvhNodes method
    }

    /// <summary>Uploads texture pixels to the GPU buffer.</summary>
    // Declare pixel upload helper
    private static void UploadTexturePixels(
        // Graphics device parameter
        GraphicsDevice gd,
        // Destination buffer parameter
        DeviceBuffer destination,
        // Texture build result parameter
        TextureBuildResult textures,
        // Cancellation token parameter
        CancellationToken cancellationToken)
    // Begin UploadTexturePixels method
    {
        // Check texture uploads exist
        if (textures.Uploads.Count == 0)
        // Begin empty texture block
        {
            // Upload white pixel placeholder
            gd.UpdateBuffer(destination, 0, new[] { 0xffffffffu });
            // Exit empty texture upload
            return;
        // End empty texture block
        }

        // Loop texture uploads
        foreach (TextureUpload upload in textures.Uploads)
        // Begin texture upload loop
        {
            // Abort if upload cancelled
            ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan texture pixels");
            // Copy packed texture pixels
            uint[] pixels = upload.Texture.CopyPackedRgba32Pixels();
            // Compute expected pixel count
            int expectedPixels = checked(Math.Max(1, upload.Texture.Width) * Math.Max(1, upload.Texture.Height));
            // Validate pixel count matches
            if (pixels.Length != expectedPixels)
                // Reject mismatched pixel data
                throw new InvalidOperationException($"Texture '{upload.Texture.Name}' returned {pixels.Length} pixels; expected {expectedPixels}.");
            // Upload texture pixels to GPU
            gd.UpdateBuffer(destination, checked((uint)((long)upload.PixelOffset * sizeof(uint))), pixels);
            // Release pixel array early
            pixels = Array.Empty<uint>();
        // End texture upload loop
        }
    // End UploadTexturePixels method
    }

    /// <summary>Validates a scene buffer size against the Vulkan buffer limit.</summary>
    // Declare buffer size validator
    private static uint CheckedSceneBufferSize(long requestedBytes, int minimumBytes, string label)
    // Begin CheckedSceneBufferSize method
    {
        // Enforce minimum buffer bytes
        long bytes = Math.Max(minimumBytes, requestedBytes);
        // Check Vulkan size limit
        if (bytes > uint.MaxValue)
            // Reject oversized scene buffer
            throw new InvalidOperationException($"The Vulkan compute {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, exceeding the 4 GB Vulkan buffer limit.");
        // Return validated buffer size
        return checked((uint)bytes);
    // End CheckedSceneBufferSize method
    }

    /// <summary>Returns the shared compute shader and pipeline, creating them when needed.</summary>
    // Declare shared resources getter
    private static SharedComputeResources GetOrCreateSharedComputeResources(GraphicsDevice gd)
    // Begin GetOrCreateSharedComputeResources method
    {
        // Lock device for shared resources
        lock (DeviceSync)
        // Begin device lock block
        {
            // Check shared resources exist
            if (sharedComputeResources != null)
            // Begin resource reuse block
            {
                // Log shared resource reuse
                Stage("Reuse shared Vulkan compute shader/layout/pipeline");
                // Return shared compute resources
                return sharedComputeResources;
            // End resource reuse block
            }

            // Get device resource factory
            ResourceFactory factory = gd.ResourceFactory;
            // Log shader locate start
            Stage("Locate compute shader source");
            // Resolve deployed shader path
            string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "vulkan_scene_primary.comp");
            // Check deployed shader missing
            if (!File.Exists(shaderPath))
                // Fall back to relative path
                shaderPath = Path.Combine("Shaders", "vulkan_scene_primary.comp");
            // Check fallback shader missing
            if (!File.Exists(shaderPath))
                // Reject missing compute shader
                throw new FileNotFoundException("Vulkan scene compute shader was not found.", shaderPath);

            // Log shader read path
            Stage($"Read compute shader: {shaderPath}");
            // Read compute shader source
            string shaderSource = File.ReadAllText(shaderPath);
            // Create shader description
            ShaderDescription computeDesc = new(
                // Select compute shader stage
                ShaderStages.Compute,
                // Encode shader source bytes
                Encoding.UTF8.GetBytes(shaderSource),
                // Name shader entry point
                "main");

            // Log shader compile start
            Stage("Compile GLSL to SPIR-V / create shared shader");
            // Create compute shader module
            Shader shader = CreateComputeShaderWithDiagnostics(factory, computeDesc, shaderPath, shaderSource);

            // Guard resource layout creation
            try
            // Begin layout creation block
            {
                // Log resource layout creation
                Stage("Create shared resource layout");
                // Create shared resource layout
                ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                    // Bind output buffer slot
                    new ResourceLayoutElementDescription("OutputBuffer", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
                    // Bind triangle buffer slot
                    new ResourceLayoutElementDescription("TriangleBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    // Bind BVH buffer slot
                    new ResourceLayoutElementDescription("BvhBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    // Bind light buffer slot
                    new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    // Bind camera constants slot
                    new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Compute),
                    // Bind pixel buffer slot
                    new ResourceLayoutElementDescription("TexturePixelBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    // Bind texture info slot
                    new ResourceLayoutElementDescription("TextureInfoBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)));

                // Guard pipeline creation
                try
                // Begin pipeline creation block
                {
                    // Log compute pipeline creation
                    Stage("Create shared compute pipeline");
                    // Create shared compute pipeline
                    Pipeline pipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
                        // Pass compute shader module
                        shader,
                        // Pass resource layout
                        layout,
                        // Set X thread group size
                        8,
                        // Set Y thread group size
                        8,
                        // Set Z thread group size
                        1));

                    // Cache shared compute resources
                    sharedComputeResources = new SharedComputeResources
                    // Begin resources initializer block
                    {
                        // Store resource layout
                        Layout = layout,
                        // Store compute shader
                        Shader = shader,
                        // Store compute pipeline
                        Pipeline = pipeline
                    // End resources initializer block
                    };
                    // Log shared resources ready
                    Stage("Shared Vulkan compute resources created");
                    // Return shared compute resources
                    return sharedComputeResources;
                // End pipeline creation block
                }
                // Catch pipeline creation failure
                catch
                // Begin pipeline cleanup block
                {
                    // Dispose orphaned resource layout
                    layout.Dispose();
                    // Rethrow pipeline failure
                    throw;
                // End pipeline cleanup block
                }
            // End layout creation block
            }
            // Catch layout creation failure
            catch
            // Begin layout cleanup block
            {
                // Dispose orphaned shader module
                shader.Dispose();
                // Rethrow layout failure
                throw;
            // End layout cleanup block
            }
        // End device lock block
        }
    // End GetOrCreateSharedComputeResources method
    }

    /// <summary>Creates the compute shader with diagnostic logging on failure.</summary>
    // Declare shader factory helper
    private static Shader CreateComputeShaderWithDiagnostics(ResourceFactory factory, ShaderDescription computeDesc, string shaderPath, string shaderSource)
    // Begin CreateComputeShaderWithDiagnostics method
    {
        // Guard shader compilation
        try
        // Begin shader compile block
        {
            // Log shader source length
            Stage($"Shader source length: {shaderSource.Length} chars");
            // Log shader first line
            Stage("Shader first line: " + (shaderSource.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<empty>"));
            // Compile shader from SPIR-V
            Shader shader = factory.CreateFromSpirv(computeDesc);
            // Log shader compile success
            Stage("Shader compile/create succeeded");
            // Return compiled shader
            return shader;
        // End shader compile block
        }
        // Catch shader compile failure
        catch (Exception ex)
        // Begin shader failure block
        {
            // Log shader compile failure
            Stage("Shader compile/create failed");
            // Log failed shader path
            Stage("Shader path: " + shaderPath);
            // Log shader exception type
            Stage("Shader exception type: " + ex.GetType().FullName);
            // Log shader exception message
            Stage("Shader exception message: " + ex.Message);
            // Log exception header line
            Stage("Shader exception full text:");
            // Log full shader exception
            Stage(ex.ToString());

            // Guard diagnostic shader copy
            try
            // Begin shader copy block
            {
                // Resolve diagnostic copy path
                string diagnosticShaderCopy = Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp");
                // Save failed shader source
                File.WriteAllText(diagnosticShaderCopy, shaderSource);
                // Log diagnostic copy path
                Stage("Failed shader source copied to: " + diagnosticShaderCopy);
            // End shader copy block
            }
            // Catch diagnostic copy failure
            catch (Exception copyEx)
            // Begin copy failure block
            {
                // Log diagnostic copy failure
                Stage("Could not copy failed shader source: " + copyEx.GetType().Name + ": " + copyEx.Message);
            // End copy failure block
            }

            // Throw shader compile error
            throw new InvalidOperationException(
                // Reference shader diagnostic logs
                "Vulkan compute shader compilation failed. See " + StageLogPath + " and " + Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp") + " for details.",
                // Attach shader compile cause
                ex);
        // End shader failure block
        }
    // End CreateComputeShaderWithDiagnostics method
    }

    /// <summary>Chooses the row height for compute tile dispatches.</summary>
    // Declare tile row chooser
    private static int ChooseTileRows(int width, int height, int triangleCount, int bounceCount, int lightCount, int sampleCount)
    // Begin ChooseTileRows method
    {
        // The GPU shader traverses a BVH, so it can use larger tiles than the
        // original brute-force shader.  However, sample batching multiplies the
        // work inside every dispatch.  The previous performance patch chose 168
        // rows for a 899x672 / 8-bounce / 8-sample batch, which can make Vulkan
        // return an all-black buffer on watchdog-sensitive drivers.  Include the
        // sample count in the scheduler so each dispatch stays bounded.
        // Clamp direct shadow rays
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        // Estimate rays per pixel
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        // Estimate BVH node visits
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        // Clamp sample count
        long samples = Math.Max(1L, sampleCount);
        // Estimate work per row
        long workPerRow = Math.Max(1L, width) * approximateNodeVisits * raysPerPixel * samples;

        // Target a modest per-dispatch workload.  Command-list batching still
        // removes most WaitForIdle overhead, so many small dispatches are safer
        // than one watchdog-prone giant dispatch.
        // Define target dispatch work
        const long targetWorkPerDispatch = 64_000_000L;
        // Compute rows per dispatch
        long computedRows = Math.Max(1L, targetWorkPerDispatch / Math.Max(1L, workPerRow));

        // Default maximum tile rows
        long maxRows = 128L;
        // Check heavy render settings
        if (bounceCount >= 6 || samples >= 4)
            // Cap rows for heavy renders
            maxRows = 32L;
        // Check heaviest render settings
        if (bounceCount >= 6 && samples >= 4)
            // Cap rows for heaviest renders
            maxRows = 16L;

        // Clamp tile rows
        int rows = (int)Math.Clamp(computedRows, 1L, maxRows);
        // Return rows within height
        return Math.Min(Math.Max(1, rows), Math.Max(1, height));
    // End ChooseTileRows method
    }


    /// <summary>Builds the bounding volume hierarchy for the scene.</summary>
    // Declare BVH build helper
    private static BvhBuildResult BuildGpuBvh(Scene scene)
    // Begin BuildGpuBvh method
    {
        // Count scene triangles
        int triangleCount = scene.Triangles.Count;
        // Compute bounds padding
        float boundsPad = ComputeGpuBoundsPad(scene);
        // Build padding vector
        Vector3 pad = new(boundsPad);

        // Store only the final triangle order. Triangle bounds and centroids are
        // already present in the scene, so duplicating them in another large
        // per-triangle structure only increases peak memory.
        // Allocate triangle index array
        int[] triangleIndices = new int[triangleCount];
        // Loop triangle indices
        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            // Initialize identity triangle order
            triangleIndices[triangleIndex] = triangleIndex;

        // Log BVH bounds pad
        Stage($"GPU BVH conservative triangle bounds pad={boundsPad:G6}");

        // Allocate BVH node array
        CpuBvhNode[] nodes = new CpuBvhNode[CountGpuBvhNodes(triangleCount)];
        // Create centroid comparer
        TriangleCentroidComparer comparer = new(scene.Triangles);
        // Reset BVH node count
        int nodeCount = 0;
        // Build BVH recursively
        BuildGpuBvhRecursive(scene.Triangles, triangleIndices, 0, triangleIndices.Length, pad, comparer, nodes, ref nodeCount);

        // Validate BVH node packing
        if (nodeCount != nodes.Length)
            // Reject BVH packing mismatch
            throw new InvalidOperationException($"Internal BVH packing mismatch: nodes={nodeCount}/{nodes.Length}.");

        // Return BVH build result
        return new BvhBuildResult
        // Begin result initializer block
        {
            // Store ordered triangle indices
            TriangleIndices = triangleIndices,
            // Store BVH nodes
            Nodes = nodes
        // End result initializer block
        };
    // End BuildGpuBvh method
    }

    /// <summary>Recursively builds one bounding volume hierarchy node.</summary>
    // Declare recursive BVH builder
    private static int BuildGpuBvhRecursive(
        // Source triangles parameter
        IReadOnlyList<Triangle> triangles,
        // Triangle indices parameter
        int[] triangleIndices,
        // Range start parameter
        int start,
        // Range count parameter
        int count,
        // Bounds padding parameter
        Vector3 boundsPad,
        // Centroid comparer parameter
        TriangleCentroidComparer comparer,
        // Node output array parameter
        CpuBvhNode[] nodes,
        // Node counter parameter
        ref int nodeCount)
    // Begin BuildGpuBvhRecursive method
    {
        // Allocate current node index
        int nodeIndex = nodeCount++;

        // Define leaf triangle limit
        const int LeafSize = 4;
        // Check leaf node case
        if (count <= LeafSize)
        // Begin leaf node block
        {
            // Compute leaf bounds call
            GetLeafBounds(
                // Pass source triangles
                triangles,
                // Pass triangle indices
                triangleIndices,
                // Pass range start
                start,
                // Pass range count
                count,
                // Pass bounds padding
                boundsPad,
                // Receive leaf minimum bounds
                out Vector3 leafBoundsMin,
                // Receive leaf maximum bounds
                out Vector3 leafBoundsMax);
            // Store leaf BVH node
            nodes[nodeIndex] = new CpuBvhNode(leafBoundsMin, leafBoundsMax, start, count, 0);
            // Return leaf node index
            return nodeIndex;
        // End leaf node block
        }

        // Compute centroid bounds
        GetCentroidBounds(triangles, triangleIndices, start, count, out Vector3 centroidMin, out Vector3 centroidMax);
        // Measure centroid extent
        Vector3 extent = centroidMax - centroidMin;
        // Pick longest split axis
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        // Set comparer split axis
        comparer.Axis = axis;
        // Sort indices by centroid
        Array.Sort(triangleIndices, start, count, comparer);

        // Split left child count
        int leftCount = count / 2;
        // Split right child count
        int rightCount = count - leftCount;
        // Build left child subtree
        int left = BuildGpuBvhRecursive(triangles, triangleIndices, start, leftCount, boundsPad, comparer, nodes, ref nodeCount);
        // Build right child subtree
        int right = BuildGpuBvhRecursive(triangles, triangleIndices, start + leftCount, rightCount, boundsPad, comparer, nodes, ref nodeCount);
        // Merge child minimum bounds
        Vector3 boundsMin = Vector3.Min(nodes[left].BoundsMin, nodes[right].BoundsMin);
        // Merge child maximum bounds
        Vector3 boundsMax = Vector3.Max(nodes[left].BoundsMax, nodes[right].BoundsMax);
        // Store interior BVH node
        nodes[nodeIndex] = new CpuBvhNode(boundsMin, boundsMax, left, 0, right);
        // Return interior node index
        return nodeIndex;
    // End BuildGpuBvhRecursive method
    }

    /// <summary>Counts the hierarchy nodes needed for a triangle count.</summary>
    // Declare node counter helper
    private static int CountGpuBvhNodes(int triangleCount)
    // Begin CountGpuBvhNodes method
    {
        // Allocate node count cache
        Dictionary<int, int> cache = new();
        // Count nodes for triangles
        return Count(triangleCount);

        /// <summary>Counts the hierarchy nodes for one subtree.</summary>
        // Declare local count function
        int Count(int count)
        // Begin local Count function
        {
            // Return single leaf node
            if (count <= 4) return 1;
            // Return cached node count
            if (cache.TryGetValue(count, out int cached)) return cached;
            // Split left child count
            int leftCount = count / 2;
            // Sum subtree node counts
            int result = checked(1 + Count(leftCount) + Count(count - leftCount));
            // Cache subtree node count
            cache[count] = result;
            // Return subtree node count
            return result;
        // End local Count function
        }
    // End CountGpuBvhNodes method
    }

    /// <summary>Computes padded bounds for a leaf triangle range.</summary>
    // Declare leaf bounds helper
    private static void GetLeafBounds(
        // Source triangles parameter
        IReadOnlyList<Triangle> triangles,
        // Triangle indices parameter
        int[] triangleIndices,
        // Range start parameter
        int start,
        // Range count parameter
        int count,
        // Padding vector parameter
        Vector3 pad,
        // Minimum bounds output
        out Vector3 boundsMin,
        // Maximum bounds output
        out Vector3 boundsMax)
    // Begin GetLeafBounds method
    {
        // Initialize minimum bounds
        boundsMin = new Vector3(float.PositiveInfinity);
        // Initialize maximum bounds
        boundsMax = new Vector3(float.NegativeInfinity);
        // Loop leaf triangle range
        for (int i = start; i < start + count; i++)
        // Begin leaf bounds loop
        {
            // Fetch current triangle
            Triangle triangle = triangles[triangleIndices[i]];
            // Convert vertex A
            Vector3 a = ToVector3(triangle.A);
            // Convert vertex B
            Vector3 b = ToVector3(triangle.B);
            // Convert vertex C
            Vector3 c = ToVector3(triangle.C);
            // Expand padded minimum bounds
            boundsMin = Vector3.Min(boundsMin, Vector3.Min(a, Vector3.Min(b, c)) - pad);
            // Expand padded maximum bounds
            boundsMax = Vector3.Max(boundsMax, Vector3.Max(a, Vector3.Max(b, c)) + pad);
        // End leaf bounds loop
        }
    // End GetLeafBounds method
    }

    /// <summary>Computes centroid bounds for a triangle range.</summary>
    // Declare centroid bounds helper
    private static void GetCentroidBounds(
        // Source triangles parameter
        IReadOnlyList<Triangle> triangles,
        // Triangle indices parameter
        int[] triangleIndices,
        // Range start parameter
        int start,
        // Range count parameter
        int count,
        // Output minimum centroid bound parameter
        out Vector3 centroidMin,
        // Output maximum centroid bound parameter
        out Vector3 centroidMax)
    // Begin GetCentroidBounds body
    {
        // Initialize minimum centroid accumulator
        centroidMin = new Vector3(float.PositiveInfinity);
        // Initialize maximum centroid accumulator
        centroidMax = new Vector3(float.NegativeInfinity);
        // Loop over triangle index range
        for (int i = start; i < start + count; i++)
        // Begin per-triangle centroid loop
        {
            // Fetch current triangle centroid position
            Vector3 centroid = ToVector3(triangles[triangleIndices[i]].Centroid);
            // Accumulate minimum centroid bound
            centroidMin = Vector3.Min(centroidMin, centroid);
            // Accumulate maximum centroid bound
            centroidMax = Vector3.Max(centroidMax, centroid);
        // End per-triangle centroid loop
        }
    // End GetCentroidBounds body
    }

    /// <summary>Computes the conservative bounds padding for the scene.</summary>
    // Declare GPU bounds padding helper
    private static float ComputeGpuBoundsPad(Scene scene)
    // Begin ComputeGpuBoundsPad body
    {
        // Handle empty scene case
        if (scene.Triangles.Count == 0)
            // Return default pad for empty scene
            return 1e-4f;

        // Initialize global minimum bound
        Vector3 globalMin = new(float.PositiveInfinity);
        // Initialize global maximum bound
        Vector3 globalMax = new(float.NegativeInfinity);
        // Loop over all scene triangles
        foreach (Triangle tri in scene.Triangles)
        // Begin per-triangle bounds loop
        {
            // Convert first vertex to vector
            Vector3 a = new((float)tri.A.X, (float)tri.A.Y, (float)tri.A.Z);
            // Convert second vertex to vector
            Vector3 b = new((float)tri.B.X, (float)tri.B.Y, (float)tri.B.Z);
            // Convert third vertex to vector
            Vector3 c = new((float)tri.C.X, (float)tri.C.Y, (float)tri.C.Z);
            // Accumulate global minimum bound
            globalMin = Vector3.Min(globalMin, Vector3.Min(a, Vector3.Min(b, c)));
            // Accumulate global maximum bound
            globalMax = Vector3.Max(globalMax, Vector3.Max(a, Vector3.Max(b, c)));
        // End per-triangle bounds loop
        }

        // Measure scene bounding diagonal
        float sceneDiagonal = Vector3.Distance(globalMin, globalMax);
        // Guard against degenerate scene extent
        if (!float.IsFinite(sceneDiagonal) || sceneDiagonal <= 0.0f)
            // Return default pad for degenerate scene
            return 1e-4f;

        // Clamp scaled diagonal to pad range
        return Math.Clamp(sceneDiagonal * 1e-6f, 1e-4f, 0.01f);
    // End ComputeGpuBoundsPad body
    }

    /// <summary>Collects scene textures into GPU upload metadata.</summary>
    // Declare texture buffer builder
    private static TextureBuildResult BuildTextureBuffers(Scene scene)
    // Begin BuildTextureBuffers body
    {
        // Collect unique scene textures
        List<TextureMap> textures = new();
        // Track already collected textures
        HashSet<TextureMap> seen = new();
        // Loop over all scene triangles
        foreach (Triangle triangle in scene.Triangles)
        // Begin per-triangle texture loop
        {
            // Collect base color texture
            AddUnique(triangle.Material.Texture);
            // Collect emissive texture
            AddUnique(triangle.Material.EmissiveTexture);
            // Collect metallic roughness texture
            AddUnique(triangle.Material.MetallicRoughnessTexture);
            // Collect normal map texture
            AddUnique(triangle.Material.NormalTexture);
            // Collect occlusion texture
            AddUnique(triangle.Material.OcclusionTexture);
        // End per-triangle texture loop
        }

        // Handle textureless scene case
        if (textures.Count == 0)
        // Begin empty texture branch
        {
            // Return placeholder texture build result
            return new TextureBuildResult
            // Begin texture result initializer
            {
                // Initialize empty texture id map
                TextureIds = new Dictionary<TextureMap, int>(),
                // Initialize empty texture upload list
                Uploads = Array.Empty<TextureUpload>(),
                // Initialize single placeholder texture info
                Infos = new[] { new GpuTextureInfo(0, 1, 1) },
                // Set placeholder pixel count
                PixelCount = 1,
                // Set zero texture count
                TextureCount = 0
            // End texture result initializer
            };
        // End empty texture branch
        }

        // Allocate per-texture GPU info array
        GpuTextureInfo[] infos = new GpuTextureInfo[textures.Count];
        // Allocate texture id lookup
        Dictionary<TextureMap, int> textureIds = new(textures.Count);
        // Allocate texture upload list
        List<TextureUpload> uploads = new(textures.Count);
        // Initialize packed pixel offset
        long pixelOffset = 0;

        // Loop over unique textures
        foreach (TextureMap texture in textures)
        // Begin per-texture packing loop
        {
            // Clamp texture width to valid
            int width = Math.Max(1, texture.Width);
            // Clamp texture height to valid
            int height = Math.Max(1, texture.Height);
            // Compute texture pixel count
            long pixelLength = checked((long)width * height);
            // Guard against 32-bit offset overflow
            if (pixelOffset > int.MaxValue || pixelOffset + pixelLength > int.MaxValue)
                // Reject texture data exceeding indexing range
                throw new InvalidOperationException("Vulkan compute texture offsets exceed the shader's 32-bit indexing range.");

            // Assign next texture index
            int textureIndex = textureIds.Count;
            // Convert running offset to integer
            int offset = checked((int)pixelOffset);
            // Register texture index mapping
            textureIds.Add(texture, textureIndex);
            // Record GPU texture info entry
            infos[textureIndex] = new GpuTextureInfo(offset, width, height);
            // Queue texture pixel upload
            uploads.Add(new TextureUpload { Texture = texture, PixelOffset = offset });
            // Advance packed pixel offset
            pixelOffset = checked(pixelOffset + pixelLength);
        // End per-texture packing loop
        }

        // Return populated texture build result
        return new TextureBuildResult
        // Begin texture result initializer
        {
            // Attach texture id map
            TextureIds = textureIds,
            // Attach texture upload list
            Uploads = uploads,
            // Attach GPU texture info array
            Infos = infos,
            // Set total packed pixel count
            PixelCount = Math.Max(1, pixelOffset),
            // Set total texture count
            TextureCount = textureIds.Count
        // End texture result initializer
        };

        /// <summary>Adds a texture to the collection when not already present.</summary>
        // Declare unique texture collector helper
        void AddUnique(TextureMap? texture)
        // Begin AddUnique body
        {
            // Skip null or duplicate textures
            if (texture != null && seen.Add(texture))
                // Append newly seen texture
                textures.Add(texture);
        // End AddUnique body
        }
    // End BuildTextureBuffers body
    }

    /// <summary>Builds the GPU light buffer from enabled scene lights.</summary>
    // Declare GPU light buffer builder
    private static GpuLight[] BuildLightBuffer(Scene scene, out int sourceLightCount)
    // Begin BuildLightBuffer body
    {
        // Record total scene light count
        sourceLightCount = scene.Lights.Count;
        // Clamp lights to GPU limit
        int count = Math.Min(sourceLightCount, MaxGpuLights);
        // Allocate GPU light list
        List<GpuLight> result = new(count);
        // Loop over capped light range
        for (int i = 0; i < count; i++)
        // Begin per-light loop
        {
            // Fetch current scene light
            SceneLight light = scene.Lights[i];
            // Skip disabled lights
            if (light.Enabled)
                // Append enabled GPU light
                result.Add(new GpuLight(light));
        // End per-light loop
        }
        // Return packed GPU light array
        return result.ToArray();
    // End BuildLightBuffer body
    }

    /// <summary>Copies mapped GPU pixels into a render image.</summary>
    // Declare GPU pixel copy helper
    private static RenderImage ToRenderImage(MappedResourceView<uint> pixels, int width, int height)
    // Begin ToRenderImage body
    {
        // Allocate output pixel buffer
        uint[] copy = new uint[checked(width * height)];
        // Loop over every output pixel
        for (int i = 0; i < copy.Length; i++)
            // Copy mapped GPU pixel value
            copy[i] = pixels[i];
        // Return copied render image
        return new RenderImage(width, height, copy);
    // End ToRenderImage body
    }

    /// <summary>Converts a math vector into a GPU vector.</summary>
    // Declare double to float vector converter
    private static Vector3 ToVector3(Vec3 value) =>
        // Build float vector from components
        new((float)value.X, (float)value.Y, (float)value.Z);

    /// <summary>Converts a math vector and scalar into a GPU vector.</summary>
    // Declare vector plus scalar converter
    private static Vector4 ToVector4(Vec3 value, float w)
        // Build float vector with w
        => new((float)value.X, (float)value.Y, (float)value.Z, w);
// End renderer class body
}
