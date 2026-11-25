// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerDialogController.cs
// Purpose: Modal dialog flow for the composer window.
//
// Opens file pickers and settings dialogs and returns the user choices to the main window controllers.
// -----------------------------------------------------------------------------

using Avalonia.Controls;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns modeless editor-window lifetime and the renderer settings dialog.
/// </summary>
internal sealed class ComposerDialogController : IDisposable
{
    private readonly Window owner;
    private readonly ComposerSceneSession session;
    private PrimitiveParametersWindow? primitiveParametersWindow;
    private MaterialEditorWindow? materialEditorWindow;
    private LightEditorWindow? lightEditorWindow;

    /// <summary>Creates a controller that owns the composer editor windows and the render settings dialog.</summary>
    public ComposerDialogController(Window owner, ComposerSceneSession session)
    {
        this.owner = owner;
        this.session = session;
    }

    /// <summary>Returns the currently open editor dialog, if any.</summary>
    internal Window? ActiveDialog() =>
        (Window?)primitiveParametersWindow ?? (Window?)materialEditorWindow ?? (Window?)lightEditorWindow;

    /// <summary>Returns true when the primitive parameters window is open for the given object.</summary>
    public bool HasPrimitiveEditorFor(int objectId) => primitiveParametersWindow?.ObjectId == objectId;
    /// <summary>Returns true when the material editor window is open for the given object.</summary>
    public bool HasMaterialEditorFor(int objectId) => materialEditorWindow?.ObjectId == objectId;

    /// <summary>Rebases the open primitive parameters window after an external transform.</summary>
    public void RebasePrimitiveAfterExternalTransform(int objectId)
    {
        if (primitiveParametersWindow?.ObjectId == objectId)
            primitiveParametersWindow.RebaseAfterExternalTransform();
    }

    /// <summary>Rebases the open primitive parameters window after a material change.</summary>
    public void RebasePrimitiveAfterMaterialChange(int objectId)
    {
        if (primitiveParametersWindow?.ObjectId == objectId)
            primitiveParametersWindow.RebaseAfterExternalEdit("Material changed. Procedural geometry parameters remain editable.");
    }

    /// <summary>Shows the render settings dialog and returns the chosen options.</summary>
    public async Task<ComposerRenderOptions?> ShowRenderSettingsAsync(
        ComposerRendererKind kind,
        string rendererLabel,
        ComposerRenderOptions current)
    {
        RenderSettingsDialog dialog = new(kind, rendererLabel, current);
        return await dialog.ShowForResultAsync(owner);
    }

    /// <summary>Opens the primitive parameters window for the given object, reusing the open window when it already targets that object.</summary>
    public void OpenPrimitiveParameters(
        int objectId,
        Action forceObjectMode,
        Action<string> setStatus,
        Action markModified,
        Action requestInteractiveRender,
        Action requestFinalRender,
        Action refreshSelection,
        Action refreshHistory,
        Action<int> refreshTree)
    {
        if (primitiveParametersWindow != null)
        {
            if (primitiveParametersWindow.ObjectId == objectId)
            {
                primitiveParametersWindow.Activate();
                return;
            }
            ClosePrimitiveParameters();
        }

        forceObjectMode();
        ComposerPrimitiveParameterModel? model = session.BeginPrimitiveParameterEdit(objectId);
        if (model == null)
        {
            setStatus("The selected object is an ordinary mesh and has no procedural parameters.");
            refreshSelection();
            return;
        }

        PrimitiveParametersWindow? dialog = null;
        dialog = new PrimitiveParametersWindow(
            session,
            model,
            onPreviewChanged: () =>
            {
                markModified();
                requestInteractiveRender();
            },
            onCommittedOrConverted: () =>
            {
                refreshTree(objectId);
                refreshSelection();
                refreshHistory();
                markModified();
                requestFinalRender();
            },
            onClosed: () =>
            {
                if (ReferenceEquals(primitiveParametersWindow, dialog))
                    primitiveParametersWindow = null;
                refreshSelection();
            });
        primitiveParametersWindow = dialog;
        dialog.Show(owner);
        setStatus($"Editing {model.PrimitiveName} parameters. All length values are meters (m).");
    }

