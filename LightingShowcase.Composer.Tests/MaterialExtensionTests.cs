using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Composer;
using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MaterialExtensionTests
{
    private static TextureMap SolidTexture(string name, byte r, byte g, byte b, byte a = 255) =>
        TextureMap.FromRgbaBytes(name, 1, 1, new byte[] { r, g, b, a });

    [Fact]
    public void SampleSpecular_Defaults_AreNeutral()
    {
        (double specular, Vec3 color) = new Material(new Vec3(1, 1, 1)).SampleSpecular(0.3, 0.7);

        Assert.Equal(1.0, specular);
        Assert.Equal(new Vec3(1, 1, 1), color);
    }

    [Fact]
    public void SampleSpecular_CombinesFactorAndTextures()
    {
        Material material = new(
            new Vec3(1, 1, 1),
            specular: 0.5,
            specularColor: new Vec3(0.5, 0.5, 0.5),
            specularTexture: SolidTexture("spec", 0, 0, 0, 128),
            specularColorTexture: SolidTexture("spec-color", 255, 0, 0));

        (double specular, Vec3 color) = material.SampleSpecular(0.5, 0.5);

        Assert.Equal(0.5 * 128.0 / 255.0, specular, precision: 9);
        Assert.Equal(0.5, color.X, precision: 9);
        Assert.Equal(0.0, color.Y, precision: 9);
        Assert.Equal(0.0, color.Z, precision: 9);
    }

    [Fact]
    public void SampleSheen_Defaults_AreDisabled()
    {
        (Vec3 color, double roughness) = new Material(new Vec3(1, 1, 1)).SampleSheen(0.3, 0.7);

        Assert.Equal(Vec3.Zero, color);
        Assert.Equal(0.0, roughness);
    }

    [Fact]
    public void SampleSheen_CombinesFactorAndTextures()
    {
        Material material = new(
            new Vec3(1, 1, 1),
            sheenColor: new Vec3(0.5, 0.25, 0.125),
            sheenRoughness: 0.5,
            sheenColorTexture: SolidTexture("sheen-color", 255, 255, 255),
            sheenRoughnessTexture: SolidTexture("sheen-rough", 0, 0, 0, 128));

        (Vec3 color, double roughness) = material.SampleSheen(0.5, 0.5);

        Assert.Equal(new Vec3(0.5, 0.25, 0.125), color);
        Assert.Equal(0.5 * 128.0 / 255.0, roughness, precision: 9);
    }

    [Fact]
    public void SampleAnisotropy_Defaults_AreIsotropic()
    {
        (double strength, Vec2 direction) = new Material(new Vec3(1, 1, 1)).SampleAnisotropy(0.3, 0.7);

        Assert.Equal(0.0, strength);
        Assert.Equal(1.0, direction.U, precision: 9);
        Assert.Equal(0.0, direction.V, precision: 9);
    }

    [Fact]
    public void SampleAnisotropy_RotationTurnsDirection()
    {
        Material material = new(new Vec3(1, 1, 1), anisotropy: 0.6, anisotropyRotation: Math.PI / 2.0);

        (double strength, Vec2 direction) = material.SampleAnisotropy(0.5, 0.5);

        Assert.Equal(0.6, strength, precision: 9);
        Assert.Equal(0.0, direction.U, precision: 9);
        Assert.Equal(1.0, direction.V, precision: 9);
    }

    [Fact]
    public void SampleAnisotropy_TextureSetsDirectionAndStrength()
    {
        Material material = new(
            new Vec3(1, 1, 1),
            anisotropy: 0.6,
            anisotropyTexture: SolidTexture("aniso", 255, 255, 128));

        (double strength, Vec2 direction) = material.SampleAnisotropy(0.5, 0.5);

        Assert.Equal(0.6 * 128.0 / 255.0, strength, precision: 9);
        Assert.Equal(1.0 / Math.Sqrt(2.0), direction.U, precision: 6);
        Assert.Equal(1.0 / Math.Sqrt(2.0), direction.V, precision: 6);
    }

    [Fact]
    public void SampleIridescence_Defaults_AreDisabledAtMaxThickness()
    {
        (double factor, double thickness) = new Material(new Vec3(1, 1, 1)).SampleIridescence(0.3, 0.7);

        Assert.Equal(0.0, factor);
        Assert.Equal(400.0, thickness);
    }

    [Fact]
    public void SampleIridescence_CombinesFactorAndThicknessTextures()
    {
        Material material = new(
            new Vec3(1, 1, 1),
            iridescence: 0.8,
            iridescenceThicknessMinimum: 100.0,
            iridescenceThicknessMaximum: 300.0,
            iridescenceTexture: SolidTexture("irid", 128, 0, 0),
            iridescenceThicknessTexture: SolidTexture("irid-thick", 0, 255, 0));

        (double factor, double thickness) = material.SampleIridescence(0.5, 0.5);

        Assert.Equal(0.8 * 128.0 / 255.0, factor, precision: 9);
        Assert.Equal(300.0, thickness, precision: 9);
    }

    [Fact]
    public void SampleIridescence_AllowsInvertedThicknessRange()
    {
        Material material = new(
            new Vec3(1, 1, 1),
            iridescenceThicknessMinimum: 400.0,
            iridescenceThicknessMaximum: 100.0,
            iridescenceThicknessTexture: SolidTexture("irid-thick", 0, 255, 0));

        (_, double thickness) = material.SampleIridescence(0.5, 0.5);

        Assert.Equal(100.0, thickness, precision: 9);
    }

    [Fact]
    public void MaterialDefinition_RoundTripsExtensionFields()
    {
        Material material = new(
            new Vec3(0.9, 0.8, 0.7),
            specular: 0.5,
            specularColor: new Vec3(0.2, 0.4, 0.6),
            sheenColor: new Vec3(0.1, 0.2, 0.3),
            sheenRoughness: 0.5,
            anisotropy: 0.6,
            anisotropyRotation: 1.1,
            iridescence: 0.7,
            iridescenceIor: 1.6,
            iridescenceThicknessMinimum: 200.0,
            iridescenceThicknessMaximum: 800.0,
            specularTexture: SolidTexture("spec", 1, 2, 3, 4),
            anisotropyTexture: SolidTexture("aniso", 5, 6, 7, 8));

        MaterialDefinition definition = MaterialDefinition.FromMaterial(material, "m1", "Mat");
        Material rebuilt = definition.ToMaterial(
            specularTexture: material.SpecularTexture,
            anisotropyTexture: material.AnisotropyTexture);

        Assert.Equal(0.5, rebuilt.Specular);
        Assert.Equal(new Vec3(0.2, 0.4, 0.6), rebuilt.SpecularColor);
        Assert.Equal(new Vec3(0.1, 0.2, 0.3), rebuilt.SheenColor);
        Assert.Equal(0.5, rebuilt.SheenRoughness);
        Assert.Equal(0.6, rebuilt.Anisotropy);
        Assert.Equal(1.1, rebuilt.AnisotropyRotation);
        Assert.Equal(0.7, rebuilt.Iridescence);
        Assert.Equal(1.6, rebuilt.IridescenceIor);
        Assert.Equal(200.0, rebuilt.IridescenceThicknessMinimum);
        Assert.Equal(800.0, rebuilt.IridescenceThicknessMaximum);
        Assert.Same(material.SpecularTexture, rebuilt.SpecularTexture);
        Assert.Same(material.AnisotropyTexture, rebuilt.AnisotropyTexture);
        Assert.Equal("m1", definition.Id);
        Assert.Equal("spec", definition.SpecularTextureId);
    }

    [Fact]
    public void BinaryScene_RoundTripsExtensionFields()
    {
        string directory = Directory.CreateTempSubdirectory("binary-ext-test").FullName;
        try
        {
            Material material = new(
                new Vec3(0.9, 0.8, 0.7),
                specular: 0.5,
                specularColor: new Vec3(0.2, 0.4, 0.6),
                sheenColor: new Vec3(0.1, 0.2, 0.3),
                sheenRoughness: 0.5,
                anisotropy: 0.6,
                anisotropyRotation: 1.1,
                iridescence: 0.7,
                iridescenceIor: 1.6,
                iridescenceThicknessMinimum: 200.0,
                iridescenceThicknessMaximum: 800.0,
                specularTexture: SolidTexture("spec", 10, 20, 30, 40),
                iridescenceThicknessTexture: SolidTexture("thick", 50, 60, 70, 80));
            Scene scene = new();
            scene.BeginGroup("ext");
            scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), material);
            scene.EndGroup();
            string path = Path.Combine(directory, "ext.lscn");
            BinarySceneFile.Save(scene, path);

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, path);
            loaded.RebuildWorldGeometry(buildAccelerationStructure: false);

            Assert.Single(loaded.Triangles);
            Material rebuilt = loaded.Triangles[0].Material;
            Assert.Equal(0.5, rebuilt.Specular, precision: 9);
            Assert.Equal(new Vec3(0.2, 0.4, 0.6), rebuilt.SpecularColor);
            Assert.Equal(new Vec3(0.1, 0.2, 0.3), rebuilt.SheenColor);
            Assert.Equal(0.5, rebuilt.SheenRoughness, precision: 9);
            Assert.Equal(0.6, rebuilt.Anisotropy, precision: 9);
            Assert.Equal(1.1, rebuilt.AnisotropyRotation, precision: 9);
            Assert.Equal(0.7, rebuilt.Iridescence, precision: 9);
            Assert.Equal(1.6, rebuilt.IridescenceIor, precision: 9);
            Assert.Equal(200.0, rebuilt.IridescenceThicknessMinimum, precision: 9);
            Assert.Equal(800.0, rebuilt.IridescenceThicknessMaximum, precision: 9);
            Assert.NotNull(rebuilt.SpecularTexture);
            Assert.Equal(10.0 / 255.0, rebuilt.SpecularTexture.Sample(0.5, 0.5).X, precision: 6);
            Assert.Equal(40.0 / 255.0, rebuilt.SpecularTexture.SampleAlpha(0.5, 0.5), precision: 6);
            Assert.NotNull(rebuilt.IridescenceThicknessTexture);
            Assert.Equal(60.0 / 255.0, rebuilt.IridescenceThicknessTexture.Sample(0.5, 0.5).Y, precision: 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RenderImage RenderQuad(Material material)
    {
        Scene scene = new();
        scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), material);
        scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, 1, 4), new Vec3(-1, 1, 4), material);
        scene.Lights.Add(new SceneLight("key", new Vec3(3, 4, -2), new Vec3(1, 1, 1), 5.0));
        CameraDefinition camera = new() { Position = new Vec3(0, 0, -4), Target = new Vec3(0, 0, 4) };
        return CpuCommandLineRenderer.Render(
            scene, camera,
            new RenderRequest { Width = 32, Height = 24, Samples = 1, Bounces = 0, FieldOfViewDegrees = 72.0, Exposure = 1.0 },
            CancellationToken.None, out _);
    }

    private static double Luminance(uint packed) =>
        (packed & 0xFF) + ((packed >> 8) & 0xFF) + ((packed >> 16) & 0xFF);

    private static double CenterLuminance(RenderImage image) =>
        Luminance(image.PackedRgba32[(image.Height / 2) * image.Width + (image.Width / 2)]);

    private static double MaxLuminance(RenderImage image)
    {
        double max = 0.0;
        foreach (uint pixel in image.PackedRgba32)
            max = Math.Max(max, Luminance(pixel));
        return max;
    }

    [Fact]
    public void GltfExport_WritesExtensionBlocks()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-ext-export-test").FullName;
        try
        {
            Material material = new(
                new Vec3(0.9, 0.8, 0.7),
                specular: 0.5,
                sheenColor: new Vec3(0.1, 0.2, 0.3),
                anisotropy: 0.6,
                iridescence: 0.7);
            Scene scene = new();
            scene.BeginGroup("ext");
            scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), material);
            scene.EndGroup();
            string path = Path.Combine(directory, "ext.gltf");
            GltfSceneIO.Save(scene, path, binary: false);

            string json = File.ReadAllText(path);
            Assert.Contains("KHR_materials_specular", json);
            Assert.Contains("KHR_materials_sheen", json);
            Assert.Contains("KHR_materials_anisotropy", json);
            Assert.Contains("KHR_materials_iridescence", json);
            Assert.Contains("specularFactor", json);
            Assert.Contains("sheenRoughnessFactor", json);
            Assert.Contains("anisotropyStrength", json);
            Assert.Contains("iridescenceThicknessMaximum", json);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SampleTransmission_CombinesFactorAndRedChannel()
    {
        Assert.Equal(0.0, new Material(new Vec3(1, 1, 1)).SampleTransmission(0.3, 0.7));

        Material half = new(new Vec3(1, 1, 1), transmission: 0.5, transmissionTexture: SolidTexture("red", 255, 0, 0));
        Assert.Equal(0.5, half.SampleTransmission(0.5, 0.5), precision: 9);

        Material blocked = new(new Vec3(1, 1, 1), transmission: 1.0, transmissionTexture: SolidTexture("black", 0, 0, 0));
        Assert.Equal(0.0, blocked.SampleTransmission(0.5, 0.5), precision: 9);
    }

    [Fact]
    public void Cpu_Clearcoat_AddsSharpHighlight()
    {
        RenderImage matte = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.9));
        RenderImage coated = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.9, clearcoat: 1.0, clearcoatRoughness: 0.1));

        Assert.False(matte.PackedRgba32.SequenceEqual(coated.PackedRgba32));
        Assert.True(MaxLuminance(coated) > MaxLuminance(matte));
    }

    [Fact]
    public void Cpu_VolumeAttenuation_TintsTransmittedLight()
    {
        Material white = new(
            new Vec3(0.9, 0.9, 0.9), roughness: 0.4, transmission: 1.0,
            thickness: 2.0, attenuationColor: new Vec3(1, 1, 1), attenuationDistance: 1.0);
        Material red = new(
            new Vec3(0.9, 0.9, 0.9), roughness: 0.4, transmission: 1.0,
            thickness: 2.0, attenuationColor: new Vec3(1, 0, 0), attenuationDistance: 1.0);
        RenderImage whiteImage = RenderQuad(white);
        RenderImage redImage = RenderQuad(red);

        Assert.False(whiteImage.PackedRgba32.SequenceEqual(redImage.PackedRgba32));
        uint whiteCenter = whiteImage.PackedRgba32[(whiteImage.Height / 2) * whiteImage.Width + (whiteImage.Width / 2)];
        uint redCenter = redImage.PackedRgba32[(redImage.Height / 2) * redImage.Width + (redImage.Width / 2)];
        double whiteRatio = (whiteCenter & 0xFF) / ((((whiteCenter >> 8) & 0xFF)) + 1.0);
        double redRatio = (redCenter & 0xFF) / ((((redCenter >> 8) & 0xFF)) + 1.0);
        Assert.True(redRatio > whiteRatio);
    }

    [Fact]
    public void Cpu_TransmissionTexture_MatchesFactor()
    {
        RenderImage open = RenderQuad(new Material(new Vec3(0.7, 0.7, 0.7), roughness: 0.4, transmission: 1.0));
        RenderImage whiteMask = RenderQuad(new Material(
            new Vec3(0.7, 0.7, 0.7), roughness: 0.4, transmission: 1.0, transmissionTexture: SolidTexture("white", 255, 255, 255)));
        RenderImage closed = RenderQuad(new Material(new Vec3(0.7, 0.7, 0.7), roughness: 0.4, transmission: 0.0));
        RenderImage blackMask = RenderQuad(new Material(
            new Vec3(0.7, 0.7, 0.7), roughness: 0.4, transmission: 1.0, transmissionTexture: SolidTexture("black", 0, 0, 0)));

        Assert.Equal(open.PackedRgba32, whiteMask.PackedRgba32);
        Assert.Equal(closed.PackedRgba32, blackMask.PackedRgba32);
        Assert.False(open.PackedRgba32.SequenceEqual(closed.PackedRgba32));
    }

    [Fact]
    public void Cpu_SpecularOff_RemovesHighlightPeak()
    {
        RenderImage baseline = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.3));
        RenderImage flat = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.3, specular: 0.0));

        Assert.False(baseline.PackedRgba32.SequenceEqual(flat.PackedRgba32));
        Assert.True(MaxLuminance(flat) < MaxLuminance(baseline));
    }

    [Fact]
    public void Cpu_SheenWhite_ScalesBaseAtNormalIncidence()
    {
        RenderImage baseline = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.8));
        RenderImage sheen = RenderQuad(new Material(new Vec3(0.5, 0.5, 0.5), roughness: 0.8, sheenColor: new Vec3(1, 1, 1), sheenRoughness: 0.5));

        Assert.False(baseline.PackedRgba32.SequenceEqual(sheen.PackedRgba32));
        Assert.True(CenterLuminance(sheen) < CenterLuminance(baseline));
    }

    [Fact]
    public void Cpu_AnisotropyRotation_ChangesHighlight()
    {
        RenderImage flat = RenderQuad(new Material(new Vec3(0.9, 0.9, 0.9), metallic: 1.0, roughness: 0.4));
        RenderImage stretched = RenderQuad(new Material(new Vec3(0.9, 0.9, 0.9), metallic: 1.0, roughness: 0.4, anisotropy: 1.0));
        RenderImage rotated = RenderQuad(new Material(new Vec3(0.9, 0.9, 0.9), metallic: 1.0, roughness: 0.4, anisotropy: 1.0, anisotropyRotation: Math.PI / 2.0));

        Assert.False(flat.PackedRgba32.SequenceEqual(stretched.PackedRgba32));
        Assert.False(stretched.PackedRgba32.SequenceEqual(rotated.PackedRgba32));
    }

    [Fact]
    public void Cpu_IridescenceThickness_ShiftsColor()
    {
        RenderImage plain = RenderQuad(new Material(new Vec3(0.6, 0.6, 0.6), roughness: 0.2));
        RenderImage thin = RenderQuad(new Material(new Vec3(0.6, 0.6, 0.6), roughness: 0.2, iridescence: 1.0, iridescenceThicknessMinimum: 300.0, iridescenceThicknessMaximum: 300.0));
        RenderImage thick = RenderQuad(new Material(new Vec3(0.6, 0.6, 0.6), roughness: 0.2, iridescence: 1.0, iridescenceThicknessMinimum: 600.0, iridescenceThicknessMaximum: 600.0));

        Assert.False(plain.PackedRgba32.SequenceEqual(thin.PackedRgba32));
        Assert.False(thin.PackedRgba32.SequenceEqual(thick.PackedRgba32));
    }
}
