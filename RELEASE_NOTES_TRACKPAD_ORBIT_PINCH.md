# Release Notes — Cross-Platform Trackpad Orbit + Pinch Test

## Base

Based directly on `LightingComposer-avalonia12-net10-orbit-only-multiplatform`.

## Framework

- .NET 10 (`net10.0` across all 12 projects)
- Avalonia Desktop 12.1.1
- Avalonia Themes Fluent 12.1.1
- Avalonia Wayland 12.1.1 (Linux opt-in only)

## Navigation changes

- Renamed the platform-neutral navigation adapter to `TrackpadViewportNavigationInput`.
- Two-finger translation continues to feed orbit through smooth `PointerWheelChanged` X/Y deltas.
- Added zoom events to `IViewportNavigationInput`.
- Added Avalonia `PinchGestureRecognizer` + public `Pinch` / `PinchEnded` handling.
- Added public `PointerTouchPadGestureMagnify` handling.
- Added Control-modified `PointerWheelChanged` as a Windows/backend pinch fallback.
- Dedicated pinch/magnify activity briefly suppresses orbit packets to avoid one physical pinch rotating and zooming at the same time.
- Orbit and zoom are coalesced together on the existing ~16 ms navigation timer.
- No button-down requirement, pointer capture, or trackpad drag state was added.
- Navigation events remain unhandled by this adapter where possible to preserve the stable event stream observed in the minimal probe.

## Diagnostics

Set:

```text
LIGHTINGSHOWCASE_NAV_DIAGNOSTICS=1
```

to print the active path. On Windows, the useful lines are:

```text
[NAV] orbit dx=... dy=...
[NAV] zoom source=pinch amount=...
[NAV] zoom source=touchpad-magnify amount=...
[NAV] zoom source=ctrl-wheel amount=...
```

`test-windows-trackpad.ps1` builds the solution, enables diagnostics, and launches Composer.

## Known test limitation

Avalonia can expose both physical mouse-wheel scrolling and trackpad scrolling through `PointerWheelChanged`. In this test release, an unmodified wheel stream is still interpreted as orbit because that is the path validated for two-finger trackpad translation. The purpose of the Windows test is to determine whether pinch arrives through a distinct public Avalonia path so the final device mapping can be refined.
