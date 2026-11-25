# Draw a Christmas Tree in Composer

Build the sample tree using only the frontend: the Add menu, the Parameters
window, Inspector transform boxes, the Material editor, and the Lighting
Editor. No code, no imports. Values below are meters and match the automated
click test (`ChristmasTreeClickTests`, project
`LightingShowcase.Composer.UiTests`) step for step.

## Setup

1. Open Composer with an empty scene (File → New).
2. Press D4 to select Object mode (required: rotate and scale gizmos only
   work in Object mode; other modes force Translate).

## Ground and trunk

3. Add menu → **Plane**. In Parameters set Width 14, Depth 14, Apply, Close.
4. With the plane selected, type Position 0, 0, 0 in the Inspector and click
   **Apply transform**.
5. Click **Material…**, set Hex `#E6EBF2`, click **Apply color**, Close.
6. Add menu → **Cylinder**. Parameters: Radius 0.18, Height 1.0, Sides 12,
   Fill Caps checked. Apply, Close.
7. Move it to Position 0, 0.5, 0 via the Inspector, Apply transform.
8. Material Hex `#59381F`, Apply color, Close.

## Foliage cones

9. Add menu → **Cone**, three times with **Radius 2 = 0** (pointed top):
   - Bottom: Radius 1 = 2.0, Height 1.8, Sides 24, Fill Caps. Move to 0, 1.9, 0.
   - Middle: Radius 1 = 1.45, Height 1.5, Sides 24. Move to 0, 3.0, 0.
   - Top: Radius 1 = 0.9, Height 1.2, Sides 24. Move to 0, 3.95, 0.
10. Paint each cone `#296B21` through the Material editor.

## Topper and ornaments

11. Add **UV Sphere**, Radius 0.16, move to 0, 4.75, 0, paint `#D9A633` (gold).
12. Add seven **UV Sphere** ornaments, Radius 0.14 each, at
    (1.33, 1.6, 0), (-1.33, 1.6, 0), (0, 1.6, 1.33), (0, 1.6, -1.33),
    (0.72, 3.0, 0), (-0.72, 3.0, 0), (0.49, 3.9, 0).
13. Paint them cycling `#B3140F` red, `#D9A633` gold, `#1A40B3` blue.

## Presents

14. Add three **Cubes**: size 0.7 at (1.9, 0.35, 1.2) red `#B3140F`,
    size 0.55 at (-1.7, 0.275, 0.8) green `#296B21`,
    size 0.45 at (1.4, 0.225, -1.6) blue `#1A40B3`.
    (Cube origin is its center, so rest each at half its size above y = 0.)

## Lights

15. Render menu → **Lighting…**. Click **+ Point**, set Position
    (3.0, 5.0, -3.0), Color `#FFD999`, Intensity 6. Click **Apply**.
16. Click **+ Directional**, set Position (0, 5, 0), Direction
    (-0.4, -1.0, 0.3), Color `#B3CCFF`, Intensity 1.5. Click **Apply**. Close.

## Save and render

17. File → Save scene to `christmas-tree.lscene` and reopen it to confirm
    everything persists.
18. Switch the renderer combo through Raster, Vulkan Raster, Vulkan Compute,
    and CPU; the tree renders on all four. Save stills to `screenshots/`.

## Tips

- After any Add, the new object is already selected — just open Parameters.
- Prefer typing Inspector positions over dragging for exact stacking; use G
  to drag when eyeballing is fine.
- Frame button centers the view on the selection if you get lost.
