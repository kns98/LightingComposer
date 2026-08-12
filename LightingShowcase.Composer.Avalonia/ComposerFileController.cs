/*
 * This controller translates Avalonia events and commands into editor operations while keeping the live scene
 * behind `ComposerSceneSession`. Its job is coordination: validate/route input, invoke the appropriate session or
 * renderer operation, and update presentation state without becoming a competing owner of scene data.
 *
 * `ComposerFileController` coordinates a focused interaction workflow. It holds the transient UI/input state
 * needed for that workflow but delegates authoritative scene mutation to the session/model layer.
 *
 * The `ComposerFileController` constructor captures `owner`, `session`, `lifetimeToken`. Those are the
 * dependencies/initial values the instance needs for its lifetime, so callbacks and later operations use the same
 * objects/configuration rather than looking them up globally.
 *
 * `PickOpenPathAsync` opens Avalonia’s platform file picker with the caller’s format filter, requires single
 * selection, and converts the selected storage item to a local path. It throws early if the platform storage
 * provider cannot open files.
 *
 * `NewSceneAsync` creates a consistently configured scene async UI/domain object so repeated controls/objects
 * share sizing, alignment, or default behavior. Cancellation is propagated so shutdown or a newer request can
 * make obsolete work stop early.
 *
 * `LoadAsync` loads async from persistent/external data and converts it into validated internal scene state
 * rather than exposing parser-specific objects to the rest of the application. Cancellation is propagated so
 * shutdown or a newer request can make obsolete work stop early.
 *
 * `InsertAsync` inserts async into the live scene/model and returns the resulting identity/value needed by
 * selection or subsequent editing. Cancellation is propagated so shutdown or a newer request can make obsolete
 * work stop early.
 *
 * `PickSavePathAsync` asks the platform save picker for an `.lscene` destination, suggesting either
 * `composition.lscene` or the current scene name. Returning `null` represents user cancellation rather than an
 * error.
 *
 * `SaveAsync` runs the blocking scene save off the UI thread while periodically reporting elapsed time and the
 * triangle count captured at save start. Awaiting the actual save task at the end ensures success/failure is not
 * hidden by the progress loop. Potentially blocking/CPU work runs on a worker task rather than Avalonia’s UI
 * thread. Cancellation is propagated so shutdown or a newer request can make obsolete work stop early.
 *
 * `ExportPackageAsync` first verifies there is renderable content, then asks the user for an export format and
 * parent folder. Only after both choices are complete does it run `session.ExportPackage` on a worker thread, so
 * cancellation of either dialog causes no scene/export mutation. Potentially blocking/CPU work runs on a worker
 * task rather than Avalonia’s UI thread. Cancellation is propagated so shutdown or a newer request can make
 * obsolete work stop early.
 */
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns desktop file/folder dialogs and long-running scene load/save/export I/O.
/// It deliberately does not own editor selection state; callers update the UI
/// after an operation completes.
/// </summary>
internal sealed class ComposerFileController
{
    private readonly Window owner;
    private readonly ComposerSceneSession session;
    private readonly CancellationToken lifetimeToken;

    public ComposerFileController(Window owner, ComposerSceneSession session, CancellationToken lifetimeToken)
    {
        this.owner = owner;
        this.session = session;
        this.lifetimeToken = lifetimeToken;
    }

    public async Task<string?> PickOpenPathAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        if (!owner.StorageProvider.CanOpen)
            throw new InvalidOperationException("The desktop file picker is unavailable.");

        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public Task NewSceneAsync() => Task.Run(() => session.NewScene(lifetimeToken), lifetimeToken);

    public Task LoadAsync(string path) => Task.Run(() => session.Load(path, lifetimeToken), lifetimeToken);

    public Task<int> InsertAsync(string path) => Task.Run(() => session.Insert(path, lifetimeToken), lifetimeToken);

    public async Task<string?> PickSavePathAsync()
    {
        if (!owner.StorageProvider.CanSave)
            throw new InvalidOperationException("The desktop save picker is unavailable.");

        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save composition",
            SuggestedFileName = session.ScenePath is null
                ? "composition.lscene"
                : Path.GetFileNameWithoutExtension(session.ScenePath) + ".lscene",
            DefaultExtension = "lscene",
            FileTypeChoices = [ComposerFileTypes.ComposerScene]
        });
        return file?.TryGetLocalPath();
    }

    public async Task<TimeSpan> SaveAsync(string path, Action<string>? progress = null)
    {
        int triangleCountAtSave = session.TriangleCount;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Task saveTask = Task.Run(() => session.Save(path, lifetimeToken), lifetimeToken);

        while (!saveTask.IsCompleted)
        {
            Task completed = await Task.WhenAny(saveTask, Task.Delay(500, lifetimeToken));
            if (ReferenceEquals(completed, saveTask))
                break;

            progress?.Invoke(
                $"Saving composition… {stopwatch.Elapsed.TotalSeconds:0.0}s ({triangleCountAtSave:N0} triangles).");
        }

        await saveTask;
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    public async Task<SceneExportPackageResult?> ExportPackageAsync()
    {
        if (!session.HasRenderableScene)
            throw new InvalidOperationException("There is no renderable scene to export.");
        if (!owner.StorageProvider.CanPickFolder)
            throw new InvalidOperationException("The desktop folder picker is unavailable.");

        SceneExportFormat? format = await new ExportFormatDialog().ShowDialog<SceneExportFormat?>(owner);
        if (format == null)
            return null;

        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Choose parent folder for {format.DisplayName}",
            AllowMultiple = false
        });
        string? parentDirectory = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(parentDirectory))
            return null;

        return await Task.Run(
            () => session.ExportPackage(parentDirectory, format, lifetimeToken),
            lifetimeToken);
    }
}
