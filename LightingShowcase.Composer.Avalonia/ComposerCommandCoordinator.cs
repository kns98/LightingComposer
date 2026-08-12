using Avalonia.Controls;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Coordinates user-level file and scene commands with render cancellation,
/// selection/tree refresh, status text and editor busy state.
/// </summary>
internal sealed class ComposerCommandCoordinator
{
    private readonly ComposerSceneSession session;
    private readonly ComposerFileController files;
    private readonly ComposerSceneCommandController sceneCommands;
    private readonly ComposerRenderController renderer;
    private readonly ComposerDialogController dialogs;
    private readonly ComposerSelectionController selection;
    private readonly TextBlock pathText;
    private readonly TextBlock statusText;
    private readonly TextBlock detailsText;
    private readonly ComboBox selectionModeBox;
    private readonly ComboBox primitiveBox;
    private readonly IReadOnlyList<string> primitiveChoices;
    private readonly Func<string> selectedRendererDescription;
    private readonly Action<bool, string?> setBusy;
    private readonly Action updateHistory;
    private readonly Action clearTransformTextBoxes;
    private readonly Action openPrimitiveParameters;
    private readonly Action<string, Exception> reportFailure;
    private readonly CancellationToken lifetimeToken;

    public ComposerCommandCoordinator(
        ComposerSceneSession session,
        ComposerFileController files,
        ComposerSceneCommandController sceneCommands,
        ComposerRenderController renderer,
        ComposerDialogController dialogs,
        ComposerSelectionController selection,
        TextBlock pathText,
        TextBlock statusText,
        TextBlock detailsText,
        ComboBox selectionModeBox,
        ComboBox primitiveBox,
        IReadOnlyList<string> primitiveChoices,
        Func<string> selectedRendererDescription,
        Action<bool, string?> setBusy,
        Action updateHistory,
        Action clearTransformTextBoxes,
        Action openPrimitiveParameters,
        Action<string, Exception> reportFailure,
        CancellationToken lifetimeToken)
    {
        this.session = session;
        this.files = files;
        this.sceneCommands = sceneCommands;
        this.renderer = renderer;
        this.dialogs = dialogs;
        this.selection = selection;
        this.pathText = pathText;
        this.statusText = statusText;
        this.detailsText = detailsText;
        this.selectionModeBox = selectionModeBox;
        this.primitiveBox = primitiveBox;
        this.primitiveChoices = primitiveChoices;
        this.selectedRendererDescription = selectedRendererDescription;
        this.setBusy = setBusy;
        this.updateHistory = updateHistory;
        this.clearTransformTextBoxes = clearTransformTextBoxes;
        this.openPrimitiveParameters = openPrimitiveParameters;
        this.reportFailure = reportFailure;
        this.lifetimeToken = lifetimeToken;
    }