    /// <summary>Opens the material editor window for the given object, reusing the open window when it already targets that object.</summary>
    public void OpenMaterialEditor(
        int objectId,
        Action forceObjectMode,
        Action<string> setStatus,
        Action markModified,
        Action requestFinalRender,
        Action refreshSelection,
        Action refreshHistory,
        Action<int> refreshTree)
    {
        if (materialEditorWindow != null)
        {
            if (materialEditorWindow.ObjectId == objectId)
            {
                materialEditorWindow.Activate();
                return;
            }
            CloseMaterialEditor();
        }

        forceObjectMode();
        ComposerMaterialModel? model = session.GetMaterialModel(objectId);
        if (model == null)
        {
            setStatus("The selected object has no material-bearing mesh geometry.");
            return;
        }

        MaterialEditorWindow? dialog = null;
        dialog = new MaterialEditorWindow(
            session,
            model,
            onMaterialChanged: () =>
            {
                RebasePrimitiveAfterMaterialChange(objectId);
                refreshTree(objectId);
                refreshSelection();
                refreshHistory();
                markModified();
                setStatus("Material updated. Procedural geometry, when present, was preserved.");
                requestFinalRender();
            },
            onClosed: () =>
            {
                if (ReferenceEquals(materialEditorWindow, dialog))
                    materialEditorWindow = null;
            });
        materialEditorWindow = dialog;
        dialog.Show(owner);
        setStatus("Material editor opened. Presets, direct PBR properties, exact RGB/hex color, and image textures apply to the selected object.");
    }


    /// <summary>Opens the light editor window and selects the given light when one is provided.</summary>
    public void OpenLightEditor(
        int? selectedLightIndex,
        Action<int?> selectLight,
        Action<string> setStatus,
        Action markModified,
        Action requestFinalRender,
        Action refreshHistory)
    {
        if (lightEditorWindow != null)
        {
            lightEditorWindow.SelectLight(selectedLightIndex ?? session.SelectedLightIndex);
            return;
        }

        LightEditorWindow? dialog = null;
        dialog = new LightEditorWindow(
            session,
            selectLight,
            onLightChanged: () =>
            {
                refreshHistory();
                markModified();
                setStatus("Lighting updated.");
                requestFinalRender();
            },
            onPreviewChanged: requestFinalRender,
            onClosed: () =>
            {
                if (ReferenceEquals(lightEditorWindow, dialog))
                    lightEditorWindow = null;
            });
        lightEditorWindow = dialog;
        dialog.Show(owner);
        if (selectedLightIndex.HasValue)
            dialog.SelectLight(selectedLightIndex);
        setStatus("Lighting editor opened. Viewport light markers are editor-only overlays and can be hidden without disabling illumination.");
    }

    /// <summary>Refreshes the open light editor from the currently selected light.</summary>
    public void RefreshLightEditor()
    {
        lightEditorWindow?.RefreshFromScene(session.SelectedLightIndex);
    }

    /// <summary>Closes the open light editor window.</summary>
    public void CloseLightEditor()
    {
        LightEditorWindow? dialog = lightEditorWindow;
        if (dialog == null)
            return;
        lightEditorWindow = null;
        try { dialog.Close(); } catch { }
    }

    /// <summary>Closes the open primitive parameters window.</summary>
    public void ClosePrimitiveParameters()
    {
        PrimitiveParametersWindow? dialog = primitiveParametersWindow;
        if (dialog == null)
            return;
        primitiveParametersWindow = null;
        try { dialog.Close(); } catch { }
    }

    /// <summary>Closes the open material editor window.</summary>
    public void CloseMaterialEditor()
    {
        MaterialEditorWindow? dialog = materialEditorWindow;
        if (dialog == null)
            return;
        materialEditorWindow = null;
        try { dialog.Close(); } catch { }
    }

    /// <summary>Closes the primitive parameters and material editor windows.</summary>
    public void CloseEditors()
    {
        ClosePrimitiveParameters();
        CloseMaterialEditor();
    }

    /// <summary>Closes all editor windows owned by this controller.</summary>
    public void Dispose()
    {
        CloseEditors();
        CloseLightEditor();
    }
}
