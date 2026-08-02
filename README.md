# LightingShowcase Avalonia Composer

A standalone, platform-neutral scene composer and renderer built with Avalonia and .NET 8.

## Initial feature set

- Open a scene or 3D model.
- Insert multiple glTF/GLB, FBX, OBJ, 3DS, PLY, STL, and PropXML assets.
- Browse objects in a recursive scene tree and select them from the tree or rendered viewport.
- Orbit, pan, and zoom the preview on every rendering backend.
- Highlight the selected object and draw overlaid selection bounds plus X/Y/Z transform axes.
- Edit position, rotation, and scale numerically; Enter or **Apply transform** commits changes.
- Inserted assets are wrapped in one top-level group while retaining their imported child hierarchy.
- Rename, show/hide, duplicate, delete, reset, and frame groups or child nodes.
- Generate object grids for performance stress testing.
- Save and reopen `.lscene` compositions.
- Preview with software raster, Vulkan raster, Vulkan compute, or CPU ray rendering.
- Show frame time, approximate FPS, object count, triangle count, and process memory.
- Use the same executable for headless command-line rendering.

## Requirements

- .NET 8 SDK for building and running from source.
- A Vulkan-capable driver for Vulkan rendering modes.
- Linux desktop libraries required by Avalonia.

## Visual Studio and cross-platform development

Open `LightingShowcase.Composer.sln` in Visual Studio 2022 17.8 or newer on Windows. The solution is organized into Application, Core Libraries, Importers, and Object Libraries folders. Set `LightingShowcase.Composer.Avalonia` as the startup project if Visual Studio does not select it automatically.

Three launch profiles are included:

- `Composer UI`
- `Composer CLI - Help`
- `Composer CLI - Formats`

The same solution can be restored and built from Linux:

```bash
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Debug
```

JetBrains Rider can also open the `.sln` directly on Linux. The solution contains no WPF, WinForms, or Windows-only application code.

## Run

```bash
./run.sh
```

Open a scene or model at startup:

```bash
./run.sh compose ./scene.glb
```

A path may also be passed without the `compose` verb:

```bash
./run.sh ./scene.glb
```


## Viewport controls

- Left click: select the highest imported object group under the pointer.
- Right drag: orbit.
- Middle drag or Shift+right drag: pan.
- Mouse wheel: zoom.
- Arrow keys: orbit; Shift+arrow keys: pan.
- `F`: frame the selected tree node.
- `Ctrl+D`: duplicate the selected node.
- `Delete`: delete the selected node.

Viewport selection intentionally resolves to the highest top-level asset group so numeric transforms move the complete inserted object. Child nodes remain directly selectable from the scene tree when a lower-level edit is needed.

## Command-line rendering

```bash
./run.sh render ./composition.lscene \
  --renderer raster-vulkan \
  --output ./composition.png
```

Available renderer values:

- `raster`
- `raster-vulkan`
- `vulkan`
- `cpu`

List accepted scene formats:

```bash
./run.sh formats
```

Show all command-line options:

```bash
./run.sh --help
```

## Publish for Linux

```bash
./publish-linux.sh linux-x64
```

The default output directory is `publish/linux-x64`.

## Repository layout

```text
LightingShowcase.Composer.Avalonia/   Avalonia UI and integrated CLI
LightingShowcase.Core/                Portable scene and rendering project
Camera/ Lighting/ Math/ Scene/        Shared scene-domain source
Rendering/ Shaders/                   Portable rendering backends
LightingShowcase.ImportExport.*/      Model and scene importers
LightingShowcase.ObjectLibrary.*/     Built-in scene objects
```

## Current scope

The current release provides hierarchical composition, numeric group transforms, navigation, selection highlighting, and non-interactive overlaid transform axes. Direct mouse dragging of individual gizmo axes, undo/redo, GPU ID picking, and detailed benchmark export remain future extensions.
