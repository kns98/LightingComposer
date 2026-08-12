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
- direction vector for spot/directional lights;
- RGB color as `#RRGGBB`;
- intensity;
- range in meters (`0` means unlimited);
- spot inner and outer cone angles in degrees.

Imported/default provenance flags are displayed but are intentionally read-only. Property edits, add/delete operations, and committed light moves participate in Composer undo/redo.

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
