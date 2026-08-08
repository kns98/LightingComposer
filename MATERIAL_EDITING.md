# Material library, direct PBR properties, texture maps, and UV mapping

Lighting Composer exposes material editing from **Inspector → Material…**. The editor is modeless and closable, and targets the currently selected object/subtree. Library presets are optional starting points: the underlying renderer-backed material values and image maps can also be entered directly.

## Material library and direct properties

The built-in PBR preset library is defined by `MaterialPresetLibrary.Common` and includes metals, paint, plastics, glass, stone, organic materials, liquids, and emissive surfaces. Applying a preset changes scalar/PBR appearance values while retaining image maps already assigned to the current material.

The floating editor exposes metallic, roughness, transmission, opacity, IOR, emission strength/color, alpha mode/cutoff, double-sided rendering, thickness and attenuation distance in meters, attenuation color, clearcoat, clearcoat roughness, normal scale, and occlusion strength. Press **Apply properties** to create one undoable material edit while preserving base color and all assigned texture maps.

## Exact base color

Base color can be entered either as `#RRGGBB` or as R/G/B channels from 0 to 255. Color changes are material-only edits and do not bake geometry or destroy procedural primitive parameters.

## Texture maps

The Material window exposes the renderer-backed PBR image inputs independently:

- Base color
- Metallic / roughness
- Normal
- Emissive
- Transmission
- Occlusion

Each slot has its own **Browse…** and **Clear** controls. The existing managed `TextureMap` decoder accepts PNG, JPEG, BMP, TGA, GIF, PSD, and HDR inputs. Assigning a map preserves every other material property and texture slot.

## Texture mapping

All current `Triangle` objects carry one stored UV channel. The Material window therefore exposes a shared geometry UV source plus a per-texture transform.

**UV source**:

- **Authored / current UVs** leaves the UV values currently stored on the mesh unchanged. This is the correct choice for imported glTF/GLB assets such as the stained-glass lamp, where the imported UV layout determines which image region lands on each piece.
- **Box projection (meters)** regenerates the shared triangle UV channel from geometry and a real-world tile size. This is useful for wood, stone, brick, fabric, and other repeating materials.

For parameterized primitives, switching from box projection back to authored/current UVs regenerates the primitive so its normal generated UVs return immediately. For imported ordinary meshes, box projection replaces the stored UV channel; use undo/reload to recover an earlier imported layout.

For the selected texture slot, the editor can set:

- Offset U / V
- Scale U / V
- Rotation in degrees
- Wrap U / V: Repeat, ClampToEdge, or MirroredRepeat

These values are stored directly on `TextureMap` and are consumed by the existing raster and ray renderers. Texture transforms are independent for each map even though all maps currently sample the same triangle UV channel.

The **UV set** row is intentionally read-only in this release because Composer's `Triangle` model currently retains one UV channel. Supporting multiple imported `TEXCOORD_n` sets and per-face UV vertex editing requires the planned dedicated UV Editor rather than silently pretending the extra data exists.

## Procedural objects and persistence

Material, color, and texture-map changes do not convert parameterized primitives to meshes. The material is reused whenever a primitive regenerates. Texture projection metadata is stored as hidden Composer metadata so the selected projection mode survives parameter edits, transforms, undo/redo, and native `.lscene` serialization. Texture image transforms and address modes are also serialized by the existing texture table.

## Undo and renderer behavior

Preset, direct-property, base-color, texture assignment, per-slot mapping, projection-mode, and clear-texture actions create undo commands using immutable triangle references. Topology does not change. Material changes invalidate prepared material/texture renderer resources so the next frame rebuilds the relevant cached scene data.
