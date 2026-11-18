// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerSceneCommandController.cs
// Purpose: Asynchronous scene editing commands.
//
// Runs session mutations such as add, duplicate, delete, group, undo, and redo on background tasks for the UI layer.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Composer;

/// <summary>
/// Application-level scene editing commands. UI selection/tree/inspector refresh
/// remains the caller's responsibility, while mutation execution is centralized.
/// </summary>
internal sealed class ComposerSceneCommandController
{
    private readonly ComposerSceneSession session;
    private readonly CancellationToken lifetimeToken;

    /// <summary>Creates a command controller that runs session edits on background tasks.</summary>
    public ComposerSceneCommandController(ComposerSceneSession session, CancellationToken lifetimeToken)
    {
        this.session = session;
        this.lifetimeToken = lifetimeToken;
    }

    /// <summary>Adds a primitive with the given name and returns its object id.</summary>
    public Task<int> AddPrimitiveAsync(string primitiveName) =>
        Task.Run(() => session.InsertPrimitive(primitiveName), lifetimeToken);

    /// <summary>Undoes the last edit and returns the restored selection id.</summary>
    public Task<int?> UndoAsync() => Task.Run(session.Undo, lifetimeToken);
    /// <summary>Redoes the next edit and returns the restored selection id.</summary>
    public Task<int?> RedoAsync() => Task.Run(session.Redo, lifetimeToken);

    /// <summary>Groups the given objects and returns the new parent id.</summary>
    public Task<int?> GroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.GroupObjects(ids), lifetimeToken);

    /// <summary>Ungroups the given objects and returns the promoted object ids.</summary>
    public Task<IReadOnlyList<int>> UngroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.UngroupObjects(ids), lifetimeToken);

    /// <summary>Duplicates the given object and returns the new object id.</summary>
    public Task<int?> DuplicateAsync(int id) => Task.Run(() => session.DuplicateObject(id), lifetimeToken);
    /// <summary>Deletes the given object and returns true when it existed.</summary>
    public Task<bool> DeleteAsync(int id) => Task.Run(() => session.DeleteObject(id), lifetimeToken);
}
