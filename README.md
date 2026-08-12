# LightingShowcase Avalonia Composer

A standalone, platform-neutral scene composer and renderer built with Avalonia 12.1 and .NET 10.

## Initial feature set

- Open a scene or 3D model.
- Insert multiple glTF/GLB, FBX, OBJ, 3DS, PLY, STL, and PropXML assets.
- Add Blender-style Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, and Grid primitives directly from the composer toolbar (Monkey/Suzanne intentionally omitted).
- Open a closable floating **Material** editor with a categorized PBR preset library, direct numeric PBR/material-property controls, exact RGB/hex color setters, six PBR texture-map slots, and UV transform/addressing controls.
- Browse objects in a recursive scene tree and select them from the tree or rendered viewport; Ctrl-click builds an object multi-selection for grouping.
- Orbit, pan, and zoom the preview on every rendering backend.
- Highlight the selected object and draw overlaid move, rotation-ring, and scale gizmos for X/Y/Z transforms, including uniform scaling.
- Switch among Object, Vertex, Edge, and Face selection. Renderer triangles are grouped into persistent logical polygon faces only when the topology proves they belong together (for example a Cube exposes six quad faces and twelve logical edges, with no selectable triangulation diagonals), with Move plus right-click Extrude/Inset face operations.
- Edit position, rotation, and scale numerically; Enter or **Apply transform** bakes the change into vertex positions and normals, clears the fields, and redraws the active renderer.
- Inserted assets are wrapped in one top-level group while retaining their imported child hierarchy.
- Rename, show/hide, duplicate, delete, reset, frame, group, or ungroup scene nodes. Ctrl-clicked sibling objects can be wrapped in one hierarchy group without changing their geometry.
- Undo and redo transforms, procedural/material edits, polygon face edits, and hierarchy Group/Ungroup operations with toolbar buttons or `Ctrl+Z` / `Ctrl+Y`.
- Expand a mesh through a lazy `… show faces` row. Logical face rows are virtual and paged 200 at a time, so a Cube shows six editable faces rather than twelve renderer triangles and browsing them does not enlarge the scene or GPU buffers.
- Save and reopen `.lscene` compositions.
- Preview with software raster, Vulkan raster, Vulkan compute, or CPU ray rendering. When CPU is selected, **CPU…** opens render settings for resolution, samples, bounces, field of view, and exposure.
- Show frame time, approximate FPS, object count, triangle count, and process memory.
- Use the same executable for headless command-line rendering.

## Requirements

- .NET 10 SDK for building and running from source.
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

