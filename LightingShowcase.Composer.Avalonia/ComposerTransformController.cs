/*
 * This controller translates Avalonia events and commands into editor operations while keeping the live scene
 * behind `ComposerSceneSession`. Its job is coordination: validate/route input, invoke the appropriate session or
 * renderer operation, and update presentation state without becoming a competing owner of scene data.
 *
 * `ComposerTransformController` coordinates a focused interaction workflow. It holds the transient UI/input state
 * needed for that workflow but delegates authoritative scene mutation to the session/model layer.
 *
 * `GizmoDragState` is a working/snapshot state object whose fields must move together; callers use it to capture
 * one coherent point in an interaction, render, or undo workflow.
 *
 * `HasActiveDrag` is derived rather than separately stored: it evaluates `drag != null`. Keeping the value
 * computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `ObjectGizmoOnly` is derived rather than separately stored: it evaluates `drag is { MeshComponent: false }`.
 * Keeping the value computed from its source fields prevents a second cached flag/value from drifting out of
 * sync.
 *
 * `TransformTextBoxes` applies the relevant coordinate transform to text boxes, making explicit whether data is
 * being moved between local, world, view, or preview space.
 *
 * `ApplyInspectorAsync` applies inspector async as a single semantic mutation. Validation, scene changes, undo
 * bookkeeping, and cache invalidation are kept inside this boundary rather than exposed as separate caller
 * responsibilities. Potentially blocking/CPU work runs on a worker task rather than Avalonia’s UI thread.
 * Cancellation is propagated so shutdown or a newer request can make obsolete work stop early.
 *
 * `ResetSelectedTransformAsync` returns selected transform async to its canonical default/identity state while
 * preserving the surrounding object/session identity. Potentially blocking/CPU work runs on a worker task rather
 * than Avalonia’s UI thread. Cancellation is propagated so shutdown or a newer request can make obsolete work
 * stop early.
 *
 * `UpdateGizmoDrag` updates gizmo drag from the newest input while preserving the identities/metadata/caches that
 * remain valid and invalidating only what the change makes stale.
 *
 * `CommitActiveDragAsync` finalizes active drag async: the current preview becomes authoritative and the
 * before/after state is recorded as one logical undoable edit. Potentially blocking/CPU work runs on a worker
 * task rather than Avalonia’s UI thread. Cancellation is propagated so shutdown or a newer request can make
 * obsolete work stop early.
 *
 * `CancelActiveDrag` evaluates whether cel active drag is currently legal/available from the existing state. It
 * is a side-effect-free guard intended to drive command enablement or reject an edit before mutation.
 *
 * `ClearTransformTextBoxes` removes/resets transform text boxes to its empty/default state. This is an explicit
 * state transition rather than leaving old values around for later code to accidentally reuse.
 *
 * `AddAxisComponent` adds axis component to the owning collection/model while using this boundary to preserve
 * indexing, ownership, and derived-state invariants.
 */
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns inspector transforms and interactive gizmo drag state/math. Selection
/// chooses the target; this controller determines and commits how it transforms.
/// </summary>
internal sealed class ComposerTransformController
{
    private sealed class GizmoDragState
    {
        public GizmoDragState(
            int selectedId,
            ComposerGizmoMode mode,
            ComposerGizmoAxis axis,
            Point startImagePoint,
            Vec3 startPosition,
            Vec3 startRotation,
            Vec3 startScale,
            ComposerGizmoHit hit,
            bool meshComponent = false)
        {
            SelectedId = selectedId;
            Mode = mode;
            Axis = axis;
            StartPosition = startPosition;
            StartRotation = startRotation;
            StartScale = startScale;
            ScreenDirectionX = hit.ScreenDirectionX;
            ScreenDirectionY = hit.ScreenDirectionY;
            WorldUnitsPerPixel = hit.WorldUnitsPerPixel;
            CenterX = hit.CenterX;
            CenterY = hit.CenterY;
            GestureSign = hit.GestureSign;
            WorldCenter = hit.WorldCenter;
            LastRotationVector = hit.RotationStartVector;
            LastPointerAngle = PointerAngle(startImagePoint.X, startImagePoint.Y, CenterX, CenterY);
            LastImagePoint = startImagePoint;
            MeshComponent = meshComponent;
        }

