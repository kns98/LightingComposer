# Parameterized primitives

Lighting Composer keeps newly added standard primitives procedural until the user converts or edits their mesh geometry. The procedural parameters are authoritative; generated `LocalTriangles` are the render, picking, and export shadow geometry.

## Standard primitive set

The Add Primitive menu mirrors Blender's standard mesh primitives except Monkey/Suzanne:

| Primitive | Editable parameters |
| --- | --- |
| Plane | Width (m), Depth (m) |
| Cube | Width (m), Height (m), Depth (m) |
| Circle | Vertices, Radius (m), Fill Type |
| UV Sphere | Segments, Rings, Radius (m) |
| Icosphere | Subdivisions, Radius (m) |
| Cylinder | Vertices, Radius (m), Depth (m), Fill Caps |
| Cone | Vertices, Radius 1 (m), Radius 2 (m), Depth (m), Fill Caps |
| Torus | Major Segments, Minor Segments, Major Radius (m), Minor Radius (m) |
| Grid | X Subdivisions, Y Subdivisions, Width (m), Depth (m) |

## Units

The scene length unit is the **meter (m)**. Primitive dimensions, object Position values, and performance-grid spacing are entered directly in meters. Rotation fields remain degrees and object Scale remains dimensionless.

Examples:

- a 2.4 m room-height reference: `Height = 2.4`
- a 900 mm tabletop: `Width = 0.9`
- a 25 cm cylinder radius: `Radius = 0.25`

No hidden conversion is performed when the object is regenerated or saved. Imported coordinates are interpreted as scene meters as supplied; unitless formats such as OBJ are not silently rescaled, so source units should be known when combining imported and specification-driven geometry.

## Floating Parameters window

Adding a standard primitive automatically opens a modeless floating Parameters window. It can be moved, resized, closed, and reopened from the Inspector's **Parameters…** button.

Numeric/text changes are debounced for live preview. **Apply** closes the current undo batch while leaving the window open for another batch. Closing the window commits the current valid preview as one undoable edit. **Revert** restores the geometry and parameters from the beginning of the current batch.

## Procedural lifetime

The object remains procedural while `PrimitiveKind` and `PrimitiveParameters` are present. Parameter changes regenerate only the selected primitive's generated geometry. The existing scene serializers already persist this primitive metadata in `.lscene` files.

The object becomes an ordinary mesh when:

- **Convert to Mesh** is chosen in the Parameters window;
- **Join + weld** is applied; or
- a Vertex, Edge, or Face geometry move is committed.

The current transform-bake workflow also turns procedural geometry into ordinary authored triangles when a transform is baked, matching Composer's existing rule that baked triangles become the source of truth.

## Rendering behavior

Changing width/radius/etc. rebuilds the selected primitive's shadow triangles and refreshes renderer geometry. Unrelated materials, textures, and scene objects are not regenerated. Parameter changes that alter segment/subdivision counts necessarily change topology and may require a Vulkan geometry-buffer rebuild; dimension-only changes can use the existing geometry-refresh path when buffer shape remains compatible.
