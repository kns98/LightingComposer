// File: Rendering/Raster/PipelineDescriptions.cs
// Purpose: Graphics pipeline descriptions for the shaded and wireframe passes.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed record RasterPipelineSet(
    GraphicsPipelineDescription Opaque,
    GraphicsPipelineDescription Transparent,
    GraphicsPipelineDescription DepthPrepass,
    GraphicsPipelineDescription Wireframe,
    GraphicsPipelineDescription Dashed);

internal static class PipelineDescriptions
{
    public static RasterPipelineSet Describe(
        OutputDescription outputs, ResourceLayout layout,
        Shader vertex, Shader fragment, Shader wireframeFragment, Shader dashedFragment)
    {
        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("MaterialIndex", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));

        RasterizerStateDescription solid = new(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false);
        RasterizerStateDescription wireframeState = solid;
        wireframeState.FillMode = PolygonFillMode.Wireframe;

        ShaderSetDescription shaded = new(new[] { vertexLayout }, [vertex, fragment]);
        ShaderSetDescription wireframeShaders = new(new[] { vertexLayout }, [vertex, wireframeFragment]);
        ShaderSetDescription dashedShaders = new(new[] { vertexLayout }, [vertex, dashedFragment]);
        ResourceLayout[] layouts = [layout];

        return new RasterPipelineSet(
            new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, ComparisonKind.Less),
                RasterizerState = solid,
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = shaded,
                Outputs = outputs
            },
            new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, ComparisonKind.Less),
                RasterizerState = solid,
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = shaded,
                Outputs = outputs
            },
            new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, ComparisonKind.Less),
                RasterizerState = solid,
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = wireframeShaders,
                Outputs = outputs
            },
            new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, ComparisonKind.LessEqual),
                RasterizerState = wireframeState,
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = wireframeShaders,
                Outputs = outputs
            },
            new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, ComparisonKind.Greater),
                RasterizerState = wireframeState,
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = dashedShaders,
                Outputs = outputs
            });
    }
}
