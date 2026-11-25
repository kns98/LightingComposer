// Curated material-library showroom: a small living room where every object
// demonstrates library presets, tab (scalar) presets, or both. Pure-library
// objects select their preset in the editor dropdown; composed objects mix a
// library preset with scalar-preset values so the tab rows light up instead.
// The checked-in samples/MaterialShowcase.lscene file is generated from Build().
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal static class MaterialShowcase
{
    public static readonly (string Group, string Preset)[] PurePresetGroups = new[]
    {
        ("Back Wall", "White Matte Paint"),
        ("Coffee Table", "Walnut Wood"),
        ("Bowl", "White Porcelain"),
        ("Red Book", "Red Automotive Paint"),
        ("Blue Book", "Blue Automotive Paint"),
        ("Lamp Shade", "Warm LED"),
        ("Window Frame", "White Plastic"),
        ("TV Screen", "Neon Blue"),
        ("Plant Pot", "Terracotta"),
        ("Plant", "Leaves"),
        ("Storage Box", "Kraft Cardboard"),
        ("Chocolate Bar", "Dark Chocolate"),
    };

    public static readonly string[] ComposedGroupNames = new[]
    {
        "Floor",
        "Left Wall",
        "Side Table",
        "Rug",
        "Sofa",
        "Floor Lamp",
        "Window Glass",
    };

    /// <summary>Builds the showroom room with one material story per object.</summary>
    public static Scene Build()
    {
        Scene scene = new();
        scene.SetDescription("Material library showroom: living room.");

        Box(scene, "Floor", Textured(new Material(new Vec3(1, 1, 1), roughness: 1.0), "Wood Planks", 4.0, 3.0),
            new Vec3(-4.0, -0.1, -3.0), new Vec3(4.0, 0.0, 3.0));
        Quad(scene, "Back Wall", Find("White Matte Paint"),
            new Vec3(-4.0, 0.0, -3.0), new Vec3(4.0, 0.0, -3.0), new Vec3(4.0, 3.0, -3.0), new Vec3(-4.0, 3.0, -3.0));
        Quad(scene, "Left Wall", Textured(new Material(new Vec3(1, 1, 1), roughness: 1.0), "Concrete Wall", 3.0, 2.0),
            new Vec3(-4.0, 0.0, 3.0), new Vec3(-4.0, 0.0, -3.0), new Vec3(-4.0, 3.0, -3.0), new Vec3(-4.0, 3.0, 3.0));

        // Rug: deep-red velvet response on the Velvet base.
        DetailEffectPreset velvetFx = DetailEffect("Velvet");
        Material rug = Composed(Find("Velvet"), props => props with
        {
            Specular = velvetFx.Specular,
            SheenColor = velvetFx.SheenColor,
            SheenRoughness = velvetFx.SheenRoughness,
        });
        Box(scene, "Rug", rug, new Vec3(-1.5, 0.001, -1.0), new Vec3(1.5, 0.03, 1.0));

        // Sofa: leather maps over a neutral base with a worn-leather sheen.
        DetailEffectPreset leatherFx = DetailEffect("Leather Grain");
        Material sofa = Textured(
            Composed(new Material(new Vec3(1, 1, 1), roughness: 1.0), props => props with
            {
                Specular = leatherFx.Specular,
                SheenColor = leatherFx.SheenColor,
                SheenRoughness = leatherFx.SheenRoughness,
            }),
            "Brown Leather", 2.0, 2.0);
        scene.BeginGroup("Sofa");
        scene.Box(new Vec3(-1.2, 0.25, -2.2), new Vec3(1.2, 0.55, -1.2), sofa);
        scene.Box(new Vec3(-1.2, 0.55, -2.2), new Vec3(1.2, 1.1, -1.9), sofa);
        scene.Box(new Vec3(-1.5, 0.25, -2.2), new Vec3(-1.2, 0.75, -1.2), sofa);
        scene.Box(new Vec3(1.2, 0.25, -2.2), new Vec3(1.5, 0.75, -1.2), sofa);
        scene.EndGroup();

        scene.BeginGroup("Coffee Table");
        Material walnut = Find("Walnut Wood");
        scene.Box(new Vec3(-0.9, 0.42, -0.9), new Vec3(0.9, 0.48, -0.1), walnut);
        scene.Box(new Vec3(-0.86, 0.03, -0.86), new Vec3(-0.78, 0.42, -0.78), walnut);
        scene.Box(new Vec3(0.78, 0.03, -0.86), new Vec3(0.86, 0.42, -0.78), walnut);
        scene.Box(new Vec3(-0.86, 0.03, -0.22), new Vec3(-0.78, 0.42, -0.14), walnut);
        scene.Box(new Vec3(0.78, 0.03, -0.22), new Vec3(0.86, 0.42, -0.14), walnut);
        scene.EndGroup();

        Sphere(scene, "Bowl", Find("White Porcelain"), new Vec3(0.45, 0.61, -0.5), 0.13, 20, 12);
        Box(scene, "Red Book", Find("Red Automotive Paint"),
            new Vec3(-0.7, 0.48, -0.5), new Vec3(-0.3, 0.52, -0.2));
        Box(scene, "Blue Book", Find("Blue Automotive Paint"),
            new Vec3(-0.65, 0.52, -0.45), new Vec3(-0.35, 0.55, -0.25));
        Box(scene, "Chocolate Bar", Find("Dark Chocolate"),
            new Vec3(0.1, 0.48, -0.7), new Vec3(0.35, 0.5, -0.3));

        // Floor lamp: brushed-brass stand with a warm glowing globe shade.
        DetailEffectPreset brushedFx = DetailEffect("Brushed Metal");
        Material brass = Composed(Find("Brass"), props => props with { Anisotropy = brushedFx.Anisotropy });
        scene.BeginGroup("Floor Lamp");
        scene.Box(new Vec3(2.65, 0.0, -2.35), new Vec3(2.95, 0.05, -2.05), brass);
        scene.Box(new Vec3(2.77, 0.05, -2.23), new Vec3(2.83, 1.6, -2.17), brass);
        scene.EndGroup();
        Sphere(scene, "Lamp Shade", Find("Warm LED"), new Vec3(2.8, 1.78, -2.2), 0.24, 20, 12);

        // Window: frosted privacy glass in a white plastic frame.
        GlassPreset frosted = Glass("Frosted");
        Material windowGlass = Composed(Find("Clear Glass"), props => props with
        {
            Transmission = frosted.Transmission,
            Thickness = frosted.Thickness,
            AttenuationColor = frosted.AttenuationColor,
            AttenuationDistance = frosted.AttenuationDistance,
            Ior = frosted.Ior,
        });
        Quad(scene, "Window Glass", windowGlass,
            new Vec3(1.2, 1.2, -2.98), new Vec3(2.8, 1.2, -2.98), new Vec3(2.8, 2.4, -2.98), new Vec3(1.2, 2.4, -2.98));
        scene.BeginGroup("Window Frame");
        Material frame = Find("White Plastic");
        scene.Box(new Vec3(1.14, 1.14, -3.0), new Vec3(2.86, 1.2, -2.94), frame);
        scene.Box(new Vec3(1.14, 2.4, -3.0), new Vec3(2.86, 2.46, -2.94), frame);
        scene.Box(new Vec3(1.14, 1.2, -3.0), new Vec3(1.2, 2.4, -2.94), frame);
        scene.Box(new Vec3(2.8, 1.2, -3.0), new Vec3(2.86, 2.4, -2.94), frame);
        scene.EndGroup();

        Quad(scene, "TV Screen", Find("Neon Blue"),
            new Vec3(-3.98, 1.0, 1.0), new Vec3(-3.98, 1.0, -0.6), new Vec3(-3.98, 1.8, -0.6), new Vec3(-3.98, 1.8, 1.0));

        scene.BeginGroup("Side Table");
        Material marble = Textured(new Material(new Vec3(1, 1, 1), roughness: 1.0), "White Marble", 1.0, 1.0);
        scene.Box(new Vec3(-3.1, 0.5, 1.5), new Vec3(-2.5, 0.55, 2.1), marble);
        scene.Box(new Vec3(-2.85, 0.0, 1.75), new Vec3(-2.75, 0.5, 1.85), marble);
        scene.EndGroup();
        Sphere(scene, "Plant Pot", Find("Terracotta"), new Vec3(-2.8, 0.66, 1.8), 0.11, 16, 10);
        Sphere(scene, "Plant", Find("Leaves"), new Vec3(-2.8, 0.88, 1.8), 0.16, 16, 10);

        Box(scene, "Storage Box", Find("Kraft Cardboard"),
            new Vec3(2.9, 0.0, 1.2), new Vec3(3.4, 0.5, 1.7));

        scene.Lights.Add(new SceneLight("key", new Vec3(2.0, 2.5, 0.5), new Vec3(1.0, 0.95, 0.88), 6.0));
        scene.Lights.Add(new SceneLight("fill", new Vec3(-2.0, 2.0, 2.0), new Vec3(0.8, 0.85, 1.0), 2.5));
        scene.RebuildWorldGeometry();
        return scene;
    }

    private static Material Find(string presetName) =>
        MaterialPresetLibrary.Common.FirstOrDefault(p => p.Name == presetName)?.Material
            ?? throw new InvalidOperationException("Showcase preset not found: " + presetName);

    private static DetailEffectPreset DetailEffect(string name) =>
        DetailEffectPresets.All.FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException("Showcase effect preset not found: " + name);

    private static GlassPreset Glass(string name) =>
        GlassPresets.All.FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException("Showcase glass preset not found: " + name);

    /// <summary>Builds a material from a base with scalar-preset tweaks applied.</summary>
    private static Material Composed(Material baseMaterial, Func<ComposerMaterialProperties, ComposerMaterialProperties> tweak)
    {
        ComposerMaterialProperties properties = PropsFrom(baseMaterial);
        return ComposerSceneSession.MaterialPropertiesFunc(tweak(properties))(baseMaterial);
    }

    /// <summary>Adds a tiled library-texture triplet to the given material.</summary>
    private static Material Textured(Material baseMaterial, string texturePresetName, double scaleU, double scaleV)
    {
        TexturePreset preset = TexturePresetLibrary.All.FirstOrDefault(p => p.Name == texturePresetName)
            ?? throw new InvalidOperationException("Showcase texture not found: " + texturePresetName);
        Material material = baseMaterial.WithTexture(MaterialTextureSlot.BaseColor, LoadMap(preset.AlbedoFile, scaleU, scaleV));
        if (preset.NormalFile != null)
            material = material.WithTexture(MaterialTextureSlot.Normal, LoadMap(preset.NormalFile, scaleU, scaleV));
        if (preset.RoughnessFile != null)
            material = material.WithTexture(MaterialTextureSlot.MetallicRoughness, LoadMap(preset.RoughnessFile, scaleU, scaleV));
        return material;
    }

    private static TextureMap LoadMap(string relativePath, double scaleU, double scaleV)
    {
        string? path = TexturePresetLibrary.ResolvePath(relativePath)
            ?? throw new InvalidOperationException("Showcase texture not found: " + relativePath);
        return TextureMap.FromFile(path)
            .WithAddressing(TextureAddressMode.Repeat, TextureAddressMode.Repeat)
            .WithTextureTransform(0.0, 0.0, scaleU, scaleV, 0.0);
    }

    private static ComposerMaterialProperties PropsFrom(Material material) => new(
        Metallic: material.Metallic,
        Roughness: material.Roughness,
        Transmission: material.Transmission,
        Alpha: material.Alpha,
        Emission: material.Emission,
        EmissionColor: material.EmissionColor,
        AlphaMode: material.AlphaMode,
        AlphaCutoff: material.AlphaCutoff,
        DoubleSided: material.DoubleSided,
        Ior: material.Ior,
        Thickness: material.Thickness,
        AttenuationColor: material.AttenuationColor,
        AttenuationDistance: material.AttenuationDistance,
        Clearcoat: material.Clearcoat,
        ClearcoatRoughness: material.ClearcoatRoughness,
        NormalScale: material.NormalScale,
        OcclusionStrength: material.OcclusionStrength,
        Specular: material.Specular,
        SpecularColor: material.SpecularColor,
        SheenColor: material.SheenColor,
        SheenRoughness: material.SheenRoughness,
        Anisotropy: material.Anisotropy,
        AnisotropyRotation: material.AnisotropyRotation,
        Iridescence: material.Iridescence,
        IridescenceIor: material.IridescenceIor,
        IridescenceThicknessMinimum: material.IridescenceThicknessMinimum,
        IridescenceThicknessMaximum: material.IridescenceThicknessMaximum);

    private static void Box(Scene scene, string group, Material material, Vec3 min, Vec3 max)
    {
        scene.BeginGroup(group);
        scene.Box(min, max, material);
        scene.EndGroup();
    }

    private static void Quad(Scene scene, string group, Material material, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        scene.BeginGroup(group);
        scene.Quad(a, b, c, d, material);
        scene.EndGroup();
    }

    private static void Sphere(Scene scene, string group, Material material, Vec3 center, double radius, int longitudeSegments, int latitudeSegments)
    {
        scene.BeginGroup(group);
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            double theta0 = Math.PI * lat / latitudeSegments;
            double theta1 = Math.PI * (lat + 1) / latitudeSegments;
            double v0 = (double)lat / latitudeSegments;
            double v1 = (double)(lat + 1) / latitudeSegments;
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                double phi0 = 2.0 * Math.PI * lon / longitudeSegments;
                double phi1 = 2.0 * Math.PI * (lon + 1) / longitudeSegments;
                double u0 = (double)lon / longitudeSegments;
                double u1 = (double)(lon + 1) / longitudeSegments;
                Vec3 p00 = SpherePoint(center, radius, theta0, phi0);
                Vec3 p01 = SpherePoint(center, radius, theta0, phi1);
                Vec3 p10 = SpherePoint(center, radius, theta1, phi0);
                Vec3 p11 = SpherePoint(center, radius, theta1, phi1);
                Vec2 t00 = new(u0, v0);
                Vec2 t01 = new(u1, v0);
                Vec2 t10 = new(u0, v1);
                Vec2 t11 = new(u1, v1);
                if (lat == 0)
                    scene.AddTriangle(p00, p11, p10, t00, t11, t10, material);
                else if (lat == latitudeSegments - 1)
                    scene.AddTriangle(p00, p01, p10, t00, t01, t10, material);
                else
                {
                    scene.AddTriangle(p00, p01, p11, t00, t01, t11, material);
                    scene.AddTriangle(p00, p11, p10, t00, t11, t10, material);
                }
            }
        }
        scene.EndGroup();
    }

    private static Vec3 SpherePoint(Vec3 center, double radius, double theta, double phi)
    {
        double sinTheta = Math.Sin(theta);
        return center + new Vec3(radius * sinTheta * Math.Cos(phi), radius * Math.Cos(theta), radius * sinTheta * Math.Sin(phi));
    }
}
