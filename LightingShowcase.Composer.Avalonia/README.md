# Composer project

This executable provides the Avalonia scene-composition UI and the headless `render` command.

Committed transforms are baked into triangle positions and normals. The UI clears the transform fields, records an undo command, refreshes the Vulkan raster vertex buffers in place, and redraws. The hierarchy uses lazy virtual triangle rows so inspecting a large mesh does not create additional renderable scene nodes. Any non-terminal selected node can be ungrouped.

See the repository-level `README.md` and `TESTING.md` for usage and verification commands.
