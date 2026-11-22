# LightingShowcase Avalonia Composer

Scene composer and renderer (Avalonia 12.1, .NET 10) for glTF/GLB, FBX, OBJ, 3DS, PLY, STL, and PropXML: procedural primitives, material and light editing, four render backends, UI or headless.

## Render gallery

Official [KhronosGroup glTF sample models](https://github.com/KhronosGroup/glTF-Sample-Models) in `models/`, rendered headless at 640×480 with 4 samples, 2 bounces, and zoom 1.6. ▶ Each turntable below is a 36-frame GIF at 12 fps that plays automatically — one per backend.

### Duck

**▶ raster**

![▶ Duck turntable, raster backend](screenshots/Duck-raster.gif)

**▶ Vulkan raster**

![▶ Duck turntable, Vulkan raster backend](screenshots/Duck-raster-vulkan.gif)

**▶ Vulkan compute**

![▶ Duck turntable, Vulkan compute backend](screenshots/Duck-vulkan.gif)

**▶ CPU**

![▶ Duck turntable, CPU backend](screenshots/Duck-cpu.gif)

### CesiumMan

**▶ raster**

![▶ CesiumMan turntable, raster backend](screenshots/CesiumMan-raster.gif)

**▶ Vulkan raster**

![▶ CesiumMan turntable, Vulkan raster backend](screenshots/CesiumMan-raster-vulkan.gif)

**▶ Vulkan compute**

![▶ CesiumMan turntable, Vulkan compute backend](screenshots/CesiumMan-vulkan.gif)

**▶ CPU**

![▶ CesiumMan turntable, CPU backend](screenshots/CesiumMan-cpu.gif)

### DamagedHelmet

**▶ raster**

![▶ DamagedHelmet turntable, raster backend](screenshots/DamagedHelmet-raster.gif)

**▶ Vulkan raster**

![▶ DamagedHelmet turntable, Vulkan raster backend](screenshots/DamagedHelmet-raster-vulkan.gif)

**▶ Vulkan compute**

![▶ DamagedHelmet turntable, Vulkan compute backend](screenshots/DamagedHelmet-vulkan.gif)

**▶ CPU**

![▶ DamagedHelmet turntable, CPU backend](screenshots/DamagedHelmet-cpu.gif)

Model licenses: Duck under SCEA Shared Source 1.0; DamagedHelmet under CC Attribution-NonCommercial; CesiumMan under CC-BY 4.0.

### Render times

AMD Ryzen 3 7320U (8 threads), AMD Radeon integrated graphics, 8 GB RAM, Windows 11, .NET 10. Steady-state time per frame (median; first frame excluded as one-time scene upload).

| Model | raster | raster-vulkan | vulkan | cpu |
|---|---|---|---|---|
| Duck | 221 ms | 4.4 ms | 342 ms | 1.7 s |
| CesiumMan | 111 ms | 4.1 ms | 181 ms | 0.7 s |
| DamagedHelmet | 368 ms | 8.5 ms | 574 ms | 2.2 s |

```bash
./run.sh render ./models/DamagedHelmet.glb --renderer raster-vulkan \
  --output ./screenshots/DamagedHelmet-raster-vulkan.gif \
  --width 640 --height 480 --samples 4 --bounces 2 --zoom 1.6 \
  --frames 36 --fps 12
```

Headless default lights are seated relative to the camera so they always cover the visible area. Override them with repeatable `--light "kind=point;position=3,4,-2;color=1,1,1;intensity=5"` (kind, direction, range, cones, shadows, id, enabled also supported).

### glTF feature support

The importer reads glTF 2.0 `.gltf`/`.glb`: triangle primitives (`POSITION`, `NORMAL`, `TEXCOORD_n`, `COLOR_0`), node hierarchies, core PBR materials, `KHR_materials_transmission`, `_ior`, `_volume`, `_clearcoat`, `KHR_texture_transform`, and `KHR_lights_punctual`. Not supported: skins, animations, morph targets, cameras, Draco/meshopt, sheen/specular/anisotropy/iridescence.

| Feature | raster | raster-vulkan | vulkan | cpu |
|---|---|---|---|---|
| Base color, metallic/roughness, emissive + textures | Yes | Yes | Yes | Yes |
| Normal maps, occlusion | No | Yes | Yes | Yes |
| Alpha blend / mask, double-sided | Discard only | Yes | Mask only | Blend approx |
| Transmission | No | Refraction | Pass-through | Blend approx |
| Clearcoat, IOR, volume | No | Yes | No | No |
| Texture transform + wrap, vertex colors, lights | Yes | Yes | Partial | Yes |

## Build and run

Requires the .NET 10 SDK and, for Vulkan modes, a Vulkan-capable driver. Open `LightingShowcase.Composer.sln` in VS 2022 17.8+/Rider, or `dotnet build`. Run `./run.sh`; headless verbs: `render`, `export`, `formats`, `--help`.

## Controls

`1/2/3/4` selection modes; `G/R/S` gizmos; drag axes or handles (Shift precise, Ctrl snap); right-drag orbit, middle-drag pan, wheel zoom; `F` frame, `Ctrl+D` duplicate, `Ctrl+G` group, `Delete`, `Ctrl+Z/Y` undo/redo; right-click a face for Extrude/Inset; Ctrl-click multi-selects.

## Docs

[Primitives](docs/PARAMETERIZED_PRIMITIVES.md) · [Materials](docs/MATERIAL_EDITING.md) · [Lighting](docs/LIGHTING_EDITOR.md) · [Mesh editing](docs/MESH_EDITING.md) · [Transform preview](docs/GIZMO_TRANSFORM_PREVIEW.md)