        public int SelectedId { get; }
        public bool MeshComponent { get; }
        public ComposerGizmoMode Mode { get; }
        public ComposerGizmoAxis Axis { get; }
        public Vec3 StartPosition { get; }
        public Vec3 StartRotation { get; }
        public Vec3 StartScale { get; }
        public double ScreenDirectionX { get; }
        public double ScreenDirectionY { get; }
        public double WorldUnitsPerPixel { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double GestureSign { get; }
        public Vec3 WorldCenter { get; }
        public Vec3 LastRotationVector { get; set; }
        public Point LastImagePoint { get; set; }
        public double LastPointerAngle { get; set; }
        public double AccumulatedGesture { get; set; }
    }

    private readonly ComposerSceneSession session;
    private readonly ComposerRenderController renderer;
    private readonly ComposerSelectionController selection;
    private readonly ComposerDialogController dialogs;
    private readonly Border viewport;
    private readonly TextBlock pathText;
    private readonly TextBlock statusText;
    private readonly TextBox nameBox;
    private readonly CheckBox visibleBox;
    private readonly TextBox positionX;
    private readonly TextBox positionY;
    private readonly TextBox positionZ;
    private readonly TextBox rotationX;
    private readonly TextBox rotationY;
    private readonly TextBox rotationZ;
    private readonly TextBox scaleX;
    private readonly TextBox scaleY;
    private readonly TextBox scaleZ;
    private readonly Func<ComposerSelectionMode> selectedSelectionMode;
    private readonly Func<ComposerGizmoMode> selectedGizmoMode;
    private readonly Func<ComposerGizmoAxis> selectedMoveAxis;
    private readonly Func<ComposerRendererKind> selectedRenderer;
    private readonly Func<string> selectedRendererLabel;
    private readonly Action<bool, string?> setBusy;
    private readonly Action updateHistory;
    private readonly Action<string, Exception> reportFailure;
    private readonly CancellationToken lifetimeToken;

    private GizmoDragState? drag;

    public ComposerTransformController(
        ComposerSceneSession session,
        ComposerRenderController renderer,
        ComposerSelectionController selection,
        ComposerDialogController dialogs,
        Border viewport,
        TextBlock pathText,
        TextBlock statusText,
        TextBox nameBox,
        CheckBox visibleBox,
        TextBox positionX,
        TextBox positionY,
        TextBox positionZ,
        TextBox rotationX,
        TextBox rotationY,
        TextBox rotationZ,
        TextBox scaleX,
        TextBox scaleY,
        TextBox scaleZ,
        Func<ComposerSelectionMode> selectedSelectionMode,
        Func<ComposerGizmoMode> selectedGizmoMode,
        Func<ComposerGizmoAxis> selectedMoveAxis,
        Func<ComposerRendererKind> selectedRenderer,
        Func<string> selectedRendererLabel,
        Action<bool, string?> setBusy,
        Action updateHistory,
        Action<string, Exception> reportFailure,
        CancellationToken lifetimeToken)
    {
        this.session = session;
        this.renderer = renderer;
        this.selection = selection;
        this.dialogs = dialogs;
        this.viewport = viewport;
        this.pathText = pathText;
        this.statusText = statusText;
        this.nameBox = nameBox;
        this.visibleBox = visibleBox;
        this.positionX = positionX;
        this.positionY = positionY;
        this.positionZ = positionZ;
        this.rotationX = rotationX;
        this.rotationY = rotationY;
        this.rotationZ = rotationZ;
        this.scaleX = scaleX;
        this.scaleY = scaleY;
        this.scaleZ = scaleZ;
        this.selectedSelectionMode = selectedSelectionMode;
        this.selectedGizmoMode = selectedGizmoMode;
        this.selectedMoveAxis = selectedMoveAxis;
        this.selectedRenderer = selectedRenderer;
        this.selectedRendererLabel = selectedRendererLabel;
        this.setBusy = setBusy;
        this.updateHistory = updateHistory;
        this.reportFailure = reportFailure;
        this.lifetimeToken = lifetimeToken;
    }

