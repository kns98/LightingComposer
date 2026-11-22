// File: Rendering/Raster/FramePassRecorder.cs
// Purpose: Records the shaded or wireframe command list for one frame.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FramePassRecorder
{
    private static readonly RgbaFloat ClearColor = new(0.035f, 0.040f, 0.050f, 1.0f);

    public void Record(
        CommandList commandList, RasterTargets targets, SharedRasterResources resources, RasterShadowResources shadow,
        PreparedRasterScene prepared, PreviewRangeSet? preview, bool wireframe, int width, int height)
    {
        commandList.Begin();
        if (!wireframe && prepared.ShadowLights.Count > 0)
            new ShadowPassRecorder().Record(commandList, shadow, prepared);
        commandList.SetFramebuffer(targets.Framebuffer);
        commandList.ClearColorTarget(0, ClearColor);
        commandList.ClearDepthStencil(1.0f);
        DrawBackground(commandList, resources);
        if (wireframe)
        {
            Draw(commandList, resources.DepthPrepassPipeline, prepared.Buffers.Opaque, prepared.Stats.OpaqueVertexCount, preview?.Opaque, prepared);
            Draw(commandList, resources.DepthPrepassPipeline, prepared.Buffers.Transparent, prepared.Stats.TransparentVertexCount, preview?.Transparent, prepared);
            commandList.ClearColorTarget(0, ClearColor);
            DrawBackground(commandList, resources);
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

    private static void DrawBackground(CommandList commandList, SharedRasterResources resources)
    {
        commandList.SetPipeline(resources.BackgroundPipeline);
        commandList.SetVertexBuffer(0, resources.BackgroundVertices);
        commandList.SetGraphicsResourceSet(0, resources.BackgroundSet);
        commandList.Draw(3);
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
