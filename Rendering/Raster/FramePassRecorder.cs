// File: Rendering/Raster/FramePassRecorder.cs
// Purpose: Records the shaded or wireframe command list for one frame.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FramePassRecorder
{
    private static readonly RgbaFloat ClearColor = new(0.035f, 0.040f, 0.050f, 1.0f);

    public void Record(
        CommandList commandList, RasterTargets targets, SharedRasterResources resources,
        PreparedRasterScene prepared, PreviewRangeSet? preview, bool wireframe, int width, int height)
    {
        commandList.Begin();
        commandList.SetFramebuffer(targets.Framebuffer);
        commandList.ClearColorTarget(0, ClearColor);
        commandList.ClearDepthStencil(1.0f);
        if (wireframe)
        {
            Draw(commandList, resources.DepthPrepassPipeline, prepared.Buffers.Opaque, prepared.Stats.OpaqueVertexCount, preview?.Opaque, prepared);
            Draw(commandList, resources.DepthPrepassPipeline, prepared.Buffers.Transparent, prepared.Stats.TransparentVertexCount, preview?.Transparent, prepared);
            commandList.ClearColorTarget(0, ClearColor);
            Draw(commandList, resources.WireframePipeline, prepared.Buffers.Opaque, prepared.Stats.OpaqueVertexCount, preview?.Opaque, prepared);
            Draw(commandList, resources.WireframePipeline, prepared.Buffers.Transparent, prepared.Stats.TransparentVertexCount, preview?.Transparent, prepared);
            Draw(commandList, resources.WireframeDashedPipeline, prepared.Buffers.Opaque, prepared.Stats.OpaqueVertexCount, preview?.Opaque, prepared);
            Draw(commandList, resources.WireframeDashedPipeline, prepared.Buffers.Transparent, prepared.Stats.TransparentVertexCount, preview?.Transparent, prepared);
        }
        else
        {
            Draw(commandList, resources.OpaquePipeline, prepared.Buffers.Opaque, prepared.Stats.OpaqueVertexCount, preview?.Opaque, prepared);
            Draw(commandList, resources.TransparentPipeline, prepared.Buffers.Transparent, prepared.Stats.TransparentVertexCount, preview?.Transparent, prepared);
        }
        commandList.CopyTexture(targets.ColorTexture, 0, 0, 0, 0, 0,
            targets.StagingTexture, 0, 0, 0, 0, 0, (uint)width, (uint)height, 1, 1);
        commandList.End();
    }

    private static void Draw(
        CommandList commandList, Pipeline pipeline, DeviceBuffer buffer, int vertexCount,
        IReadOnlyList<VertexRange>? ranges, PreparedRasterScene prepared)
    {
        if (vertexCount <= 0)
            return;
        commandList.SetPipeline(pipeline);
        commandList.SetVertexBuffer(0, buffer);
        RangeDrawDispatcher.Draw(commandList, (uint)vertexCount, ranges, prepared.Descriptors.Identity, prepared.Descriptors.Preview);
    }
}
