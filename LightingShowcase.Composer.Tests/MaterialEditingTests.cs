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

    [Fact]
    public void DirectMaterialPropertiesAreEditableAndUndoableWithoutLosingPrimitiveParameters()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cylinder");
        Vec3 baseColor = new(0.31, 0.42, 0.53);
        Assert.True(session.SetObjectBaseColor(id, baseColor));

        ComposerMaterialProperties properties = new(
            Metallic: 0.65,
            Roughness: 0.21,
            Transmission: 0.35,
            Alpha: 0.72,
            Emission: 2.4,
            EmissionColor: new Vec3(1.0, 0.45, 0.2),
            AlphaMode: MaterialAlphaMode.Blend,
            AlphaCutoff: 0.33,
            DoubleSided: true,
            Ior: 1.61,
            Thickness: 0.012,
            AttenuationColor: new Vec3(0.7, 0.85, 1.0),
            AttenuationDistance: 1.75,
            Clearcoat: 0.8,
            ClearcoatRoughness: 0.11,
            NormalScale: 1.4,
            OcclusionStrength: 0.6);

        Assert.True(session.SetObjectMaterialProperties(id, properties));
        Assert.True(session.CanEditPrimitiveParameters(id));

        ComposerMaterialModel? edited = session.GetMaterialModel(id);
        Assert.NotNull(edited);
        Assert.Equal(baseColor.X, edited!.BaseColor.X, 6);
        Assert.Equal(0.65, edited.Metallic, 6);
        Assert.Equal(0.21, edited.Roughness, 6);
        Assert.Equal(0.35, edited.Transmission, 6);
        Assert.Equal(0.72, edited.Alpha, 6);
        Assert.Equal(2.4, edited.Emission, 6);
        Assert.Equal(MaterialAlphaMode.Blend, edited.AlphaMode);
        Assert.True(edited.DoubleSided);
        Assert.Equal(1.61, edited.Ior, 6);
        Assert.Equal(0.012, edited.Thickness, 6);
        Assert.Equal(1.75, edited.AttenuationDistance, 6);
        Assert.Equal(0.8, edited.Clearcoat, 6);
        Assert.Equal(0.11, edited.ClearcoatRoughness, 6);
        Assert.Equal(1.4, edited.NormalScale, 6);
        Assert.Equal(0.6, edited.OcclusionStrength, 6);

        Assert.Equal(id, session.Undo());
        ComposerMaterialModel? restored = session.GetMaterialModel(id);
        Assert.NotNull(restored);
        Assert.NotEqual(0.65, restored!.Metallic);
        Assert.True(session.CanEditPrimitiveParameters(id));
    }

    [Fact]
    public void TextureSlotsAndPerTextureMappingAreEditableIndependently()
    {
        string texturePath = Path.Combine(Path.GetTempPath(), $"lighting-composer-slot-texture-{Guid.NewGuid():N}.png");
        try
        {
            TextureMap.FromRgbaBytes("slot.png", 1, 1, [200, 120, 60, 255]).SavePng(texturePath);

            using ComposerSceneSession session = new();
            int id = session.InsertPrimitive("Cube");
            Assert.True(session.SetObjectTexture(id, MaterialTextureSlot.BaseColor, texturePath, 0.25, boxProjection: false));
            Assert.True(session.SetObjectTexture(id, MaterialTextureSlot.Normal, texturePath, 0.25, boxProjection: false));
            Assert.True(session.SetObjectTextureMappingAndProjection(
                id,
                MaterialTextureSlot.Normal,
                tileMeters: 0.25,
                boxProjection: false,
                offsetU: 0.125,
                offsetV: -0.25,
                scaleU: 2.0,
                scaleV: 0.5,
                rotationDegrees: 30.0,
                wrapU: TextureAddressMode.ClampToEdge,
                wrapV: TextureAddressMode.MirroredRepeat));

            ComposerMaterialModel? model = session.GetMaterialModel(id);
            Assert.NotNull(model);
            ComposerTextureSlotModel baseColor = model!.TextureSlot(MaterialTextureSlot.BaseColor);
            ComposerTextureSlotModel normal = model.TextureSlot(MaterialTextureSlot.Normal);
            Assert.True(baseColor.HasTexture);
            Assert.True(normal.HasTexture);
            Assert.Equal(0.0, baseColor.OffsetU, 6);
            Assert.Equal(0.125, normal.OffsetU, 6);
            Assert.Equal(-0.25, normal.OffsetV, 6);
            Assert.Equal(2.0, normal.ScaleU, 6);
            Assert.Equal(0.5, normal.ScaleV, 6);
            Assert.Equal(30.0, normal.RotationDegrees, 6);
            Assert.Equal(TextureAddressMode.ClampToEdge, normal.WrapU);
            Assert.Equal(TextureAddressMode.MirroredRepeat, normal.WrapV);
            Assert.True(session.CanEditPrimitiveParameters(id));

            Assert.True(session.ClearObjectTexture(id, MaterialTextureSlot.Normal));
            ComposerMaterialModel? cleared = session.GetMaterialModel(id);
            Assert.NotNull(cleared);
            Assert.True(cleared!.TextureSlot(MaterialTextureSlot.BaseColor).HasTexture);
            Assert.False(cleared.TextureSlot(MaterialTextureSlot.Normal).HasTexture);
        }
        finally
        {
            try { File.Delete(texturePath); } catch { }
        }
    }

    [Fact]
    public void SecondaryTextureMappingRoundTripsThroughComposerScene()
    {
        string texturePath = Path.Combine(Path.GetTempPath(), $"lighting-composer-emissive-texture-{Guid.NewGuid():N}.png");
        string scenePath = Path.Combine(Path.GetTempPath(), $"lighting-composer-emissive-scene-{Guid.NewGuid():N}.lscene");
        try
        {
            TextureMap.FromRgbaBytes("emissive.png", 1, 1, [255, 180, 40, 255]).SavePng(texturePath);
            using (ComposerSceneSession writer = new())
            {
                int id = writer.InsertPrimitive("Cylinder");
                Assert.True(writer.SetObjectTexture(id, MaterialTextureSlot.Emissive, texturePath, 0.3, boxProjection: true));
                Assert.True(writer.SetObjectTextureMappingAndProjection(
                    id,
                    MaterialTextureSlot.Emissive,
                    0.3,
                    true,
                    0.2,
                    0.1,
                    1.5,
                    0.75,
                    12.0,
                    TextureAddressMode.Repeat,
                    TextureAddressMode.ClampToEdge));
                writer.Save(scenePath, CancellationToken.None);
            }

            using ComposerSceneSession reader = new();
            reader.Load(scenePath, CancellationToken.None);
            int loadedId = Assert.Single(reader.GetObjectInfos()).Id;
            ComposerMaterialModel? model = reader.GetMaterialModel(loadedId);
            Assert.NotNull(model);
            Assert.True(model!.UsesBoxProjection);
            Assert.Equal(0.3, model.TextureTileMeters, 6);
            ComposerTextureSlotModel emissive = model.TextureSlot(MaterialTextureSlot.Emissive);
            Assert.True(emissive.HasTexture);
            Assert.Equal(0.2, emissive.OffsetU, 6);
            Assert.Equal(1.5, emissive.ScaleU, 6);
            Assert.Equal(12.0, emissive.RotationDegrees, 6);
            Assert.Equal(TextureAddressMode.ClampToEdge, emissive.WrapV);
        }
        finally
        {
            try { File.Delete(texturePath); } catch { }
            try { File.Delete(scenePath); } catch { }
        }
    }

}
