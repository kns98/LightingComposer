# Cleaner object manipulation and viewport deselection

## Changes

- Object selection still shows its normal bounds/wireframe while idle.
- As soon as an Object-mode move, rotate, or scale gizmo drag begins, Composer renders only the active transform gizmo over the live object preview. The object bounding box and selected-triangle wireframe are suppressed for the duration of that drag.
- Releasing or cancelling the drag restores the normal idle selection overlay.
- A short left click on empty rendered background in Object mode now clears the selected object.
- Clicking empty letterbox/viewport area outside the fitted render image also clears Object-mode selection.
- Deselecting clears the tree selection, disables the inspector, clears virtual triangle selection, closes an object-specific primitive Parameters window, and redraws without a selection overlay.
- Component edit modes keep their existing component-selection semantics; empty clicks do not unexpectedly exit the active edit object.

## Implementation

`ComposerSceneSession.Render` now uses the selected bounds only to position the Object-mode gizmo. It always passes no selected-triangle wireframe and `drawBounds: false`, so the extra outlines do not return when a move, rotate, or scale drag ends.

Object picking now explicitly handles a null viewport hit as deselection through `DeselectObjectFromViewport()`.

## Validation performed

- `git diff --check`
- XML parsing for all `.csproj` files
- lightweight C# comment/string-aware delimiter validation on the modified C# files
- generated the patch from a clean copy of the preceding preserve-parameters source

The execution environment does not provide the .NET SDK or a Vulkan desktop session, so runtime compilation and interactive GPU validation were not run here.
