/*
 * This controller translates Avalonia events and commands into editor operations while keeping the live scene
 * behind `ComposerSceneSession`. Its job is coordination: validate/route input, invoke the appropriate session or
 * renderer operation, and update presentation state without becoming a competing owner of scene data.
 */
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

    public ComposerDialogController(Window owner, ComposerSceneSession session)
    {
        this.owner = owner;
        this.session = session;
    }

    // HasPrimitiveEditorFor reports whether primitive editor for is present/usable in the current state, without
    // changing that state.
    public bool HasPrimitiveEditorFor(int objectId) => primitiveParametersWindow?.ObjectId == objectId;
    // HasMaterialEditorFor reports whether material editor for is present/usable in the current state, without
    // changing that state.
    public bool HasMaterialEditorFor(int objectId) => materialEditorWindow?.ObjectId == objectId;

    public void RebasePrimitiveAfterExternalTransform(int objectId)
    {
        if (primitiveParametersWindow?.ObjectId == objectId)
            primitiveParametersWindow.RebaseAfterExternalTransform();
    }

    public void RebasePrimitiveAfterMaterialChange(int objectId)
    {
        if (primitiveParametersWindow?.ObjectId == objectId)
            primitiveParametersWindow.RebaseAfterExternalEdit("Material changed. Procedural geometry parameters remain editable.");
    }

    public async Task<ComposerRenderOptions?> ShowRenderSettingsAsync(
        ComposerRendererKind kind,
        string rendererLabel,
        ComposerRenderOptions current)
    {
        RenderSettingsDialog dialog = new(kind, rendererLabel, current);
        return await dialog.ShowForResultAsync(owner);
    }

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

    public void ClosePrimitiveParameters()
    {
        PrimitiveParametersWindow? dialog = primitiveParametersWindow;
        if (dialog == null)
            return;
        primitiveParametersWindow = null;
        try { dialog.Close(); } catch { }
    }

    public void CloseMaterialEditor()
    {
        MaterialEditorWindow? dialog = materialEditorWindow;
        if (dialog == null)
            return;
        materialEditorWindow = null;
        try { dialog.Close(); } catch { }
    }

    public void CloseEditors()
    {
        ClosePrimitiveParameters();
        CloseMaterialEditor();
    }

    // Dispose ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
    // windows/renderers do not keep receiving work or retain unmanaged memory.
    public void Dispose() => CloseEditors();
}
