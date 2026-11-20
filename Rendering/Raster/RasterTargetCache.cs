// File: Rendering/Raster/RasterTargetCache.cs
// Purpose: Small LRU of render targets across interactive and settled resolutions.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterTargetCache
{
    // Interactive and settled frames use different resolutions. Keep a small
    // LRU so orbit/release does not recreate color, depth, staging and sync
    // resources every time the viewer switches between those sizes.
    private const int MaxCachedTargetSizes = 2;

    private readonly RasterStageLog log;
    private readonly Dictionary<(int Width, int Height), RasterTargets> cached = new();
    private long useSerial;

    public RasterTargetCache(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>Returns cached render targets for a resolution, evicting the least recently used size.</summary>
    public RasterTargets GetOrCreate(GraphicsDevice gd, int width, int height)
    {
        (int Width, int Height) key = (width, height);
        if (cached.TryGetValue(key, out RasterTargets? hit))
        {
            hit.LastUseSerial = ++useSerial;
            return hit;
        }

        if (cached.Count >= MaxCachedTargetSizes)
        {
            var oldest = cached.MinBy(pair => pair.Value.LastUseSerial);
            cached.Remove(oldest.Key);
            oldest.Value.Dispose();
            log.Stage($"Evict Vulkan raster target cache: {oldest.Key.Width}x{oldest.Key.Height}");
        }

        ResourceFactory factory = gd.ResourceFactory;
        Texture color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Texture depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
        Texture staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        RasterTargets created = new()
        {
            Width = width,
            Height = height,
            ColorTexture = color,
            DepthTexture = depth,
            StagingTexture = staging,
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color)),
            CommandList = factory.CreateCommandList(),
            Fence = factory.CreateFence(false),
            LastUseSerial = ++useSerial
        };
        cached.Add(key, created);
        log.Stage($"Cache Vulkan raster targets: {width}x{height} ({cached.Count}/{MaxCachedTargetSizes})");
        return created;
    }

    public void DisposeAll()
    {
        RasterTargets[] targets = cached.Values.ToArray();
        cached.Clear();
        foreach (RasterTargets target in targets)
            GpuDisposal.Dispose(target);
    }
}
