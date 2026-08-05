# Rotation and scale gizmo preview

## User controls

- `G`: move gizmo.
- `R`: rotation rings around X, Y, and Z.
- `S`: axis scale handles plus a white uniform-scale handle at the pivot.
- `Shift` while dragging: precision motion.
- `Ctrl` while dragging: snap translation, rotation, or scale.
- Pointer release: bake the pending transform once and create one undo entry.
- Pointer capture loss: cancel the pending transform.

Rotation primarily intersects the pointer ray with the selected world-space rotation plane and accumulates signed 3D angle deltas. This keeps the response stable when the ring projects as an ellipse. A wrapped screen-angle path is retained for nearly edge-on planes, and crossing the -180/180 degree boundary remains continuous. Scale uses an exponential response, which keeps scaling positive and gives useful control over both small and large factors. Modifier changes are applied to incremental pointer motion, so engaging precision mode during a drag does not jump the object.

## Vulkan raster live path

The Vulkan raster renderer keeps the existing flattened world-space vertex buffers. During a gizmo drag it receives an editor-only `VulkanRasterTransformPreview` containing the selected subtree IDs, pivot, translation, Euler rotation, and scale.

When the Vulkan scene cache is prepared, the renderer also records contiguous opaque and transparent vertex ranges for each leaf group. On the first preview for a selection, it merges the already prepared ranges for that subtree and caches the result; it does not rescan all triangles. Each subsequent pointer frame only:

1. updates a 64-byte transform uniform;
2. records identity and preview draw ranges against the existing vertex buffers;
3. submits and reads back the normal preview target.

The vertex shader applies the same scale-around-pivot, X/Y/Z Euler rotation, translation, and inverse-transpose normal convention used by `TransformConverter`. No world-triangle rebuild or vertex upload occurs during the drag. The existing pointer-release path still bakes geometry once, refreshes the cached Vulkan vertex buffers in place, resets node transform metadata to identity, and records undo.

This design targets the existing roughly 20 ms Vulkan raster hot-frame behavior. Actual timing remains dependent on scene size, number of disjoint selected ranges, output resolution, GPU/driver, and readback cost. Render details include `live-transform=<id>` and phase timing so the path can be verified on the target machine.

## Other renderers

- **Software raster:** coalesced pseudo-real-time frames when the renderer is already fast enough. The transformed bounds and sampled wireframe update immediately; the final shaded geometry appears after release.
- **Vulkan compute:** uses the same adaptive coalescing threshold as camera interaction. It provides a pseudo-real-time transformed overlay while preserving the compute scene cache.
- **CPU ray renderer:** does not render on every pointer move. It updates after release to avoid a backlog of obsolete frames.

## Validation

The normal regression test verifies that pending move, rotation, and scale do not increment the scene revision and that commit returns transform metadata to identity. An opt-in Vulkan test verifies that pending rotation and non-uniform scaling change rendered pixels while the scene revision remains unchanged. Run it on a Vulkan-capable machine with:

```bash
LIGHTINGSHOWCASE_RUN_GPU_TESTS=1 ./run-tests.sh
```
