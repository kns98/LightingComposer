using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class CornellTopDumpTests
{
    private static string FindRepoFile(params string[] parts)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());
    }

    private static string V(Vec3 v) => $"({v.X:F4},{v.Y:F4},{v.Z:F4})";

    [Fact]
    public void DumpLightAScanline()
    {
        DumpTopHalves("cornell-lightA-clean.png");
        DumpTopHalves("cornell-lightC.png");
    }

    private static void DumpTopHalves(string file)
    {
        string path = FindRepoFile(".build-tmp", file);
        TextureMap image = TextureMap.FromFile(path);
        double left = 0.0;
        double right = 0.0;
        int n = 0;
        for (int y = 190; y <= 230; y += 4)
        {
            for (int x = 150; x <= 230; x += 4)
            {
                Vec3 c = image.Sample((x + 0.5) / image.Width, (y + 0.5) / image.Height);
                double lum = (c.X + c.Y + c.Z) / 3.0;
                if (x < 190)
                    left += lum;
                else
                    right += lum;
                n++;
            }
        }
        Console.WriteLine($"{file} left={left / (n / 2.0):F4} right={right / (n / 2.0):F4}");
    }

    [Fact]
    public void DumpBoxTopTriangles()
    {
        string path = FindRepoFile("models", "CornellBox", "CornellBox.gltf");
        Scene scene = new();
        GltfSceneIO.LoadIntoScene(scene, path, new Material(new Vec3(0.82, 0.82, 0.78)));
        foreach (Triangle tri in scene.Triangles)
        {
            Vec3 centroid = (tri.A + tri.B + tri.C) / 3.0;
            Vec3 geometric = (tri.B - tri.A).Cross(tri.C - tri.A).Normalize();
            if (centroid.Y > -1.0 && centroid.Y < 0.1 && geometric.Y > 0.9)
            {
                Console.WriteLine($"tri A={V(tri.A)} B={V(tri.B)} C={V(tri.C)}");
                Console.WriteLine($"  geo={V(geometric)} NA={V(tri.NormalA)} NB={V(tri.NormalB)} NC={V(tri.NormalC)}");
            }
        }
    }
}
