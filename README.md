# LightingShowcase Avalonia Composer

A standalone, platform-neutral scene composer and renderer built with Avalonia and .NET 8.

## Initial feature set

- Open a scene or 3D model.
- Insert multiple glTF/GLB, FBX, OBJ, 3DS, PLY, STL, and PropXML assets.
- Add Blender-style Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, and Grid primitives directly from the composer toolbar (Monkey/Suzanne intentionally omitted).
- Open a closable floating **Material** editor with a categorized PBR preset library, exact RGB/hex base-color setter, and image texture assignment.
- Browse objects in a recursive scene tree and select them from the tree or rendered viewport.
- Orbit, pan, and zoom the preview on every rendering backend.
- Highlight the selected object and draw overlaid move, rotation-ring, and scale gizmos for X/Y/Z transforms, including uniform scaling.
- Switch among Object, Vertex, Edge, and Face selection. Mesh-component editing initially supports move only.
- Reconstruct welded indexed topology from imported triangle meshes, and use **Join + weld** to flatten an imported subtree into one editable mesh.
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

- Left click in Object mode: select the highest imported object group under the pointer. Click empty viewport space to deselect the current object.
- `1`, `2`, `3`, and `4`: switch to Vertex, Edge, Face, and Object selection. Edge and vertex picks must be close to the projected component; Face mode selects the directly clicked front face. Component modes hide the object bounding box until a component is selected, then show only the component highlight and move gizmo.
- Choose Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, or Grid and press **Add primitive**. A closable floating **Parameters** window opens for procedural editing.
- Use **Material…** in the Inspector to apply a library preset, set an exact RGB/hex color, or assign/clear a base-color texture for the selected object/subtree.
- **Join + weld** bakes the selected hierarchy, flattens its descendants, and merges coincident positions into common vertex/edge topology. This operation is undoable.
- `G`, `R`, and `S`: choose Move, Rotate, or Scale, matching Blender's primary transform shortcuts. The toolbar selector provides the same modes.
- Drag a red, green, or blue move axis, rotation ring, or scale handle. The white center scale handle scales uniformly. Hold Shift for precision and Ctrl for snapping. In Object mode, Composer uses the transform gizmo as the selection cue and does not draw an additional object bounding box or triangle wireframe before, during, or after a transform.
- Vulkan raster previews object transforms live through a transform uniform. Component movement patches only affected triangle vertices in the existing GPU buffers, so the rendered mesh deforms during the drag; shared welded vertices are committed once on release.
- Software raster and sufficiently fast Vulkan compute frames are coalesced and throttled as pseudo-real-time previews. CPU ray rendering waits for release.
- Right drag: orbit.
- Middle drag or Shift+right drag: pan.
- Mouse wheel: zoom.
- Arrow keys: orbit; Shift+arrow keys: pan.
- `F`: frame the selected tree node.
- `Ctrl+D`: duplicate the selected node.
- `Delete`: delete the selected node.
- `Ctrl+Z` / `Ctrl+Y`: undo or redo.
- Use a disclosure arrow to open group nodes. Use the lazy `…` row to page triangle leaves without creating thousands of scene nodes.

Object-mode viewport selection resolves to the highest top-level asset group, so clicking a model moves the complete inserted asset by default. The hierarchy panel has separate disclosure arrows for expanding and collapsing nodes. Selecting a child explicitly in the hierarchy makes position, rotation, scale, visibility, name, framing, and gizmo operations target that child node.

## Parameterized primitives and real-world units

Composer uses **meters (m)** as its scene length unit. Object Position fields, stress-grid spacing, and every primitive dimension labeled as a length are authored directly in meters; scale remains dimensionless and rotation remains degrees in the inspector. This makes values such as `2.4 m × 0.9 m × 0.75 m` directly usable for real specifications.

The standard primitive set mirrors Blender's Add → Mesh primitives except Monkey/Suzanne:

- Plane — width, depth
- Cube — width, height, depth
- Circle — vertices, radius, fill type
- UV Sphere — segments, rings, radius
- Icosphere — subdivisions, radius
- Cylinder — vertices, radius, depth, caps
- Cone — vertices, radius 1, radius 2, depth, caps
- Torus — major/minor segments and major/minor radius
- Grid — X/Y subdivisions, width, depth

