# Trackpad Navigation — Multi-Platform Test Release

This release extends the validated orbit-only navigation experiment with pinch zoom while keeping the input layer platform-neutral.

## Supported desktop targets

The existing GitHub Actions matrix publishes:

- Windows x64 / arm64
- macOS x64 / arm64
- Linux x64 / arm64

Framework:

- .NET 10
- Avalonia 12.1.1

## Intended interaction

Inside the Composer viewport:

| Physical input | Application action |
|---|---|
| Two-finger trackpad translation | Orbit |
| Trackpad pinch / magnify | Zoom |
| Right mouse drag | Orbit |
| Middle mouse drag | Pan |
| Keyboard `+` / `-` | Zoom |

No mouse-button press, pointer capture, or drag state is required for trackpad orbit or pinch zoom.

## Why orbit still uses `PointerWheelChanged`

Avalonia exposes mouse-wheel and trackpad scrolling through the same `PointerWheelChanged` abstraction on desktop platforms. In the Linux diagnostic tests, two-finger trackpad translation arrived as smooth X/Y wheel deltas even though the physical interaction was a trackpad gesture.

Therefore the current adapter treats ordinary non-zero `PointerWheelChanged` X/Y deltas as **trackpad orbit transport**. The application-level feature is still named trackpad orbit.

Because Avalonia can also use this event for a physical mouse wheel, a mouse wheel over the viewport can still orbit in this test release unless the event carries a distinct zoom signal such as Control.

## Pinch paths enabled in this release

`TrackpadViewportNavigationInput` listens for three public Avalonia pinch/magnify paths:

1. `InputElement.PinchEvent`, enabled by a `PinchGestureRecognizer`.
2. `InputElement.PointerTouchPadGestureMagnify`.
3. A Control-modified `PointerWheelChanged` stream as a fallback.

This is intentional for cross-platform testing. Different desktop backends and trackpad drivers can expose precision pinch differently.

When a dedicated pinch or magnify path fires, ordinary orbit packets are suppressed briefly so the same physical pinch does not also rotate the camera.

## Input coalescing

Both orbit and zoom are accumulated and applied by Composer on a roughly 16 ms `DispatcherTimer` rather than forcing a render for every raw input packet.

```text
trackpad packets
      ↓
accumulate orbit / zoom
      ↓
~16 ms UI navigation tick
      ↓
Camera.Orbit / Camera.Zoom
      ↓
interactive render
```

## Windows test procedure

Build and run:

```powershell
dotnet restore .\LightingShowcase.Composer.sln
dotnet build .\LightingShowcase.Composer.sln -c Debug
dotnet run --project .\LightingShowcase.Composer.Avalonia\LightingShowcase.Composer.Avalonia.csproj
```

Test in this order:

1. Put two fingers on the trackpad without clicking and move left/right/up/down. Confirm orbit.
2. Stop moving, then pinch outward. Confirm zoom in.
3. Pinch inward. Confirm zoom out.
4. Repeat orbit after pinch and verify orbit does not remain suppressed or stuck.
5. Try several rapid transitions between orbit and pinch.

For navigation diagnostics:

```powershell
$env:LIGHTINGSHOWCASE_NAV_DIAGNOSTICS="1"
dotnet run --project .\LightingShowcase.Composer.Avalonia\LightingShowcase.Composer.Avalonia.csproj
```

Expected diagnostic examples:

```text
[NAV] attached on Windows
[NAV] orbit dx=... dy=...
[NAV] zoom source=pinch amount=...
[NAV] zoom source=touchpad-magnify amount=...
[NAV] zoom source=ctrl-wheel amount=...
```

The source shown after `zoom source=` tells us which Avalonia path Windows actually used for the pinch.

## Linux backend selection

Linux uses Avalonia's normal `UsePlatformDetect()` behavior by default. Native Wayland is not forced.

To explicitly test native Wayland:

```bash
LIGHTINGSHOWCASE_NATIVE_WAYLAND=1 dotnet run --project LightingShowcase.Composer.Avalonia
```

## Current limitation

This release does not contain OS-specific raw trackpad APIs. It deliberately tests how far the public Avalonia 12 input layer can carry Blender-style trackpad navigation across Windows, macOS, and Linux.

If Windows provides a clean dedicated pinch event, the next step is to keep that path and refine device separation so a physical mouse wheel can return to zoom without affecting two-finger trackpad orbit.
