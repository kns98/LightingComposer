# Signed inset depth

The Face-mode **Inset Face…** dialog uses two independent dimensions in meters plus a depth-profile choice:

- **Inset distance**: parallel offset from the selected logical face boundary.
- **Signed depth**: displacement of the inner cap normal to the source face.
- **Depth profile**:
  - **Square (90° reveal)** keeps the inset ring coplanar with the source face and connects it to the displaced cap with perpendicular reveal walls. This is the existing/default behavior.
  - **Sloped (Blender-style)** connects the original face boundary directly to the displaced inset boundary, producing a tapered border instead of a vertical reveal.

Signed depth semantics:

- `+0.02 m` = 2 cm inward/recessed.
- `0 m` = planar inset.
- `-0.02 m` = 2 cm outward/raised.

For closed/solid-looking objects, Composer estimates the exterior side of the selected face from the face center relative to the welded mesh centroid. This makes the sign convention independent of triangle winding on ordinary objects such as cubes. If the exterior direction is geometrically ambiguous (for example, a single isolated plane centered on itself), the authored face normal is used as the fallback.

With **Square**, any non-zero signed depth creates side walls between the planar inset ring and displaced inner cap. Positive depth winds those walls toward the recess opening; negative depth winds them away from the raised section. Texture depth scaling uses the absolute depth so both directions remain valid.

With **Sloped**, the border quads themselves span from the source boundary to the displaced inset boundary, so no separate reveal-wall geometry is emitted. At `0 m` depth both profiles intentionally reduce to the same planar inset.

Inset/raised operations are topology edits and therefore convert procedural primitives to editable meshes, as before. Undo restores the previous object state.
