// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerTransformController.cs
// Purpose: Gizmo drag handling for object transforms.
//
// Turns pointer drags on move, rotate, and scale gizmos into live previews and final baked transform edits.
// -----------------------------------------------------------------------------

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
    private sealed class GizmoDragTarget
    {
        /// <summary>Captures one drag target with its starting transform.</summary>
        public GizmoDragTarget(int id, Vec3 startPosition, Vec3 startRotation, Vec3 startScale)
        {
            Id = id;
            StartPosition = startPosition;
            StartRotation = startRotation;
            StartScale = startScale;
        }

        public int Id { get; }
        public Vec3 StartPosition { get; }
        public Vec3 StartRotation { get; }
        public Vec3 StartScale { get; }
    }

    private sealed class GizmoDragState
    {
        /// <summary>Captures the starting selection, transform, and gizmo hit for a drag.</summary>
        public GizmoDragState(
            int selectedId,
            IReadOnlyList<GizmoDragTarget> targets,
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
            Targets = targets;
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
        public IReadOnlyList<GizmoDragTarget> Targets { get; }
        public bool MeshComponent { get; }
        public ComposerGizmoMode Mode { get; }
        public ComposerGizmoAxis Axis { get; }
        public Vec3 StartPosition { get; }
        public Vec3 StartRotation { get; }
        public Vec3 StartScale { get; }
        public bool Touched { get; set; }
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

    /// <summary>Initializes the transform controller with its session, views, and callbacks.</summary>
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

    /// <summary>Enumerates the position, rotation, and scale inspector text boxes.</summary>
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

    /// <summary>Applies the inspector transform values and bakes them into geometry.</summary>
    public async Task ApplyInspectorAsync()
    {
        if (selection.ActiveObjectId is not int id)
            return;

        if (selection.SelectedObjectIds.Count > 1)
            await ApplyInspectorMultiAsync(selection.SelectedObjectIds.ToArray(), id);
        else
            await ApplyInspectorSingleAsync(id);
    }

    /// <summary>Applies the inspector name, visibility, and transform values to one object.</summary>
    private async Task ApplyInspectorSingleAsync(int id)
    {
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

            bool nonIdentity = IsNonIdentityRequest(request);
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

    /// <summary>
    /// Applies the inspector visibility and transform values to every topmost
    /// object in the set. Each object keeps its own name; renaming stays a
    /// single-object operation.
    /// </summary>
    private async Task ApplyInspectorMultiAsync(int[] ids, int activeId)
    {
        dialogs.ClosePrimitiveParameters();
        try
        {
            ComposerTransformRequest request = ComposerTransformRequest.Parse(
                positionX.Text, positionY.Text, positionZ.Text,
                rotationX.Text, rotationY.Text, rotationZ.Text,
                scaleX.Text, scaleY.Text, scaleZ.Text);
            bool visible = visibleBox.IsChecked ?? true;
            IReadOnlyList<int> topmost = session.GetTopmostSelection(ids);
            if (topmost.Count == 0)
                throw new InvalidOperationException("The selected scene nodes no longer exist.");

            Dictionary<int, ComposerModelEvidence> beforeEvidence = topmost
                .Select(id => session.GetModelEvidence(id))
                .OfType<ComposerModelEvidence>()
                .ToDictionary(evidence => evidence.ObjectId);
            long beforeRevision = beforeEvidence.TryGetValue(activeId, out ComposerModelEvidence? activeBefore)
                ? activeBefore.SceneRevision
                : beforeEvidence.Values.First().SceneRevision;
            await renderer.StopCurrentRenderAsync();
            setBusy(true, $"Baking transform into {topmost.Count} objects…");
            int updated = await Task.Run(
                () => session.UpdateObjects(ids, visible, request.Position, request.RotationRadians, request.Scale),
                lifetimeToken);
            if (updated == 0)
                throw new InvalidOperationException("The selected scene nodes no longer exist.");

            pathText.Text = "Untitled composition (modified)";
            bool geometryChanged = false;
            foreach (int id in topmost)
            {
                ComposerObjectState? appliedState = session.GetObjectState(id);
                if (appliedState == null)
                    continue;
                if (!NearlyEqual(appliedState.Position, Vec3.Zero) ||
                    !NearlyEqual(appliedState.Rotation, Vec3.Zero) ||
                    !NearlyEqual(appliedState.Scale, new Vec3(1, 1, 1)))
                {
                    throw new InvalidOperationException("The transform was not fully baked into geometry.");
                }
                if (beforeEvidence.TryGetValue(id, out ComposerModelEvidence? before) &&
                    session.GetModelEvidence(id) is ComposerModelEvidence after &&
                    before.WorldGeometryHash != after.WorldGeometryHash)
                {
                    geometryChanged = true;
                }
            }

            ComposerModelEvidence? afterActiveEvidence = session.GetModelEvidence(activeId);
            if (afterActiveEvidence != null && afterActiveEvidence.SceneRevision <= beforeRevision)
                throw new InvalidOperationException("The scene revision did not advance after the transform.");
            if (IsNonIdentityRequest(request) && !geometryChanged)
                throw new InvalidOperationException("The underlying triangle geometry did not change.");

            selection.ClearVirtualTriangleSelection();
            selection.RefreshObjectTree(activeId);
            ClearTransformTextBoxes();
            updateHistory();
            bool retainedParameters = topmost.All(session.CanEditPrimitiveParameters);
            string countSuffix = updated < topmost.Count ? $"{updated} of {topmost.Count} objects" : $"{updated} objects";
            statusText.Text = retainedParameters
                ? $"Applied transform to {countSuffix}; procedural parameters were preserved. {session.LastGeometryRefreshDetails}"
                : $"Baked transform into {countSuffix}. {session.LastGeometryRefreshDetails}";
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

    /// <summary>Returns true when the transform request changes position, rotation, or scale.</summary>
    private static bool IsNonIdentityRequest(ComposerTransformRequest request) =>
        request.Position.Length() > 1e-12 ||
        request.RotationRadians.Length() > 1e-12 ||
        Math.Abs(request.Scale.X - 1.0) > 1e-12 ||
        Math.Abs(request.Scale.Y - 1.0) > 1e-12 ||
        Math.Abs(request.Scale.Z - 1.0) > 1e-12;

    /// <summary>Resets the selected node transform to identity.</summary>
    public async Task ResetSelectedTransformAsync()
    {
        if (selection.ActiveObjectId is not int id)
            return;

        int[] ids = selection.SelectedObjectIds.Count > 1 ? selection.SelectedObjectIds.ToArray() : new[] { id };
        await renderer.StopCurrentRenderAsync();
        setBusy(true, ids.Length > 1 ? $"Resetting {ids.Length} node transforms…" : "Resetting the selected node transform…");
        try
        {
            int reset = await Task.Run(
                () => ids.Length > 1 ? session.ResetObjectTransforms(ids) : (session.ResetObjectTransform(id) ? 1 : 0),
                lifetimeToken);
            if (reset == 0)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            selection.ClearVirtualTriangleSelection();
            selection.RefreshObjectTree(id);
            selection.LoadInspectorFromSelection();
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = ids.Length > 1 ? $"Reset transforms on {reset} objects." : "Selected node transform reset.";
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

    /// <summary>Begins a gizmo drag when the pointer hits the active gizmo.</summary>
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

        // Object-mode drags move the whole multi-selection: the active gizmo is
        // the handle, and its deltas apply to every topmost object about each
        // object's own pivot. Mesh-component drags stay single-target.
        List<GizmoDragTarget> targets = new();
        if (!meshComponent)
        {
            IReadOnlyList<int> topmost = session.GetTopmostSelection(selection.SelectedObjectIds);
            List<int> ordered = new(topmost.Count + 1) { selectedId };
            foreach (int id in topmost)
            {
                if (id != selectedId)
                    ordered.Add(id);
            }
            foreach (int id in ordered)
            {
                ComposerObjectState? targetState = id == selectedId ? state : session.GetTransformTargetState(id);
                if (targetState != null)
                    targets.Add(new GizmoDragTarget(id, targetState.Position, targetState.Rotation, targetState.Scale));
            }
            if (targets.Count == 0 || targets[0].Id != selectedId)
                return false;
        }
        else
        {
            targets.Add(new GizmoDragTarget(selectedId, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1)));
        }

        drag = new GizmoDragState(
            selectedId,
            targets,
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
            : targets.Count > 1
                ? $"Dragging {hit.Axis} {mode.ToString().ToLowerInvariant()} gizmo on {targets.Count} objects; release to bake each about its own pivot…"
                : $"Dragging {hit.Axis} {mode.ToString().ToLowerInvariant()} gizmo…";
        return true;
    }

    /// <summary>Updates the live transform preview from the current pointer position.</summary>
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

        bool updated;
        if (current.MeshComponent)
        {
            updated = session.UpdateMeshElementMovePreview(current.SelectedId, updatedPosition);
        }
        else
        {
            if (!NearlyEqual(updatedPosition, current.StartPosition) ||
                !NearlyEqual(updatedRotation, current.StartRotation) ||
                !NearlyEqual(updatedScale, current.StartScale))
            {
                current.Touched = true;
            }
            updated = ApplyDragPreviewToTargets(current, updatedPosition, updatedRotation, updatedScale) > 0;
        }
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

    /// <summary>
    /// Applies the active drag values to every drag target. Position and rotation
    /// deltas are added to each target's own start; scale applies as a factor of
    /// the active start so differently scaled objects grow proportionally.
    /// </summary>
    private int ApplyDragPreviewToTargets(GizmoDragState current, Vec3 updatedPosition, Vec3 updatedRotation, Vec3 updatedScale)
    {
        Vec3 positionDelta = updatedPosition - current.StartPosition;
        Vec3 rotationDelta = updatedRotation - current.StartRotation;
        Vec3 scaleFactor = new(
            DivideOrOne(updatedScale.X, current.StartScale.X),
            DivideOrOne(updatedScale.Y, current.StartScale.Y),
            DivideOrOne(updatedScale.Z, current.StartScale.Z));
        (int Id, Vec3 Position, Vec3 RotationRadians, Vec3 Scale)[] previews = current.Targets
            .Select(target => (
                target.Id,
                target.StartPosition + positionDelta,
                target.StartRotation + rotationDelta,
                new Vec3(
                    target.StartScale.X * scaleFactor.X,
                    target.StartScale.Y * scaleFactor.Y,
                    target.StartScale.Z * scaleFactor.Z)))
            .ToArray();
        return session.UpdateTransformTargets(previews);
    }

    /// <summary>Divides two scale components, returning one when the divisor is not positive.</summary>
    private static double DivideOrOne(double value, double divisor) =>
        divisor > 0.0 && double.IsFinite(value) && double.IsFinite(divisor) ? value / divisor : 1.0;

    /// <summary>Commits the active gizmo drag and bakes its transform.</summary>
    public async Task<bool> CommitActiveDragAsync(Point releasePoint, KeyModifiers modifiers)
    {
        if (drag == null)
            return false;

        UpdateGizmoDrag(releasePoint, modifiers);
        GizmoDragState? current = drag;
        if (current == null)
            return true;

        int commitId = current.SelectedId;
        int[] targetIds = current.Targets.Select(target => target.Id).ToArray();
        ComposerGizmoMode committedMode = current.Mode;
        bool meshComponent = current.MeshComponent;
        bool touched = current.Touched;
        drag = null;

        await renderer.StopCurrentRenderAsync();
        int committedCount = await Task.Run(
            () => meshComponent
                ? (session.CommitMeshElementMove(commitId) ? 1 : 0)
                : session.CommitPendingTransforms(targetIds),
            lifetimeToken);
        if (committedCount == 0 && (meshComponent || touched))
        {
            statusText.Text = targetIds.Length > 1 && !meshComponent
                ? "The transform targets no longer exist."
                : "The transform target no longer exists.";
            return true;
        }

        if (!meshComponent)
        {
            selection.ClearVirtualTriangleSelection();
            ClearTransformTextBoxes();
        }
        bool retainedParameters = !meshComponent && targetIds.All(session.CanEditPrimitiveParameters);
        if (retainedParameters)
            dialogs.RebasePrimitiveAfterExternalTransform(commitId);

        selection.RefreshObjectTree(commitId);
        updateHistory();
        pathText.Text = "Untitled composition (modified)";
        statusText.Text = meshComponent
            ? $"Moved the selected {selectedSelectionMode().ToString().ToLowerInvariant()} and baked shared welded vertices once. {session.LastGeometryRefreshDetails}"
            : targetIds.Length > 1
                ? retainedParameters
                    ? $"Applied {committedMode.ToString().ToLowerInvariant()} transform to {committedCount} objects; procedural parameters were preserved. {session.LastGeometryRefreshDetails}"
                    : $"Baked {committedMode.ToString().ToLowerInvariant()} transform into {committedCount} objects. {session.LastGeometryRefreshDetails}"
                : retainedParameters
                    ? $"Applied {committedMode.ToString().ToLowerInvariant()} transform; procedural parameters were preserved. {session.LastGeometryRefreshDetails}"
                    : $"Baked {committedMode.ToString().ToLowerInvariant()} transform into mesh geometry. {session.LastGeometryRefreshDetails}";
        await renderer.RequestRenderAsync(interactive: false);
        return true;
    }

    /// <summary>Cancels the active gizmo drag and discards its preview.</summary>
    public void CancelActiveDrag()
    {
        GizmoDragState? current = drag;
        if (current == null)
            return;

        if (current.MeshComponent)
            session.CancelMeshElementMovePreview(current.SelectedId);
        else
        {
            session.CancelPendingTransforms(current.Targets.Select(target => target.Id));
            selection.LoadInspectorFromSelection();
        }
        drag = null;
    }

    /// <summary>Converts a viewport point into render image coordinates.</summary>
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

    /// <summary>Clears the transform inspector text boxes.</summary>
    public void ClearTransformTextBoxes()
    {
        foreach (TextBox box in TransformTextBoxes())
            box.Text = string.Empty;
    }

    /// <summary>Returns the unit vector for a gizmo axis.</summary>
    private static Vec3 AxisVector(ComposerGizmoAxis axis) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(1, 0, 0),
        ComposerGizmoAxis.Y => new Vec3(0, 1, 0),
        ComposerGizmoAxis.Z => new Vec3(0, 0, 1),
        _ => Vec3.Zero
    };

    /// <summary>Adds a delta to one component of a vector.</summary>
    private static Vec3 AddAxisComponent(Vec3 start, ComposerGizmoAxis axis, double delta) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(start.X + delta, start.Y, start.Z),
        ComposerGizmoAxis.Y => new Vec3(start.X, start.Y + delta, start.Z),
        ComposerGizmoAxis.Z => new Vec3(start.X, start.Y, start.Z + delta),
        _ => start
    };

    /// <summary>Scales one axis of a vector by the given factor.</summary>
    private static Vec3 ScaleAxis(Vec3 start, ComposerGizmoAxis axis, double factor) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(start.X * factor, start.Y, start.Z),
        ComposerGizmoAxis.Y => new Vec3(start.X, start.Y * factor, start.Z),
        ComposerGizmoAxis.Z => new Vec3(start.X, start.Y, start.Z * factor),
        _ => start * factor
    };

    /// <summary>Computes the pointer angle around the gizmo center.</summary>
    private static double PointerAngle(double x, double y, double centerX, double centerY) =>
        Math.Atan2(centerY - y, x - centerX);

    /// <summary>Wraps an angle into the range negative pi to pi.</summary>
    private static double WrapAngle(double angle)
    {
        while (angle > Math.PI) angle -= Math.PI * 2.0;
        while (angle < -Math.PI) angle += Math.PI * 2.0;
        return angle;
    }

    /// <summary>Returns true when two vectors match within tolerance.</summary>
    private static bool NearlyEqual(Vec3 left, Vec3 right)
    {
        const double tolerance = 1e-8;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance &&
               Math.Abs(left.Z - right.Z) <= tolerance;
    }
}
