# Primitive and mesh editing

## Add a primitive

Choose **Cube**, **Plane**, **Sphere**, or **Cylinder** in the toolbar and press **Add primitive**. The new object is selected in Object mode and participates in the normal transform, undo, save, and export workflows.

## Selection modes

| Key | Mode | Initial edit operation |
| --- | --- | --- |
| `1` | Vertex | Move one welded vertex |
| `2` | Edge | Move both welded edge vertices |
| `3` | Face | Move the three welded vertices of one triangle face |
| `4` | Object | Move, rotate, and scale the complete object/subtree |

In Face mode, click the visible face directly. Vertex and edge picking deliberately include a screen-space margin of error: vertices accept the nearest projected corner within **22 pixels**, while edges accept the nearest projected segment within **18 pixels**. Nearby sample rays also cover the margin immediately outside the object's silhouette, so the pointer does not have to land on an exact one-pixel line.

When the pointer enters a valid pick region, the candidate component pulses green before selection. Click while it is pulsing to select it. The hover probe is throttled to 30 Hz, and its pulse changes every 280 ms. Component modes do not display the object bounding box. After a component is selected, only its highlight and move gizmo are shown.

### Movement constraints

The **Axis** toolbar control and keyboard shortcuts constrain vertex, edge, and face movement:

- `X`, `Y`, or `Z` locks movement to that world axis.
- `A` returns to automatic/unlocked movement.
- When locked, only the constrained gizmo axis is drawn and hit-testable.
- Shift enables precision and Ctrl snaps.

The selected constraint remains active while changing among Vertex, Edge, and Face modes, and resets when returning to Object mode or loading/replacing geometry.

The first implementation treats each render triangle as one selectable face. Polygon reconstruction, box selection, multi-selection, rotation, scaling, extrusion, inset, and deletion are intentionally left for later iterations.

## Welded topology

The renderer stores immutable triangle soup, so the composer reconstructs an indexed edit topology on demand:

- Positions are placed in a spatial hash.
- The 27 neighboring hash cells are searched and actual Euclidean distance is checked.
- The automatic weld tolerance is `clamp(bounds diagonal × 1e-7, 1e-9, 1e-4)` scene units.
- Triangle corners that resolve to the same welded vertex move together.
- Edges are unique unordered pairs of welded vertex indices.
- UVs and materials remain per triangle corner; affected face normals are recalculated after a component move.

The topology cache is tied to the scene revision and is discarded after geometry changes.

## Join + weld imported assets

Imported assets are normally wrapped in a top-level object while retaining their internal hierarchy. Select that wrapper and press **Join + weld** to:

1. Bake pending transforms through the selected subtree.
2. Flatten all descendant triangles into the selected object.
3. Assign the selected object ID to the merged triangles.
4. Weld coincident positions with the topology tolerance.
5. Remove the former child hierarchy and rebuild renderer caches once.

Materials and UVs remain attached to their source triangle corners. The operation is backed by before/after scene snapshots and can be undone.

## Preview and commit behavior

During a Vulkan raster component drag, the renderer maps the welded selection to affected triangle corners once, then patches only those triangle vertices in the already allocated opaque or transparent GPU buffer. Adjacent faces that share a welded vertex are updated too, including their preview normals. The scene hierarchy, scene revision, textures, descriptors, pipelines, render targets, and buffer allocations remain unchanged during the drag. Mouse release performs one authored-geometry update, one world-geometry rebuild, one renderer-cache refresh, and one undo command. Software raster and other sufficiently fast modes retain the coalesced overlay preview; CPU rendering waits for release.
