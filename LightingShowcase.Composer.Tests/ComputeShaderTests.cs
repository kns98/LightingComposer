using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Veldrid;
using Veldrid.SPIRV;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class ComputeShaderTests
{
    [Fact]
    public void GpuTextureInfo_Default_IsIdentityTransformRepeat()
    {
        GpuTextureInfo info = new(0, 1, 1);

        Assert.Equal(new Vector4(0, 1, 1, 0), info.Data);
        Assert.Equal(new Vector4(0, 0, 1, 1), info.Transform);
        Assert.Equal(new Vector4(0, 0, 0, 0), info.Address);
    }

    [Fact]
    public void GpuTextureInfo_PacksTransformAndAddress()
    {
        TextureMap texture = TextureMap.FromRgbaBytes("transformed", 2, 2, new byte[16])
            .WithAddressing(TextureAddressMode.ClampToEdge, TextureAddressMode.MirroredRepeat)
            .WithTextureTransform(0.5, -0.25, 2.0, 3.0, 1.5);
        GpuTextureInfo info = new(8, 2, 2, texture);

        Assert.Equal(new Vector4(8, 2, 2, 0), info.Data);
        Assert.Equal(new Vector4(0.5f, -0.25f, 2f, 3f), info.Transform);
        Assert.Equal(new Vector4(1f, 2f, 1.5f, 0f), info.Address);
    }

    [Fact]
    public void GpuTextureInfo_StrideMatchesShaderLayout()
    {
        Assert.Equal(48, Marshal.SizeOf<GpuTextureInfo>());
    }

    [Fact]
    public void GpuTriangle_PacksDoubleSidedFlag()
    {
        Dictionary<TextureMap, int> ids = new();
        GpuTriangle single = new(MakeTriangle(new Material(new Vec3(1, 1, 1))), ids);
        GpuTriangle both = new(MakeTriangle(new Material(new Vec3(1, 1, 1), doubleSided: true)), ids);
        GpuTriangle maskedBoth = new(MakeTriangle(new Material(new Vec3(1, 1, 1), alphaMode: MaterialAlphaMode.Mask, doubleSided: true)), ids);

        Assert.Equal(0, (int)single.TextureInfo.Z);
        Assert.Equal(4, (int)both.TextureInfo.Z);
        Assert.Equal(5, (int)maskedBoth.TextureInfo.Z);
    }

    [Fact]
    public void GpuTriangle_PacksOpticalFactors()
    {
        TextureMap transmission = TextureMap.FromRgbaBytes("trans", 1, 1, new byte[] { 255, 255, 255, 255 });
        Dictionary<TextureMap, int> ids = new() { [transmission] = 7 };
        Material material = new(
            new Vec3(1, 1, 1),
            transmissionTexture: transmission,
            ior: 2.0,
            thickness: 3.0,
            attenuationColor: new Vec3(0.1, 0.2, 0.3),
            attenuationDistance: 4.0,
            clearcoat: 0.5,
            clearcoatRoughness: 0.2,
            clearcoatUsesTransmissionTexture: true);
        GpuTriangle packed = new(MakeTriangle(material), ids);

        Assert.Equal(new Vector4(2f, 3f, 0.5f, 0.2f), packed.OpticalFactors);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 4f), packed.Attenuation);
        Assert.Equal(new Vector4(7f, 1f, 0f, 0f), packed.TextureInfo3);
    }

    [Fact]
    public void GpuTriangle_StrideMatchesShaderLayout()
    {
        Assert.Equal(224, Marshal.SizeOf<GpuTriangle>());
    }

    [Fact]
    public void ComputeShader_CompilesToSpirv()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", "vulkan_scene_primary.comp");
        Assert.True(File.Exists(path), "Compute shader was not copied to test output: " + path);
        string source = File.ReadAllText(path);

        SpirvCompilationResult result = SpirvCompilation.CompileGlslToSpirv(source, "main", ShaderStages.Compute, new GlslCompileOptions());

        Assert.True(result.SpirvBytes.Length > 0);
    }

    private static Triangle MakeTriangle(Material material) =>
        new(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), material);
}
