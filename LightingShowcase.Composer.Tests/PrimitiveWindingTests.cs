using LightingShowcase.Math3D;
using LightingShowcase.ObjectLibrary.BuiltIns;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class PrimitiveWindingTests
{
    private static List<Triangle> BuildTriangles(PrimitiveBase primitive)
    {
        List<Triangle> tris = new();
        primitive.Build(
            new SceneMaterials(),
            primitive.CreateDefaultParameters(),
            new Material(new Vec3(1, 1, 1)),
            (a, b, c, uva, uvb, uvc, material) => tris.Add(new Triangle(a, b, c, uva, uvb, uvc, material)));
        return tris;
    }

    [Fact]
    public void PlanePrimitive_FacesUp()
    {
        List<Triangle> tris = BuildTriangles(new PlanePrimitive());
        Assert.NotEmpty(tris);
        foreach (Triangle tri in tris)
            Assert.True(tri.Normal.Y > 0.9, $"Plane normal points down: {tri.Normal.Y}");
    }

    [Fact]
    public void CirclePrimitive_FacesUp()
    {
        List<Triangle> tris = BuildTriangles(new CirclePrimitive());
        Assert.NotEmpty(tris);
        foreach (Triangle tri in tris)
            Assert.True(tri.Normal.Y > 0.9, $"Circle normal points down: {tri.Normal.Y}");
    }

    [Fact]
    public void ConePrimitive_BottomCapFacesDown()
    {
        List<Triangle> tris = BuildTriangles(new ConePrimitive());
        Assert.NotEmpty(tris);
        Assert.Contains(tris, static tri => tri.Normal.Y < -0.9);
    }
}
