# Animation Studio

Open it from **Render → Animation Studio…**. The Studio stores named cameras,
a shot timeline, and object motion tracks in the scene file, and renders them
to a PNG sequence plus an animated GIF.

## Concepts

- **Cameras** are named viewpoints (position, target, field of view) managed
  in **Render → Cameras…** (add from view, preview, update from view, rename,
  delete, numeric transform, marker toggle) and picked up by shots. Deleting
  a camera deletes its shots. Cameras also live in the viewport like lights:
  magenta markers with a view-direction tick, click to select, drag the axis
  gizmo to move (position and target travel together), double-click a marker
  to switch to its view. Camera edits participate in undo.
- **Shots** each use one camera for N frames with a move: **Static** holds the
  framing, **Orbit** sweeps a yaw arc in degrees around the shot target, and
  **Push** dollies toward the target (amount is the end radius as a fraction
  of the start radius, so 0.6 pushes to 60%). Moves ease in and out. Shots
  play back to back; a cut hides any change between them.
- **Motion tracks** keyframe one object's full transform (position, rotation,
  scale) on **global** movie frames, so motion continues correctly through
  cuts. Outside the key range the nearest key holds. Rotation interpolates in
  the same XYZ euler values the Inspector shows; keep arcs under 180 degrees.
- **Movie settings** (fps, size, backend, samples, bounces, exposure, output
  name) travel with the scene. Background, ambient, and shadows follow the
  current viewport options when rendering from the UI.

## Workflow

1. Frame the viewport and define each camera in **Render → Cameras…**, then open the Studio.
2. Keyframe motion in **Render → Motion…**: pick an object, pose it in the main window, enter a frame, and Capture. The car in the sample drives this way.
2. Build shots: name, camera, frames, move, amount.
3. Scrub to a frame, select an object in the main window, and **Capture
   keyframe**; repeat along the timeline. Click a key to jump the scrubber.
4. Scrub or **Play** to preview motion in the viewport. Scrubbing poses the
   live scene and restores it when the Studio closes (**Reset pose** restores
   immediately). Saving while scrubbed keeps the current poses as authored.
5. Pick the output folder and **Render movie**. Each frame poses the scene,
   renders offscreen, and writes `name.frame-NNNN.png`; then `name.gif` is
   assembled. Cancel stops after the current frame; finished frames are kept.

Studio edits (cameras, shots, keys, settings) apply immediately without undo
steps and mark the scene modified. Deleting an object prunes its track on the
next studio refresh, save, or load.

## Headless

`render <scene.lscene> --timeline [--output <movie.gif>]` renders the saved
timeline exactly as stored; per-movie flags (`--fps`, `--width`, `--renderer`,
`--frames`, `--camera-*`, `--view`, `--zoom`, `--variant`, …) are rejected so
the file stays the single source of truth. Ambient, background, shadow, and
light flags still apply. Movies are capped at 360 frames.

## Sample

`models/HouseDrive/HouseDrive.lscene` stages a toy car driving up to the
house: three cameras, three shots (orbit, locked-off tracking, push-in), and
one four-keyframe track whose mid-timeline yaw snap hides inside a cut.
The rendered `models/HouseDrive/housedrive.gif` next to it shows the expected
result; re-render it with `render models/HouseDrive/HouseDrive.lscene --timeline`.
