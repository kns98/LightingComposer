# Texture library licenses

The `textures/` folder ships 1K JPG maps from six [Poly Haven](https://polyhaven.com/)
assets. All Poly Haven assets are [CC0](https://polyhaven.com/license) (public
domain): commercial use and redistribution are allowed with no attribution
required. The table below still credits each source asset.

Downloaded 2026-11-25 at 1K JPG. Files were renamed from their upstream names
(`*_diff_1k.jpg` / `*_diffuse_1k.jpg` to `*_albedo_1k.jpg`, `*_nor_dx_1k.jpg` to
`*_normal_1k.jpg`, `*_rough_1k.jpg` to `*_roughness_1k.jpg`).

Normal convention: the `nor_dx` (DirectX) variant is shipped because the engine
samples textures with V=0 at the top of the bitmap (`TextureMap.Sample`) and
unpacks normal Y without a flip (`RayTracer.ApplyNormalMap`), which matches
top-left-origin UVs.

| File | Source asset | Map |
|---|---|---|
| brick_wall_001_albedo_1k.jpg | [Brick Wall 001](https://polyhaven.com/a/brick_wall_001) | Diffuse |
| brick_wall_001_normal_1k.jpg | [Brick Wall 001](https://polyhaven.com/a/brick_wall_001) | Normal (DirectX) |
| brick_wall_001_roughness_1k.jpg | [Brick Wall 001](https://polyhaven.com/a/brick_wall_001) | Roughness |
| concrete_wall_001_albedo_1k.jpg | [Concrete Wall 001](https://polyhaven.com/a/concrete_wall_001) | Diffuse |
| concrete_wall_001_normal_1k.jpg | [Concrete Wall 001](https://polyhaven.com/a/concrete_wall_001) | Normal (DirectX) |
| concrete_wall_001_roughness_1k.jpg | [Concrete Wall 001](https://polyhaven.com/a/concrete_wall_001) | Roughness |
| wood_planks_albedo_1k.jpg | [Wood Planks](https://polyhaven.com/a/wood_planks) | Diffuse |
| wood_planks_normal_1k.jpg | [Wood Planks](https://polyhaven.com/a/wood_planks) | Normal (DirectX) |
| wood_planks_roughness_1k.jpg | [Wood Planks](https://polyhaven.com/a/wood_planks) | Roughness |
| marble_01_albedo_1k.jpg | [Marble 01](https://polyhaven.com/a/marble_01) | Diffuse |
| marble_01_normal_1k.jpg | [Marble 01](https://polyhaven.com/a/marble_01) | Normal (DirectX) |
| marble_01_roughness_1k.jpg | [Marble 01](https://polyhaven.com/a/marble_01) | Roughness |
| denim_fabric_albedo_1k.jpg | [Denim Fabric](https://polyhaven.com/a/denim_fabric) | Diffuse |
| denim_fabric_normal_1k.jpg | [Denim Fabric](https://polyhaven.com/a/denim_fabric) | Normal (DirectX) |
| denim_fabric_roughness_1k.jpg | [Denim Fabric](https://polyhaven.com/a/denim_fabric) | Roughness |
| metal_plate_albedo_1k.jpg | [Metal Plate](https://polyhaven.com/a/metal_plate) | Diffuse |
| metal_plate_normal_1k.jpg | [Metal Plate](https://polyhaven.com/a/metal_plate) | Normal (DirectX) |
| metal_plate_roughness_1k.jpg | [Metal Plate](https://polyhaven.com/a/metal_plate) | Roughness |
| brown_leather_albedo_1k.jpg | [Brown Leather](https://polyhaven.com/a/brown_leather) | Diffuse |
| brown_leather_normal_1k.jpg | [Brown Leather](https://polyhaven.com/a/brown_leather) | Normal (DirectX) |
| brown_leather_roughness_1k.jpg | [Brown Leather](https://polyhaven.com/a/brown_leather) | Roughness |
