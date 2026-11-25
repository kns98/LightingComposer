using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class ChristmasTreeReplayTests
{
    private static string FindRepoDir(string name)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(directory, name);
            if (Directory.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    private static int InsertSized(ComposerSceneSession session, string kind, Dictionary<string, double> parameters)
    {
        int id = session.InsertPrimitive(kind);
        Assert.True(session.BeginPrimitiveParameterEdit(id) != null, "Begin edit: " + kind);
        Assert.True(session.PreviewPrimitiveParameters(id, parameters), "Preview params: " + kind);
        session.CommitPrimitiveParameterEdit(id);
        return id;
    }

    private static Dictionary<string, double> Params(params (string Key, double Value)[] values)
    {
        Dictionary<string, double> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, double value) in values)
            result[key] = value;
        return result;
    }

    private static void Place(ComposerSceneSession session, int id, Vec3 center)
    {
        Aabb? bounds = session.GetTransformTargetBounds(id);
        Assert.NotNull(bounds);
        Aabb box = bounds!.Value;
        Vec3 offset = center - (box.Min + box.Max) * 0.5;
        Assert.True(session.UpdateTransformTarget(id, offset, Vec3.Zero, new Vec3(1, 1, 1)));
        Assert.True(session.CommitPendingTransform(id));
    }

    [Fact]
    public void ChristmasTree_ReplayBuildsAndRenders()
    {
        using ComposerSceneSession session = new();
        int lightsBefore = session.LightCount;

        int snow = InsertSized(session, "Plane", Params(("width", 14), ("depth", 14)));
        Place(session, snow, new Vec3(0, 0, 0));
        Assert.True(session.SetObjectBaseColor(snow, new Vec3(0.90, 0.92, 0.95)));
        int trunk = InsertSized(session, "Cylinder", Params(("radius", 0.18), ("height", 1.0), ("sides", 12), ("capFill", 1)));
        Place(session, trunk, new Vec3(0, 0.5, 0));
        Assert.True(session.SetObjectBaseColor(trunk, new Vec3(0.35, 0.22, 0.12)));

        Vec3 leaf = new(0.16, 0.42, 0.13);
        (double Y, double R, double H)[] cones = [(1.9, 2.0, 1.8), (3.0, 1.45, 1.5), (3.95, 0.9, 1.2)];
        foreach ((double y, double r, double h) in cones)
        {
            int cone = InsertSized(session, "Cone", Params(("radius1", r), ("radius2", 0), ("height", h), ("sides", 24), ("capFill", 1)));
            Place(session, cone, new Vec3(0, y, 0));
            Assert.True(session.SetObjectBaseColor(cone, leaf));
        }

        int topper = InsertSized(session, "UV Sphere", Params(("radius", 0.16), ("longitudeSegments", 16), ("latitudeSegments", 12)));
        Place(session, topper, new Vec3(0, 4.75, 0));
        Assert.True(session.SetObjectBaseColor(topper, new Vec3(0.85, 0.65, 0.20)));

        Vec3[] ornamentColors = [new Vec3(0.70, 0.08, 0.06), new Vec3(0.85, 0.65, 0.20), new Vec3(0.10, 0.25, 0.70)];
        (double X, double Y, double Z)[] ornaments =
        [
            (1.33, 1.6, 0), (-1.33, 1.6, 0), (0, 1.6, 1.33), (0, 1.6, -1.33),
            (0.72, 3.0, 0), (-0.72, 3.0, 0), (0.49, 3.9, 0)
        ];
        for (int i = 0; i < ornaments.Length; i++)
        {
            (double x, double y, double z) = ornaments[i];
            int ornament = InsertSized(session, "UV Sphere", Params(("radius", 0.14), ("longitudeSegments", 12), ("latitudeSegments", 8)));
            Place(session, ornament, new Vec3(x, y, z));
            Assert.True(session.SetObjectBaseColor(ornament, ornamentColors[i % ornamentColors.Length]));
        }

        (double X, double Y, double Z, double S, Vec3 Color)[] presents =
        [
            (1.9, 0.35, 1.2, 0.7, new Vec3(0.70, 0.08, 0.06)),
            (-1.7, 0.275, 0.8, 0.55, new Vec3(0.16, 0.42, 0.13)),
            (1.4, 0.225, -1.6, 0.45, new Vec3(0.10, 0.25, 0.70))
        ];
        foreach ((double x, double y, double z, double s, Vec3 color) in presents)
        {
            int present = InsertSized(session, "Cube", Params(("width", s), ("height", s), ("depth", s)));
            Place(session, present, new Vec3(x, y, z));
            Assert.True(session.SetObjectBaseColor(present, color));
        }

        Assert.Equal(16, session.ObjectCount);
        Assert.True(session.TriangleCount > 500);

        int keyIndex = session.AddLight(SceneLightKind.Point);
        ComposerLightModel? keyModel = session.GetLightInfo(keyIndex);
        Assert.NotNull(keyModel);
        Assert.True(session.UpdateLight(keyIndex, keyModel! with
        {
            Position = new Vec3(3.0, 5.0, -3.0),
            Color = new Vec3(1.0, 0.85, 0.60),
            Intensity = 6.0
        }));
        int fillIndex = session.AddLight(SceneLightKind.Directional);
        ComposerLightModel? fillModel = session.GetLightInfo(fillIndex);
        Assert.NotNull(fillModel);
        Assert.True(session.UpdateLight(fillIndex, fillModel! with
        {
            Direction = new Vec3(-0.4, -1.0, 0.3),
            Color = new Vec3(0.70, 0.80, 1.0),
            Intensity = 1.5
        }));
        Assert.Equal(lightsBefore + 2, session.LightCount);

        string scenePath = Path.Combine(Path.GetTempPath(), "christmas-tree-replay.lscene");
        session.Save(scenePath, CancellationToken.None);
        Assert.True(File.Exists(scenePath));

        session.SetSelectedObject(null);
        CameraDefinition camera = new() { Position = new Vec3(2.17, 3.51, -6.52), Target = new Vec3(0, 2.2, 0) };
        string screenshots = FindRepoDir("screenshots");
        (ComposerRendererKind Kind, string File)[] backends =
        [
            (ComposerRendererKind.Raster, "ChristmasTree-ui-raster.png"),
            (ComposerRendererKind.VulkanRaster, "ChristmasTree-ui-vraster.png"),
            (ComposerRendererKind.VulkanCompute, "ChristmasTree-ui-vulkan.png"),
            (ComposerRendererKind.Cpu, "ChristmasTree-ui-cpu.png")
        ];
        foreach ((ComposerRendererKind kind, string file) in backends)
        {
            ComposerFrame frame = session.Render(kind, camera, 480, 360, interactive: false, CancellationToken.None);
            string output = Path.Combine(screenshots, file);
            PngWriter.Write(output, frame.Image);
            Assert.True(new FileInfo(output).Length > 4096, file);
        }
    }
}
