// File: Rendering/Compute/FrameResources.cs
// Purpose: Per-frame output, constants, staging, and descriptor resources.

using System.Runtime.InteropServices;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FrameResources : IDisposable
{
    public DeviceBuffer Output { get; }
    public DeviceBuffer Constants { get; }
    public DeviceBuffer Staging { get; }
    public ResourceSet Set { get; private set; } = null!;
    public uint OutputBytes { get; }
    public uint ConstantBytes { get; }

    public static uint OutputBytesFor(int width, int height) =>
        checked((uint)((long)width * height * sizeof(uint)));

    public FrameResources(GraphicsDevice gd, int width, int height, ComputeStageLog log, CancellationToken token)
    {
        OutputBytes = OutputBytesFor(width, height);
        ConstantBytes = checked((uint)Marshal.SizeOf<CameraConstants>());

        ResourceFactory factory = gd.ResourceFactory;
        log.Stage("Create output buffer");
        log.ThrowIfCancellationRequested(token, "Create output buffer");
        Output = factory.CreateBuffer(new BufferDescription(OutputBytes, BufferUsage.StructuredBufferReadWrite, structureByteStride: sizeof(uint)));

        log.Stage("Create constants buffer");
        Constants = factory.CreateBuffer(new BufferDescription(ConstantBytes, BufferUsage.UniformBuffer));

        log.Stage("Create staging readback buffer");
        Staging = factory.CreateBuffer(new BufferDescription(OutputBytes, BufferUsage.Staging));
    }

    /// <summary>Creates the per-frame descriptor set binding cached scene buffers.</summary>
    public void CreateSet(ComputeStageLog log, ResourceFactory factory, ResourceLayout layout, PreparedComputeScene prepared)
    {
        log.Stage("Create resource set");
        Set = factory.CreateResourceSet(new ResourceSetDescription(
            layout,
            Output,
            prepared.Buffers.Triangles,
            prepared.Buffers.BvhNodes,
            prepared.Buffers.Lights,
            Constants,
            prepared.Buffers.TexturePixels,
            prepared.Buffers.TextureInfos));
    }

    public void Dispose()
    {
        GpuDisposal.Dispose(Set, Staging, Constants, Output);
    }
}
