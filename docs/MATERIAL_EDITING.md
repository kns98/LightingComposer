# Material library, direct PBR properties, texture maps, and UV mapping

Lighting Composer exposes material editing from **Inspector → Material…**. The editor is modeless and closable, and targets the currently selected object/subtree. Library presets are optional starting points: the underlying renderer-backed material values and image maps can also be entered directly.

The editor is organized into **Library**, **Color**, **Surface**, **Light & Volume**, **Detail**, **Textures**, and **Mapping** tabs. The current-material summary, status line, and **Close** button stay in a persistent footer below the tabs so apply feedback is always visible.

## Material library and direct properties

The built-in PBR preset library is defined by `MaterialPresetLibrary.Common` and includes metals, paint, plastics, glass, stone, organic materials, liquids, emissive surfaces, ceramics, gems, paper, food, and nature materials. The **Library** tab offers a category filter over these categories. The preset dropdown reflects the current material: it selects the matching preset, or **Unassigned** when the values deviate from every library entry (textures, light links, and double-sided rendering never break a match). The `models/MaterialShowcase/MaterialShowcase.lscene` showroom is a furnished living room: pure-library objects select their dropdown entry on selection, while composed objects (library bases plus scalar-preset tweaks and texture triplets) light up their tab rows instead.

The **Library** tab also hosts **Scene materials**: press **Save as…** to store the current dialog values under a name in the scene file, then re-apply a saved entry to any object later. Saved materials capture base color and PBR values only; applying one behaves like a library preset (textures, light link, and double-sided rendering are preserved). Applying a preset overwrites the base color and resets extended PBR values (IOR, clearcoat, sheen, anisotropy, iridescence, …) to the preset, while retaining image maps already assigned to the current material, its light link, and its double-sided rendering choice. Double-sided rendering is the default for new materials; uncheck **Render both sides** on the **Surface** tab to opt a material back out.

Direct properties are split across three tabs that share one **Apply properties** button each: **Surface** (metallic, roughness, transmission, opacity, IOR, alpha mode/cutoff, double-sided), **Light & Volume** (emission strength/color, thickness and attenuation distance in meters, attenuation color, clearcoat, clearcoat roughness), and **Detail** (normal scale, occlusion strength, specular and specular color, sheen color and roughness, anisotropy and rotation, iridescence values). Every tab's Apply button validates and writes all three tabs at once, creating one undoable material edit while preserving base color and all assigned texture maps.

Each property tab also has one-click preset rows that fill that tab's editors without applying, so values can be tweaked before pressing Apply: finish and alpha presets on Surface, glow and glass presets on Light & Volume, and effect presets on Detail. The **Color** tab keeps the existing swatch library alongside the hex, RGB, and spectrum editors. A picked preset stays highlighted until its values are edited, and every tab pick immediately re-evaluates the **Library** dropdown against the dialog values.

## Exact base color

Base color can be entered either as `#RRGGBB` or as R/G/B channels from 0 to 255. Color changes are material-only edits and do not bake geometry or destroy procedural primitive parameters.

## Texture maps

The **Textures** tab exposes the renderer-backed PBR image inputs independently:

- Base color
- Metallic / roughness
- Normal
- Emissive
- Transmission
- Occlusion

Each slot has its own **Browse…** and **Clear** controls. The existing managed `TextureMap` decoder accepts PNG, JPEG, BMP, TGA, GIF, PSD, and HDR inputs. Assigning a map preserves every other material property and texture slot.

The same tab also hosts a built-in texture library: six CC0 1K materials (brick, concrete, wood planks, marble, denim, metal), each with albedo, normal, and roughness maps. Pick a material, map, and target slot, then press **Apply to slot** — library images flow through the same decode, undo, and per-slot mapping path as browsed files, and save/load with scenes unchanged. Per-file source credits live in `textures/TEXTURES_LICENSES.md`.

## Texture mapping

All current `Triangle` objects carry one stored UV channel. The **Mapping** tab therefore exposes a shared geometry UV source plus a per-texture transform, with mapping presets (authored UVs, box sizes, tiling, clamp) that fill the editors without applying.

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
