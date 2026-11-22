// File: Rendering/Raster/RasterShadowFactory.cs
// Purpose: Device-global shadow maps, cameras, and depth pipeline.

using System.Runtime.InteropServices;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterShadowFactory
{
    private readonly RasterStageLog log;
    private readonly object deviceLock;
    private RasterShadowResources? cached;

    public RasterShadowFactory(RasterStageLog log, object deviceLock)
    {
        this.log = log;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the shared shadow maps, cameras, and depth pipeline.</summary>
    public RasterShadowResources GetOrCreate(GraphicsDevice gd)
    {
        lock (deviceLock)
        {
            if (cached != null)
                return cached;

            ResourceFactory factory = gd.ResourceFactory;
            log.Stage("Create Vulkan raster shadow resources");
            using GpuResourceScope scope = new();
            uint size = (uint)RasterLimits.ShadowMapSize;
            uint layers = (uint)RasterLimits.MaxShadowLights;
            Texture shadowMaps = scope.Track(factory.CreateTexture(TextureDescription.Texture2D(
                size, size, 1, layers, PixelFormat.R32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled)));
            Texture shadowDepth = scope.Track(factory.CreateTexture(TextureDescription.Texture2D(
                size, size, 1, layers, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil)));
            Sampler sampler = scope.Track(factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp,
                SamplerAddressMode.Clamp,
                SamplerAddressMode.Clamp,
                SamplerFilter.MinPoint_MagPoint_MipPoint,
                null,
                0,
                0,
                0,
                0,
                SamplerBorderColor.TransparentBlack)));
            DeviceBuffer matrices = scope.Track(factory.CreateBuffer(new BufferDescription(
                (uint)(RasterLimits.MaxShadowLights * 5 * 16), BufferUsage.StructuredBufferReadOnly, 16)));

            ResourceLayout depthLayout = scope.Track(factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ShadowCamera", ResourceKind.UniformBuffer, ShaderStages.Vertex))));
            RasterShaderCompiler shaders = new(log);
            Shader depthVertex = scope.Track(shaders.CreateShadowVertexShader(factory));
            Shader depthFragment = scope.Track(shaders.CreateShadowFragmentShader(factory));
            VertexLayoutDescription positionLayout = new(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
            {
                Stride = (uint)Marshal.SizeOf<RasterVertex>()
            };
            OutputDescription shadowOutputs = new(
                new OutputAttachmentDescription(PixelFormat.D32_Float_S8_UInt),
                new OutputAttachmentDescription(PixelFormat.R32_Float));
            Pipeline depthPipeline = scope.Track(factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, ComparisonKind.Less),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { depthLayout },
                ShaderSet = new ShaderSetDescription(new[] { positionLayout }, new[] { depthVertex, depthFragment }),
                Outputs = shadowOutputs
            }));

            Framebuffer[] framebuffers = new Framebuffer[RasterLimits.MaxShadowLights];
            DeviceBuffer[] uniforms = new DeviceBuffer[RasterLimits.MaxShadowLights];
            ResourceSet[] sets = new ResourceSet[RasterLimits.MaxShadowLights];
            for (int layer = 0; layer < RasterLimits.MaxShadowLights; layer++)
            {
                framebuffers[layer] = scope.Track(factory.CreateFramebuffer(new FramebufferDescription(
                    new FramebufferAttachmentDescription(shadowDepth, (uint)layer),
                    new[] { new FramebufferAttachmentDescription(shadowMaps, (uint)layer) })));
                uniforms[layer] = scope.Track(factory.CreateBuffer(new BufferDescription(80, BufferUsage.UniformBuffer)));
                sets[layer] = scope.Track(factory.CreateResourceSet(new ResourceSetDescription(depthLayout, uniforms[layer])));
            }

            cached = new RasterShadowResources
            {
                ShadowMaps = shadowMaps,
                ShadowDepth = shadowDepth,
                ShadowSampler = sampler,
                ShadowMatrices = matrices,
                DepthPipeline = depthPipeline,
                DepthLayout = depthLayout,
                DepthVertex = depthVertex,
                DepthFragment = depthFragment,
                LayerFramebuffers = framebuffers,
                LayerUniforms = uniforms,
                LayerSets = sets
            };
            scope.Commit();
            log.Stage("Vulkan raster shadow resources created");
            return cached;
        }
    }

    public void Release()
    {
        RasterShadowResources? owned = cached;
        cached = null;
        GpuDisposal.Dispose(owned);
    }
}
