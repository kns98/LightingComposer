# Avalonia 12.1 / .NET 10 upgrade

Baseline: current public `kns98/LightingComposer` `main` source, with the experimental gesture/navigation customizations removed from the Composer project.

Changes in this branch:

- Every project targets `net10.0`.
- `global.json` requests the .NET 10 SDK and rolls forward to the latest installed feature band.
- Composer uses Avalonia `12.1.1` packages.
- `Avalonia.Wayland` `12.1.1` is included.
- On Linux, when `WAYLAND_DISPLAY` is present, startup opts into Avalonia's native Wayland backend with `UseWayland()`.
- Windows, macOS, and Linux X11 continue through `UsePlatformDetect()`.
- No custom pinch recognizer, raw libinput bridge, custom navigation interface, or Blender-derived gesture code is included.

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
