# Testing the Avalonia composer

The automated suite is in `LightingShowcase.Composer.Tests` and is included in
`LightingShowcase.Composer.sln`.

## Windows / Visual Studio

1. Extract the repository to a normal local folder. Do not open the solution from inside the ZIP.
2. Open `LightingShowcase.Composer.sln` in Visual Studio 2022.
3. Install the workloads listed in `.vsconfig` when prompted.
4. Build **Solution**.
5. Open **Test > Test Explorer** and run all tests.
6. Set `LightingShowcase.Composer.Avalonia` as the startup project to run the UI.

PowerShell equivalent:

```powershell
.\run-tests.ps1
```

## Linux

```bash
./run-tests.sh
```

## What the suite proves

- Inspector text is parsed by the same `ComposerTransformRequest` used by the
  **Apply transform** button.
- A committed transform changes the authoritative local triangle positions and
  normals, then leaves position/rotation/scale metadata at identity.
- Scene revision, local-geometry hash, world-geometry hash, and bounds are checked.
- Undo restores the exact original immutable triangle references; redo produces
  the exact committed geometry hashes again.
- Blank transform fields parse as identity, matching the editor after it clears
  all nine text boxes.
- Gizmo pointer moves do not increment scene revision or trigger cache rebuilds;
  the revision changes only when the pending transform is committed.
- Selection and virtual triangle selection do not mutate the scene, increase
  object count, or invalidate the Vulkan geometry cache stamp.
- Lazy triangle pages expose triangle leaves without adding scene nodes.
- Root and nested nodes can be ungrouped, and the operation is undoable.
- Software preview pixels change after a baked transform.
- Every project referenced by the Visual Studio solution exists.
- CPU and GPU renderer caches are tied to both Scene identity and Scene revision.

## Optional Vulkan verification

The default suite does not require a Vulkan-capable CI runner. On a machine with
Vulkan support, run the opt-in raster and compute tests:

```powershell
$env:LIGHTINGSHOWCASE_RUN_GPU_TESTS = "1"
dotnet test .\LightingShowcase.Composer.Tests\LightingShowcase.Composer.Tests.csproj -c Debug --filter Category=Gpu
```

```bash
LIGHTINGSHOWCASE_RUN_GPU_TESTS=1 dotnet test \
  LightingShowcase.Composer.Tests/LightingShowcase.Composer.Tests.csproj \
  -c Debug --filter Category=Gpu
```

These render the same Scene instance before and after a baked transform, assert that the Vulkan output pixels differ, and verify that Vulkan raster reports an in-place vertex refresh rather than a complete texture/pipeline rebuild when topology and materials are unchanged.

## Avalonia UI-thread regression

`InspectorThreadSafetyTests` verifies that the transform worker payload contains no
Avalonia objects and that the same work item used by the Apply button can execute
on a worker thread while modifying authoritative model geometry.

## Embedded resource and export-package tests

The normal suite also verifies that `.lscene` files reopen textures without the
original image files, distinct in-memory textures are preserved, every advertised
export route resolves to a registered exporter, and each format creates a new
package directory containing its primary file, manifest, and related textures.

## Live transform gizmo validation

The opt-in GPU suite includes a Vulkan raster test that applies pending rotation and non-uniform scale without committing the scene. It verifies that:

- the scene revision remains unchanged while dragging;
- the render details report `live-transform=<selection id>`;
- preview pixels change before the geometry bake; and
- cancellation restores the identity pending transform.

For an interactive timing check, select Vulkan raster, load a representative model, press `R` or `S`, and drag a gizmo. The frame details separate uniform update, command recording, GPU wait, readback, and total frame time. The hot path should show `cache=hot` and `live-transform=...`; it should not report a vertex-buffer refresh until pointer release.
## Live component-edit validation

The normal mesh-edit tests verify that entering Edge or Face mode does not expose an object-level bounding box before a component is picked. The opt-in GPU suite also moves a welded face without committing geometry and verifies that:

- the scene revision stays unchanged during the drag;
- render details report `live-mesh-edit=<selection id>`;
- the Vulkan raster pixels change while the pointer is moving; and
- cancellation leaves the authoritative mesh unchanged.

For an interactive timing check, choose Vulkan raster, enter Edge or Face mode, click a visible component, and drag its move gizmo. The frame details report `mesh-edit-upload=...ms`; the cache should remain hot and no full geometry refresh should occur until pointer release.
