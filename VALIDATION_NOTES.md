# Parameterized primitive update — implementation and validation notes

## Baseline

The public GitHub `kns98/LightingComposer` `main` branch was rechecked on 2026-08-07 and still exposes the 10-commit repository tree and original Composer feature documentation. Direct `git clone` from the execution container was attempted but DNS access to `github.com` is blocked (`Could not resolve host: github.com`).

This update therefore uses the complete current-main source snapshot already obtained for the Composer work, plus the subsequent rotation/scale gizmo, welded mesh editing, live Vulkan component preview, forgiving hover picking, axis locks, and build fix requested in this conversation. The patch supplied with this delivery is relative to `LightingComposer-hover-pick-axis-lock-source.zip` so none of those newer editor changes are lost.

## Implemented

- Standard two-finger mesh primitive menu, excluding Monkey/Suzanne:
  - Plane
  - Cube
  - Circle
  - UV Sphere
  - Icosphere
  - Cylinder
  - Cone
  - Torus
  - Grid
- Procedural parameter descriptors shared through `IEditablePrimitiveDefinition`.
- All dimensional/length descriptors are authored in meters (`m`).
- Object inspector labels Position in meters.
- A closable, modeless floating `PrimitiveParametersWindow` opens automatically for a new standard primitive and can be reopened from the Inspector.
- Parameter controls are generated from descriptors: precise numeric entry, integer counts, toggle fields, and choices.
- Parameter edits debounce live geometry regeneration and are committed as undoable batches rather than one history entry per keystroke.
- Explicit **Convert to Mesh** removes primitive metadata while keeping the generated triangles.
- Existing committed Vertex/Edge/Face edits continue to make mesh geometry authoritative.
- Native `.lscene` serialization already retains `PrimitiveKind`, `PrimitiveSourceName`, and `PrimitiveParameters`, so procedural objects survive save/reopen while still parameterized.
- Added tests for the standard primitive set, meter descriptors, geometry regeneration, parameter undo/redo, conversion, and conversion undo.

## Unit behavior

Composer now treats its scene length values as meters for specification-driven authoring. Primitive lengths and Position/spacing UI values are direct meter values; rotation is degrees and scale is dimensionless. Imported geometry is interpreted as scene meters as supplied. Unitless formats are not silently rescaled.

## Static validation performed

- Parsed all project/props XML files successfully.
- Scanned all C# sources for balanced braces/brackets/parentheses after stripping comments and strings.
- Confirmed the standard primitive menu contains exactly the nine requested two-finger entries and no Monkey/Suzanne entry.
- Confirmed every exposed length descriptor is created with unit label `m`.
- Confirmed component-move commit still clears primitive metadata so a procedurally generated object becomes a normal mesh after component geometry editing.
- Generated a Git patch against a clean extraction of the previous corrected Composer package and verified it applies cleanly.
- Compared the patched baseline tree byte-for-byte against the packaged source tree (excluding repository metadata).

## Runtime limitation

This execution environment does not contain `dotnet`, `csc`, `mcs`, or a Vulkan desktop session. Therefore the updated source was not compiled or interactively exercised here. Do not treat the static checks as a substitute for a .NET build.

## Target-machine validation

```bash
dotnet clean
dotnet restore LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj
dotnet build LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj -c Release --no-restore
dotnet test LightingShowcase.Composer.Tests/LightingShowcase.Composer.Tests.csproj -c Release
```

Interactive check:

1. Start Composer and select **Cube** → **Add primitive**.
2. Confirm the floating Parameters window opens and shows Width/Height/Depth in meters.
3. Enter `2.4`, `0.9`, and `0.75` and confirm the viewport regenerates the cube.
4. Close the window, select the cube, and reopen **Parameters…**; values should remain available.
5. Test Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, and Grid controls.
6. Click **Convert to Mesh** and confirm **Parameters…** becomes disabled while Vertex/Edge/Face editing remains available.
7. Undo conversion and confirm parameter editing is restored.
8. With a procedural primitive, commit a Vertex/Edge/Face move and confirm the object thereafter behaves as a normal mesh.
