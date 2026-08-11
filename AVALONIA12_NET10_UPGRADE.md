# Avalonia 12.1 / .NET 10 upgrade

Baseline: current public `kns98/LightingComposer` `main` source, with the experimental gesture/navigation customizations removed from the Composer project.

Changes in this branch:

- Every project targets `net10.0`.
- `global.json` requests the .NET 10 SDK and rolls forward to the latest installed feature band.
- Composer uses Avalonia `12.1.1` packages.
- `Avalonia.Wayland` `12.1.1` is included.
- Windows, macOS, and Linux use `UsePlatformDetect()` by default.
- Avalonia native Wayland remains an explicit Linux test opt-in through `LIGHTINGSHOWCASE_NATIVE_WAYLAND=1`.
- The current cross-platform navigation adapter maps two-finger translation to orbit and adds public Avalonia pinch/magnify paths for zoom. No raw libinput/native OS bridge is included.

Build:

```bash
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Debug
```

On a Wayland session, verify:

```bash
echo "$XDG_SESSION_TYPE"
echo "$WAYLAND_DISPLAY"
dotnet run --project LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj
```

The Avalonia 12.1 Wayland backend is experimental and intentionally opt-in.

## Validation performed in this package

- Parsed every SDK-style project file successfully.
- Confirmed every project targets `net10.0`.
- Confirmed all Avalonia package references are `12.1.1`.
- Confirmed GitHub Actions installs .NET `10.0.x`.
- Scanned Composer C# for the common Avalonia 12 removed/renamed APIs called out by the migration guide.
- A full `dotnet build` was **not** run in the packaging environment because the .NET SDK is not installed there; run the commands above on a .NET 10 machine for compile/runtime verification.

## Cross-platform trackpad navigation variant

`TrackpadViewportNavigationInput` is platform-neutral. Ordinary `PointerWheelChanged` X/Y deltas are used as the transport for two-finger trackpad orbit, while `InputElement.PinchEvent`, `PointerTouchPadGestureMagnify`, and Control-modified wheel input are accepted as zoom paths. Composer coalesces orbit and zoom at approximately 16 ms before camera/render updates.

See `TRACKPAD_NAVIGATION_MULTIPLATFORM.md` and `TRACKPAD_ORBIT_INPUT.md` for the test architecture and current device-separation limitation.
