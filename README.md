# LightingShowcase Avalonia Composer

A standalone, platform-neutral scene composer and renderer built with Avalonia and .NET 8.

## Initial feature set

- Open a scene or 3D model.
- Insert multiple glTF/GLB, FBX, OBJ, 3DS, PLY, STL, and PropXML assets.
- Browse objects in a recursive scene tree and select them from the tree or rendered viewport.
- Orbit, pan, and zoom the preview on every rendering backend.
- Highlight the selected object and draw overlaid selection bounds plus draggable X/Y/Z translation axes.
- Edit position, rotation, and scale numerically; Enter or **Apply transform** bakes the change into vertex positions and normals, clears the fields, and redraws the active renderer.
- Inserted assets are wrapped in one top-level group while retaining their imported child hierarchy.
- Rename, show/hide, duplicate, delete, reset, frame, or ungroup any non-terminal group or mesh node.
- Undo and redo baked transforms and ungroup operations with toolbar buttons or `Ctrl+Z` / `Ctrl+Y`.
- Expand a mesh through a lazy `… show triangles` row. Triangle leaves are virtual and paged 200 at a time until explicitly ungrouped, so browsing them does not enlarge the scene or GPU buffers.
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

Extract the ZIP to a normal writable folder, then open `LightingShowcase.Composer.sln` in Visual Studio 2022 17.8 or newer on Windows. The solution uses the standard SDK-style C# project GUID, Windows CRLF line endings, and includes every referenced project. Set `LightingShowcase.Composer.Avalonia` as the startup project if Visual Studio does not select it automatically.

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
- Drag a red, green, or blue gizmo axis: stage a translation along X, Y, or Z. The geometry is baked once and Vulkan is refreshed when the pointer is released; pointer moves do not rebuild or reupload the scene.
- Right drag: orbit.
- Middle drag or Shift+right drag: pan.
- Mouse wheel: zoom.
- Arrow keys: orbit; Shift+arrow keys: pan.
- `F`: frame the selected tree node.
- `Ctrl+D`: duplicate the selected node.
- `Delete`: delete the selected node.
- `Ctrl+Z` / `Ctrl+Y`: undo or redo.
- Use a disclosure arrow to open group nodes. Use the lazy `…` row to page triangle leaves without creating thousands of scene nodes.

Viewport selection resolves to the highest top-level asset group, so clicking a model moves the complete inserted asset by default. The hierarchy panel has separate disclosure arrows for expanding and collapsing nodes. Selecting a child explicitly in the hierarchy makes position, rotation, scale, visibility, name, framing, and gizmo operations target that child node.

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

Run the built-in hierarchy and transform regression check:

```bash
./run.sh self-test-transforms
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

## Transform and Vulkan behavior

Transforms are authored as temporary editor values only until they are committed. A commit rewrites the selected subtree's triangle positions and inverse-transpose transformed normals, converts transformed procedural primitives to mesh authoring data, resets the node transform to identity, increments the scene revision, and refreshes the existing Vulkan raster vertex buffers in place. Textures, descriptor sets, pipelines, and render-target allocations remain cached. This means the commit has a one-time vertex upload cost, but there is no extra matrix or scene-graph transform cost on later frames.

Selection is a cached post-process overlay and does not change scene materials or invalidate Vulkan geometry. At most 256 sampled triangles are retained for the orange wire overlay; the complete selected mesh is not traversed on every frame.

Rotation and scale gizmo modes, GPU ID picking, and detailed benchmark export remain future extensions.


## Automated tests

Run the cross-platform test suite with:

```bash
./run-tests.sh
```

On Windows:

```powershell
.\run-tests.ps1
```

The suite verifies baked local-geometry mutation, identity transform metadata after commit, exact undo/redo hashes, deferred gizmo commits, Vulkan cache revision handling, rendered-pixel changes, lazy triangle browsing without object growth, root/nested ungroup behavior, hierarchy expansion, and Visual Studio solution integrity.
See `TESTING.md` for the optional Vulkan tests.

## Self-contained scenes and portable exports

`.lscene` version 11 embeds decoded RGBA texture pixels during normal **Save
scene…**, together with geometry, materials, lights, hierarchy, and baked
transforms. Reopening a normally saved scene does not require the original image
files.

Use **Export package…** to open an explicit format-selection dialog and then
choose a parent folder. The composer creates a new uniquely named directory.
Export resources are external in the package root and use deterministic numbered
names such as `res_0001.bin`, `res_0002.png`, and `res_0003.mtl`. The primary
model keeps the composition name.

Command line:

```bash
./run.sh export-formats
./run.sh export composition.lscene --format gltf --output-dir ./exports
./run.sh export composition.lscene --format obj --output-dir ./exports
```

Supported export IDs are `lscene`, `lsb`, `prop-xml`, `xml`, `obj`, `stl-binary`,
`stl-ascii`, `ply-binary`, `ply-ascii`, `3ds`, `fbx-binary`, `fbx-ascii`,
`gltf`, and `glb`.
