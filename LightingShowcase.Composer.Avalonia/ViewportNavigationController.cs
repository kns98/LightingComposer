// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ViewportNavigationController.cs
// Purpose: Viewport orbit, pan, and zoom input.
//
// Interprets mouse and gesture drags as camera navigation on every rendering backend.
// -----------------------------------------------------------------------------

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using LightingShowcase.Composer.Navigation.Windows;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns viewport camera navigation: right-drag orbit, middle/Shift-right pan,
/// mouse-wheel zoom and the native Windows Precision Touchpad path.
/// Selection and gizmo dragging remain separate editor concerns.
/// </summary>
internal sealed class ViewportNavigationController : IDisposable
{
    private enum DragMode
    {
        None,
        Orbit,
        Pan
    }

    private readonly Window owner;
    private readonly Border viewport;
    private readonly ComposerSceneSession session;
    private readonly ComposerRenderController renderer;
    private readonly TextBlock statusText;
    private readonly Func<ComposerRendererKind> selectedRenderer;
    private readonly Func<string> selectedRendererLabel;
    private readonly Action<bool> clearHover;
    private readonly Func<Point, Task> rightClick;
    private readonly CancellationToken lifetimeToken;
    private readonly DispatcherTimer trackpadFrameTimer;
    private readonly DispatcherTimer trackpadIdleRenderTimer;

    private DragMode dragMode;
    private bool rightPressed;
    private Point rightPressPoint;
    private Point previousPointer;
    private WindowsPrecisionTouchpadGestureSource? windowsTrackpadInput;
    private Win32Properties.CustomWndProcHookCallback? windowsTrackpadWndProcHook;
    private bool windowsTrackpadGestureCapturedByViewport;
    private double pendingWindowsTrackpadOrbitX;
    private double pendingWindowsTrackpadOrbitY;
    private double pendingWindowsTrackpadZoom;
    private double pendingWindowsTrackpadTurn;

    /// <summary>Initializes viewport navigation with its owner, session, renderer, and input callbacks.</summary>
    public ViewportNavigationController(
        Window owner,
        Border viewport,
        ComposerSceneSession session,
        ComposerRenderController renderer,
        TextBlock statusText,
        Func<ComposerRendererKind> selectedRenderer,
        Func<string> selectedRendererLabel,
        Action<bool> clearHover,
        Func<Point, Task> rightClick,
        CancellationToken lifetimeToken)
    {
        this.owner = owner;
        this.viewport = viewport;
        this.session = session;
        this.renderer = renderer;
        this.statusText = statusText;
        this.selectedRenderer = selectedRenderer;
        this.selectedRendererLabel = selectedRendererLabel;
        this.clearHover = clearHover;
        this.rightClick = rightClick;
        this.lifetimeToken = lifetimeToken;

        trackpadFrameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        trackpadFrameTimer.Tick += (_, _) => ApplyPendingWindowsTrackpadNavigation();

        trackpadIdleRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        trackpadIdleRenderTimer.Tick += (_, _) =>
        {
            trackpadIdleRenderTimer.Stop();
            if (session.HasRenderableScene && !lifetimeToken.IsCancellationRequested)
                _ = renderer.RequestRenderAsync(interactive: false);
        };
    }

    public bool IsNavigating => rightPressed || dragMode != DragMode.None;

