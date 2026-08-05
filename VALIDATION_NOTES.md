# Validation notes: primitive and mesh editing update

## Scope implemented

- Toolbar insertion for Cube, Plane, Sphere, and Cylinder.
- Object, Vertex, Edge, and Face selection modes (`4`, `1`, `2`, `3`).
- Move-only vertex, edge, and triangle-face editing with X/Y/Z gizmos.
- On-demand welded indexed topology reconstructed from triangle soup.
- Neighbor-cell spatial-hash welding with actual-distance verification and a bounded automatic tolerance.
- `Join + weld` to bake and flatten an imported subtree into one editable mesh.
- Overlay-based drag preview followed by one geometry/cache rebuild on release.
- Undo/redo records for component movement and scene-snapshot undo for join/weld.
- Topology and import-flattening unit tests.

## Checks completed in the artifact environment

- Lexical C# delimiter validation for every added or modified `.cs` file, ignoring comments and string/character/raw-string contents.
- XML parsing of every `.csproj`.
- Verification that every project path referenced by `LightingShowcase.Composer.sln` exists.
- `git diff --check` with no whitespace errors.
- Clean application of `LightingComposer-mesh-edit-primitives.patch` to the rotation/scale baseline.
- Exact recursive comparison between the patch-applied tree and the packaged source tree.
- Verification that the source archive contains no `.git`, `bin`, or `obj` directories.

## Build and runtime limitation

The container used to prepare this update does not contain `dotnet`, MSBuild, Mono, or a C# compiler, and it has no Vulkan desktop session. The solution and tests therefore could not be compiled or executed here. Run the commands below on a development machine before merging:

```bash
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Release --no-restore
dotnet test LightingShowcase.Composer.Tests/LightingShowcase.Composer.Tests.csproj -c Release --no-build
```

Then verify interactively:

1. Add each primitive and enter Vertex, Edge, and Face modes.
2. Move a shared cube corner and confirm all adjacent triangles remain connected.
3. Import a hierarchy, select its wrapper, press **Join + weld**, and confirm the tree becomes one mesh.
4. Move a shared edge/face and test undo/redo.
5. On Vulkan raster, drag a component and confirm responsive overlay updates, with one final geometry refresh on release.
6. Save/reopen `.lscene` and export glTF/GLB to confirm materials and UVs remain attached.

## Deliberate first-version limits

- A face is one render triangle; polygon/quad reconstruction is not yet implemented.
- Mesh components support move only. Rotation, scaling, extrusion, inset, deletion, and multi-selection are future work.
- Component drag preview is an editor overlay; the underlying shaded mesh is committed on mouse release.
- The weld distance is automatic rather than user-configurable.
