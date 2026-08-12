/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 *
 * `NativeTrackpadOrbit` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`X`, `Y`) travel together because consumers need a consistent snapshot
 * rather than reading those values independently from mutable objects.
 *
 * `NativeTrackpadZoom` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Amount`) travel together because consumers need a consistent snapshot
 * rather than reading those values independently from mutable objects.
 *
 * `NativeTrackpadTurn` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Radians`) travel together because consumers need a consistent snapshot
 * rather than reading those values independently from mutable objects.
 *
 * `WindowsPrecisionTouchpadGestureSource` owns resources/subscriptions whose lifetime must be ended explicitly.
 *
 * `IsAvailable` is derived rather than separately stored: it evaluates `attached && api is not null`. Keeping the
 * value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `IsGestureActive` is derived rather than separately stored: it evaluates `gestureActive`. Keeping the value
 * computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `ReadPositiveEnvironmentDouble` reads positive environment double from the external stream/document, advancing
 * through the format in the order required to resolve references and produce valid internal data.
 *
 * `Dispose` ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
 * windows/renderers do not keep receiving work or retain unmanaged memory.
 */
namespace LightingShowcase.Composer.Navigation.Windows;

internal readonly record struct NativeTrackpadOrbit(double X, double Y);
internal readonly record struct NativeTrackpadZoom(double Amount);
internal readonly record struct NativeTrackpadTurn(double Radians);

/// <summary>
/// Direct Windows 11 Precision Touchpad source.
///
/// Avalonia owns the HWND and UI. This class reads the two physical contacts
/// from WM_POINTER/GetPointerFrameTouchpadInfo and emits application-level
/// orbit, zoom and circular turntable deltas.
/// </summary>
internal sealed class WindowsPrecisionTouchpadGestureSource : IDisposable
{
    internal const uint WmPointerUpdate = 0x0245;
    internal const uint WmPointerDown = 0x0246;
    internal const uint WmPointerUp = 0x0247;

    private const double CameraZoomExponentPerUnit = 0.12;

    // Native contact positions are HIMETRIC (0.01 mm). The 0.02 conversion was
    // the stable value used by the earlier working Windows TPProbe integration.
    private readonly double orbitScale = ReadPositiveEnvironmentDouble(
        "LIGHTINGSHOWCASE_WINDOWS_TRACKPAD_ORBIT_SCALE", 0.02);

    private readonly double circularScale = ReadPositiveEnvironmentDouble(
        "LIGHTINGSHOWCASE_WINDOWS_TRACKPAD_CIRCULAR_SCALE", 1.0);

    private readonly bool diagnosticsEnabled =
        Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_NAV_DIAGNOSTICS") == "1";

    private readonly WindowsPrecisionTouchpadTracker tracker = new();
    private WindowsPrecisionTouchpadApi? api;
    private nint hwnd;
    private bool attached;
    private bool gestureActive;

    public string BackendName => "Windows 11 native Precision Touchpad (two contacts)";
    public bool IsAvailable => attached && api is not null;
    public bool IsGestureActive => gestureActive;

    public event EventHandler<NativeTrackpadOrbit>? Orbit;
    public event EventHandler<NativeTrackpadZoom>? Zoom;
    public event EventHandler<NativeTrackpadTurn>? Turn;

    public void Attach(nint hwnd)
    {
        if (attached)
            throw new InvalidOperationException("Windows native touchpad source is already attached.");
        if (hwnd == 0)
            throw new ArgumentException("A valid HWND is required.", nameof(hwnd));

        WindowsPrecisionTouchpadApi loaded = WindowsPrecisionTouchpadApi.Load();
        try
        {
            loaded.RegisterWindow(hwnd);
            api = loaded;
            this.hwnd = hwnd;
            attached = true;
            Trace($"registered HWND 0x{hwnd:X}");
        }
        catch
        {
            loaded.Dispose();
            throw;
        }
    }

    public bool TryProcessWindowMessage(uint message, nint wParam, nint lParam)
    {
        _ = lParam;

        if (!attached || api is null)
            return false;
        if (message != WmPointerDown && message != WmPointerUpdate && message != WmPointerUp)
            return false;

        uint pointerId = unchecked((uint)((long)wParam & 0xFFFF));
        bool isTouchpad = api.IsTouchpadPointer(pointerId);

        // On the final UP Windows may retire the pointer id before it can be
        // queried. If we already own this sequence, consume the UP and reset.
        if (!isTouchpad)
        {
            if (message == WmPointerUp && gestureActive)
            {
                tracker.Reset();
                gestureActive = false;
                return true;
            }
            return false;
        }

        if (!api.TryGetFrame(pointerId, out WindowsTouchContact[] contacts))
        {
            if (message == WmPointerUp)
            {
                tracker.Reset();
                gestureActive = false;
            }

            // Still consume native touchpad pointer input so Windows does not
            // convert this same gesture into legacy wheel packets afterward.
            return true;
        }

        gestureActive = contacts.Length > 0;
        WindowsTrackpadGesture gesture = tracker.Update(contacts);

        switch (gesture.Kind)
        {
            case WindowsTrackpadGestureKind.Orbit:
            {
                double x = gesture.OrbitX * orbitScale;
                double y = gesture.OrbitY * orbitScale;
                Orbit?.Invoke(this, new NativeTrackpadOrbit(x, y));
                Trace($"ORBIT x={x:0.####} y={y:0.####} frame={gesture.FrameId}");
                break;
            }

            case WindowsTrackpadGestureKind.Zoom:
            {
                if (double.IsFinite(gesture.Scale) && gesture.Scale > 0)
                {
                    double amount = Math.Log(gesture.Scale) / CameraZoomExponentPerUnit;
                    if (double.IsFinite(amount) && Math.Abs(amount) > 1e-6)
                    {
                        amount = Math.Clamp(amount, -8.0, 8.0);
                        Zoom?.Invoke(this, new NativeTrackpadZoom(amount));
                        Trace($"ZOOM amount={amount:0.####} scale={gesture.Scale:0.#####} frame={gesture.FrameId}");
                    }
                }
                break;
            }

            case WindowsTrackpadGestureKind.Circular:
            {
                double radians = -gesture.AngleDeltaRadians * circularScale;
                if (double.IsFinite(radians) && Math.Abs(radians) > 1e-8)
                {
                    Turn?.Invoke(this, new NativeTrackpadTurn(radians));
                    Trace($"CIRCULAR-TURN radians={radians:0.######} frame={gesture.FrameId}");
                }
                break;
            }
        }

        if (message == WmPointerUp && contacts.Length == 0)
        {
            tracker.Reset();
            gestureActive = false;
        }

        return true;
    }

    public void Detach()
    {
        if (!attached)
            return;

        try
        {
            api?.UnregisterWindow(hwnd);
        }
        catch
        {
            // Best effort during window teardown.
        }

        tracker.Reset();
        gestureActive = false;
        attached = false;
        hwnd = 0;
        api?.Dispose();
        api = null;
    }

    private void Trace(string text)
    {
        if (diagnosticsEnabled)
            Console.WriteLine($"[NAV-WIN32] {text}");
    }

    private static double ReadPositiveEnvironmentDouble(string name, double fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        return double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            && double.IsFinite(value)
            && value > 0
                ? value
                : fallback;
    }

    public void Dispose() => Detach();
}
