using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class MaterialEditingTests
{
    [Fact]
    public void PresetAndColorEditsKeepProceduralParameters()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cube");
        Assert.True(session.CanEditPrimitiveParameters(id));

        MaterialPreset steel = MaterialPresetLibrary.Common.First(p => p.Name == "Steel");
        Assert.True(session.ApplyMaterialPreset(id, steel));
        Assert.True(session.CanEditPrimitiveParameters(id));

        ComposerMaterialModel? material = session.GetMaterialModel(id);
        Assert.NotNull(material);
        Assert.Equal(1.0, material!.Metallic, 6);
        Assert.Equal(steel.Material.Roughness, material.Roughness, 6);

        Vec3 custom = new(0.25, 0.5, 0.75);
        Assert.True(session.SetObjectBaseColor(id, custom));
        Assert.True(session.CanEditPrimitiveParameters(id));
        ComposerMaterialModel? recolored = session.GetMaterialModel(id);
        Assert.NotNull(recolored);
        Assert.Equal(custom.X, recolored!.BaseColor.X, 6);
        Assert.Equal(custom.Y, recolored.BaseColor.Y, 6);
        Assert.Equal(custom.Z, recolored.BaseColor.Z, 6);
    }

    [Fact]
    public void MeterTiledTextureSurvivesProceduralRegeneration()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lighting-composer-texture-{Guid.NewGuid():N}.png");
        try
        {
            TextureMap.FromRgbaBytes(
                "test.png",
                2,
                2,
                [
                    255, 0, 0, 255, 0, 255, 0, 255,
                    0, 0, 255, 255, 255, 255, 255, 255
                ]).SavePng(path);

            using ComposerSceneSession session = new();
            int id = session.InsertPrimitive("Cube");
            Assert.True(session.SetObjectTexture(id, path, 0.5, boxProjection: true));
            Assert.True(session.CanEditPrimitiveParameters(id));

            ComposerMaterialModel? textured = session.GetMaterialModel(id);
            Assert.NotNull(textured);
            Assert.NotNull(textured!.TextureName);
            Assert.True(textured.UsesBoxProjection);
            Assert.Equal(0.5, textured.TextureTileMeters, 6);

            ComposerPrimitiveParameterModel? parameters = session.BeginPrimitiveParameterEdit(id);
            Assert.NotNull(parameters);
            Dictionary<string, double> changed = parameters!.Values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            changed["width"] = changed["width"] * 1.4;
            Assert.True(session.PreviewPrimitiveParameters(id, changed));
            Assert.True(session.CommitPrimitiveParameterEdit(id));

            ComposerMaterialModel? regenerated = session.GetMaterialModel(id);
            Assert.NotNull(regenerated);
            Assert.NotNull(regenerated!.TextureName);
            Assert.True(regenerated.UsesBoxProjection);
            Assert.Equal(0.5, regenerated.TextureTileMeters, 6);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void MaterialEditParticipatesInUndo()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cube");
        Vec3 before = session.GetMaterialModel(id)!.BaseColor;

        Assert.True(session.SetObjectBaseColor(id, new Vec3(0.9, 0.1, 0.2)));
        Assert.True(session.CanUndo);
        Assert.Equal(id, session.Undo());

        Vec3 restored = session.GetMaterialModel(id)!.BaseColor;
        Assert.Equal(before.X, restored.X, 6);
        Assert.Equal(before.Y, restored.Y, 6);
        Assert.Equal(before.Z, restored.Z, 6);
        Assert.True(session.CanEditPrimitiveParameters(id));
    }

    [Fact]
    public void TexturedProceduralPrimitiveRoundTripsThroughComposerScene()
    {
        string texturePath = Path.Combine(Path.GetTempPath(), $"lighting-composer-roundtrip-texture-{Guid.NewGuid():N}.png");
        string scenePath = Path.Combine(Path.GetTempPath(), $"lighting-composer-roundtrip-{Guid.NewGuid():N}.lscene");
        try
        {
            TextureMap.FromRgbaBytes(
                "roundtrip.png",
                1,
                1,
                [32, 96, 224, 255]).SavePng(texturePath);

            using (ComposerSceneSession writer = new())
            {
                int id = writer.InsertPrimitive("Cube");
                Assert.True(writer.SetObjectTexture(id, texturePath, 0.4, boxProjection: true));
                writer.Save(scenePath, CancellationToken.None);
            }

            using ComposerSceneSession reader = new();
            reader.Load(scenePath, CancellationToken.None);
            int loadedId = Assert.Single(reader.GetObjectInfos()).Id;
            Assert.True(reader.CanEditPrimitiveParameters(loadedId));
            ComposerMaterialModel? material = reader.GetMaterialModel(loadedId);
            Assert.NotNull(material);
            Assert.NotNull(material!.TextureName);
            Assert.True(material.HasStoredTextureProjection);
            Assert.True(material.UsesBoxProjection);
            Assert.Equal(0.4, material.TextureTileMeters, 6);
        }
        finally
        {
            try { File.Delete(texturePath); } catch { }
            try { File.Delete(scenePath); } catch { }
        }
    }
}
