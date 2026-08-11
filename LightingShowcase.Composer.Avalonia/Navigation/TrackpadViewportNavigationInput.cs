using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LightingShowcase.Composer.Navigation;

/// <summary>
/// Cross-platform viewport navigation focused on trackpad semantics.
///
/// Application-level mapping:
///   two-finger translation -> orbit
///   pinch / magnify         -> zoom
///
/// Avalonia can expose ordinary trackpad translation through
/// PointerWheelChanged, so that event is used as the transport for orbit even
/// though the user interaction is a trackpad gesture rather than a mouse-wheel
/// feature.
///
/// Pinch is accepted through three public Avalonia paths so different desktop
/// backends can be tested without platform-specific native code:
///   1. InputElement.PinchEvent (PinchGestureRecognizer)
///   2. PointerTouchPadGestureMagnify
///   3. Ctrl+PointerWheelChanged fallback
///
/// The adapter does not require a pressed mouse button, pointer capture, or
/// drag state for trackpad navigation.
/// </summary>
public sealed class TrackpadViewportNavigationInput : IViewportNavigationInput
{
    private const double Epsilon = 1e-6;
    private const double CameraZoomExponentPerUnit = 0.12;
    private const long OrbitSuppressionAfterZoomMs = 140;

    private readonly bool diagnosticsEnabled =
        Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_NAV_DIAGNOSTICS") == "1";

    private Control? viewport;
    private PinchGestureRecognizer? pinchRecognizer;
    private bool pinchActive;
    private double lastPinchScale = 1.0;
    private long suppressOrbitUntilMs;

    public string BackendName => "Avalonia trackpad orbit + pinch zoom";
    public bool IsAvailable => true;

    public event EventHandler<OrbitInput>? Orbit;
    public event EventHandler<ZoomInput>? Zoom;

    public void Attach(Control viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (this.viewport is not null)
            throw new InvalidOperationException("Navigation input is already attached.");

        this.viewport = viewport;

        // Trackpad two-finger translation. Avalonia documents
        // PointerWheelChanged as the common mouse-wheel / trackpad-scroll event.
        viewport.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Native touchpad magnification path when a desktop backend exposes it.
        viewport.PointerTouchPadGestureMagnify += OnTouchPadMagnify;

        // Generic two-contact pinch recognizer. Avalonia 12 exposes Pinch and
        // PinchEnded as public InputElement events.
        pinchRecognizer = new PinchGestureRecognizer();
        viewport.GestureRecognizers.Add(pinchRecognizer);
        viewport.Pinch += OnPinch;
        viewport.PinchEnded += OnPinchEnded;

        TraceNavigation($"attached on {GetPlatformName()}");
    }

    public void Detach()
    {
        if (viewport is null)
            return;

        viewport.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
        viewport.PointerTouchPadGestureMagnify -= OnTouchPadMagnify;
        viewport.Pinch -= OnPinch;
        viewport.PinchEnded -= OnPinchEnded;

        if (pinchRecognizer is not null)
            viewport.GestureRecognizers.Remove(pinchRecognizer);

        pinchRecognizer = null;
        pinchActive = false;
        lastPinchScale = 1.0;
        suppressOrbitUntilMs = 0;
        viewport = null;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Vector delta = e.Delta;
        if (Math.Abs(delta.X) <= Epsilon && Math.Abs(delta.Y) <= Epsilon)
            return;

        // Some desktop stacks represent precision-touchpad pinch as a
        // Ctrl-modified wheel stream. Treat that form as zoom instead of orbit.
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            double zoomDelta = DominantComponent(delta);
            if (Math.Abs(zoomDelta) > Epsilon)
            {
                SuppressOrbitBriefly();
                EmitZoom(zoomDelta, "ctrl-wheel");
            }
            return;
        }

        // If a dedicated pinch/magnify event is active, ignore any companion
        // wheel packets so one physical pinch cannot also rotate the camera.
        if (pinchActive || Environment.TickCount64 < suppressOrbitUntilMs)
            return;

        // Preserve the sign convention validated by the orbit-only probe.
        Orbit?.Invoke(this, new OrbitInput(-delta.X, -delta.Y));
        TraceNavigation($"orbit dx={-delta.X:0.####} dy={-delta.Y:0.####}");

        // Deliberately do not set e.Handled. The standalone probe was most
        // stable when the wheel/trackpad stream remained unconsumed.
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        double scale = e.Scale;
        if (!double.IsFinite(scale) || scale <= Epsilon)
            return;

        SuppressOrbitBriefly();

        if (!pinchActive)
        {
            pinchActive = true;
            lastPinchScale = scale;
            TraceNavigation($"pinch begin scale={scale:0.####}");
            return;
        }

        double previousScale = lastPinchScale;
        lastPinchScale = scale;
        if (previousScale <= Epsilon)
            return;

        double ratio = scale / previousScale;
        if (!double.IsFinite(ratio) || ratio <= Epsilon || Math.Abs(ratio - 1.0) <= 0.0001)
            return;

        // ComposerCamera.Zoom(amount) multiplies radius by exp(-amount * 0.12).
        // Converting the scale ratio through log makes pinch scale changes map
        // naturally to camera distance: a ratio > 1 zooms in, < 1 zooms out.
        double zoomAmount = Math.Log(ratio) / CameraZoomExponentPerUnit;
        EmitZoom(zoomAmount, "pinch");
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        pinchActive = false;
        lastPinchScale = 1.0;
        SuppressOrbitBriefly();
        TraceNavigation("pinch ended");

        // Deliberately leave the routed event unhandled for the same reason as
        // the orbit path: this adapter should observe rather than monopolize the
        // desktop input stream during platform testing.
    }

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        double raw = DominantComponent(e.Delta);
        if (Math.Abs(raw) <= Epsilon)
            return;

        SuppressOrbitBriefly();

        // Avalonia exposes the native magnification change as PointerDeltaEventArgs.
        // Keep the raw signed delta for this test release so Windows/macOS results
        // can be compared before introducing platform-specific sensitivity curves.
        EmitZoom(raw, "touchpad-magnify");
    }

    private void EmitZoom(double amount, string source)
    {
        if (!double.IsFinite(amount) || Math.Abs(amount) <= Epsilon)
            return;

        // Prevent one malformed platform packet from causing an extreme camera jump.
        amount = Math.Clamp(amount, -8.0, 8.0);
        Zoom?.Invoke(this, new ZoomInput(amount, source));
        TraceNavigation($"zoom source={source} amount={amount:0.####}");
    }

    private void SuppressOrbitBriefly()
    {
        suppressOrbitUntilMs = Environment.TickCount64 + OrbitSuppressionAfterZoomMs;
    }

    private static double DominantComponent(Vector delta) =>
        Math.Abs(delta.Y) >= Math.Abs(delta.X) ? delta.Y : delta.X;

    private void TraceNavigation(string message)
    {
        if (diagnosticsEnabled)
            Console.WriteLine($"[NAV] {message}");
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows())
            return "Windows";
        if (OperatingSystem.IsMacOS())
            return "macOS";
        if (OperatingSystem.IsLinux())
        {
            return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                ? "Linux/X11"
                : "Linux/Wayland";
        }

        return Environment.OSVersion.Platform.ToString();
    }

    public void Dispose() => Detach();
}
