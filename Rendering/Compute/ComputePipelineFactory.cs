// File: Rendering/Compute/ComputePipelineFactory.cs
// Purpose: Shared compute layout, shader, and pipeline with caching.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ComputePipelineFactory
{
    private readonly ComputeStageLog log;
    private readonly object deviceLock;
    private SharedComputeResources? cached;

    public ComputePipelineFactory(ComputeStageLog log, object deviceLock)
    {
        this.log = log;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the shared compute shader and pipeline, creating them when needed.</summary>
    public SharedComputeResources GetOrCreate(GraphicsDevice gd)
    {
        lock (deviceLock)
        {
            if (cached != null)
            {
                log.Stage("Reuse shared Vulkan compute shader/layout/pipeline");
                return cached;
            }

            ResourceFactory factory = gd.ResourceFactory;
            using GpuResourceScope scope = new();
            Shader shader = scope.Track(new ComputeShaderCompiler(log).Create(factory));

            log.Stage("Create shared resource layout");
            ResourceLayout layout = scope.Track(factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("OutputBuffer", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
                new ResourceLayoutElementDescription("TriangleBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                new ResourceLayoutElementDescription("BvhBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Compute),
                new ResourceLayoutElementDescription("TexturePixelBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                new ResourceLayoutElementDescription("TextureInfoBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute))));

            log.Stage("Create shared compute pipeline");
            Pipeline pipeline = scope.Track(factory.CreateComputePipeline(new ComputePipelineDescription(shader, layout, 8, 8, 1)));

            cached = new SharedComputeResources { Layout = layout, Shader = shader, Pipeline = pipeline };
            scope.Commit();
            log.Stage("Shared Vulkan compute resources created");
            return cached;
        }
    }

    public void Release()
    {
        SharedComputeResources? owned = cached;
        cached = null;
        GpuDisposal.Dispose(owned);
    }
}
