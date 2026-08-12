using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LightingShowcase.CameraSystem;
using LightingShowcase.Rendering;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns Composer's render scheduling, renderer-specific settings, frame timing,
/// image presentation, cancellation and resize debouncing.
/// </summary>
internal sealed class ComposerRenderController : IDisposable
{
    private readonly ComposerSceneSession session;
    private readonly Image image;
    private readonly TextBlock statusText;
    private readonly TextBlock detailsText;
    private readonly Func<ComposerRendererKind> selectedRenderer;
    private readonly Func<string> selectedRendererLabel;
    private readonly Func<ComposerGizmoMode> selectedGizmoMode;
    private readonly CancellationToken lifetimeToken;
    private readonly Dictionary<ComposerRendererKind, double> lastFrameTimes = new();
    private readonly Dictionary<ComposerRendererKind, ComposerRenderOptions> renderOptions = new()
    {
        [ComposerRendererKind.Raster] = ComposerRenderOptions.DefaultsFor(ComposerRendererKind.Raster),
        [ComposerRendererKind.VulkanRaster] = ComposerRenderOptions.DefaultsFor(ComposerRendererKind.VulkanRaster),
        [ComposerRendererKind.VulkanCompute] = ComposerRenderOptions.DefaultsFor(ComposerRendererKind.VulkanCompute),
        [ComposerRendererKind.Cpu] = ComposerRenderOptions.DefaultsFor(ComposerRendererKind.Cpu)
    };

    private WriteableBitmap? bitmap;
    private bool rendering;
    private bool renderAgain;
    private bool pendingInteractive;
    private long renderVersion;
    private CancellationTokenSource? activeRenderCancellation;
    private CancellationTokenSource? resizeDebounceCancellation;

    public ComposerRenderController(
        ComposerSceneSession session,
        Image image,
        TextBlock statusText,
        TextBlock detailsText,
        Func<ComposerRendererKind> selectedRenderer,
        Func<string> selectedRendererLabel,
        Func<ComposerGizmoMode> selectedGizmoMode,
        CancellationToken lifetimeToken)
    {
        this.session = session;
        this.image = image;
        this.statusText = statusText;
        this.detailsText = detailsText;
        this.selectedRenderer = selectedRenderer;
        this.selectedRendererLabel = selectedRendererLabel;
        this.selectedGizmoMode = selectedGizmoMode;
        this.lifetimeToken = lifetimeToken;
    }

    public Func<bool> ObjectGizmoOnlyProvider { get; set; } = static () => false;

    public bool IsRendering => rendering;
    public int LastRenderWidth { get; private set; } = 1;
    public int LastRenderHeight { get; private set; } = 1;

    public ComposerRenderOptions GetOptions(ComposerRendererKind kind) => renderOptions[kind];

    public void SetOptions(ComposerRendererKind kind, ComposerRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        renderOptions[kind] = options;
    }

    public bool CanRenderContinuously(ComposerRendererKind renderer)
    {
        if (renderer == ComposerRendererKind.Raster)
            return true;
        if (renderer == ComposerRendererKind.Cpu)
            return false;
        if (!lastFrameTimes.TryGetValue(renderer, out double milliseconds))
            return false;
        return milliseconds <= (renderer == ComposerRendererKind.VulkanRaster ? 160.0 : 220.0);
    }

    public async Task RequestRenderAsync(bool interactive)
    {
        if (!session.HasRenderableScene || lifetimeToken.IsCancellationRequested)
            return;

        pendingInteractive = interactive;
        renderVersion++;
        if (!interactive)
            activeRenderCancellation?.Cancel();

        if (rendering)
        {
            renderAgain = true;
            return;
        }

        rendering = true;
        try
        {
            do
            {
                renderAgain = false;
                bool thisInteractive = pendingInteractive;
                pendingInteractive = false;
                long thisVersion = renderVersion;

                using CancellationTokenSource frameCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
                activeRenderCancellation = frameCancellation;
                try
                {
                    await RenderOneFrameAsync(thisInteractive, thisVersion, frameCancellation.Token);
                }
                finally
                {
                    if (ReferenceEquals(activeRenderCancellation, frameCancellation))
                        activeRenderCancellation = null;
                }
            }
            while (renderAgain && !lifetimeToken.IsCancellationRequested);
        }
        finally
        {
            rendering = false;
        }
    }

