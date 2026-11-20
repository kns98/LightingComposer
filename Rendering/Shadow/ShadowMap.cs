// File: Rendering/Shadow/ShadowMap.cs
// Purpose: Per-light shadow depth buffer with its projection.

namespace LightingShowcase.Rendering;

internal sealed class ShadowMap
{
    public string LightId { get; }
    public int Size { get; }
    public ShadowProjection Projection { get; }
    public double[] Depth { get; }
    public bool Enabled => HasDepth;
    internal bool HasDepth { get; set; }

    /// <summary>Initializes an empty shadow map depth buffer.</summary>
    public ShadowMap(string lightId, int size, ShadowProjection projection)
    {
        LightId = lightId;
        Size = size;
        Projection = projection;
        Depth = new double[size * size];
        Array.Fill(Depth, double.PositiveInfinity);
    }
}
