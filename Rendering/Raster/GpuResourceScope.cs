// File: Rendering/Raster/GpuResourceScope.cs
// Purpose: Tracks GPU resources for reverse-order disposal when creation fails.

namespace LightingShowcase.Rendering;

internal sealed class GpuResourceScope : IDisposable
{
    private readonly List<IDisposable> owned = new();
    private bool committed;

    public T Track<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    public void Commit() => committed = true;

    public void Dispose()
    {
        if (committed)
            return;
        committed = true;
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            try { owned[i].Dispose(); }
            catch { }
        }
    }
}
