# Trackpad Orbit Input Architecture

## Purpose

Lighting Composer should treat a two-finger trackpad translation as an **orbit gesture**.

The user interaction is:

```text
Two fingers touch the trackpad
        +
Two fingers move together
        ↓
Camera orbit
```

No mouse button, click, pointer capture, or held-finger button state should be required.

---

## Important Terminology

The interaction is a **trackpad gesture**, not a mouse-wheel gesture.

However, in the Avalonia 12 tests performed for Lighting Composer, ordinary two-finger trackpad translation was delivered to the application through:

```csharp
PointerWheelChanged
```

Therefore:

```text
Physical action:
    Trackpad two-finger translation

Avalonia transport:
    PointerWheelChanged

Application meaning:
    Orbit
```

The Avalonia event name should not determine the application-level meaning of the input.

For that reason, names such as:

```csharp
WheelOrbitViewportNavigationInput
```

are misleading.

Prefer:

```csharp
TrackpadOrbitNavigationInput
```

or:

```csharp
TrackpadOrbitViewportNavigationInput
```

---

## Observed Behavior

The diagnostic Avalonia application showed that two-finger trackpad movement produced smooth X/Y deltas.

Example:

```text
dx=-0.328125  dy=0
dx=-0.164063  dy=-0.164063
dx=0.328125   dy=0.988281
dx=0.824219   dy=0.328125
```

The stream can contain independent horizontal and vertical movement.

This is sufficient for orbit:

```text
Delta.X → yaw
Delta.Y → pitch
```

The trackpad was reported by Avalonia as a mouse-like pointer and no distinct pinch event was observed in the diagnostic tests.

Therefore the current implementation should not depend on:

```text
PinchGestureRecognizer
PointerPressed
PointerReleased
mouse-button state
pointer capture
drag state
```

for two-finger orbit.

---

## Recommended Architecture

```text
                USER INPUT
                    │
                    │
          Two-finger trackpad move
                    │
                    ▼
        Avalonia PointerWheelChanged
                    │
                    ▼
      TrackpadOrbitNavigationInput
                    │
             Delta.X / Delta.Y
                    │
                    ▼
              OrbitInput
                    │
                    ▼
             Camera.Orbit()
```

`PointerWheelChanged` should be considered an **input transport mechanism** here, not proof that the device is a mouse wheel.

---

## Orbit Handler

A minimal implementation can look like this:

```csharp
private void OnTrackpadScroll(
    object? sender,
    PointerWheelEventArgs e)
{
    double dx = e.Delta.X;
    double dy = e.Delta.Y;

    if (Math.Abs(dx) < 0.001 &&
        Math.Abs(dy) < 0.001)
    {
        return;
    }

    Orbit?.Invoke(
        this,
        new OrbitInput(
            -dx,
            -dy));
}
```

The trackpad path should be stateless.

Do not require:

```csharp
_isDragging
IsLeftButtonPressed
IsRightButtonPressed
Pointer.Capture(...)
PointerPressed
```

before accepting two-finger orbit input.

---

## High-Frequency Input

Trackpad input can arrive at a high frequency.

Lighting Composer should therefore avoid forcing a full camera/render update for every individual packet.

Recommended pattern:

```csharp
_pendingOrbitX += e.X;
_pendingOrbitY += e.Y;
```

Then consume accumulated input approximately once per UI/render cycle:

```csharp
var x = _pendingOrbitX;
var y = _pendingOrbitY;

_pendingOrbitX = 0;
_pendingOrbitY = 0;

Camera.Orbit(x, y);
RequestNavigationRender();
```

This keeps navigation responsive while reducing unnecessary render requests.

---

## Intended Cross-Platform Behavior

The desired application-level behavior is:

| Input | Action |
|---|---|
| Trackpad two-finger translation | Orbit |
| Trackpad pinch | Zoom |
| Physical mouse wheel | Zoom |
| Right mouse drag | Orbit |
| Middle mouse drag | Pan |
| Keyboard `+` / `-` | Zoom |

The first item is the currently validated trackpad behavior.

---

## Important Limitation

Avalonia may expose both:

```text
trackpad two-finger scrolling
```

and:

```text
physical mouse-wheel scrolling
```

through the same:

```csharp
PointerWheelChanged
```

event.

Therefore this code:

```csharp
private void OnPointerWheelChanged(
    object? sender,
    PointerWheelEventArgs e)
{
    Orbit(e.Delta.X, e.Delta.Y);
}
```

can successfully implement trackpad orbit, but it may also cause a physical mouse wheel to orbit.

That is acceptable for an **orbit-only diagnostic build**, but it is not the final desired production behavior.

---

## Production Goal

The final navigation layer should conceptually normalize platform-specific input into application commands:

```text
Platform input
      │
      ▼
Navigation adapter
      │
      ├── Orbit(dx, dy)
      ├── Zoom(amount)
      └── Pan(dx, dy)
      │
      ▼
Lighting Composer camera
```

The renderer and camera should not need to know whether an orbit command came from:

```text
Windows trackpad
macOS trackpad
Linux/X11 trackpad
Linux/Wayland trackpad
mouse drag
```

Platform/device detection belongs in the navigation adapter.

---

## Recommended Class Structure

```text
INavigationInput
    │
    ├── TrackpadOrbitNavigationInput
    ├── MouseNavigationInput
    └── PlatformSpecificNavigationInput
```

or, if a single adapter is preferred:

```text
DesktopNavigationInput
    │
    ├── Detect trackpad-like smooth X/Y stream
    ├── Detect physical wheel where possible
    ├── Emit OrbitInput
    ├── Emit ZoomInput
    └── Emit PanInput
```

The important point is that the class should be named according to its **application semantics**, not according to Avalonia's event name.

---

## Diagnostic Result

The minimal Avalonia orbit probe demonstrated that two-finger trackpad translation can be received continuously without requiring:

```text
click
button hold
pointer capture
drag state
```

This makes it a useful regression test.

When changing Avalonia versions or platform backends, verify:

```text
1. Put two fingers on trackpad.
2. Do not click.
3. Move left/right.
4. Move up/down.
5. Confirm continuous Delta.X / Delta.Y events.
6. Confirm orbit continues without entering a pressed state.
```

If the minimal probe works but Lighting Composer stalls, the problem is likely inside Lighting Composer's navigation/render integration rather than the basic gesture mapping.

---

## Current Decision

For the current orbit-only build:

```text
Two-finger trackpad translation
        ↓
PointerWheelChanged
        ↓
Trackpad orbit
```

This is intentional.

`PointerWheelChanged` is only the event used to carry the trackpad motion. It should not be described in the UI, code comments, or architecture documentation as a "mouse-wheel orbit" feature.

The feature should be described as:

> **Two-finger trackpad orbit**

with the implementation note:

> On platforms where Avalonia exposes two-finger translation through `PointerWheelChanged`, the navigation adapter uses the event's smooth X/Y deltas as trackpad orbit input.
