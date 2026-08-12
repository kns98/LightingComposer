namespace LightingShowcase.Composer;

/// <summary>
/// Application-level scene editing commands. UI selection/tree/inspector refresh
/// remains the caller's responsibility, while mutation execution is centralized.
/// </summary>
internal sealed class ComposerSceneCommandController
{
    private readonly ComposerSceneSession session;
    private readonly CancellationToken lifetimeToken;

    public ComposerSceneCommandController(ComposerSceneSession session, CancellationToken lifetimeToken)
    {
        this.session = session;
        this.lifetimeToken = lifetimeToken;
    }

    public Task<int> AddPrimitiveAsync(string primitiveName) =>
        Task.Run(() => session.InsertPrimitive(primitiveName), lifetimeToken);

    public Task<int?> UndoAsync() => Task.Run(session.Undo, lifetimeToken);
    public Task<int?> RedoAsync() => Task.Run(session.Redo, lifetimeToken);

    public Task<int?> GroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.GroupObjects(ids), lifetimeToken);

    public Task<IReadOnlyList<int>> UngroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.UngroupObjects(ids), lifetimeToken);

    public Task<int?> DuplicateAsync(int id) => Task.Run(() => session.DuplicateObject(id), lifetimeToken);
    public Task<bool> DeleteAsync(int id) => Task.Run(() => session.DeleteObject(id), lifetimeToken);
}
