// File: Rendering/Raster/SceneBufferFactory.cs
// Purpose: Creates prepared scene buffers, atlas, and descriptor sets.

using System.Runtime.InteropServices;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed record SceneGpuResources(SceneGpuBuffers Buffers, SceneAtlas Atlas, SceneDescriptorSets Descriptors);

internal sealed class SceneBufferFactory
{
    /// <summary>Creates sized vertex, uniform, storage, atlas, and descriptor resources.</summary>
    public SceneGpuResources Create(
        ResourceFactory factory, ResourceLayout layout,
        RasterGeometryBuildResult geometry, int lightCount, TextureBuildResult textures,
        RasterShadowResources shadow)
    {
        int vertexStride = Marshal.SizeOf<RasterVertex>();
        int lightStride = Marshal.SizeOf<RasterLight>();
        int materialStride = Marshal.SizeOf<RasterMaterial>();

        uint opaqueBytes = CheckedBufferSize((long)geometry.OpaqueVertexCount * vertexStride, vertexStride, "opaque vertex");
        uint transparentBytes = CheckedBufferSize((long)geometry.TransparentVertexCount * vertexStride, vertexStride, "transparent vertex");
        uint lightBytes = CheckedBufferSize((long)lightCount * lightStride, lightStride, "light");
        uint materialBytes = CheckedBufferSize((long)geometry.MaterialCount * materialStride, materialStride, "material");

        using GpuResourceScope scope = new();
        DeviceBuffer opaque = scope.Track(factory.CreateBuffer(new BufferDescription(opaqueBytes, BufferUsage.VertexBuffer)));
        DeviceBuffer transparent = scope.Track(factory.CreateBuffer(new BufferDescription(transparentBytes, BufferUsage.VertexBuffer)));
        DeviceBuffer camera = scope.Track(factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterCameraConstants>(), BufferUsage.UniformBuffer)));
        DeviceBuffer identityTransform = scope.Track(factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterTransformConstants>(), BufferUsage.UniformBuffer)));
        DeviceBuffer previewTransform = scope.Track(factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterTransformConstants>(), BufferUsage.UniformBuffer)));
        DeviceBuffer light = scope.Track(factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, (uint)lightStride)));
        DeviceBuffer material = scope.Track(factory.CreateBuffer(new BufferDescription(materialBytes, BufferUsage.StructuredBufferReadOnly, (uint)materialStride)));
        Texture atlas = scope.Track(factory.CreateTexture(TextureDescription.Texture2D(
            (uint)textures.Width, (uint)textures.Height, 1, (uint)textures.PageCount,
            Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)));
        Sampler sampler = scope.Track(factory.CreateSampler(SamplerDescription.Linear));

        SceneGpuBuffers buffers = new()
        {
            Opaque = opaque, Transparent = transparent, Camera = camera,
            IdentityTransform = identityTransform, PreviewTransform = previewTransform,
            Light = light, Material = material
        };
        SceneAtlas sceneAtlas = new() { Texture = atlas, Sampler = sampler };
        SceneDescriptorSets descriptors = new()
        {
            Identity = scope.Track(factory.CreateResourceSet(new ResourceSetDescription(
                layout, camera, identityTransform, light, material, atlas, sampler,
                shadow.ShadowMatrices, shadow.ShadowMaps, shadow.ShadowSampler))),
            Preview = scope.Track(factory.CreateResourceSet(new ResourceSetDescription(
                layout, camera, previewTransform, light, material, atlas, sampler,
                shadow.ShadowMatrices, shadow.ShadowMaps, shadow.ShadowSampler)))
        };

        scope.Commit();
        return new SceneGpuResources(buffers, sceneAtlas, descriptors);
    }

    /// <summary>Validates a requested GPU buffer size against the minimum and 4 GB limit.</summary>
    private static uint CheckedBufferSize(long requestedBytes, int minimumBytes, string label)
    {
        long bytes = Math.Max(minimumBytes, requestedBytes);
        if (bytes > uint.MaxValue)
            throw new InvalidOperationException($"The Vulkan {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, which exceeds the 4 GB buffer limit.");
        return checked((uint)bytes);
    }
}