The parameter window is modeless and can be closed and reopened while the object remains procedural. Parameter changes regenerate only that object's shadow mesh and are grouped into undoable edit batches. **Convert to Mesh**, **Join + weld**, or a committed Vertex/Edge/Face geometry edit removes the procedural metadata and leaves the generated triangles as an ordinary editable mesh. Saved `.lscene` files retain the primitive kind and parameter values while the object is still procedural. See [`PARAMETERIZED_PRIMITIVES.md`](PARAMETERIZED_PRIMITIVES.md).

## Materials, exact color, and textures

Select an object and press **Material…** in the Inspector to open a modeless, closable material editor. The built-in material library includes categorized metal, paint, plastic, glass, stone, organic, liquid, and emissive presets. Applying a preset changes the material's PBR values while retaining any base-color/normal/metallic-roughness texture maps already assigned to the object.

Base color can be entered as exact 0–255 RGB channels or hexadecimal `#RRGGBB`. Material and color edits are independent of geometry: moving, rotating, scaling, recoloring, or assigning a texture to a parameterized primitive does **not** convert it to a mesh. The material is reused whenever its procedural shadow mesh is regenerated.

The base-color texture setter accepts PNG, JPEG, BMP, TGA, GIF, PSD, and HDR images supported by the existing managed texture decoder. Two UV modes are available:

- **Box-project UVs using real-world tile size** — enter the repeat size in meters, such as `0.3 m` for a 30 cm material tile. For parameterized primitives, this projection mode and tile size are stored as hidden procedural metadata and reapplied after parameter changes.
- **Use authored UVs** — preserves imported/model UV coordinates exactly.

Preset, color, texture, and clear-texture operations each create an undo entry. Material edits invalidate renderer material/texture caches but do not alter object topology.

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

Transforms are authored as temporary editor values while dragging. For ordinary meshes, commit rewrites triangle positions/normals as before. Parameterized primitives instead accumulate Move/Rotate/Scale into a hidden authored affine layer, regenerate their shadow mesh, and keep all procedural shape parameters editable. The node transform fields still return to identity, so later render frames keep the same baked-geometry performance model. Vertex/edge/face editing, Join + weld, or explicit Convert to Mesh are the operations that intentionally discard the procedural definition. Vulkan raster refreshes the existing vertex buffers in place; textures, descriptor sets, pipelines, and render-target allocations remain cached.

Selection is a cached post-process overlay and does not change scene materials or invalidate Vulkan geometry. Object mode displays only the active transform gizmo; it does not add a bounding box or sampled triangle wireframe. Component modes show only their component highlight/gizmo. For Vulkan raster component movement, a small cached list of affected triangle-buffer offsets is patched during dragging and restored or replaced on cancellation, reselection, or commit.

GPU ID picking and detailed benchmark export remain future extensions.

For the implementation and performance strategy, see [`GIZMO_TRANSFORM_PREVIEW.md`](GIZMO_TRANSFORM_PREVIEW.md).


## Automated tests

Run the cross-platform test suite with:

```bash
./run-tests.sh
```

On Windows:

```powershell
.\run-tests.ps1
```

The suite verifies parameterized primitive registration and meter units, procedural regeneration/undo/redo/conversion, material-library/color/texture edits and texture persistence through procedural regeneration, baked local-geometry mutation, identity transform metadata after commit, exact undo/redo hashes, deferred move/rotation/scale commits, Vulkan cache revision handling, rendered-pixel changes, lazy triangle browsing without object growth, root/nested ungroup behavior, hierarchy expansion, and Visual Studio solution integrity.
See `TESTING.md` for the optional Vulkan tests, including the live pending-transform preview.

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

## Optimized glTF and GLB export

The export dialog offers two glTF/GLB modes:

- **Optimized** (default): ignores editor-only spatial chunk boundaries, combines all visible geometry into one mesh, emits one primitive per material, welds vertices that share position/normal/UV data, uses 16-bit indices where possible, and writes compact JSON. This is intended for fast loading and runtime rendering, including scenes that were ungrouped into many chunks.
- **Preserve editor chunks**: retains one exported mesh for each current top-level editor object. Use this only when the chunk organization is needed in another application.

Optimized export does not alter the open `.lscene` document. It only changes the generated glTF/GLB package.
