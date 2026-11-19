// -----------------------------------------------------------------------------
// File: Rendering/VulkanRasterTransform.cs
// Purpose: CPU-to-GPU transformation for the Vulkan rasterizer.
//
// Packs prepared scene data into GPU resources and drives scene preparation
// plus mesh-edit preview flows. Owns the CPU/GPU contract and the prepared
// scene assembly shared by both sides.
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
// Import CPU preparation helpers
using static LightingShowcase.Rendering.VulkanRasterCpu;

// Declare rendering namespace
namespace LightingShowcase.Rendering;

/// <summary>Transforms prepared CPU scene data into GPU resources for the Vulkan rasterizer.</summary>
// Declare Vulkan raster transform helper class
internal static class VulkanRasterTransform
// Begin transform helper class
{
    /// <summary>Applies the active mesh edit delta to affected GPU vertices, or restores them when idle.</summary>
    // Patch GPU vertices for mesh edits
    internal static void UpdateMeshEditPreview(
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
    internal static MeshEditPatchState BuildMeshEditPatchState(
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
    internal static void RestoreMeshEditPreview(GraphicsDevice gd, PreparedRasterScene prepared)
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
    internal static void UploadTrianglePatch(
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

    /// <summary>Returns cached vertex ranges for a transform preview selection, building them on first use.</summary>
    // Resolve cached preview vertex ranges
    internal static PreviewRangeSet GetOrCreatePreviewRanges(
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

    /// <summary>Builds a fresh prepared GPU scene without consulting the cache.</summary>
    // Declare scene builder
    internal static PreparedRasterScene BuildPreparedScene(GraphicsDevice gd, SharedRasterResources resources, Scene scene, CancellationToken cancellationToken, bool gpuTextureSamplingRequested)
    // Begin BuildPreparedScene method
    {

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
            // Build fresh prepared scene record
            PreparedRasterScene built = new PreparedRasterScene
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
            return built;
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
    // End BuildPreparedScene method
    }

    /// <summary>Uploads scene triangles into the opaque and transparent vertex buffers in chunks.</summary>
    // Declare geometry upload helper
    internal static void UploadRasterGeometry(
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
    internal static void UploadTextureAtlas(GraphicsDevice gd, Texture atlas, TextureBuildResult textures, CancellationToken cancellationToken)
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
    internal static uint CheckedBufferSize(long requestedBytes, int minimumBytes, string label)
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

    /// <summary>Packs scene textures into a GPU texture array and computes their placements.</summary>
    // Declare texture atlas builder method
    internal static TextureBuildResult BuildTextureAtlas(GraphicsDevice gd, Scene scene, bool gpuTextureSamplingRequested)
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
    internal static TextureBuildResult CreateBakedTextureResult(string? reason) => new()
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

// End VulkanRasterTransform class
}
// Declare prepared scene cache
internal sealed class PreparedRasterScene : IDisposable
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
