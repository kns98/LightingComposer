// File: Rendering/Raster/ShadowPassRecorder.cs
// Purpose: Records one depth prepass per shadow-casting light.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ShadowPassRecorder
{
    private static readonly RgbaFloat FarDepth = new(1.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>Records one depth prepass per shadow-casting light.</summary>
    public void Record(CommandList commandList, RasterShadowResources shadow, PreparedRasterScene prepared)
    {
        foreach (PreparedShadowLight shadowLight in prepared.ShadowLights)
        {
            int layer = shadowLight.Layer;
            if (layer < 0 || layer >= RasterLimits.MaxShadowLights)
                continue;
            commandList.SetFramebuffer(shadow.LayerFramebuffers[layer]);
            commandList.ClearColorTarget(0, FarDepth);
            commandList.ClearDepthStencil(1.0f);
            commandList.SetPipeline(shadow.DepthPipeline);
            commandList.SetGraphicsResourceSet(0, shadow.LayerSets[layer]);
            if (prepared.Stats.OpaqueVertexCount > 0)
            {
                commandList.SetVertexBuffer(0, prepared.Buffers.Opaque);
                commandList.Draw((uint)prepared.Stats.OpaqueVertexCount);
            }
            if (prepared.Stats.TransparentVertexCount > 0)
            {
                commandList.SetVertexBuffer(0, prepared.Buffers.Transparent);
                commandList.Draw((uint)prepared.Stats.TransparentVertexCount);
            }
        }
    }
}