    private async Task RenderOneFrameAsync(bool interactive, long requestVersion, CancellationToken token)
    {
        ComposerRendererKind renderer = selectedRenderer();
        string rendererLabel = selectedRendererLabel();
        ComposerRenderOptions options = renderOptions[renderer];
        (int width, int height) = ChooseRenderSize(renderer, interactive, options);
        CameraDefinition camera = session.Camera.Snapshot();
        if (ComposerRenderOptions.SupportsFieldOfView(renderer))
            camera.FieldOfViewDegrees = options.FieldOfViewDegrees;

        RenderOptions.SetBitmapInterpolationMode(
            image,
            interactive ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.HighQuality);

        if (!interactive)
            statusText.Text = $"Rendering {rendererLabel} at {width}x{height}…";

        try
        {
            ComposerFrame frame = await Task.Run(
                () => session.Render(
                    renderer,
                    camera,
                    width,
                    height,
                    interactive,
                    token,
                    selectedGizmoMode(),
                    ObjectGizmoOnlyProvider(),
                    options),
                token);

            if (token.IsCancellationRequested || (!interactive && requestVersion != renderVersion))
                return;

            LastRenderWidth = frame.Image.Width;
            LastRenderHeight = frame.Image.Height;
            lastFrameTimes[renderer] = lastFrameTimes.TryGetValue(renderer, out double previous)
                ? previous * 0.70 + frame.ElapsedMilliseconds * 0.30
                : frame.ElapsedMilliseconds;

            ShowImage(frame.Image);
            double fps = frame.ElapsedMilliseconds > 0.001 ? 1000.0 / frame.ElapsedMilliseconds : 0.0;
            long workingSet = Process.GetCurrentProcess().WorkingSet64;
            statusText.Text = $"{rendererLabel}: {frame.ElapsedMilliseconds:0.0} ms ({fps:0.0} FPS) | " +
                              $"{session.ObjectCount:N0} objects | {session.TriangleCount:N0} triangles | " +
                              $"{FormatBytes(workingSet)} process memory";
            detailsText.Text = frame.Details;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion == renderVersion && !lifetimeToken.IsCancellationRequested)
                statusText.Text = $"{rendererLabel} failed: {ex.Message}";
        }
    }

    public void ScheduleResizeRender()
    {
        if (!session.HasRenderableScene || lifetimeToken.IsCancellationRequested)
            return;

        resizeDebounceCancellation?.Cancel();
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        resizeDebounceCancellation = cancellation;
        _ = RenderAfterResizeDelayAsync(cancellation);
    }

    private async Task RenderAfterResizeDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(140, cancellation.Token);
            await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(resizeDebounceCancellation, cancellation))
                resizeDebounceCancellation = null;
            cancellation.Dispose();
        }
    }

    public async Task StopCurrentRenderAsync()
    {
        renderVersion++;
        renderAgain = false;
        pendingInteractive = false;
        activeRenderCancellation?.Cancel();

        while (rendering && !lifetimeToken.IsCancellationRequested)
            await Task.Delay(8, lifetimeToken);
    }

    public void CancelCurrentRender()
    {
        renderVersion++;
        activeRenderCancellation?.Cancel();
    }

    public void ClearImage()
    {
        WriteableBitmap? old = bitmap;
        bitmap = null;
        image.Source = null;
        old?.Dispose();
        LastRenderWidth = 1;
        LastRenderHeight = 1;
    }

    private static (int Width, int Height) ChooseRenderSize(
        ComposerRendererKind renderer,
        bool interactive,
        ComposerRenderOptions options)
    {
        options.Validate();

        if (!interactive || renderer == ComposerRendererKind.Cpu)
            return (AlignToEight(Math.Max(8, options.Width)), AlignToEight(Math.Max(8, options.Height)));

        int maxWidth = renderer == ComposerRendererKind.VulkanCompute ? 640 : 960;
        int maxHeight = renderer == ComposerRendererKind.VulkanCompute ? 360 : 540;

        double scale = Math.Min(
            1.0,
            Math.Min(
                maxWidth / (double)Math.Max(1, options.Width),
                maxHeight / (double)Math.Max(1, options.Height)));

        int width = AlignToEight(Math.Max(160, (int)Math.Round(options.Width * scale)));
        int height = AlignToEight(Math.Max(96, (int)Math.Round(options.Height * scale)));
        return (width, height);
    }

    private unsafe void ShowImage(RenderImage rendered)
    {
        bool sizeChanged = bitmap == null ||
                           bitmap.PixelSize.Width != rendered.Width ||
                           bitmap.PixelSize.Height != rendered.Height;
        if (sizeChanged)
        {
            WriteableBitmap next = new(
                new PixelSize(rendered.Width, rendered.Height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
            WriteableBitmap? old = bitmap;
            bitmap = next;
            image.Source = next;
            old?.Dispose();
        }

        using ILockedFramebuffer framebuffer = bitmap!.Lock();
        fixed (uint* sourceBase = rendered.PackedRgba32)
        {
            long sourceRowBytes = checked((long)rendered.Width * sizeof(uint));
            for (int y = 0; y < rendered.Height; y++)
            {
                byte* source = (byte*)(sourceBase + y * rendered.Width);
                byte* destination = (byte*)framebuffer.Address + y * framebuffer.RowBytes;
                Buffer.MemoryCopy(source, destination, framebuffer.RowBytes, sourceRowBytes);
            }
        }
        image.InvalidateVisual();
    }

    private static int AlignToEight(int value) => Math.Max(8, (value + 7) & ~7);

    private static string FormatBytes(long bytes)
    {
        const double gib = 1024.0 * 1024.0 * 1024.0;
        const double mib = 1024.0 * 1024.0;
        return bytes >= gib ? $"{bytes / gib:0.00} GiB" : $"{bytes / mib:0} MiB";
    }

    public void Dispose()
    {
        activeRenderCancellation?.Cancel();
        resizeDebounceCancellation?.Cancel();
        bitmap?.Dispose();
        bitmap = null;
    }
}