- Left click in Object mode: select the highest imported object group under the pointer. **Ctrl+left click** toggles objects into/out of a multi-selection. Click empty viewport space to deselect everything.
- `1`, `2`, `3`, and `4`: switch to Vertex, Edge, Face, and Object selection. Edge and vertex picks must be close to the projected component; Face mode selects the complete logical polygon under the pointer, not merely one renderer triangle. Component modes hide the object bounding box until a component is selected, then show only the component highlight and move gizmo.
- In Face mode, **right-click a face** for **Extrude Face…** or **Inset Face…**. Extrude uses a signed real-world distance: positive moves toward the object exterior and negative moves inward, even if imported source triangles are wound backwards. Inset has independent **Inset distance** and **Signed depth** controls in meters; `+0.02 m` moves the inset inward, `-0.02 m` protrudes it outward, and `0` keeps it planar. Inset also offers a **Depth profile**: **Square (90° reveal)** preserves the existing flat-ring-plus-wall construction, while **Sloped (Blender-style)** connects the source boundary directly to the displaced inset boundary for a tapered border. At zero depth the profiles are geometrically identical. A topology operation explicitly converts a procedural primitive to an editable mesh; Undo restores the prior procedural definition.
- Logical faces are editor topology layered over the triangle renderer. Authored primitive faces are retained explicitly; triangle-only imports are merged conservatively only for structurally valid, connected, consistently wound faces with one boundary loop. Inferred groups must also be planar, material-compatible, and preserve textured UV seams. Ambiguous coplanar edges remain separate. Native `.lscene` saves persist explicit logical-face groups.
- Ctrl-click two or more sibling objects, then press **Group** (or `Ctrl+G`). Select one or more hierarchy groups and press **Ungroup** (`Ctrl+Shift+G`) to dissolve them. The active object remains the gizmo/Inspector target while the whole multi-selection stays highlighted in the tree.
- Choose Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, or Grid and press **Add primitive**. A closable floating **Parameters** window opens for procedural editing.
- Use **Material…** in the Inspector to apply a library preset, directly edit PBR/material values, set an exact RGB/hex color, assign Base Color / Metallic-Roughness / Normal / Emissive / Transmission / Occlusion maps, and edit texture mapping for the selected object/subtree.
- `G`, `R`, and `S`: choose Move, Rotate, or Scale, matching Blender's primary transform shortcuts. The toolbar selector provides the same modes.
- Drag a red, green, or blue move axis, rotation ring, or scale handle. The white center scale handle scales uniformly. Hold Shift for precision and Ctrl for snapping. In Object mode, Composer uses the transform gizmo as the selection cue and does not draw an additional object bounding box or triangle wireframe before, during, or after a transform.
- Vulkan raster previews object transforms live through a transform uniform. Component movement patches only affected triangle vertices in the existing GPU buffers, so the rendered mesh deforms during the drag; shared welded vertices are committed once on release.
- Software raster and sufficiently fast Vulkan compute frames are coalesced and throttled as pseudo-real-time previews. CPU ray rendering waits for release.
- Right drag: orbit.
- Middle drag or Shift+right drag: pan.
- Mouse wheel: zoom.
- Precision touchpad: on Windows 11, native two-contact input maps common movement to orbit, pinch/spread to zoom, and two-finger twist to turntable rotation around the scene center. Other platforms keep their existing Avalonia input paths.
- Touchscreen: two-finger centroid movement orbits while finger separation zooms, simultaneously.
- Arrow keys: orbit; Shift+arrow keys: pan.
- `F`: frame the selected tree node.
- `Ctrl+D`: duplicate the selected node.
- `Ctrl+G`: group the current object multi-selection; `Ctrl+Shift+G`: ungroup selected hierarchy groups.
- `Delete`: delete the selected node.
- `Ctrl+Z` / `Ctrl+Y`: undo or redo.
- Use a disclosure arrow to open group nodes. Use the lazy `… show faces` row to page logical polygon faces without creating thousands of scene nodes.

Object-mode viewport selection resolves to the highest top-level asset group, so clicking a model moves the complete inserted asset by default. The hierarchy panel has separate disclosure arrows for expanding and collapsing nodes. Selecting a child explicitly in the hierarchy makes position, rotation, scale, visibility, name, framing, and gizmo operations target that child node.

## Parameterized primitives and real-world units

Composer uses **meters (m)** as its scene length unit. Object Position fields and every primitive dimension labeled as a length are authored directly in meters; scale remains dimensionless and rotation remains degrees in the inspector. This makes values such as `2.4 m × 0.9 m × 0.75 m` directly usable for real specifications.

The standard primitive set mirrors 3D viewport's Add → Mesh primitives except Monkey/Suzanne:

- Plane — width, depth
- Cube — width, height, depth
- Circle — vertices, radius, fill type
- UV Sphere — segments, rings, radius
- Icosphere — subdivisions, radius
- Cylinder — vertices, radius, depth, caps
- Cone — vertices, radius 1, radius 2, depth, caps
- Torus — major/minor segments and major/minor radius
- Grid — X/Y subdivisions, width, depth