    public async Task NewSceneAsync()
    {
        dialogs.CloseEditors();
        renderer.CancelCurrentRender();
        setBusy(true, "Creating a new composition…");
        try
        {
            await files.NewSceneAsync();
            renderer.ClearImage();
            pathText.Text = "Untitled composition";
            selection.ResetForScene();
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree();
            selection.SetInspectorEnabled(false);
            statusText.Text = "New empty composition. Add a primitive or insert a model to begin.";
            detailsText.Text = selectedRendererDescription();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Could not create scene: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task BrowseAndOpenAsync()
    {
        try
        {
            string? path = await files.PickOpenPathAsync("Open scene or model", ComposerFileTypes.OpenPickerTypes);
            if (!string.IsNullOrWhiteSpace(path))
                await LoadSceneAsync(path);
        }
        catch (Exception ex)
        {
            statusText.Text = $"File picker failed: {ex.Message}";
        }
    }

    public async Task BrowseAndInsertAsync()
    {
        try
        {
            string? path = await files.PickOpenPathAsync("Insert 3D model", ComposerFileTypes.InsertPickerTypes);
            if (!string.IsNullOrWhiteSpace(path))
                await InsertModelAsync(path);
        }
        catch (Exception ex)
        {
            statusText.Text = $"File picker failed: {ex.Message}";
        }
    }

    public async Task LoadSceneAsync(string path)
    {
        dialogs.CloseEditors();
        renderer.CancelCurrentRender();
        setBusy(true, $"Loading {Path.GetFileName(path)}…");
        try
        {
            await files.LoadAsync(path);
            pathText.Text = session.ScenePath ?? Path.GetFileName(path);
            selection.ResetForScene();
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree();
            statusText.Text = $"Loaded {Path.GetFileName(path)} — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles{FormatImportDetails()}.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Load failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task InsertModelAsync(string path)
    {
        dialogs.CloseEditors();
        renderer.CancelCurrentRender();
        setBusy(true, $"Inserting {Path.GetFileName(path)}…");
        try
        {
            int insertedId = await files.InsertAsync(path);
            pathText.Text = "Untitled composition (modified)";
            selection.SetSingleSelection(insertedId, expand: true);
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(insertedId);
            statusText.Text = $"Inserted {Path.GetFileName(path)} — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles{FormatImportDetails()}.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Insert failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task AddPrimitiveAsync()
    {
        string primitiveName = primitiveBox.SelectedItem as string ?? primitiveChoices[0];
        int? insertedId = null;
        try
        {
            dialogs.CloseEditors();
            await renderer.StopCurrentRenderAsync();
            setBusy(true, $"Adding {primitiveName}…");
            insertedId = await sceneCommands.AddPrimitiveAsync(primitiveName);
            selection.SetSingleSelection(insertedId, expand: true);
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(insertedId);
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = $"Added {primitiveName}. Edit its procedural dimensions in meters in the Parameters window.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            reportFailure("Could not add primitive", ex);
        }
        finally
        {
            setBusy(false, null);
        }

        if (insertedId.HasValue && selection.ActiveObjectId == insertedId)
            openPrimitiveParameters();
    }

    public async Task SaveSceneAsync()
    {
        try
        {
            string? path = await files.PickSavePathAsync();
            if (string.IsNullOrWhiteSpace(path))
                return;

            await renderer.StopCurrentRenderAsync();
            setBusy(true, "Saving composition…");
            TimeSpan elapsed = await files.SaveAsync(path, message => statusText.Text = message);
            pathText.Text = session.ScenePath ?? path;
            statusText.Text = $"Saved {Path.GetFileName(session.ScenePath ?? path)} in {elapsed.TotalSeconds:0.0}s.";
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Save failed: {ex.Message}";
            Console.Error.WriteLine($"Save failed: {ex}");
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task ExportPackageAsync()
    {
        try
        {
            setBusy(true, "Preparing export…");
            SceneExportPackageResult? result = await files.ExportPackageAsync();
            if (result == null)
                return;
            statusText.Text = $"Exported {Path.GetFileName(result.PrimaryFilePath)} with {result.Files.Count:N0} files to {result.DirectoryPath}.";
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Export failed: {ex.Message}";
            Console.Error.WriteLine($"Export failed: {ex}");
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task UndoAsync()
    {
        dialogs.CloseEditors();
        if (!session.CanUndo)
            return;

        await renderer.StopCurrentRenderAsync();
        setBusy(true, "Undoing edit…");
        try
        {
            int? preferred = await sceneCommands.UndoAsync();
            selection.SetSingleSelection(preferred);
            pathText.Text = "Untitled composition (modified)";
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(preferred);
            clearTransformTextBoxes();
            updateHistory();
            statusText.Text = $"Undo complete. {session.LastGeometryRefreshDetails}";
            if (session.HasRenderableScene)
                await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Undo failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task RedoAsync()
    {
        dialogs.CloseEditors();
        if (!session.CanRedo)
            return;

        await renderer.StopCurrentRenderAsync();
        setBusy(true, "Redoing edit…");
        try
        {
            int? preferred = await sceneCommands.RedoAsync();
            selection.SetSingleSelection(preferred);
            pathText.Text = "Untitled composition (modified)";
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(preferred);
            clearTransformTextBoxes();
            updateHistory();
            statusText.Text = $"Redo complete. {session.LastGeometryRefreshDetails}";
            if (session.HasRenderableScene)
                await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Redo failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task GroupSelectedAsync()
    {
        dialogs.CloseEditors();
        if (selection.SelectedObjectIds.Count < 2 || !session.CanGroupObjects(selection.SelectedObjectIds))
        {
            statusText.Text = "Ctrl-click at least two sibling objects before grouping.";
            return;
        }

        int[] ids = selection.SelectedObjectIds.ToArray();
        await renderer.StopCurrentRenderAsync();
        setBusy(true, "Grouping selected objects…");
        try
        {
            int? groupId = await sceneCommands.GroupAsync(ids);
            if (!groupId.HasValue)
            {
                statusText.Text = "The selected objects could not be grouped. They must share the same parent.";
                return;
            }
            selection.SetSingleSelection(groupId.Value);
            pathText.Text = "Untitled composition (modified)";
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(groupId.Value);
            updateHistory();
            statusText.Text = $"Grouped {ids.Length} objects. Ctrl-click can build another multi-selection.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Group failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task UngroupSelectedAsync()
    {
        dialogs.CloseEditors();
        int[] ids = selection.SelectedObjectIds.Count > 0
            ? selection.SelectedObjectIds.ToArray()
            : selection.ActiveObjectId is int id ? new[] { id } : Array.Empty<int>();
        if (ids.Length == 0 || !session.CanUngroupObjects(ids))
        {
            statusText.Text = "None of the selected objects can be ungrouped further.";
            return;
        }

        await renderer.StopCurrentRenderAsync();
        setBusy(true, ids.Length > 1 ? "Ungrouping selected objects…" : "Ungrouping selected node…");
        try
        {
            IReadOnlyList<int> promoted = await sceneCommands.UngroupAsync(ids);
            foreach (int oldId in ids)
                selection.RemoveCachedObject(oldId);
            selection.SetMultipleSelection(promoted, promoted.Count > 0 ? promoted[0] : null);
            pathText.Text = "Untitled composition (modified)";
            selectionModeBox.SelectedIndex = 0;
            selection.RefreshObjectTree(selection.ActiveObjectId);
            updateHistory();
            statusText.Text = $"Ungrouped into {promoted.Count:N0} node(s).";
            if (session.HasRenderableScene)
                await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Ungroup failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task DuplicateSelectedAsync()
    {
        dialogs.CloseEditors();
        if (selection.ActiveObjectId is not int id)
            return;

        renderer.CancelCurrentRender();
        setBusy(true, "Duplicating object…");
        try
        {
            int? duplicateId = await sceneCommands.DuplicateAsync(id);
            pathText.Text = "Untitled composition (modified)";
            selection.ClearVirtualTriangleSelection();
            selection.RefreshObjectTree(duplicateId);
            statusText.Text = $"Duplicated object — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
            await renderer.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Duplicate failed: {ex.Message}";
        }
        finally
        {
            setBusy(false, null);
        }
    }

    public async Task DeleteSelectedAsync()
    {
        dialogs.CloseEditors();
        if (selection.ActiveObjectId is not int id)
            return;

        renderer.CancelCurrentRender();
        await sceneCommands.DeleteAsync(id);
        selection.SetSingleSelection(null);
        pathText.Text = "Untitled composition (modified)";
        selection.RefreshObjectTree();
        statusText.Text = $"Deleted object — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
        if (session.HasRenderableScene)
            await renderer.RequestRenderAsync(interactive: false);
        else
        {
            renderer.ClearImage();
            selection.SetInspectorEnabled(false);
        }
    }

    private string FormatImportDetails() =>
        string.IsNullOrWhiteSpace(session.LastImportDetails)
            ? string.Empty
            : $"; {session.LastImportDetails}";
}
