# Lighting Editor

Lighting Composer now exposes scene lighting as a first-class editor workflow rather than requiring lights to be authored only through imported scene data.

## Open the editor

Open **Render → Lighting…** to show the modeless Lighting Editor. A light can also be opened directly from the viewport: **right-click its light marker** without dragging. Right-drag continues to orbit the camera; the existing Face-mode right-click menu remains available when no light marker is under the pointer.

## Light list and properties

The editor lists every `SceneLight` in the active scene, including built-in defaults and imported lights. It can add or delete user lights and can edit the renderer-backed properties of point, spot, and directional lights:

- name / ID;
- type: Point, Spot, or Directional;
- enabled state;
- shadow casting;
- position in meters;
- direction vector for spot/directional lights; the editor always fills a valid normalized direction for spot/directional lights instead of leaving the fields blank;
- RGB color as `#RRGGBB`;
- intensity;
- range in meters (`0` means unlimited);
- spot inner and outer cone angles in degrees.

Imported/default provenance flags are displayed but are intentionally read-only. Property edits, add/delete operations, and committed light moves participate in Composer undo/redo.

For spot and directional lights, **Aim at object** lists scene objects that contain geometry. Choose a target and press **Aim at object** to calculate a normalized direction from the light's currently entered position to the target object's current world-space bounding-box center. The calculated X/Y/Z direction is placed in the direction fields so it can still be adjusted manually before saving. For a directional light, position does not affect illumination; Composer uses the entered position only as an editor marker/aim origin for calculating the direction vector.

Press **Apply** to validate and commit the selected light's properties. A successful Apply closes the Lighting Editor. Invalid values leave the editor open and show the validation message.

## Verifying that a light is affecting the render

Composer scenes start with built-in `key` and `fill` lights, and the renderers also include environment/indirect lighting. A newly added light at an intensity similar to the defaults can therefore be visually subtle.

For an unmistakable check:

1. Temporarily disable the built-in `key` and `fill` lights in the Lighting Editor.
2. Set the light under test to a saturated color such as `#FF0000`.
3. Use a high temporary intensity such as `25` to `50`.
4. For a spot light, aim it at the target, use an outer cone around `45` to `60` degrees, and set Range to `0` (unlimited) while testing.
5. Turn **Casts shadows** off for the first check so a shadow-map issue cannot hide the direct contribution.
6. Apply and render once with the light enabled, then disable that same light and render again. A strong color/brightness difference between the two renders confirms the light path is active.

The viewport marker, direction arrow, and spot cone show where the editor believes the light is and where it points; they do not themselves prove that the renderer is receiving illumination. The enabled/disabled render comparison is the definitive check.

## Viewport markers and move gizmo

When **Show light markers and light move gizmo in preview** is enabled, each light is represented by an editor overlay after the selected renderer produces its image:

- point lights use a radial lamp/sun marker;
- spot lights show a direction arrow and cone hint;
- directional lights show a directional marker/arrow;
- disabled lights remain visible as gray editor markers;
- the selected light gets the standard X/Y/Z translation gizmo.

Left-click a marker to select the light. Drag the selected light's X/Y/Z gizmo to move it in world space. **Shift** provides precision movement and **Ctrl** snaps, matching object-gizmo conventions.

## Hiding light representations

Clear **Show light markers and light move gizmo in preview** to remove all light icons and the light gizmo from Composer's preview image. This setting affects only the editor representation: it does **not** set `SceneLight.Enabled = false`, remove lights, or change their illumination. The Lighting Editor remains reachable from **Render → Lighting…** while markers are hidden.

Light markers are generated only by the Composer overlay stage. They are not scene geometry and are not added to exported/headless scene rendering.
