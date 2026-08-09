# Primitive and mesh editing

## Add a primitive

Choose **Plane**, **Cube**, **Circle**, **UV Sphere**, **Icosphere**, **Cylinder**, **Cone**, **Torus**, or **Grid** and press **Add primitive**. The new object starts as a parameterized procedural object and opens a modeless Parameters window. Length parameters are in meters. Monkey/Suzanne is intentionally not included.

## Procedural object to mesh

A newly added standard primitive keeps `PrimitiveKind` and `PrimitiveParameters` as its authoring source. `LocalTriangles` are the generated render/pick mesh. The Parameters window can be closed and reopened without losing this state.

Choose **Convert to Mesh** when the procedural controls are no longer wanted. If a Vertex, Edge, or Face move is committed, the mesh edit becomes authoritative and primitive metadata is cleared automatically. Undo restores the prior procedural state where the operation has an undo record.

## Selection modes

| Key | Mode | Initial edit operation |
| --- | --- | --- |
| `1` | Vertex | Move one welded vertex |
| `2` | Edge | Move both welded edge vertices |
| `3` | Face | Move a complete logical polygon face; right-click for Extrude/Inset |
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

## Logical polygon faces

Rendering still uses triangles, but editing does not have to expose those triangulation diagonals. `ComposerMeshTopology` groups renderer triangles into logical polygon faces. Standard Composer primitives use their authored primitive kind and parameters so the grouping is deterministic:

- Plane: one quad face.
- Cube: six quad faces (12 render triangles).
- Grid and Torus: one quad face for each generated two-triangle cell.
- UV Sphere: triangle faces at the poles and quad faces between rings.
- Icosphere: triangle-native faces.
- Cylinder/Cone: side polygons plus each filled cap as one polygon face.
- Circle with N-gon fill: the complete fan is one face.

After conversion/import, Composer falls back to welded adjacency and coplanar fan/quad recovery. A selected polygon highlights all of its renderer triangles and only its boundary edges.

### Face context menu

In Face mode, right-click a visible polygon to select it and open the face menu:

- **Extrude Face…** offsets the complete polygon by a signed distance in meters and builds side quads around its boundary. Positive distance means **outward** and negative means **inward**. Composer resolves the exterior side from the logical face relative to the mesh rather than trusting triangle winding, so a reversed imported face does not invert the control.
- **Inset Face…** performs a parallel edge inset in the face plane for convex faces. The dialog also provides **Signed depth (m)**. The default `+0.02 m` moves the inner cap inward and builds reveal walls; a negative value such as `-0.02 m` moves the cap outward to create a raised/protruding inset, while `0` gives a classic coplanar inset. Both dimensions are real scene distances in meters; planar UV placement is preserved where possible.

Extrude/Inset are topology edits, so a procedural primitive becomes an ordinary mesh after the operation. The edit is undoable; Undo restores the triangles and the procedural primitive metadata.

## Object multi-selection and hierarchy grouping

In Object mode, Ctrl-click in either the viewport or object tree to toggle objects in the current multi-selection. The most recently selected object is the active Inspector/gizmo target. **Group** / `Ctrl+G` wraps two or more selected sibling nodes in a new identity parent, preserving their existing geometry and local transforms. **Ungroup** / `Ctrl+Shift+G` dissolves selected hierarchy groups and promotes their children. Multi-selection ungroup deliberately does not explode leaf meshes into render triangles; the legacy single-node Ungroup operation remains available for mesh decomposition.

## Welded topology

The renderer stores immutable triangle soup, so the composer reconstructs an indexed edit topology on demand:

- Positions are placed in a spatial hash.
- The 27 neighboring hash cells are searched and actual Euclidean distance is checked.
- The automatic weld tolerance is `clamp(bounds diagonal × 1e-7, 1e-9, 1e-4)` scene units.
- Triangle corners that resolve to the same welded vertex move together.
- Edges are unique unordered pairs of welded vertex indices.
- UVs and materials remain per triangle corner; affected face normals are recalculated after a component move.

The topology cache is tied to the scene revision and is discarded after geometry changes.


## Preview and commit behavior

During a Vulkan raster component drag, the renderer maps the welded selection to affected triangle corners once, then patches only those triangle vertices in the already allocated opaque or transparent GPU buffer. Adjacent faces that share a welded vertex are updated too, including their preview normals. The scene hierarchy, scene revision, textures, descriptors, pipelines, render targets, and buffer allocations remain unchanged during the drag. Mouse release performs one authored-geometry update, one world-geometry rebuild, one renderer-cache refresh, and one undo command. Software raster and other sufficiently fast modes retain the coalesced overlay preview; CPU rendering waits for release.


## Logical face validation

The renderer remains triangle-based, while the editor stores persistent logical face groups with ordered boundary loops. Candidate groups must be connected, consistently wound, 2-manifold, and have one simple boundary. Triangle-only imports are merged conservatively; textured UV seams, folds, material changes, holes, and ambiguous coplanar edges prevent automatic merging. Internal triangulation diagonals are removed from Edge mode once a logical face is proven. Native `.lscene` saves persist the grouping.
