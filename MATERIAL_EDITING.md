# Material library, direct PBR properties, color, and texture editing

Lighting Composer exposes material editing from **Inspector → Material…**. The editor is modeless and closable, and targets the currently selected object/subtree. Library presets are optional starting points: the underlying renderer-backed material values can also be entered directly.

## Material library

The built-in PBR preset library is defined by `MaterialPresetLibrary.Common` and includes metals, paint, plastics, glass, stone, organic materials, liquids, and emissive surfaces. Applying a preset changes scalar/PBR appearance values while retaining image maps already assigned to the current material.

## Direct material properties

The floating editor exposes the material values already consumed by the renderers rather than storing UI-only metadata. Press **Apply properties** to create one undoable material edit while preserving base color and all assigned texture maps.

Direct controls include:

- metallic and roughness;
- transmission and opacity;
- index of refraction (IOR);
- emission strength and emission color;
- alpha mode and alpha cutoff;
- double-sided rendering;
- thickness in meters;
- attenuation color and attenuation distance in meters;
- clearcoat and clearcoat roughness;
- normal-map scale and occlusion strength.

Presets refresh these controls after application, so a preset can be selected and then numerically tuned. Direct material edits do not convert parameterized primitives to meshes.

## Exact base color

Base color can be entered either as `#RRGGBB` or as R/G/B channels from 0 to 255. The UI shows a swatch before the color is applied. Color changes are material-only edits and do not bake geometry or destroy procedural primitive parameters.

## Base-color textures

The texture setter uses the existing managed `TextureMap` decoder. It accepts PNG, JPEG, BMP, TGA, GIF, PSD, and HDR inputs.

Two UV modes are available:

- **Box projection**: generates UVs from object position and a repeat/tile size expressed in scene meters. This is useful for architectural and product-scale materials.
- **Authored UVs**: preserves the UV coordinates already stored on imported or generated triangles.

For procedural primitives, the box-projection flag and tile size are stored as hidden double parameters (`__composerTextureBoxProjection` and `__composerTextureTileMeters`). They are intentionally omitted from the public Parameters window, but survive transforms, parameter edits, undo/redo, and `.lscene` serialization. When procedural geometry is regenerated, meter-based box projection is reapplied automatically.

## Undo and renderer behavior

Preset, direct-property, base-color, texture assignment, and clear-texture actions each create an undo command using immutable triangle references. Topology does not change. Material changes invalidate prepared material/texture renderer resources so the next frame rebuilds the relevant cached scene data.
