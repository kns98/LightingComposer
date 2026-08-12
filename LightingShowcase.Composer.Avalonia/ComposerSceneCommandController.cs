/*
 * This controller translates Avalonia events and commands into editor operations while keeping the live scene
 * behind `ComposerSceneSession`. Its job is coordination: validate/route input, invoke the appropriate session or
 * renderer operation, and update presentation state without becoming a competing owner of scene data.
 */
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

    // GroupAsync collects async under a common hierarchy node so they can be manipulated as a unit without baking
    // away each child’s own geometry/material state. Potentially blocking/CPU work runs on a worker task rather
    // than Avalonia’s UI thread. Cancellation is propagated so shutdown or a newer request can make obsolete work
    // stop early.
    public Task<int?> GroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.GroupObjects(ids), lifetimeToken);

    // UngroupAsync removes the grouping relationship around async while preserving children and their world-space
    // meaning, then returns/updates the identities needed for selection. Potentially blocking/CPU work runs on a
    // worker task rather than Avalonia’s UI thread. Cancellation is propagated so shutdown or a newer request can
    // make obsolete work stop early.
    public Task<IReadOnlyList<int>> UngroupAsync(IEnumerable<int> ids) =>
        Task.Run(() => session.UngroupObjects(ids), lifetimeToken);

    // DuplicateAsync creates an independent copy of async with a new scene identity while preserving the source
    // geometry/material/authored metadata that should carry over. Cancellation is propagated so shutdown or a newer
    // request can make obsolete work stop early.
    public Task<int?> DuplicateAsync(int id) => Task.Run(() => session.DuplicateObject(id), lifetimeToken);
    // DeleteAsync deletes async as a logical editor operation, including the bookkeeping needed so
    // selection/history/caches do not retain a dangling object reference. Cancellation is propagated so shutdown or
    // a newer request can make obsolete work stop early.
    public Task<bool> DeleteAsync(int id) => Task.Run(() => session.DeleteObject(id), lifetimeToken);
}
