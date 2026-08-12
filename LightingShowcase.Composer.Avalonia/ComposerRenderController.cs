/*
 * Rendering requests arrive much faster than a heavyweight renderer can necessarily finish them, so this
 * controller behaves like a small scheduler. It coalesces repeated requests, cancels obsolete non-interactive
 * frames, lowers interactive resolution when appropriate, and only presents a completed frame if it still
 * corresponds to the newest request. That prevents stale renders from flashing into the viewport after the user
 * has already moved on.
 */
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
    // Only the newest interactive render is allowed to publish. This token source belongs to the currently running
    // request so camera movement, edits, or a backend change can cancel obsolete work before it races with the next
    // frame.
    private CancellationTokenSource? activeRenderCancellation;
    // Resize events arrive in bursts. This token source cancels the previous debounce delay, so expensive rendering
    // resumes only after the latest size has had a short quiet period.
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

    // IsRendering mirrors the controller’s render-loop flag, allowing input/window code to ask whether a frame is
    // actively being produced without being able to mutate the scheduler state.
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

    // CanRenderContinuously decides whether drag/navigation can request another frame immediately. The software
    // rasterizer is always considered fast enough, the CPU ray tracer is deliberately excluded, and Vulkan modes
    // are enabled only after a measured frame time exists and stays below 160 ms for raster or 220 ms for compute.
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

    // RequestRenderAsync is the render coalescer. It increments a version for every request, cancels an active
    // frame when a new non-interactive request supersedes it, and if a render is already running merely sets
    // renderAgain. The loop then renders the newest pending state without starting overlapping frames. Cancellation
    // is propagated so shutdown or a newer request can make obsolete work stop early.
    public async Task RequestRenderAsync(bool interactive)
    {
        if (!session.HasRenderableScene || lifetimeToken.IsCancellationRequested)
            return;

        pendingInteractive = interactive;
        renderVersion++;
        if (!interactive)
            // Interactive rendering is latest-wins. Cancel the older request before starting another so a slow
            // frame cannot finish later and overwrite a newer camera/scene view.
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

    // RenderOneFrameAsync takes a snapshot of renderer choice, options, camera, output size, and gizmo mode for one
    // frame, renders that snapshot on a worker thread, and discards the result if cancellation or a newer request
    // makes it stale. Accepted frames update exponentially smoothed timing, bitmap pixels, FPS/memory statistics,
    // and backend details. Potentially blocking/CPU work runs on a worker task rather than Avalonia’s UI thread.
    // Cancellation is propagated so shutdown or a newer request can make obsolete work stop early.
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
            // Rendering is CPU/GPU orchestration work that must not block Avalonia’s UI thread. The worker task
            // produces an immutable frame that is published only after cancellation/revision checks succeed.
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

    // ScheduleResizeRender debounces resize-driven rendering. Each resize cancels the previous delay and starts a
    // linked cancellation source, so only the last resize in a burst survives long enough to request a full render.
    // Cancellation is propagated so shutdown or a newer request can make obsolete work stop early.
    public void ScheduleResizeRender()
    {
        if (!session.HasRenderableScene || lifetimeToken.IsCancellationRequested)
            return;

        resizeDebounceCancellation?.Cancel();
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        resizeDebounceCancellation = cancellation;
        _ = RenderAfterResizeDelayAsync(cancellation);
    }

    // RenderAfterResizeDelayAsync waits 140 ms after the most recent resize before requesting a non-interactive
    // frame. Cancellation is expected during continuous resizing and is swallowed; the exact cancellation source is
    // then cleared and disposed.
    private async Task RenderAfterResizeDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            // The resize debounce intentionally waits for a short quiet period. Continuous window-resize events
            // cancel this delay, avoiding a full render for every intermediate pixel size.
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

    // StopCurrentRenderAsync invalidates the current render version, clears queued follow-up work, cancels the
    // active frame, and waits in short 8 ms intervals until the render loop has actually exited. Callers can
    // therefore know that no old frame is still in flight before replacing scene state. Cancellation is propagated
    // so shutdown or a newer request can make obsolete work stop early.
    public async Task StopCurrentRenderAsync()
    {
        renderVersion++;
        renderAgain = false;
        pendingInteractive = false;
        // Interactive rendering is latest-wins. Cancel the older request before starting another so a slow frame
        // cannot finish later and overwrite a newer camera/scene view.
        activeRenderCancellation?.Cancel();

        while (rendering && !lifetimeToken.IsCancellationRequested)
            await Task.Delay(8, lifetimeToken);
    }

    // CancelCurrentRender marks any current result obsolete by advancing the version and signals its cancellation
    // token. Unlike StopCurrentRenderAsync, it does not wait for the worker to finish.
    public void CancelCurrentRender()
    {
        renderVersion++;
        activeRenderCancellation?.Cancel();
    }

    // ClearImage detaches and disposes the current WriteableBitmap, then resets remembered dimensions to 1×1.
    // Disposing the old bitmap is necessary because Avalonia bitmaps own unmanaged pixel resources.
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

    // ShowImage copies the renderer’s packed RGBA pixels into an Avalonia WriteableBitmap. It reallocates only when
    // dimensions change, locks the framebuffer for direct row copies, respects the destination row stride, then
    // invalidates the image so Avalonia repaints the new pixels. Row data is copied directly into the destination
    // buffer, so byte count and destination stride are handled explicitly.
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

    // AlignToEight rounds a positive dimension up to the next multiple of eight with bit masking, never allowing a
    // value below eight.
    private static int AlignToEight(int value) => Math.Max(8, (value + 7) & ~7);

    // FormatBytes formats process memory as GiB once it reaches one GiB and otherwise as MiB, keeping the status
    // line compact while retaining useful scale.
    private static string FormatBytes(long bytes)
    {
        const double gib = 1024.0 * 1024.0 * 1024.0;
        const double mib = 1024.0 * 1024.0;
        return bytes >= gib ? $"{bytes / gib:0.00} GiB" : $"{bytes / mib:0} MiB";
    }

    // Dispose ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
    // windows/renderers do not keep receiving work or retain unmanaged memory.
    public void Dispose()
    {
        activeRenderCancellation?.Cancel();
        resizeDebounceCancellation?.Cancel();
        bitmap?.Dispose();
        bitmap = null;
    }
}