    /// <summary>Begins an orbit or pan drag on right or middle press and returns true when handled.</summary>
    public bool TryHandlePointerPressed(PointerPressedEventArgs e)
    {
        if (!session.HasRenderableScene)
            return false;

        PointerPoint point = e.GetCurrentPoint(viewport);
        Point position = e.GetPosition(viewport);

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsRightButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            clearHover(false);
            dragMode = DragMode.Pan;
            previousPointer = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
            return true;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            clearHover(false);
            rightPressed = true;
            rightPressPoint = position;
            previousPointer = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>Applies orbit or pan movement from pointer drags and returns true when handled.</summary>
    public bool TryHandlePointerMoved(PointerEventArgs e)
    {
        Point current = e.GetPosition(viewport);
        if (rightPressed && dragMode == DragMode.None)
        {
            Vector movement = current - rightPressPoint;
            if (movement.Length > 5.0)
            {
                dragMode = DragMode.Orbit;
                previousPointer = current;
            }
            e.Handled = true;
            return true;
        }

        if (dragMode == DragMode.None)
            return false;

        Vector delta = current - previousPointer;
        previousPointer = current;

        if (dragMode == DragMode.Orbit)
            session.Camera.Orbit(delta.X, delta.Y);
        else
            session.Camera.Pan(delta.X, delta.Y, viewport.Bounds.Height);

        ComposerRendererKind kind = selectedRenderer();
        if (renderer.CanRenderContinuously(kind))
            _ = renderer.RequestRenderAsync(interactive: true);
        else
            statusText.Text = $"{selectedRendererLabel()}: release the mouse to render the new view.";

        e.Handled = true;
        return true;
    }

    /// <summary>Finishes a navigation drag or fires a right-click and returns true when handled.</summary>
    public async Task<bool> TryHandlePointerReleasedAsync(PointerReleasedEventArgs e)
    {
        Point releasePoint = e.GetPosition(viewport);
        if (rightPressed)
        {
            rightPressed = false;
            e.Pointer.Capture(null);
            Vector movement = releasePoint - rightPressPoint;
            if (dragMode == DragMode.Orbit)
            {
                dragMode = DragMode.None;
                await renderer.RequestRenderAsync(interactive: false);
            }
            else if (movement.Length <= 5.0)
            {
                await rightClick(releasePoint);
            }
            e.Handled = true;
            return true;
        }

        if (dragMode != DragMode.None)
        {
            dragMode = DragMode.None;
            e.Pointer.Capture(null);
            await renderer.RequestRenderAsync(interactive: false);
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>Zooms the camera from a mouse wheel event.</summary>
    public void HandleWheel(PointerWheelEventArgs e)
    {
        if (!session.HasRenderableScene)
            return;

        clearHover(false);
        session.Camera.Zoom(e.Delta.Y);
        _ = renderer.RequestRenderAsync(interactive: false);
        e.Handled = true;
    }

    /// <summary>Clears any active navigation drag after pointer capture is lost.</summary>
    public void HandleCaptureLost()
    {
        dragMode = DragMode.None;
        rightPressed = false;
    }

    /// <summary>Attaches the native Windows precision touchpad gesture source to the window.</summary>
    public void AttachWindowsTrackpadInput()
    {
        if (!OperatingSystem.IsWindows() || windowsTrackpadInput is not null)
            return;

        IPlatformHandle? handle = owner.TryGetPlatformHandle();
        if (handle is null || handle.Handle == 0)
            return;

        var source = new WindowsPrecisionTouchpadGestureSource();
        source.Orbit += OnWindowsTrackpadOrbit;
        source.Zoom += OnWindowsTrackpadZoom;
        source.Turn += OnWindowsTrackpadTurn;

        Win32Properties.CustomWndProcHookCallback hook = OnWindowsTrackpadWndProc;
        try
        {
            Win32Properties.AddWndProcHookCallback(owner, hook);
            source.Attach(handle.Handle);
            windowsTrackpadInput = source;
            windowsTrackpadWndProcHook = hook;

            if (Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_NAV_DIAGNOSTICS") == "1")
                Console.WriteLine($"[NAV-WIN32] attached {source.BackendName} to HWND 0x{handle.Handle:X}");
        }
        catch (Exception ex)
        {
            try { Win32Properties.RemoveWndProcHookCallback(owner, hook); } catch { }
            source.Orbit -= OnWindowsTrackpadOrbit;
            source.Zoom -= OnWindowsTrackpadZoom;
            source.Turn -= OnWindowsTrackpadTurn;
            source.Dispose();

            if (Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_NAV_DIAGNOSTICS") == "1")
                Console.WriteLine($"[NAV-WIN32] unavailable: {ex.Message}");
        }
    }

    /// <summary>Routes precision touchpad window messages to the gesture source when the pointer is over the viewport.</summary>
    private nint OnWindowsTrackpadWndProc(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (windowsTrackpadInput is null ||
            (message != WindowsPrecisionTouchpadGestureSource.WmPointerDown &&
             message != WindowsPrecisionTouchpadGestureSource.WmPointerUpdate &&
             message != WindowsPrecisionTouchpadGestureSource.WmPointerUp))
        {
            return 0;
        }

        if (!windowsTrackpadGestureCapturedByViewport && !viewport.IsPointerOver)
            return 0;

        bool consumed = windowsTrackpadInput.TryProcessWindowMessage(message, wParam, lParam);
        if (consumed)
        {
            windowsTrackpadGestureCapturedByViewport =
                windowsTrackpadInput.IsGestureActive ||
                message != WindowsPrecisionTouchpadGestureSource.WmPointerUp;
            handled = true;
        }

        if (message == WindowsPrecisionTouchpadGestureSource.WmPointerUp &&
            !windowsTrackpadInput.IsGestureActive)
        {
            windowsTrackpadGestureCapturedByViewport = false;
        }

        return 0;
    }

    /// <summary>Queues a pending orbit delta from a touchpad orbit event.</summary>
    private void OnWindowsTrackpadOrbit(object? sender, NativeTrackpadOrbit e)
    {
        if (!session.HasRenderableScene)
            return;
        pendingWindowsTrackpadOrbitX += e.X;
        pendingWindowsTrackpadOrbitY += e.Y;
        if (!trackpadFrameTimer.IsEnabled)
            trackpadFrameTimer.Start();
    }

    /// <summary>Queues a pending zoom delta from a touchpad zoom event.</summary>
    private void OnWindowsTrackpadZoom(object? sender, NativeTrackpadZoom e)
    {
        if (!session.HasRenderableScene)
            return;
        pendingWindowsTrackpadZoom += e.Amount;
        if (!trackpadFrameTimer.IsEnabled)
            trackpadFrameTimer.Start();
    }

    /// <summary>Queues a pending turn delta from a touchpad turn event.</summary>
    private void OnWindowsTrackpadTurn(object? sender, NativeTrackpadTurn e)
    {
        if (!session.HasRenderableScene)
            return;
        pendingWindowsTrackpadTurn += e.Radians;
        if (!trackpadFrameTimer.IsEnabled)
            trackpadFrameTimer.Start();
    }

    /// <summary>Applies queued touchpad orbit, zoom, and turn deltas to the camera.</summary>
    private void ApplyPendingWindowsTrackpadNavigation()
    {
        double orbitX = pendingWindowsTrackpadOrbitX;
        double orbitY = pendingWindowsTrackpadOrbitY;
        double zoom = pendingWindowsTrackpadZoom;
        double turn = pendingWindowsTrackpadTurn;

        pendingWindowsTrackpadOrbitX = 0.0;
        pendingWindowsTrackpadOrbitY = 0.0;
        pendingWindowsTrackpadZoom = 0.0;
        pendingWindowsTrackpadTurn = 0.0;

        bool hasOrbit = Math.Abs(orbitX) >= 1e-9 || Math.Abs(orbitY) >= 1e-9;
        bool hasZoom = Math.Abs(zoom) >= 1e-9;
        bool hasTurn = Math.Abs(turn) >= 1e-9;

        if (!hasOrbit && !hasZoom && !hasTurn)
        {
            trackpadFrameTimer.Stop();
            return;
        }

        if (!session.HasRenderableScene || lifetimeToken.IsCancellationRequested)
        {
            trackpadFrameTimer.Stop();
            return;
        }

        clearHover(false);
        if (hasOrbit)
            session.Camera.Orbit(orbitX, orbitY);
        if (hasZoom)
            session.Camera.Zoom(Math.Clamp(zoom, -8.0, 8.0));
        if (hasTurn)
            session.Camera.Turn(turn);

        if (renderer.CanRenderContinuously(selectedRenderer()))
            _ = renderer.RequestRenderAsync(interactive: true);

        trackpadIdleRenderTimer.Stop();
        trackpadIdleRenderTimer.Start();
    }

    /// <summary>Detaches the touchpad gesture source and removes its window hook.</summary>
    private void DetachWindowsTrackpadInput()
    {
        windowsTrackpadGestureCapturedByViewport = false;

        if (windowsTrackpadInput is not null)
        {
            windowsTrackpadInput.Orbit -= OnWindowsTrackpadOrbit;
            windowsTrackpadInput.Zoom -= OnWindowsTrackpadZoom;
            windowsTrackpadInput.Turn -= OnWindowsTrackpadTurn;
            windowsTrackpadInput.Dispose();
            windowsTrackpadInput = null;
        }

        if (windowsTrackpadWndProcHook is not null)
        {
            try { Win32Properties.RemoveWndProcHookCallback(owner, windowsTrackpadWndProcHook); }
            catch { }
            windowsTrackpadWndProcHook = null;
        }
    }

    /// <summary>Stops navigation timers and detaches the touchpad input.</summary>
    public void Dispose()
    {
        trackpadFrameTimer.Stop();
        trackpadIdleRenderTimer.Stop();
        pendingWindowsTrackpadOrbitX = 0.0;
        pendingWindowsTrackpadOrbitY = 0.0;
        pendingWindowsTrackpadZoom = 0.0;
        pendingWindowsTrackpadTurn = 0.0;
        DetachWindowsTrackpadInput();
    }
}
