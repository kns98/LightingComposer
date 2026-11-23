using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class RasterClipTests
{
    private static readonly CameraBasis Basis = new(new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

    private static Triangle StraddlingTriangle() => new(
        new Vec3(0, -0.1, 0.3), new Vec3(2, -0.5, -1), new Vec3(-2, 0.5, -1),
        new Material(new Vec3(0.8, 0.8, 0.8)));

    [Fact]
    public void Clip_TwoVerticesBehindNear_ReturnsSinglePayloadTriangle()
    {
        Triangle tri = StraddlingTriangle();

        bool clipped = RasterClipper.TryClip(tri, Vec3.Zero, Basis, 160, 120,
            out RasterClipper.ClippedTriangle[] pieces, out _, out _, out _, out _);

        Assert.True(clipped);
        Assert.Single(pieces);
        foreach (RasterClipper.ClippedVertex v in new[] { pieces[0].A, pieces[0].B, pieces[0].C })
        {
            Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y));
            Assert.Equal(1.0, v.Wa + v.Wb + v.Wc, precision: 9);
        }
    }

    [Fact]
    public void Clip_OneVertexBehindNear_ReturnsTwoPieces()
    {
        Triangle tri = new(
            new Vec3(0, -0.1, 0.3), new Vec3(0.2, 0.1, 0.4), new Vec3(-2, 0.5, -1),
            new Material(new Vec3(0.8, 0.8, 0.8)));

        bool clipped = RasterClipper.TryClip(tri, Vec3.Zero, Basis, 160, 120,
            out RasterClipper.ClippedTriangle[] pieces, out _, out _, out _, out _);

        Assert.True(clipped);
        Assert.Equal(2, pieces.Length);
    }

    [Fact]
    public void Clip_FullyBehindNear_ReturnsFalse()
    {
        Triangle tri = new(
            new Vec3(-4, -3, -1), new Vec3(4, -3, -2), new Vec3(0, 3, -3),
            new Material(new Vec3(0.8, 0.8, 0.8)));

        bool clipped = RasterClipper.TryClip(tri, Vec3.Zero, Basis, 160, 120,
            out RasterClipper.ClippedTriangle[] pieces, out _, out _, out _, out _);

        Assert.False(clipped);
        Assert.Empty(pieces);
    }

    [Fact]
    public void Clip_ExcludesNearPlaneCutFromWireframeEdges()
    {
        Triangle tri = StraddlingTriangle();

        bool clipped = RasterClipper.TryClip(tri, Vec3.Zero, Basis, 160, 120,
            out _, out RasterClipper.ClippedEdge[] edges, out _, out _, out _);

        Assert.True(clipped);
        Assert.Equal(2, edges.Length);
    }

    [Fact]
    public void Rasterize_NearStraddlingTriangle_DrawsClippedPixels()
    {
        Triangle tri = StraddlingTriangle();
        int width = 160;
        int height = 120;
        uint[] pixels = new uint[width * height];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        uint background = ColorMath.PackDisplay(new Vec3(0.1, 0.2, 0.3));
        Array.Fill(pixels, background);

        new TriangleRasterizer().Rasterize(pixels, zBuffer, width, height, tri, Vec3.Zero, Basis,
            Array.Empty<PreparedLight>(), interactiveFast: false, exposure: 1.0);

        int changed = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == background)
                continue;
            changed++;
            Assert.True(double.IsFinite(zBuffer[i]));
        }
        Assert.True(changed > 0);
    }

    [Fact]
    public void DepthOnly_NearStraddlingTriangle_WritesDepth()
    {
        Triangle tri = StraddlingTriangle();
        int width = 160;
        int height = 120;
        double[] zBuffer = new double[width * height];
        Array.Fill(zBuffer, double.PositiveInfinity);

        DepthOnlyTriangleRasterizer.Rasterize(zBuffer, width, height, tri, Vec3.Zero, Basis);

        Assert.Contains(zBuffer, static z => double.IsFinite(z));
    }

    [Fact]
    public void Wireframe_NearStraddlingTriangle_DrawsEdgePixels()
    {
        Triangle tri = StraddlingTriangle();
        int width = 160;
        int height = 120;
        uint[] pixels = new uint[width * height];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        int solidPixels = 0;
        int hiddenPixels = 0;

        WireframeTriangleRenderer.Rasterize(pixels, zBuffer, width, height, tri, Vec3.Zero, Basis,
            ref solidPixels, ref hiddenPixels);

        Assert.True(solidPixels + hiddenPixels > 0);
    }
}
