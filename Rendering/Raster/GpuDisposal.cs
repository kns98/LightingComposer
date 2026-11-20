// File: Rendering/Raster/GpuDisposal.cs
// Purpose: Best-effort ordered disposal of GPU resources.

namespace LightingShowcase.Rendering;

internal static class GpuDisposal
{
    public static void Dispose(params IDisposable?[] resources)
    {
        foreach (IDisposable? resource in resources)
        {
            try { resource?.Dispose(); }
            catch { }
        }
    }
}
