/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 *
 * `WindowsTrackpadGestureKind` makes a closed set of choices compiler-visible instead of passing loosely related
 * integers or strings. Code that switches over `Waiting`, `Baseline`, `Orbit`, `Zoom`, `Circular` is where the
 * behavioral meaning of each choice is implemented.
 *
 * `WindowsTrackpadGesture` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Kind`, `OrbitX`, `OrbitY`, `Scale`, `AngleDeltaRadians`, `FrameId`)
 * travel together because consumers need a consistent snapshot rather than reading those values independently
 * from mutable objects.
 *
 * `WindowsPrecisionTouchpadTracker` retains temporal/input history needed to interpret a stream of events as a
 * higher-level gesture or state transition.
 */
namespace LightingShowcase.Composer.Navigation.Windows;

internal enum WindowsTrackpadGestureKind
{
    Waiting,
    Baseline,
    Orbit,
    Zoom,
    Circular
}

internal readonly record struct WindowsTrackpadGesture(
    WindowsTrackpadGestureKind Kind,
    double OrbitX,
    double OrbitY,
    double Scale,
    double AngleDeltaRadians,
    uint FrameId);

/// <summary>
/// Classifies the two physical Precision Touchpad contacts directly.
///
///   centroid translation -> orbit
///   contact separation   -> zoom
///   finger-line angle    -> circular orbit / view roll
///
/// Circular recognition has a 120 ms hold plus small-angle accumulation so a
/// continuous twist does not flicker back to Baseline on quiet frames.
/// </summary>
internal sealed class WindowsPrecisionTouchpadTracker
{
    private bool hasPrevious;
    private uint previousId1;
    private uint previousId2;
    private double previousX1;
    private double previousY1;
    private double previousX2;
    private double previousY2;
    private double previousCx;
    private double previousCy;
    private double previousDistance;
    private double previousAngle;
    private uint lastFrameId;

    private bool circularActive;
    private long lastCircularDetectedMs;
    private double accumulatedAngleDelta;

    private const long CircularHoldMs = 120;
    private const double CircularAccumulationThresholdRadians = 0.0015;
    private const double CircularAccumulationDecay = 0.55;

    public WindowsTrackpadGesture Update(WindowsTouchContact[] source)
    {
        if (source.Length != 2)
        {
            Reset();
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Waiting, 0, 0, 1, 0,
                source.Length > 0 ? source[0].FrameId : 0);
        }

        WindowsTouchContact a = source[0];
        WindowsTouchContact b = source[1];

        // Stable ordering keeps the directed segment consistent throughout a
        // contact sequence. Reversing both endpoints would add pi to the angle,
        // but stable IDs also avoid artificial discontinuities.
        if (a.PointerId > b.PointerId)
            (a, b) = (b, a);

        uint frameId = a.FrameId;
        double cx = (a.X + b.X) * 0.5;
        double cy = (a.Y + b.Y) * 0.5;
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;
        double distance = Math.Sqrt(vx * vx + vy * vy);
        double angle = Math.Atan2(vy, vx);

        if (hasPrevious && frameId == lastFrameId)
        {
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Baseline, 0, 0,
                previousDistance > 0.001 ? distance / previousDistance : 1,
                0, frameId);
        }

        bool sameContacts =
            hasPrevious &&
            a.PointerId == previousId1 &&
            b.PointerId == previousId2;

        if (!sameContacts || previousDistance <= 0.001)
        {
            Store(a, b, cx, cy, distance, angle, frameId);
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Baseline, 0, 0, 1, 0, frameId);
        }

        double dx = cx - previousCx;
        double dy = cy - previousCy;
        double distanceDelta = distance - previousDistance;
        double scale = distance / previousDistance;
        double angleDelta = NormalizeRadians(angle - previousAngle);

        Store(a, b, cx, cy, distance, angle, frameId);

        double translationMagnitude = Math.Sqrt(dx * dx + dy * dy);
        double zoomMagnitude = Math.Abs(distanceDelta) * 0.5;
        double angularTangentialMagnitude = (distance * 0.5) * Math.Abs(angleDelta);
        const double noiseFloor = 0.5;
        long nowMs = Environment.TickCount64;

        if (Math.Sign(accumulatedAngleDelta) != 0 &&
            Math.Sign(angleDelta) != 0 &&
            Math.Sign(accumulatedAngleDelta) != Math.Sign(angleDelta))
        {
            accumulatedAngleDelta *= CircularAccumulationDecay;
        }
        accumulatedAngleDelta += angleDelta;

        bool zoomDetected =
            zoomMagnitude > noiseFloor &&
            zoomMagnitude > translationMagnitude * 1.20 &&
            zoomMagnitude > angularTangentialMagnitude * 0.85;

        bool circularDetected =
            angularTangentialMagnitude > noiseFloor &&
            angularTangentialMagnitude > translationMagnitude * 0.85 &&
            Math.Abs(angleDelta) > 0.0005;

        bool accumulatedCircularDetected =
            !zoomDetected &&
            translationMagnitude <= noiseFloor * 1.35 &&
            Math.Abs(accumulatedAngleDelta) >= CircularAccumulationThresholdRadians;

        bool translationDetected = translationMagnitude > noiseFloor;

        // Strong radial motion owns the gesture immediately.
        if (zoomDetected)
        {
            circularActive = false;
            accumulatedAngleDelta = 0;
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Zoom, 0, 0, scale, 0, frameId);
        }

        // Angle/tangential motion owns circular orbit before ordinary centroid
        // translation. This is the top-right / bottom-left gesture validated in
        // the standalone probe.
        if (circularDetected || accumulatedCircularDetected)
        {
            circularActive = true;
            lastCircularDetectedMs = nowMs;
            accumulatedAngleDelta *= 0.20;
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Circular, 0, 0, 1, angleDelta, frameId);
        }

        // A clearly dominant common translation switches directly to orbit.
        if (translationDetected &&
            translationMagnitude > angularTangentialMagnitude * 1.35)
        {
            circularActive = false;
            accumulatedAngleDelta = 0;
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Orbit, dx, dy, 1, 0, frameId);
        }

        // Preserve classification over brief quiet frames without inventing
        // motion: AngleDeltaRadians remains the real measured delta for the frame.
        if (circularActive && nowMs - lastCircularDetectedMs <= CircularHoldMs)
        {
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Circular, 0, 0, 1, angleDelta, frameId);
        }

        if (translationDetected)
        {
            circularActive = false;
            accumulatedAngleDelta *= CircularAccumulationDecay;
            return new WindowsTrackpadGesture(
                WindowsTrackpadGestureKind.Orbit, dx, dy, 1, 0, frameId);
        }

        circularActive = false;
        accumulatedAngleDelta *= CircularAccumulationDecay;
        if (Math.Abs(accumulatedAngleDelta) < 0.00005)
            accumulatedAngleDelta = 0;

        return new WindowsTrackpadGesture(
            WindowsTrackpadGestureKind.Baseline, 0, 0, 1, 0, frameId);
    }

    public void Reset()
    {
        hasPrevious = false;
        previousId1 = 0;
        previousId2 = 0;
        previousX1 = 0;
        previousY1 = 0;
        previousX2 = 0;
        previousY2 = 0;
        previousCx = 0;
        previousCy = 0;
        previousDistance = 0;
        previousAngle = 0;
        lastFrameId = 0;
        circularActive = false;
        lastCircularDetectedMs = 0;
        accumulatedAngleDelta = 0;
    }

    private void Store(
        WindowsTouchContact a,
        WindowsTouchContact b,
        double cx,
        double cy,
        double distance,
        double angle,
        uint frameId)
    {
        hasPrevious = true;
        previousId1 = a.PointerId;
        previousId2 = b.PointerId;
        previousX1 = a.X;
        previousY1 = a.Y;
        previousX2 = b.X;
        previousY2 = b.Y;
        previousCx = cx;
        previousCy = cy;
        previousDistance = distance;
        previousAngle = angle;
        lastFrameId = frameId;
    }

    private static double NormalizeRadians(double radians)
    {
        while (radians > Math.PI)
            radians -= Math.PI * 2.0;
        while (radians < -Math.PI)
            radians += Math.PI * 2.0;
        return radians;
    }
}
