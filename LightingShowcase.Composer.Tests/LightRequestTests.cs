using LightingShowcase.CommandLine;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class LightRequestTests
{
    [Fact]
    public void Parse_FullSpec_SetsAllFields()
    {
        LightRequest light = LightRequest.Parse(
            "kind=spot;position=1,2,3;color=1,0.5,0.25;intensity=7;direction=0,-1,0;range=10;inner=0.1;outer=0.7;shadows=false;id=key2;enabled=true", 0);

        Assert.Equal(SceneLightKind.Spot, light.Kind);
        Assert.Equal(new Vec3(1, 2, 3), light.Position);
        Assert.Equal(new Vec3(1, 0.5, 0.25), light.Color);
        Assert.Equal(7.0, light.Intensity);
        Assert.Equal(new Vec3(0, -1, 0), light.Direction);
        Assert.Equal(10.0, light.Range);
        Assert.Equal(0.1, light.InnerCone);
        Assert.Equal(0.7, light.OuterCone);
        Assert.False(light.CastsShadow);
        Assert.True(light.Enabled);
        Assert.Equal("key2", light.Id);
    }

    [Fact]
    public void Parse_PositionOnly_UsesDefaults()
    {
        LightRequest light = LightRequest.Parse("position=3,4,-2", 1);

        Assert.Equal(SceneLightKind.Point, light.Kind);
        Assert.Equal(new Vec3(3, 4, -2), light.Position);
        Assert.Equal(new Vec3(1, 1, 1), light.Color);
        Assert.Equal(5.0, light.Intensity);
        Assert.Equal(0.0, light.Range);
        Assert.True(light.CastsShadow);
        Assert.True(light.Enabled);
        Assert.Equal("light2", light.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kind=point;color=1,1,1")]
    public void Parse_MissingPosition_Throws(string spec)
    {
        Assert.Throws<ArgumentException>(() => LightRequest.Parse(spec, 0));
    }

    [Theory]
    [InlineData("position=0,0,0;kind=torch")]
    [InlineData("position=0,0;")]
    [InlineData("position=0,0,0;intensity=much")]
    [InlineData("position=0,0,0;wat=1")]
    [InlineData("position=0,0,0;shadows=maybe")]
    public void Parse_InvalidSpec_Throws(string spec)
    {
        Assert.Throws<ArgumentException>(() => LightRequest.Parse(spec, 0));
    }

    [Fact]
    public void ToSceneLight_MapsFields()
    {
        LightRequest light = LightRequest.Parse("kind=directional;position=0,5,0;direction=0,-1,0;color=1,1,1;intensity=3;id=sun", 0);

        SceneLight sceneLight = light.ToSceneLight();

        Assert.Equal("sun", sceneLight.Id);
        Assert.Equal(SceneLightKind.Directional, sceneLight.Kind);
        Assert.Equal(new Vec3(0, 5, 0), sceneLight.Position);
        Assert.Equal(new Vec3(0, -1, 0), sceneLight.Direction);
        Assert.Equal(3.0, sceneLight.Intensity);
    }
}
