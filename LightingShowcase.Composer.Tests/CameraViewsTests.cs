using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class CameraViewsTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("iso")]
    [InlineData(new object?[] { null })]
    [InlineData("")]
    public void ApplyView_IdentityViews_PreserveOffset(string? view)
    {
        Vec3 offset = new(1, 2, 3);

        Assert.Equal(offset, CameraViews.ApplyView(offset, view));
    }

    [Fact]
    public void ApplyView_Front_NegatesHorizontalOffset()
    {
        Vec3 result = CameraViews.ApplyView(new Vec3(1, 2, 3), "front");

        Assert.Equal(-1.0, result.X, precision: 9);
        Assert.Equal(2.0, result.Y, precision: 9);
        Assert.Equal(-3.0, result.Z, precision: 9);
    }

    [Fact]
    public void ApplyView_LeftAndRight_RotateOppositeWays()
    {
        Vec3 left = CameraViews.ApplyView(new Vec3(1, 2, 3), "left");
        Vec3 right = CameraViews.ApplyView(new Vec3(1, 2, 3), "right");

        Assert.Equal(3.0, left.X, precision: 9);
        Assert.Equal(-1.0, left.Z, precision: 9);
        Assert.Equal(-3.0, right.X, precision: 9);
        Assert.Equal(1.0, right.Z, precision: 9);
    }

    [Fact]
    public void ApplyView_Top_PreservesDistancePointingUp()
    {
        Vec3 result = CameraViews.ApplyView(new Vec3(1, 2, 3), "top");

        Assert.Equal(new Vec3(1, 2, 3).Length(), result.Length(), precision: 9);
        Assert.True(result.Y > Math.Abs(result.X) && result.Y > Math.Abs(result.Z));
    }

    [Fact]
    public void ApplyView_UnknownView_Throws()
    {
        Assert.Throws<ArgumentException>(() => CameraViews.ApplyView(new Vec3(1, 2, 3), "underneath"));
        Assert.False(CameraViews.IsKnownView("underneath"));
        Assert.True(CameraViews.IsKnownView("FRONT"));
    }
}
