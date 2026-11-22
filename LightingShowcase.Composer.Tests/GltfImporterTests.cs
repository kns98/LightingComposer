using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class GltfImporterTests
{
    private static string TrianglePositions()
    {
        float[] verts = { -1f, -1f, 4f, 1f, -1f, 4f, 1f, 1f, 4f };
        byte[] bytes = new byte[verts.Length * 4];
        Buffer.BlockCopy(verts, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static string WriteFixture(string directory, string fileName, string materialsJson, string primitiveExtra = "")
    {
        string json = $$"""
            {
              "asset": { "version": "2.0" },
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0 }],
              "meshes": [{ "primitives": [{
                "attributes": { "POSITION": 0 },
                "material": 0{{primitiveExtra}}
              }] }],
              "materials": [{{materialsJson}}],
              "extensions": { "KHR_materials_variants": { "variants": [{ "name": "alt" }] } },
              "accessors": [{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                "min": [-1.0, -1.0, 4.0], "max": [1.0, 1.0, 4.0] }],
              "bufferViews": [{ "buffer": 0, "byteLength": 36 }],
              "buffers": [{ "byteLength": 36, "uri": "data:application/octet-stream;base64,{{TrianglePositions()}}" }]
            }
            """;
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static Scene ImportFixture(string path, string? variant = null)
    {
        Scene scene = new();
        GltfSceneIO.LoadIntoScene(scene, path, new Material(new Vec3(0.82, 0.82, 0.78)), variant: variant);
        return scene;
    }

    private static byte[] FloatBytes(params float[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] UShortBytes(params ushort[] values)
    {
        byte[] bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] UIntBytes(params uint[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int length = 0;
        foreach (byte[] part in parts)
            length += part.Length;
        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            Buffer.BlockCopy(part, 0, bytes, offset, part.Length);
            offset += part.Length;
        }
        return bytes;
    }

    private static string WriteSparseFixture(string directory, string fileName, string attributesJson, string primitiveExtra, string accessorsJson, string bufferViewsJson, byte[] bufferBytes)
    {
        string json = $$"""
            {
              "asset": { "version": "2.0" },
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0 }],
              "meshes": [{ "primitives": [{
                "attributes": { {{attributesJson}} },
                "material": 0{{primitiveExtra}}
              }] }],
              "materials": [{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] } }],
              "accessors": [{{accessorsJson}}],
              "bufferViews": [{{bufferViewsJson}}],
              "buffers": [{ "byteLength": {{bufferBytes.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(bufferBytes)}}" }]
            }
            """;
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Import_EmissiveStrength_ScalesEmission()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-emissive-test").FullName;
        try
        {
            string path = WriteFixture(directory, "emissive.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 0, 0, 1] }, "emissiveFactor": [0.5, 0, 0], "extensions": { "KHR_materials_emissive_strength": { "emissiveStrength": 2.0 } } }""");
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(2.0, scene.Triangles[0].Material.Emission);
            Assert.Equal(new Vec3(0.5, 0, 0), scene.Triangles[0].Material.EmissionColor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_Variant_SelectsMappedMaterial()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-variant-test").FullName;
        try
        {
            string path = WriteFixture(directory, "variant.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 0, 0, 1] } }, { "pbrMetallicRoughness": { "baseColorFactor": [0, 0, 1, 1] } }""",
                """, "extensions": { "KHR_materials_variants": { "mappings": [{ "material": 1, "variants": [0] }] } }""");

            Scene @default = ImportFixture(path);
            Scene alt = ImportFixture(path, variant: "alt");

            Assert.Equal(new Vec3(1, 0, 0), @default.Triangles[0].Material.Color);
            Assert.Equal(new Vec3(0, 0, 1), alt.Triangles[0].Material.Color);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_UnknownVariant_Throws()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-variant-missing-test").FullName;
        try
        {
            string path = WriteFixture(directory, "variant.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 0, 0, 1] } }""");

            Assert.Throws<InvalidDataException>(() => ImportFixture(path, variant: "nope"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SparsePositions_AppliesOverrides()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sparse-test").FullName;
        try
        {
            byte[] buffer = Concat(
                FloatBytes(0f, 0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f),
                UShortBytes(2),
                new byte[2],
                FloatBytes(0f, 2f, 0f));
            string path = WriteSparseFixture(directory, "sparse.gltf",
                "\"POSITION\": 0",
                "",
                """{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [2, 2, 0], "sparse": { "count": 1, "indices": { "bufferView": 1, "componentType": 5123 }, "values": { "bufferView": 2 } } }""",
                """{ "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 2 }, { "buffer": 0, "byteOffset": 40, "byteLength": 12 }""",
                buffer);
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(-1.075, scene.Triangles[0].A.X, precision: 6);
            Assert.Equal(-1.48, scene.Triangles[0].A.Y, precision: 6);
            Assert.Equal(3.45, scene.Triangles[0].A.Z, precision: 6);
            Assert.Equal(1.075, scene.Triangles[0].B.X, precision: 6);
            Assert.Equal(-1.075, scene.Triangles[0].C.X, precision: 6);
            Assert.Equal(0.67, scene.Triangles[0].C.Y, precision: 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SparseOnlyPositions_UsesZeroBase()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sparse-nobase-test").FullName;
        try
        {
            byte[] buffer = Concat(
                new byte[] { 0, 1, 2 },
                new byte[1],
                FloatBytes(0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f));
            string path = WriteSparseFixture(directory, "sparse-nobase.gltf",
                "\"POSITION\": 0",
                "",
                """{ "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [2, 2, 0], "sparse": { "count": 3, "indices": { "bufferView": 0, "componentType": 5121 }, "values": { "bufferView": 1 } } }""",
                """{ "buffer": 0, "byteOffset": 0, "byteLength": 3 }, { "buffer": 0, "byteOffset": 4, "byteLength": 36 }""",
                buffer);
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(-1.075, scene.Triangles[0].A.X, precision: 6);
            Assert.Equal(1.075, scene.Triangles[0].B.X, precision: 6);
            Assert.Equal(0.67, scene.Triangles[0].C.Y, precision: 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SparseUvs_AppliesOverrides()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sparse-uv-test").FullName;
        try
        {
            byte[] buffer = Concat(
                FloatBytes(-1f, -1f, 4f, 1f, -1f, 4f, 1f, 1f, 4f),
                new byte[24],
                UIntBytes(0, 2),
                FloatBytes(0.25f, 0.5f, 0.75f, 1f));
            string path = WriteSparseFixture(directory, "sparse-uv.gltf",
                "\"POSITION\": 0, \"TEXCOORD_0\": 1",
                "",
                """{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [-1, -1, 4], "max": [1, 1, 4] }, { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC2", "sparse": { "count": 2, "indices": { "bufferView": 2, "componentType": 5125 }, "values": { "bufferView": 3 } } }""",
                """{ "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 24 }, { "buffer": 0, "byteOffset": 60, "byteLength": 8 }, { "buffer": 0, "byteOffset": 68, "byteLength": 16 }""",
                buffer);
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(0.25, scene.Triangles[0].UvA.U, precision: 9);
            Assert.Equal(0.5, scene.Triangles[0].UvA.V, precision: 9);
            Assert.Equal(0.0, scene.Triangles[0].UvB.U, precision: 9);
            Assert.Equal(0.0, scene.Triangles[0].UvB.V, precision: 9);
            Assert.Equal(0.75, scene.Triangles[0].UvC.U, precision: 9);
            Assert.Equal(1.0, scene.Triangles[0].UvC.V, precision: 9);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SparseIndices_AppliesOverrides()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sparse-index-test").FullName;
        try
        {
            byte[] buffer = Concat(
                FloatBytes(-1f, -1f, 4f, 1f, -1f, 4f, 1f, 1f, 4f),
                new byte[] { 0, 1, 2 },
                new byte[1],
                UShortBytes(2, 1, 0),
                new byte[2]);
            string path = WriteSparseFixture(directory, "sparse-index.gltf",
                "\"POSITION\": 0",
                """, "indices": 1""",
                """{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [-1, -1, 4], "max": [1, 1, 4] }, { "componentType": 5123, "count": 3, "type": "SCALAR", "sparse": { "count": 3, "indices": { "bufferView": 1, "componentType": 5121 }, "values": { "bufferView": 2 } } }""",
                """{ "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 3 }, { "buffer": 0, "byteOffset": 40, "byteLength": 6 }""",
                buffer);
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(1.075, scene.Triangles[0].A.X, precision: 6);
            Assert.Equal(0.67, scene.Triangles[0].A.Y, precision: 6);
            Assert.Equal(1.075, scene.Triangles[0].B.X, precision: 6);
            Assert.Equal(-1.48, scene.Triangles[0].B.Y, precision: 6);
            Assert.Equal(-1.075, scene.Triangles[0].C.X, precision: 6);
            Assert.Equal(-1.48, scene.Triangles[0].C.Y, precision: 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SpecularExtension_ReadsFactors()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-specular-test").FullName;
        try
        {
            string path = WriteFixture(directory, "specular.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] }, "extensions": { "KHR_materials_specular": { "specularFactor": 0.5, "specularColorFactor": [0.25, 0.5, 1.0] } } }""");
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(0.5, scene.Triangles[0].Material.Specular);
            Assert.Equal(new Vec3(0.25, 0.5, 1.0), scene.Triangles[0].Material.SpecularColor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SheenExtension_ReadsFactors()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sheen-test").FullName;
        try
        {
            string path = WriteFixture(directory, "sheen.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] }, "extensions": { "KHR_materials_sheen": { "sheenColorFactor": [0.9, 0.8, 0.7], "sheenRoughnessFactor": 0.5 } } }""");
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(new Vec3(0.9, 0.8, 0.7), scene.Triangles[0].Material.SheenColor);
            Assert.Equal(0.5, scene.Triangles[0].Material.SheenRoughness);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_AnisotropyExtension_ReadsFactors()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-aniso-test").FullName;
        try
        {
            string path = WriteFixture(directory, "aniso.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] }, "extensions": { "KHR_materials_anisotropy": { "anisotropyStrength": 0.6, "anisotropyRotation": 1.57 } } }""");
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(0.6, scene.Triangles[0].Material.Anisotropy);
            Assert.Equal(1.57, scene.Triangles[0].Material.AnisotropyRotation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_IridescenceExtension_ReadsFactors()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-irid-test").FullName;
        try
        {
            string path = WriteFixture(directory, "irid.gltf",
                """{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] }, "extensions": { "KHR_materials_iridescence": { "iridescenceFactor": 0.8, "iridescenceIor": 1.5, "iridescenceThicknessMinimum": 200.0, "iridescenceThicknessMaximum": 800.0 } } }""");
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            Assert.Equal(0.8, scene.Triangles[0].Material.Iridescence);
            Assert.Equal(1.5, scene.Triangles[0].Material.IridescenceIor);
            Assert.Equal(200.0, scene.Triangles[0].Material.IridescenceThicknessMinimum);
            Assert.Equal(800.0, scene.Triangles[0].Material.IridescenceThicknessMaximum);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_SpecularColorTexture_LoadsImage()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-specular-tex-test").FullName;
        try
        {
            PngWriter.WriteRgba(Path.Combine(directory, "spec.png"), 1, 1, new byte[] { 0, 255, 0, 255 });
            string json = $$"""
            {
              "asset": { "version": "2.0" },
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0 }],
              "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "material": 0 }] }],
              "materials": [{ "pbrMetallicRoughness": { "baseColorFactor": [1, 1, 1, 1] },
                "extensions": { "KHR_materials_specular": { "specularColorTexture": { "index": 0 } } } }],
              "textures": [{ "source": 0 }],
              "images": [{ "uri": "spec.png" }],
              "accessors": [{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                "min": [-1.0, -1.0, 4.0], "max": [1.0, 1.0, 4.0] }],
              "bufferViews": [{ "buffer": 0, "byteLength": 36 }],
              "buffers": [{ "byteLength": 36, "uri": "data:application/octet-stream;base64,{{TrianglePositions()}}" }]
            }
            """;
            string path = Path.Combine(directory, "spec-tex.gltf");
            File.WriteAllText(path, json);
            Scene scene = ImportFixture(path);

            Assert.Single(scene.Triangles);
            TextureMap? texture = scene.Triangles[0].Material.SpecularColorTexture;
            Assert.NotNull(texture);
            Assert.Equal(0.0, texture.Sample(0.5, 0.5).X, precision: 9);
            Assert.Equal(1.0, texture.Sample(0.5, 0.5).Y, precision: 9);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

    [Fact]
    public void Import_ToyCarFabric_HasSheenAndTransforms()
    {
        string path = FindRepoFile("models", "ToyCar", "ToyCar.gltf");
        Assert.True(File.Exists(path), "Showcase model missing: " + path);
        Scene scene = ImportFixture(path);

        Material? fabric = scene.Triangles.Select(triangle => triangle.Material)
            .FirstOrDefault(material => material.SheenColor.X > 0.5);
        Assert.NotNull(fabric);
        Assert.Equal(new Vec3(1, 0, 0), fabric.SheenColor);
        Assert.Equal(0.5, fabric.SheenRoughness);
        Assert.NotNull(fabric.Texture);
        Assert.Equal(3.0, fabric.Texture.ScaleU);
        Assert.Equal(3.0, fabric.Texture.ScaleV);
    }

    [Fact]
    public void Import_SparseIndexOutOfRange_Throws()
    {
        string directory = Directory.CreateTempSubdirectory("gltf-sparse-range-test").FullName;
        try
        {
            byte[] buffer = Concat(
                FloatBytes(0f, 0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f),
                UShortBytes(5),
                new byte[2],
                FloatBytes(0f, 2f, 0f));
            string path = WriteSparseFixture(directory, "sparse-range.gltf",
                "\"POSITION\": 0",
                "",
                """{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [2, 2, 0], "sparse": { "count": 1, "indices": { "bufferView": 1, "componentType": 5123 }, "values": { "bufferView": 2 } } }""",
                """{ "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 2 }, { "buffer": 0, "byteOffset": 40, "byteLength": 12 }""",
                buffer);

            Assert.Throws<InvalidDataException>(() => ImportFixture(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
