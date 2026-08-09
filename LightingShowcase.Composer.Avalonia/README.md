# Composer project

This executable provides the Avalonia scene-composition UI and the headless `render` command.

Committed transforms are baked into triangle positions and normals. The UI clears the transform fields, records an undo command, refreshes the Vulkan raster vertex buffers in place, and redraws. The hierarchy uses lazy virtual triangle rows so inspecting a large mesh does not create additional renderable scene nodes. Any non-terminal selected node can be ungrouped. The toolbar adds Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, and Grid primitives (Monkey/Suzanne omitted). New primitives open a closable modeless Parameters window; all length parameters and Position fields are meters. The Inspector also has a closable modeless **Material…** editor with categorized PBR presets, direct renderer-backed PBR/material-property fields, exact RGB/hex colors, six PBR texture slots, and per-texture offset/scale/rotation/wrap controls. Meter-based box projection or authored/current UVs can be selected for textures. Object/Vertex/Edge/Face selection uses the 4/1/2/3 keys. Component editing starts with move only. Edge and face modes hide whole-object bounds, require direct component picking, and use live affected-vertex updates in Vulkan raster.

See the repository-level `README.md`, `PARAMETERIZED_PRIMITIVES.md`, `MATERIAL_EDITING.md`, `MESH_EDITING.md`, `VALIDATION_NOTES.md`, and `TESTING.md` for usage and verification commands.

## Export package

Choose **Export package…**, select the format in the dialog, and then select the
parent folder. The composer creates a new directory for each export. Every related
resource is external and numbered in the package root (`res_0001.ext`,
`res_0002.ext`, and so on). Normal **Save scene…** remains self-contained.

## Save responsiveness

Native scene saves stop the active preview render before taking the scene lock,
write atomically through a temporary file, and use fast compression. Shared
materials and textures are deduplicated by object reference, so a texture is not
rehashed once for every triangle that uses it. The status bar displays elapsed
save time for large scenes.

Parameterized primitives retain their procedural parameters after object Move/Rotate/Scale. Only topology edits or explicit Convert to Mesh make them ordinary meshes.
