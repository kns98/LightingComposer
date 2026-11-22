// File: Rendering/Raster/RasterPipelineFactory.cs
// Purpose: Shared raster layout, shaders, and graphics pipelines with caching.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterPipelineFactory
{
    private readonly RasterStageLog log;
    private readonly object deviceLock;
    private SharedRasterResources? cached;

    public RasterPipelineFactory(RasterStageLog log, object deviceLock)
    {
        this.log = log;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the shared raster layout, shaders, and pipelines.</summary>
    public SharedRasterResources GetOrCreate(GraphicsDevice gd, OutputDescription outputs)
    {
        lock (deviceLock)
        {
            if (cached != null)
                return cached;

            ResourceFactory factory = gd.ResourceFactory;
            log.Stage("Create Vulkan raster resource layout");
            using GpuResourceScope scope = new();
            ResourceLayout layout = scope.Track(factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("TransformConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("AtlasTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment))));

            RasterShaderCompiler shaders = new(log);
            Shader vertex = scope.Track(shaders.CreateVertexShader(factory));
            Shader fragment = scope.Track(shaders.CreateFragmentShader(factory, null));
            Shader wireframeFragment = scope.Track(shaders.CreateFragmentShader(factory, "WIREFRAME_SOLID"));
            Shader dashedFragment = scope.Track(shaders.CreateFragmentShader(factory, "WIREFRAME_DASHED"));

            RasterPipelineSet set = PipelineDescriptions.Describe(outputs, layout, vertex, fragment, wireframeFragment, dashedFragment);
            Pipeline opaque = scope.Track(factory.CreateGraphicsPipeline(set.Opaque));
            Pipeline transparent = scope.Track(factory.CreateGraphicsPipeline(set.Transparent));
            Pipeline prepass = scope.Track(factory.CreateGraphicsPipeline(set.DepthPrepass));
            Pipeline wireframe = scope.Track(factory.CreateGraphicsPipeline(set.Wireframe));
            Pipeline dashed = scope.Track(factory.CreateGraphicsPipeline(set.Dashed));

            BackgroundResources background = new RasterBackgroundFactory(log).Create(gd, outputs, scope);

            cached = new SharedRasterResources
            {
                Layout = layout,
                Shaders = [vertex, fragment, wireframeFragment, dashedFragment, background.Vertex, background.Fragment],
                OpaquePipeline = opaque,
                TransparentPipeline = transparent,
                DepthPrepassPipeline = prepass,
                WireframePipeline = wireframe,
                WireframeDashedPipeline = dashed,
                BackgroundLayout = background.Layout,
                BackgroundPipeline = background.Pipeline,
                BackgroundConstants = background.Constants,
                BackgroundVertices = background.Vertices,
                BackgroundSet = background.Set
            };
            scope.Commit();
            log.Stage("Vulkan raster graphics pipelines created: CPU-raster parity opaque z-buffer path");
            return cached;
        }
    }

    public void Release()
    {
        SharedRasterResources? owned = cached;
        cached = null;
        GpuDisposal.Dispose(owned);
    }
}
