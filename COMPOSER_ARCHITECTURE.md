# Lighting Composer UI architecture

`ComposerWindow` is intentionally **not** split with C# `partial` classes. Behavior is separated into normal classes with explicit ownership boundaries.

## Composition root

`LightingShowcase.Composer.Avalonia/ComposerWindow.cs`

The window now owns application composition and the remaining tightly coupled Avalonia event routing. It creates the controls and controllers, wires controller callbacks, and owns window lifetime. It does not own native Windows touchpad processing, renderer scheduling/bitmap presentation, menu-item state, selection state, transform drag math, file picker/I/O behavior, or dialog lifetime.

## Controllers

### `ComposerRenderController`
Owns renderer selection settings, render request/cancellation scheduling, interactive versus idle render behavior, resize debounce, rendered bitmap presentation, and per-renderer `ComposerRenderOptions`.

### `ViewportNavigationController`
Owns right-drag orbit, middle/Shift-right pan, wheel zoom, Windows Precision Touchpad attachment, `WM_POINTER` bridging, native two-contact orbit/zoom/circular-turn coalescing, and navigation idle rendering.

### `ComposerMenuController`
Owns the File/Edit/Add/Object/Mode/Render menu tree, radio/check state, enabled state, and history menu labels. It calls supplied application commands instead of directly mutating the scene.

### `ComposerSelectionController`
Owns active/multi-object selection, selected logical face state, hierarchy expansion/paging, object-tree projection, session selection synchronization, inspector loading, and selection-dependent UI enablement.

### `ComposerTransformController`
Owns transform inspector application/reset, gizmo hit testing, gizmo drag state, translate/rotate/scale math, component movement, transform commit/cancel, and viewport-to-render coordinate conversion.

### `ComposerFileController`
Owns Avalonia storage pickers and actual new/load/insert/save/export file operations.

### `ComposerSceneCommandController`
Provides the narrow scene-mutation API used by the UI coordinator: add primitive, undo/redo, group/ungroup, duplicate, and delete.

### `ComposerDialogController`
Owns render-settings dialog invocation and the lifetime/rebasing of modeless primitive-parameter and material-editor windows.

### `ComposerCommandCoordinator`
Coordinates user-level commands that span multiple services—for example open/load, insert, save, undo, grouping, and delete—with render cancellation, selection refresh, status/path updates, history refresh, and busy state.

### `ComposerWindowLayout`
Builds the menu + scene tree + viewport + inspector + status-bar visual shell. It contains layout only and has no scene/render/navigation behavior.

## Platform-specific input

Windows native touchpad implementation remains below `ViewportNavigationController`:

```text
ComposerWindow
    -> ViewportNavigationController
        -> WindowsPrecisionTouchpadGestureSource
            -> WindowsPrecisionTouchpadTracker
            -> WindowsPrecisionTouchpadApi
```

The native path remains Windows-only. Linux/macOS continue through the ordinary Avalonia mouse/navigation behavior.

## Rendering boundary

```text
ComposerWindow
    -> ComposerRenderController
        -> ComposerSceneSession.Render(...)
            -> CPU / Raster / Vulkan Raster / Vulkan Compute
```

The Window no longer creates `WriteableBitmap` frames or schedules renderer work itself.

## Selection/transform boundary

```text
ComposerSelectionController  = what is selected
ComposerTransformController  = how the selection is transformed
```

This keeps selection semantics independent from gizmo/transform math.

## Refactor scope

This refactor is intended to preserve behavior. Renderer algorithms, Windows Precision Touchpad gesture classification, render-setting capability rules, menu commands, scene editing semantics, and file formats were not intentionally changed.