The parameter window is modeless and can be closed and reopened while the object remains procedural. Parameter changes regenerate only that object's shadow mesh and are grouped into undoable edit batches. **Convert to Mesh** or a committed Vertex/Edge/Face geometry edit removes the procedural metadata and leaves the generated triangles as an ordinary editable mesh. Saved `.lscene` files retain the primitive kind and parameter values while the object is still procedural. See [`PARAMETERIZED_PRIMITIVES.md`](PARAMETERIZED_PRIMITIVES.md).

## Materials, exact color, and textures

Select an object and press **Material…** in the Inspector to open a modeless, closable material editor. The built-in material library includes categorized metal, paint, plastic, glass, stone, organic, liquid, and emissive presets. Applying a preset changes the material's PBR values while retaining assigned texture maps. The same window exposes those renderer-backed values directly: metallic, roughness, transmission, opacity, IOR, emission/color, alpha mode/cutoff, double-sided, thickness and attenuation in meters, clearcoat, normal scale, and occlusion strength.

Base color can be entered as exact 0–255 RGB channels or hexadecimal `#RRGGBB`. Material and color edits are independent of geometry: moving, rotating, scaling, recoloring, or assigning a texture to a parameterized primitive does **not** convert it to a mesh. The material is reused whenever its procedural shadow mesh is regenerated.

The texture editor accepts PNG, JPEG, BMP, TGA, GIF, PSD, and HDR images supported by the existing managed texture decoder. Separate renderer-backed slots are available for **Base Color**, **Metallic/Roughness**, **Normal**, **Emissive**, **Transmission**, and **Occlusion** maps.

Texture mapping provides **Authored / current UVs** or **Box projection (meters)**, plus per-texture U/V offset, U/V scale, rotation in degrees, and Repeat / ClampToEdge / MirroredRepeat address modes. Box projection uses a real-world tile size such as `0.3 m` for a 30 cm material tile. Parameterized primitives retain the projection mode through regeneration. Imported meshes currently retain one UV channel in Composer; multiple `TEXCOORD_n` sets and face-by-face UV editing are reserved for a future UV Editor.

Preset, direct-property, color, texture-slot, mapping, and clear-texture operations each create an undo entry. Material edits invalidate renderer material/texture caches but do not alter object topology.

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

Transforms are authored as temporary editor values while dragging. For ordinary meshes, commit rewrites triangle positions/normals as before. Parameterized primitives instead accumulate Move/Rotate/Scale into a hidden authored affine layer, regenerate their shadow mesh, and keep all procedural shape parameters editable. The node transform fields still return to identity, so later render frames keep the same baked-geometry performance model. Vertex/edge/face editing or explicit Convert to Mesh are the operations that intentionally discard the procedural definition. Vulkan raster refreshes the existing vertex buffers in place; textures, descriptor sets, pipelines, and render-target allocations remain cached.

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

`.lscene` version 12 embeds decoded RGBA texture pixels during normal **Save
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

### Renderer settings

Composer has a general **Settings…** dialog next to the renderer selector. The same dialog is used for Raster, Vulkan raster, Vulkan compute, and CPU. Unsupported controls remain visible but disabled for the selected mode.

- Raster: output width/height.
- Vulkan raster: output width/height.
- Vulkan compute: width/height, samples, bounces, FOV, exposure, ambient strength, shadows, and background colors.
- CPU: width/height, samples, bounces, FOV, and exposure.

During interactive Vulkan-compute camera movement, Composer temporarily uses one sample and zero bounces for responsiveness, then applies the configured quality to the idle render.

### Menu bar

The former top toolbar has been replaced with an application menu bar:

- **File**: New, Open, Insert model, Save scene, Export package
- **Edit**: Undo, Redo, Duplicate, Group, Ungroup, Delete
- **Add**: Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, Grid
- **Object**: Parameters, Material, Apply/Reset transform, Frame selected
- **Mode**: Selection mode, transform gizmo, component move-axis lock
- **Render**: renderer selection and renderer-specific Settings

Renderer and mode entries are radio menu items and remain synchronized with the
existing keyboard shortcuts and internal selection state. The current scene
path is shown in the bottom status area.