    public bool HasActiveDrag => drag != null;
    public bool ObjectGizmoOnly => drag is { MeshComponent: false };

    public IEnumerable<TextBox> TransformTextBoxes()
    {
        yield return positionX;
        yield return positionY;
        yield return positionZ;
        yield return rotationX;
        yield return rotationY;
        yield return rotationZ;
        yield return scaleX;
        yield return scaleY;
        yield return scaleZ;
    }

    public async Task ApplyInspectorAsync()
    {
        if (selection.ActiveObjectId is not int id)
            return;

        dialogs.ClosePrimitiveParameters();
        try
        {
            ComposerTransformRequest request = ComposerTransformRequest.Parse(
                positionX.Text, positionY.Text, positionZ.Text,
                rotationX.Text, rotationY.Text, rotationZ.Text,
                scaleX.Text, scaleY.Text, scaleZ.Text);
            ComposerTransformWorkItem workItem = new(
                id,
                nameBox.Text ?? string.Empty,
                visibleBox.IsChecked ?? true,
                request);

            ComposerModelEvidence? beforeEvidence = session.GetModelEvidence(id);
            await renderer.StopCurrentRenderAsync();
            setBusy(true, "Baking transform into the selected geometry…");
            bool updated = await Task.Run(() => workItem.Apply(session), lifetimeToken);
            if (!updated)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            pathText.Text = "Untitled composition (modified)";
            ComposerObjectState? appliedState = session.GetObjectState(id);
            ComposerModelEvidence? afterEvidence = session.GetModelEvidence(id);
            if (appliedState == null || afterEvidence == null)
                throw new InvalidOperationException("The transformed scene node could not be verified.");

            if (!NearlyEqual(appliedState.Position, Vec3.Zero) ||
                !NearlyEqual(appliedState.Rotation, Vec3.Zero) ||
                !NearlyEqual(appliedState.Scale, new Vec3(1, 1, 1)))
            {
                throw new InvalidOperationException("The transform was not fully baked into geometry.");
            }
            if (beforeEvidence != null && afterEvidence.SceneRevision <= beforeEvidence.SceneRevision)
                throw new InvalidOperationException("The scene revision did not advance after the transform.");

            bool nonIdentity = request.Position.Length() > 1e-12 ||
                               request.RotationRadians.Length() > 1e-12 ||
                               Math.Abs(request.Scale.X - 1.0) > 1e-12 ||
                               Math.Abs(request.Scale.Y - 1.0) > 1e-12 ||
                               Math.Abs(request.Scale.Z - 1.0) > 1e-12;
            if (nonIdentity && beforeEvidence != null && beforeEvidence.WorldGeometryHash == afterEvidence.WorldGeometryHash)
                throw new InvalidOperationException("The underlying triangle geometry did not change.");

            selection.ClearVirtualTriangleSelection();
            selection.RefreshObjectTree(id);
            ClearTransformTextBoxes();
            updateHistory();
            bool retainedParameters = session.CanEditPrimitiveParameters(id);
            statusText.Text = retainedParameters
                ? $"Applied transform to {appliedState.Name}; procedural parameters were preserved. Scene revision {afterEvidence.SceneRevision}. {session.LastGeometryRefreshDetails}"
                : $"Baked transform into {appliedState.Name}; scene revision {afterEvidence.SceneRevision}. {session.LastGeometryRefreshDetails}";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            reportFailure("Transform update failed", ex);
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task ResetSelectedTransformAsync()
    {
        if (selection.ActiveObjectId is not int id)
            return;

        await renderer.StopCurrentRenderAsync();
        setBusy(true, "Resetting the selected node transform…");
        try
        {
            bool reset = await Task.Run(() => session.ResetObjectTransform(id), lifetimeToken);
            if (!reset)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            selection.ClearVirtualTriangleSelection();
            selection.RefreshObjectTree(id);
            selection.LoadInspectorFromSelection();
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = "Selected node transform reset.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Transform reset failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public bool TryBeginGizmoDrag(Point viewportPoint)
    {
        if (selection.ActiveObjectId is not int selectedId ||
            session.GetActiveSelectionBounds() is not Aabb bounds ||
            !TryViewportToImagePoint(viewportPoint, out Point imagePoint))
        {
            return false;
        }

        bool meshComponent = selectedSelectionMode() != ComposerSelectionMode.Object && session.HasMeshComponentSelection;
        ComposerObjectState? state = session.GetTransformTargetState(selectedId);
        if (!meshComponent && state == null)
            return false;

        CameraDefinition camera = session.Camera.Snapshot();
        ComposerGizmoMode mode = meshComponent ? ComposerGizmoMode.Translate : selectedGizmoMode();
        if (!ComposerOverlayRenderer.TryHitGizmo(
                mode,
                camera,
                bounds,
                renderer.LastRenderWidth,
                renderer.LastRenderHeight,
                imagePoint.X,
                imagePoint.Y,
                meshComponent ? selectedMoveAxis() : ComposerGizmoAxis.None,
                out ComposerGizmoHit hit))
        {
            return false;
        }

        drag = new GizmoDragState(
            selectedId,
            mode,
            hit.Axis,
            imagePoint,
            meshComponent ? Vec3.Zero : state!.Position,
            meshComponent ? Vec3.Zero : state!.Rotation,
            meshComponent ? new Vec3(1, 1, 1) : state!.Scale,
            hit,
            meshComponent);
        statusText.Text = meshComponent
            ? $"Moving selected {selectedSelectionMode().ToString().ToLowerInvariant()} on {hit.Axis}…"
            : $"Dragging {hit.Axis} {mode.ToString().ToLowerInvariant()} gizmo…";
        return true;
    }

    public void UpdateGizmoDrag(Point viewportPoint, KeyModifiers modifiers)
    {
        GizmoDragState? current = drag;
        if (current == null || !TryViewportToImagePoint(viewportPoint, out Point imagePoint))
            return;

        Vec3 updatedPosition = current.StartPosition;
        Vec3 updatedRotation = current.StartRotation;
        Vec3 updatedScale = current.StartScale;
        double precision = modifiers.HasFlag(KeyModifiers.Shift) ? 0.20 : 1.0;
        bool snap = modifiers.HasFlag(KeyModifiers.Control);

        switch (current.Mode)
        {
            case ComposerGizmoMode.Rotate:
            {
                double angle = PointerAngle(imagePoint.X, imagePoint.Y, current.CenterX, current.CenterY);
                double angularStep;
                CameraDefinition camera = session.Camera.Snapshot();
                bool hasPlaneVector = ComposerOverlayRenderer.TryGetRotationPlaneVector(
                    camera,
                    renderer.LastRenderWidth,
                    renderer.LastRenderHeight,
                    imagePoint.X,
                    imagePoint.Y,
                    current.WorldCenter,
                    current.Axis,
                    out Vec3 currentVector);
                if (hasPlaneVector && current.LastRotationVector.Length() > 1e-8)
                {
                    Vec3 axis = AxisVector(current.Axis);
                    angularStep = Math.Atan2(
                        axis.Dot(current.LastRotationVector.Cross(currentVector)),
                        Math.Clamp(current.LastRotationVector.Dot(currentVector), -1.0, 1.0));
                    current.LastRotationVector = currentVector;
                }
                else
                {
                    angularStep = WrapAngle(angle - current.LastPointerAngle) * current.GestureSign;
                    current.LastRotationVector = hasPlaneVector ? currentVector : Vec3.Zero;
                }

                current.LastPointerAngle = angle;
                current.AccumulatedGesture += angularStep * precision;
                double rotationDelta = current.AccumulatedGesture;
                if (snap)
                {
                    double increment = Math.PI / 36.0;
                    rotationDelta = Math.Round(rotationDelta / increment) * increment;
                }
                updatedRotation = AddAxisComponent(current.StartRotation, current.Axis, rotationDelta);
                break;
            }
            case ComposerGizmoMode.Scale:
            {
                double deltaX = imagePoint.X - current.LastImagePoint.X;
                double deltaY = imagePoint.Y - current.LastImagePoint.Y;
                double pixelStep = current.Axis == ComposerGizmoAxis.Uniform
                    ? deltaX - deltaY
                    : deltaX * current.ScreenDirectionX + deltaY * current.ScreenDirectionY;
                current.AccumulatedGesture += pixelStep * precision;
                current.LastImagePoint = imagePoint;
                double factor = Math.Exp(current.AccumulatedGesture / 140.0);
                factor = Math.Clamp(factor, 0.01, 100.0);
                if (snap)
                    factor = Math.Max(0.01, Math.Round(factor * 10.0) / 10.0);
                updatedScale = ScaleAxis(current.StartScale, current.Axis, factor);
                break;
            }
            default:
            {
                double deltaX = imagePoint.X - current.LastImagePoint.X;
                double deltaY = imagePoint.Y - current.LastImagePoint.Y;
                double pixelStep = deltaX * current.ScreenDirectionX + deltaY * current.ScreenDirectionY;
                current.AccumulatedGesture += pixelStep * precision;
                current.LastImagePoint = imagePoint;
                double worldDistance = current.AccumulatedGesture * current.WorldUnitsPerPixel;
                if (snap)
                {
                    double increment = Math.Max(0.01, current.WorldUnitsPerPixel * 10.0);
                    worldDistance = Math.Round(worldDistance / increment) * increment;
                }
                updatedPosition = current.StartPosition + AxisVector(current.Axis) * worldDistance;
                break;
            }
        }

        bool updated = current.MeshComponent
            ? session.UpdateMeshElementMovePreview(current.SelectedId, updatedPosition)
            : session.UpdateTransformTarget(current.SelectedId, updatedPosition, updatedRotation, updatedScale);
        if (!updated)
        {
            drag = null;
            statusText.Text = "The transform target no longer exists.";
            return;
        }

        if (!current.MeshComponent)
            selection.LoadInspectorFromSelection();
        pathText.Text = "Untitled composition (modified)";

        ComposerRendererKind rendererKind = selectedRenderer();
        if (rendererKind == ComposerRendererKind.VulkanRaster || renderer.CanRenderContinuously(rendererKind))
        {
            _ = renderer.RequestRenderAsync(interactive: true);
            statusText.Text = current.MeshComponent
                ? rendererKind == ComposerRendererKind.VulkanRaster
                    ? "Live Vulkan mesh deformation preview; release to bake welded vertices once."
                    : "Pseudo-real-time component overlay; release to rebuild welded vertices once."
                : rendererKind == ComposerRendererKind.VulkanRaster
                    ? $"Live Vulkan {current.Mode.ToString().ToLowerInvariant()} preview; release to bake once."
                    : $"Pseudo-real-time {current.Mode.ToString().ToLowerInvariant()} preview; release for the final frame.";
        }
        else
        {
            statusText.Text = current.MeshComponent
                ? $"{selectedRendererLabel()}: release to bake the component move."
                : $"{selectedRendererLabel()}: release to render the {current.Mode.ToString().ToLowerInvariant()} transform.";
        }
    }

    public async Task<bool> CommitActiveDragAsync(Point releasePoint, KeyModifiers modifiers)
    {
        if (drag == null)
            return false;

        UpdateGizmoDrag(releasePoint, modifiers);
        GizmoDragState? current = drag;
        if (current == null)
            return true;

        int commitId = current.SelectedId;
        ComposerGizmoMode committedMode = current.Mode;
        bool meshComponent = current.MeshComponent;
        drag = null;

        await renderer.StopCurrentRenderAsync();
        bool committed = await Task.Run(
            () => meshComponent
                ? session.CommitMeshElementMove(commitId)
                : session.CommitPendingTransform(commitId),
            lifetimeToken);
        if (!committed)
        {
            statusText.Text = "The transform target no longer exists.";
            return true;
        }

        if (!meshComponent)
        {
            selection.ClearVirtualTriangleSelection();
            ClearTransformTextBoxes();
        }
        bool retainedParameters = !meshComponent && session.CanEditPrimitiveParameters(commitId);
        if (retainedParameters)
            dialogs.RebasePrimitiveAfterExternalTransform(commitId);

        selection.RefreshObjectTree(commitId);
        updateHistory();
        pathText.Text = "Untitled composition (modified)";
        statusText.Text = meshComponent
            ? $"Moved the selected {selectedSelectionMode().ToString().ToLowerInvariant()} and baked shared welded vertices once. {session.LastGeometryRefreshDetails}"
            : retainedParameters
                ? $"Applied {committedMode.ToString().ToLowerInvariant()} transform; procedural parameters were preserved. {session.LastGeometryRefreshDetails}"
                : $"Baked {committedMode.ToString().ToLowerInvariant()} transform into mesh geometry. {session.LastGeometryRefreshDetails}";
        await renderer.RequestRenderAsync(interactive: false);
        return true;
    }

    public void CancelActiveDrag()
    {
        GizmoDragState? current = drag;
        if (current == null)
            return;

        if (current.MeshComponent)
            session.CancelMeshElementMovePreview(current.SelectedId);
        else
        {
            session.CancelPendingTransform(current.SelectedId);
            selection.LoadInspectorFromSelection();
        }
        drag = null;
    }

    public bool TryViewportToImagePoint(Point viewportPoint, out Point imagePoint)
    {
        double viewportWidth = viewport.Bounds.Width;
        double viewportHeight = viewport.Bounds.Height;
        if (renderer.LastRenderWidth <= 0 || renderer.LastRenderHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            imagePoint = default;
            return false;
        }

        double scale = Math.Min(viewportWidth / renderer.LastRenderWidth, viewportHeight / renderer.LastRenderHeight);
        double displayedWidth = renderer.LastRenderWidth * scale;
        double displayedHeight = renderer.LastRenderHeight * scale;
        double offsetX = (viewportWidth - displayedWidth) * 0.5;
        double offsetY = (viewportHeight - displayedHeight) * 0.5;
        double localX = viewportPoint.X - offsetX;
        double localY = viewportPoint.Y - offsetY;
        if (localX < 0 || localY < 0 || localX > displayedWidth || localY > displayedHeight)
        {
            imagePoint = default;
            return false;
        }

        imagePoint = new Point(localX / scale, localY / scale);
        return true;
    }

    public void ClearTransformTextBoxes()
    {
        foreach (TextBox box in TransformTextBoxes())
            box.Text = string.Empty;
    }

    private static Vec3 AxisVector(ComposerGizmoAxis axis) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(1, 0, 0),
        ComposerGizmoAxis.Y => new Vec3(0, 1, 0),
        ComposerGizmoAxis.Z => new Vec3(0, 0, 1),
        _ => Vec3.Zero
    };

    private static Vec3 AddAxisComponent(Vec3 start, ComposerGizmoAxis axis, double delta) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(start.X + delta, start.Y, start.Z),
        ComposerGizmoAxis.Y => new Vec3(start.X, start.Y + delta, start.Z),
        ComposerGizmoAxis.Z => new Vec3(start.X, start.Y, start.Z + delta),
        _ => start
    };

    private static Vec3 ScaleAxis(Vec3 start, ComposerGizmoAxis axis, double factor) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(start.X * factor, start.Y, start.Z),
        ComposerGizmoAxis.Y => new Vec3(start.X, start.Y * factor, start.Z),
        ComposerGizmoAxis.Z => new Vec3(start.X, start.Y, start.Z * factor),
        _ => start * factor
    };

    private static double PointerAngle(double x, double y, double centerX, double centerY) =>
        Math.Atan2(centerY - y, x - centerX);

    private static double WrapAngle(double angle)
    {
        while (angle > Math.PI) angle -= Math.PI * 2.0;
        while (angle < -Math.PI) angle += Math.PI * 2.0;
        return angle;
    }

    private static bool NearlyEqual(Vec3 left, Vec3 right)
    {
        const double tolerance = 1e-8;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance &&
               Math.Abs(left.Z - right.Z) <= tolerance;
    }
}
