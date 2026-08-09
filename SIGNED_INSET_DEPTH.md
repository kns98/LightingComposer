# Signed inset depth

The Face-mode **Inset Face…** dialog uses two independent dimensions in meters:

- **Inset distance**: parallel offset from the selected logical face boundary.
- **Signed depth**: displacement of the inner cap normal to the source face.

Signed depth semantics:

- `+0.02 m` = 2 cm inward/recessed.
- `0 m` = planar inset.
- `-0.02 m` = 2 cm outward/raised.

For closed/solid-looking objects, Composer estimates the exterior side of the selected face from the face center relative to the welded mesh centroid. This makes the sign convention independent of triangle winding on ordinary objects such as cubes. If the exterior direction is geometrically ambiguous (for example, a single isolated plane centered on itself), the authored face normal is used as the fallback.

Any non-zero signed depth creates side walls between the planar inset ring and displaced inner cap. Positive depth winds those walls toward the recess opening; negative depth winds them away from the raised section. Texture depth scaling uses the absolute depth so both directions remain valid.

Inset/raised operations are topology edits and therefore convert procedural primitives to editable meshes, as before. Undo restores the previous object state.
