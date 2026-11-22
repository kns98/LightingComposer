// File: Rendering/Raster/RasterBackgroundFactory.cs
// Purpose: Fullscreen gradient background layout, pipeline, and buffers.

using System.Runtime.InteropServices;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed record BackgroundResources(
    ResourceLayout Layout,
    Pipeline Pipeline,
    DeviceBuffer Constants,
    DeviceBuffer Vertices,
    ResourceSet Set,
    Shader Vertex,
    Shader Fragment);

internal sealed class RasterBackgroundFactory
{
    private readonly RasterStageLog log;

    public RasterBackgroundFactory(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>Creates background layout, pipeline, buffers, and descriptor set.</summary>
    public BackgroundResources Create(GraphicsDevice gd, OutputDescription outputs, GpuResourceScope scope)
    {
        ResourceFactory factory = gd.ResourceFactory;
        log.Stage("Create Vulkan raster background resources");
        ResourceLayout layout = scope.Track(factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BackgroundConstants", ResourceKind.UniformBuffer, ShaderStages.Fragment))));

        RasterShaderCompiler shaders = new(log);
        Shader vertex = scope.Track(shaders.CreateBackgroundVertexShader(factory));
        Shader fragment = scope.Track(shaders.CreateBackgroundFragmentShader(factory));
        Pipeline pipeline = scope.Track(factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: false, depthWriteEnabled: false, ComparisonKind.Always),
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [layout],
            ShaderSet = new ShaderSetDescription(
                [new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))],
                [vertex, fragment]),
            Outputs = outputs
        }));

        DeviceBuffer constants = scope.Track(factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<RasterBackgroundConstants>(), BufferUsage.UniformBuffer)));
        DeviceBuffer vertices = scope.Track(factory.CreateBuffer(new BufferDescription(
            (uint)(3 * 2 * sizeof(float)), BufferUsage.VertexBuffer)));
        gd.UpdateBuffer(vertices, 0, [-1.0f, -1.0f, 3.0f, -1.0f, -1.0f, 3.0f]);

        return new BackgroundResources(
            layout, pipeline, constants, vertices,
            scope.Track(factory.CreateResourceSet(new ResourceSetDescription(layout, constants))),
            vertex, fragment);
    }
}
