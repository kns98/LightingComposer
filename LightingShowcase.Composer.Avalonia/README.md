# Composer project

This executable provides the Avalonia scene-composition UI and the headless `render` command.

Committed transforms are baked into triangle positions and normals. The UI clears the transform fields, records an undo command, refreshes the Vulkan raster vertex buffers in place, and redraws. The hierarchy uses lazy virtual triangle rows so inspecting a large mesh does not create additional renderable scene nodes. Any non-terminal selected node can be ungrouped.

See the repository-level `README.md` and `TESTING.md` for usage and verification commands.

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
