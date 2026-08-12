/*
 * Importing GLTF is a translation problem, not a file-copy operation. The code parses the external representation,
 * resolves indices/resources/transforms, and creates Composer triangles, object groups, materials, and textures in
 * the coordinate and ownership conventions expected by the scene layer.
 */
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Imports and exports glTF/GLB scenes, including KHR_lights_punctual lights.</summary>
public static class GltfSceneIO
{
    private const uint GlbMagic = 0x46546C67; // glTF
    private const uint JsonChunkType = 0x4E4F534A; // JSON
    private const uint BinChunkType = 0x004E4942; // BIN\0
    private static readonly JsonDocument EmptyArrayDocument = JsonDocument.Parse("[]");

    public static ObjLoadResult LoadIntoScene(
        Scene scene,
        string filePath,
        Material fallbackMaterial,
        double targetSize = 2.15,
        Vec3? targetCenter = null,
        double floorY = -1.48,
        Action<ObjLoadProgress>? progress = null,
        double? simplifyKeepFraction = null)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();
        Stopwatch phaseTimer = Stopwatch.StartNew();
        progress?.Invoke(new ObjLoadProgress("Reading glTF JSON", 5, 0, 0, 0));
        GltfDocument doc = ReadDocument(filePath);
        using JsonDocument json = JsonDocument.Parse(doc.JsonUtf8);
        JsonElement root = json.RootElement;
        long documentMilliseconds = phaseTimer.ElapsedMilliseconds;

        phaseTimer.Restart();
        progress?.Invoke(new ObjLoadProgress("Loading glTF buffers and materials", 12, 0, 0, 0));
        List<byte[]> buffers = LoadBuffers(root, doc, filePath);
        List<GltfMaterial> materials = ReadMaterials(root, buffers, filePath, fallbackMaterial);
        List<ImportedLight> lights = ReadLights(root);
        long resourceMilliseconds = phaseTimer.ElapsedMilliseconds;

        int vertexCount = 0;
        int faceCount = 0;
        int triangleCount = 0;
        int accessorBoundsCount = 0;
        int scannedBoundsCount = 0;
        int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneProp) && sceneProp.ValueKind == JsonValueKind.Number ? sceneProp.GetInt32() : 0;
        JsonElement scenes = GetArray(root, "scenes");
        JsonElement nodes = GetArray(root, "nodes");
        JsonElement meshesArray = GetArray(root, "meshes");

        // Compute bounds before building triangles so glTF imports get the same
        // fit-to-editor transform as OBJ/3DS imports.  Without this, many real
        // glTF samples stay in authoring units far from the default ray-trace
        // lights, making the render look unlit even when a realtime viewer adds built-in
        // directional lights still show the model.
        Vec3 boundsMin = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vec3 boundsMax = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        bool hasBounds = false;
        phaseTimer.Restart();
        progress?.Invoke(new ObjLoadProgress("Reading glTF bounds", 20, 0, 0, 0));
        TraverseSceneRoots(TraverseBounds);
        long boundsMilliseconds = phaseTimer.ElapsedMilliseconds;
        if (!hasBounds)
            throw new InvalidDataException("glTF file does not contain any mesh positions.");

        Vec3 boundsSize = boundsMax - boundsMin;
        double largestAxis = Math.Max(boundsSize.X, Math.Max(boundsSize.Y, boundsSize.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("glTF model bounds are degenerate.");

        double importScale = targetSize / largestAxis;
        Vec3 sourceCenter = (boundsMin + boundsMax) * 0.5;
        Vec3 desiredCenter = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (boundsMin.Y - sourceCenter.Y) * importScale + desiredCenter.Y;
        Vec3 importOffset = new(desiredCenter.X, desiredCenter.Y + (floorY - scaledMinY), desiredCenter.Z);

        Vec3 emissiveCenterSum = Vec3.Zero;
        Vec3 emissiveColorSum = Vec3.Zero;
        int emissiveTriangleCount = 0;
        double strongestEmission = 0.0;

        phaseTimer.Restart();
        progress?.Invoke(new ObjLoadProgress("Building glTF triangles", 35, 0, 0, 0));
        TraverseSceneRoots(TraverseNode);
        long geometryMilliseconds = phaseTimer.ElapsedMilliseconds;

        if (lights.Count == 0 && emissiveTriangleCount > 0)
        {
            // Some glTF samples, including common lantern assets, do not contain
            // KHR_lights_punctual lights.  They rely on emissiveTexture instead.
            // The ray tracer is not a global-illumination/path tracer, so a purely
            // emissive surface would look bright but would not illuminate nearby
            // geometry.  Add one editable helper point light at the emissive mesh
            // centroid to give the expected practical-lantern effect.
            scene.Lights.RemoveAll(l => l.Id.StartsWith("gltf_emissive_", StringComparison.OrdinalIgnoreCase));
            Vec3 lightPosition = emissiveCenterSum / emissiveTriangleCount;
            Vec3 lightColor = emissiveColorSum / emissiveTriangleCount;
            double intensity = Math.Max(2.0, strongestEmission * 5.0);
            scene.Lights.Add(new SceneLight("gltf_emissive_light", lightPosition, lightColor, intensity, true, SceneLightKind.Point, range: targetSize * 1.8, isImported: true));
        }

        if (lights.Count > 0)
        {
            scene.Lights.RemoveAll(l => l.Id.Equals("ceiling", StringComparison.OrdinalIgnoreCase) || l.Id.Equals("lamp", StringComparison.OrdinalIgnoreCase));
            foreach (ImportedLight light in lights)
                scene.Lights.Add(new SceneLight(
                    light.Id,
                    light.Position,
                    light.Color,
                    light.Intensity,
                    light.Enabled,
                    light.Kind,
                    light.Direction,
                    light.Range,
                    light.InnerConeAngle,
                    light.OuterConeAngle,
                    isImported: true));
        }
        else if (scene.Lights.Count == 0)
        {
            scene.Lights.Add(new SceneLight("gltf_default_key", new Vec3(2.5, 4.0, -3.0), new Vec3(1.0, 0.96, 0.88), 5.0, isDefault: true));
            scene.Lights.Add(new SceneLight("gltf_default_fill", new Vec3(-3.0, 2.2, 2.0), new Vec3(0.75, 0.85, 1.0), 2.2, isDefault: true));
        }

        if (simplifyKeepFraction.HasValue && simplifyKeepFraction.Value < 0.999)
        {
            progress?.Invoke(new ObjLoadProgress("Simplifying glTF mesh", 90, vertexCount, faceCount, triangleCount));
            foreach (SceneObjectGroup group in scene.ObjectGroups)
                group.SimplifyGeometry(simplifyKeepFraction.Value);
            triangleCount = scene.ObjectGroups.Sum(g => g.CountLocalTrianglesRecursively());
        }

        phaseTimer.Restart();
        progress?.Invoke(new ObjLoadProgress("Finalizing glTF scene", 94, vertexCount, faceCount, triangleCount));
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        long finalizeMilliseconds = phaseTimer.ElapsedMilliseconds;
        totalTimer.Stop();

        string details = FormattableString.Invariant(
            $"total={totalTimer.ElapsedMilliseconds}ms; json={documentMilliseconds}ms; resources={resourceMilliseconds}ms; bounds={boundsMilliseconds}ms; geometry={geometryMilliseconds}ms; finalize={finalizeMilliseconds}ms; accessorBounds={accessorBoundsCount}; scannedBounds={scannedBoundsCount}; bvh=deferred");
        progress?.Invoke(new ObjLoadProgress($"Finished glTF in {totalTimer.ElapsedMilliseconds:N0} ms", 100, vertexCount, faceCount, triangleCount));
        return new ObjLoadResult(filePath, vertexCount, faceCount, triangleCount, details);

        void TraverseSceneRoots(Action<int, Matrix4x4> visitor)
        {
            if (scenes.GetArrayLength() > 0 && sceneIndex >= 0 && sceneIndex < scenes.GetArrayLength() && scenes[sceneIndex].TryGetProperty("nodes", out JsonElement rootNodes))
            {
                foreach (JsonElement nodeIndexEl in rootNodes.EnumerateArray())
                    visitor(nodeIndexEl.GetInt32(), Matrix4x4.Identity);
            }
            else
            {
                for (int i = 0; i < nodes.GetArrayLength(); i++)
                    visitor(i, Matrix4x4.Identity);
            }
        }

        void TraverseBounds(int nodeIndex, Matrix4x4 parent)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
                return;

            JsonElement node = nodes[nodeIndex];
            Matrix4x4 world = GetNodeTransform(node) * parent;
            if (node.TryGetProperty("mesh", out JsonElement meshEl))
            {
                int meshIndex = meshEl.GetInt32();
                if (meshIndex >= 0 && meshIndex < meshesArray.GetArrayLength())
                    ExpandMeshBounds(meshesArray[meshIndex], world);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    TraverseBounds(child.GetInt32(), world);
            }
        }

        void ExpandMeshBounds(JsonElement mesh, Matrix4x4 world)
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
                return;

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeEl) ? modeEl.GetInt32() : 4;
                if (mode != 4 || !primitive.TryGetProperty("attributes", out JsonElement attributes) || !attributes.TryGetProperty("POSITION", out JsonElement posEl))
                    continue;

                int positionAccessorIndex = posEl.GetInt32();
                if (TryReadVec3AccessorBounds(root, positionAccessorIndex, out Vec3 accessorMin, out Vec3 accessorMax))
                {
                    accessorBoundsCount++;
                    ExpandTransformedBounds(accessorMin, accessorMax, world);
                }
                else
                {
                    scannedBoundsCount++;
                    foreach (Vec3 position in ReadVec3Accessor(root, buffers, positionAccessorIndex, world))
                        ExpandBounds(position);
                }
            }
        }

        void ExpandTransformedBounds(Vec3 min, Vec3 max, Matrix4x4 transform)
        {
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new((float)min.X, (float)min.Y, (float)min.Z),
                new((float)max.X, (float)min.Y, (float)min.Z),
                new((float)min.X, (float)max.Y, (float)min.Z),
                new((float)max.X, (float)max.Y, (float)min.Z),
                new((float)min.X, (float)min.Y, (float)max.Z),
                new((float)max.X, (float)min.Y, (float)max.Z),
                new((float)min.X, (float)max.Y, (float)max.Z),
                new((float)max.X, (float)max.Y, (float)max.Z)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 transformed = Vector3.Transform(corner, transform);
                ExpandBounds(new Vec3(transformed.X, transformed.Y, transformed.Z));
            }
        }

        void ExpandBounds(Vec3 position)
        {
            boundsMin = Min(boundsMin, position);
            boundsMax = Max(boundsMax, position);
            hasBounds = true;
        }

        Vec3 NormalizePosition(Vec3 position) => (position - sourceCenter) * importScale + importOffset;

        void TraverseNode(int nodeIndex, Matrix4x4 parent)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
                return;

            JsonElement node = nodes[nodeIndex];
            Matrix4x4 world = GetNodeTransform(node) * parent;
            string nodeName = node.TryGetProperty("name", out JsonElement nameEl) ? SanitizeName(nameEl.GetString(), $"gltf_node_{nodeIndex}") : $"gltf_node_{nodeIndex}";

            if (node.TryGetProperty("extensions", out JsonElement nodeExt) &&
                nodeExt.TryGetProperty("KHR_lights_punctual", out JsonElement lightRef) &&
                lightRef.TryGetProperty("light", out JsonElement lightIndexEl))
            {
                int lightIndex = lightIndexEl.GetInt32();
                if (lightIndex >= 0 && lightIndex < lights.Count)
                {
                    Vector3 transformed = Vector3.Transform(Vector3.Zero, world);
                    Vector3 direction = Vector3.TransformNormal(new Vector3(0.0f, 0.0f, -1.0f), world);
                    Vec3 normalizedDirection = new Vec3(direction.X, direction.Y, direction.Z).Normalize();
                    lights[lightIndex] = lights[lightIndex] with
                    {
                        Position = NormalizePosition(new Vec3(transformed.X, transformed.Y, transformed.Z)),
                        Direction = normalizedDirection.Length() < 1e-8 ? new Vec3(0.0, 0.0, -1.0) : normalizedDirection,
                        Range = lights[lightIndex].Range > 0.0 ? lights[lightIndex].Range * importScale : 0.0,
                        Id = nodeName
                    };
                }
            }

            if (node.TryGetProperty("mesh", out JsonElement meshEl))
            {
                int meshIndex = meshEl.GetInt32();
                if (meshIndex >= 0 && meshIndex < meshesArray.GetArrayLength())
                    ImportMesh(meshesArray[meshIndex], nodeName, world);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    TraverseNode(child.GetInt32(), world);
            }
        }

        // ImportMesh imports mesh by translating external geometry/resources into Composer conventions and
        // ownership structures.
        void ImportMesh(JsonElement mesh, string nodeName, Matrix4x4 world)
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
                return;

            int primitiveIndex = 0;
            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeEl) ? modeEl.GetInt32() : 4;
                if (mode != 4 || !primitive.TryGetProperty("attributes", out JsonElement attributes) || !attributes.TryGetProperty("POSITION", out JsonElement posEl))
                    continue;

                List<Vec3> positions = ReadNormalizedPositionAccessor(
                    root, buffers, posEl.GetInt32(), world, sourceCenter, importScale, importOffset);

                List<Vec3> normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessorEl)
                    ? ReadNormalAccessor(root, buffers, normalAccessorEl.GetInt32(), world)
                    : new List<Vec3>(0);
                int materialIndex = primitive.TryGetProperty("material", out JsonElement materialEl) ? materialEl.GetInt32() : -1;
                GltfMaterial? gltfMaterial = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : null;
                string uvAttributeName = $"TEXCOORD_{Math.Max(0, gltfMaterial?.BaseColorTexCoord ?? 0)}";
                bool hasUv = attributes.TryGetProperty(uvAttributeName, out JsonElement uvEl) ||
                    attributes.TryGetProperty("TEXCOORD_0", out uvEl);
                List<Vec2> uvs = hasUv
                    ? ReadVec2Accessor(root, buffers, uvEl.GetInt32())
                    : new List<Vec2>(0);
                List<Vec3> vertexColors = attributes.TryGetProperty("COLOR_0", out JsonElement colorAccessorEl)
                    ? ReadColorAccessor(root, buffers, colorAccessorEl.GetInt32())
                    : new List<Vec3>(0);
                List<int>? indices = primitive.TryGetProperty("indices", out JsonElement indicesEl)
                    ? ReadIndexAccessor(root, buffers, indicesEl.GetInt32())
                    : null;
                int indexCount = indices?.Count ?? positions.Count;

                Material material = gltfMaterial?.Material ?? fallbackMaterial;
                string groupName = primitiveIndex == 0 ? nodeName : $"{nodeName}_{primitiveIndex + 1}";
                SceneObjectGroup importedGroup = scene.BeginGroup(groupName);
                importedGroup.EnsureTriangleCapacity(indexCount / 3);
                for (int i = 0; i + 2 < indexCount; i += 3)
                {
                    int ia = indices?[i] ?? i;
                    int ib = indices?[i + 1] ?? i + 1;
                    int ic = indices?[i + 2] ?? i + 2;
                    if (!IsValidIndex(ia, positions.Count) || !IsValidIndex(ib, positions.Count) || !IsValidIndex(ic, positions.Count))
                        continue;

                    Vec2 uva = ia < uvs.Count ? uvs[ia] : new Vec2(0, 0);
                    Vec2 uvb = ib < uvs.Count ? uvs[ib] : new Vec2(1, 0);
                    Vec2 uvc = ic < uvs.Count ? uvs[ic] : new Vec2(0, 1);
                    Material triangleMaterial = material;
                    if (ia < vertexColors.Count && ib < vertexColors.Count && ic < vertexColors.Count)
                    {
                        Vec3 averageVertexColor = (vertexColors[ia] + vertexColors[ib] + vertexColors[ic]) / 3.0;
                        triangleMaterial = new Material(
                            material.Color.Multiply(averageVertexColor), material.Emission, material.LightId, material.Texture,
                            material.EmissionColor, material.EmissiveTexture, material.Alpha, material.AlphaBlend,
                            material.Metallic, material.Roughness, material.Transmission, material.MetallicRoughnessTexture,
                            material.NormalTexture, material.OcclusionTexture, material.NormalScale, material.OcclusionStrength,
                            material.AlphaMode, material.AlphaCutoff, material.DoubleSided, material.TransmissionTexture,
                            material.Ior, material.Thickness, material.AttenuationColor, material.AttenuationDistance,
                            material.Clearcoat, material.ClearcoatRoughness, material.ClearcoatUsesTransmissionTexture);
                    }
                    if (ia < normals.Count && ib < normals.Count && ic < normals.Count)
                    {
                        importedGroup.AddTriangle(
                            positions[ia], positions[ib], positions[ic],
                            uva, uvb, uvc,
                            normals[ia], normals[ib], normals[ic],
                            triangleMaterial);
                    }
                    else
                    {
                        importedGroup.AddTriangle(positions[ia], positions[ib], positions[ic], uva, uvb, uvc, triangleMaterial);
                    }
                    if (triangleMaterial.Emission > 0.0 && triangleMaterial.EmissiveTexture != null)
                    {
                        Vec3 centroid = (positions[ia] + positions[ib] + positions[ic]) / 3.0;
                        Vec3 emissiveSample = (triangleMaterial.SampleEmissionLinear(uva.U, uva.V) + triangleMaterial.SampleEmissionLinear(uvb.U, uvb.V) + triangleMaterial.SampleEmissionLinear(uvc.U, uvc.V)) / 3.0;
                        double luminance = 0.2126 * emissiveSample.X + 0.7152 * emissiveSample.Y + 0.0722 * emissiveSample.Z;
                        if (luminance > 0.02)
                        {
                            emissiveCenterSum += centroid;
                            emissiveColorSum += emissiveSample;
                            strongestEmission = Math.Max(strongestEmission, luminance);
                            emissiveTriangleCount++;
                        }
                    }
                    triangleCount++;
                    if ((triangleCount & 0x3FFF) == 0)
                    {
                        int inPrimitivePercent = indexCount <= 0 ? 35 : 35 + (int)Math.Min(50L, 50L * (i + 3L) / indexCount);
                        progress?.Invoke(new ObjLoadProgress("Building glTF triangles", inPrimitivePercent, vertexCount + positions.Count, faceCount + i / 3, triangleCount));
                    }
                }
                scene.EndGroup();
                vertexCount += positions.Count;
                faceCount += indexCount / 3;
                primitiveIndex++;
                int geometryPercent = Math.Min(88, 35 + Math.Max(1, triangleCount / 50000));
                progress?.Invoke(new ObjLoadProgress("Building glTF triangles", geometryPercent, vertexCount, faceCount, triangleCount));
            }
        }
    }

    public static void Save(Scene scene, string filePath, bool binary, SceneSaveOptions? options = null)
    {
        ExportBuild build = BuildExport(scene, options?.TexturePathResolver, options?.OptimizeGeometry ?? false);
        if (binary)
            WriteGlb(build, filePath, options?.BufferFileName);
        else
            WriteGltf(build, filePath, options?.BufferFileName);
    }

    private static ExportBuild BuildExport(Scene scene, Func<TextureMap, string?>? texturePathResolver, bool optimizeGeometry)
    {
        List<byte> bin = new();
        List<object> bufferViews = new();
        List<object> accessors = new();
        List<object> meshes = new();
        List<object> nodes = new();
        List<int> rootNodes = new();
        List<object> materials = new();
        Dictionary<Material, int> materialIds = new(ReferenceEqualityComparer.Instance);
        List<object> images = new();
        List<object> textureDefs = new();
        List<object> samplers = new();
        Dictionary<TextureMap, int> textureIds = new(ReferenceEqualityComparer.Instance);
        HashSet<string> materialExtensionsUsed = new(StringComparer.Ordinal);

        int TextureIndex(TextureMap? texture)
        {
            if (texture == null || texturePathResolver == null)
                return -1;
            if (textureIds.TryGetValue(texture, out int existing))
                return existing;

            string? uri = texturePathResolver(texture);
            if (string.IsNullOrWhiteSpace(uri))
                return -1;

            int samplerIndex = samplers.Count;
            samplers.Add(new Dictionary<string, object?>
            {
                ["wrapS"] = ToGltfWrap(texture.WrapU),
                ["wrapT"] = ToGltfWrap(texture.WrapV)
            });
            int imageIndex = images.Count;
            images.Add(new Dictionary<string, object?>
            {
                ["name"] = texture.Name,
                ["uri"] = uri.Replace('\\', '/')
            });
            int textureIndex = textureDefs.Count;
            textureDefs.Add(new Dictionary<string, object?>
            {
                ["name"] = texture.Name,
                ["sampler"] = samplerIndex,
                ["source"] = imageIndex
            });
            textureIds[texture] = textureIndex;
            return textureIndex;
        }

        int GetMaterialId(Material material)
        {
            if (materialIds.TryGetValue(material, out int existingMaterialId))
                return existingMaterialId;

            int materialId = materials.Count;
            materialIds[material] = materialId;

            Dictionary<string, object?> pbr = new()
            {
                ["baseColorFactor"] = new[] { Clamp01(material.Color.X), Clamp01(material.Color.Y), Clamp01(material.Color.Z), Clamp01(material.Alpha) },
                ["metallicFactor"] = Clamp01(material.Metallic),
                ["roughnessFactor"] = Clamp01(material.Roughness)
            };
            int baseTexture = TextureIndex(material.Texture);
            if (baseTexture >= 0)
                pbr["baseColorTexture"] = new Dictionary<string, object?> { ["index"] = baseTexture };
            int metallicRoughnessTexture = TextureIndex(material.MetallicRoughnessTexture);
            if (metallicRoughnessTexture >= 0)
                pbr["metallicRoughnessTexture"] = new Dictionary<string, object?> { ["index"] = metallicRoughnessTexture };

            Dictionary<string, object?> materialDef = new()
            {
                ["name"] = $"mat_{materialId + 1}",
                ["pbrMetallicRoughness"] = pbr,
                ["emissiveFactor"] = material.Emission > 0.0
                    ? new[] { Clamp01(material.EmissionColor.X * material.Emission), Clamp01(material.EmissionColor.Y * material.Emission), Clamp01(material.EmissionColor.Z * material.Emission) }
                    : new[] { 0.0, 0.0, 0.0 },
                ["alphaMode"] = material.AlphaMode switch
                {
                    MaterialAlphaMode.Mask => "MASK",
                    MaterialAlphaMode.Blend => "BLEND",
                    _ => "OPAQUE"
                },
                ["doubleSided"] = material.DoubleSided
            };
            if (material.AlphaMode == MaterialAlphaMode.Mask)
                materialDef["alphaCutoff"] = material.AlphaCutoff;
            int normalTexture = TextureIndex(material.NormalTexture);
            if (normalTexture >= 0)
                materialDef["normalTexture"] = new Dictionary<string, object?> { ["index"] = normalTexture, ["scale"] = material.NormalScale };
            int occlusionTexture = TextureIndex(material.OcclusionTexture);
            if (occlusionTexture >= 0)
                materialDef["occlusionTexture"] = new Dictionary<string, object?> { ["index"] = occlusionTexture, ["strength"] = material.OcclusionStrength };
            int emissiveTexture = TextureIndex(material.EmissiveTexture);
            if (emissiveTexture >= 0)
                materialDef["emissiveTexture"] = new Dictionary<string, object?> { ["index"] = emissiveTexture };

            Dictionary<string, object?> materialExtensions = new();
            int transmissionTexture = TextureIndex(material.TransmissionTexture);
            if (material.Transmission > 0.0 || transmissionTexture >= 0)
            {
                Dictionary<string, object?> transmission = new()
                {
                    ["transmissionFactor"] = Clamp01(material.Transmission)
                };
                if (transmissionTexture >= 0)
                    transmission["transmissionTexture"] = new Dictionary<string, object?> { ["index"] = transmissionTexture };
                materialExtensions["KHR_materials_transmission"] = transmission;
                materialExtensionsUsed.Add("KHR_materials_transmission");
            }
            if (Math.Abs(material.Ior - 1.5) > 1e-9)
            {
                materialExtensions["KHR_materials_ior"] = new Dictionary<string, object?> { ["ior"] = material.Ior };
                materialExtensionsUsed.Add("KHR_materials_ior");
            }
            if (material.Thickness > 0.0 || material.AttenuationDistance > 0.0)
            {
                Dictionary<string, object?> volume = new()
                {
                    ["thicknessFactor"] = material.Thickness,
                    ["attenuationColor"] = new[] { Clamp01(material.AttenuationColor.X), Clamp01(material.AttenuationColor.Y), Clamp01(material.AttenuationColor.Z) }
                };
                if (material.AttenuationDistance > 0.0)
                    volume["attenuationDistance"] = material.AttenuationDistance;
                materialExtensions["KHR_materials_volume"] = volume;
                materialExtensionsUsed.Add("KHR_materials_volume");
            }
            if (material.Clearcoat > 0.0)
            {
                Dictionary<string, object?> clearcoat = new()
                {
                    ["clearcoatFactor"] = Clamp01(material.Clearcoat),
                    ["clearcoatRoughnessFactor"] = Clamp01(material.ClearcoatRoughness)
                };
                if (material.ClearcoatUsesTransmissionTexture && transmissionTexture >= 0)
                    clearcoat["clearcoatTexture"] = new Dictionary<string, object?> { ["index"] = transmissionTexture };
                materialExtensions["KHR_materials_clearcoat"] = clearcoat;
                materialExtensionsUsed.Add("KHR_materials_clearcoat");
            }
            if (materialExtensions.Count > 0)
                materialDef["extensions"] = materialExtensions;

            materials.Add(materialDef);
            return materialId;
        }

        Dictionary<string, object?> BuildPrimitive(Material material, List<Triangle> materialTris)
        {
            Dictionary<ExportVertex, uint> vertexIds = new();
            List<float> positions = new(materialTris.Count * 6);
            List<float> normals = new(materialTris.Count * 6);
            List<float> texcoords = new(materialTris.Count * 4);
            List<uint> indices = new(materialTris.Count * 3);
            Vec3 min = materialTris[0].A;
            Vec3 max = materialTris[0].A;

            foreach (Triangle tri in materialTris)
            {
                AddVertex(tri.A, tri.UvA, tri.NormalA);
                AddVertex(tri.B, tri.UvB, tri.NormalB);
                AddVertex(tri.C, tri.UvC, tri.NormalC);
            }

            int posAccessor = AddFloatAccessor(bin, bufferViews, accessors, positions.ToArray(), "VEC3", min, max);
            int normalAccessor = AddFloatAccessor(bin, bufferViews, accessors, normals.ToArray(), "VEC3", null, null);
            int uvAccessor = AddFloatAccessor(bin, bufferViews, accessors, texcoords.ToArray(), "VEC2", null, null);
            int indexAccessor = AddIndexAccessor(bin, bufferViews, accessors, indices, vertexIds.Count);
            return new Dictionary<string, object?>
            {
                ["attributes"] = new Dictionary<string, object?>
                {
                    ["POSITION"] = posAccessor,
                    ["NORMAL"] = normalAccessor,
                    ["TEXCOORD_0"] = uvAccessor
                },
                ["indices"] = indexAccessor,
                ["material"] = GetMaterialId(material),
                ["mode"] = 4
            };

            void AddVertex(Vec3 p, Vec2 uv, Vec3 normal)
            {
                ExportVertex key = new(
                    (float)p.X, (float)p.Y, (float)p.Z,
                    (float)normal.X, (float)normal.Y, (float)normal.Z,
                    (float)uv.U, (float)uv.V);
                if (!vertexIds.TryGetValue(key, out uint index))
                {
                    index = checked((uint)vertexIds.Count);
                    vertexIds.Add(key, index);
                    positions.Add(key.Px);
                    positions.Add(key.Py);
                    positions.Add(key.Pz);
                    normals.Add(key.Nx);
                    normals.Add(key.Ny);
                    normals.Add(key.Nz);
                    texcoords.Add(key.U);
                    texcoords.Add(key.V);
                    min = Min(min, p);
                    max = Max(max, p);
                }
                indices.Add(index);
            }
        }

        List<(string Name, List<Triangle> Triangles)> geometrySets = new();
        if (optimizeGeometry)
        {
            List<Triangle> combined = scene.ObjectGroups
                .Where(group => group.Visible)
                .SelectMany(group => group.BuildWorldTriangles())
                .ToList();
            if (combined.Count > 0)
            {
                string optimizedName = string.IsNullOrWhiteSpace(scene.Description)
                    ? "Optimized scene"
                    : scene.Description;
                geometrySets.Add((optimizedName, combined));
            }
        }
        else
        {
            foreach (SceneObjectGroup group in scene.ObjectGroups)
            {
                if (!group.Visible)
                    continue;
                List<Triangle> groupTriangles = group.BuildWorldTriangles().ToList();
                if (groupTriangles.Count > 0)
                    geometrySets.Add((group.Name, groupTriangles));
            }
        }

        foreach ((string geometryName, List<Triangle> tris) in geometrySets)
        {
            Dictionary<Material, List<Triangle>> byMaterial = new(ReferenceEqualityComparer.Instance);
            foreach (Triangle triangle in tris)
            {
                if (!byMaterial.TryGetValue(triangle.Material, out List<Triangle>? materialTriangles))
                {
                    materialTriangles = new List<Triangle>();
                    byMaterial.Add(triangle.Material, materialTriangles);
                }
                materialTriangles.Add(triangle);
            }

            List<object> primitives = new(byMaterial.Count);
            foreach (KeyValuePair<Material, List<Triangle>> entry in byMaterial)
                primitives.Add(BuildPrimitive(entry.Key, entry.Value));

            int meshIndex = meshes.Count;
            meshes.Add(new Dictionary<string, object?> { ["name"] = geometryName, ["primitives"] = primitives });
            int nodeIndex = nodes.Count;
            nodes.Add(new Dictionary<string, object?> { ["name"] = geometryName, ["mesh"] = meshIndex });
            rootNodes.Add(nodeIndex);
        }

        List<object> lightDefs = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;

            int lightIndex = lightDefs.Count;
            Dictionary<string, object?> lightDef = new()
            {
                ["name"] = light.Id,
                ["type"] = LightTypeName(light.Kind),
                ["color"] = new[] { Clamp01(light.Color.X), Clamp01(light.Color.Y), Clamp01(light.Color.Z) },
                ["intensity"] = Math.Max(0.0, light.Intensity)
            };
            if (light.Range > 0.0 && light.Kind != SceneLightKind.Directional)
                lightDef["range"] = light.Range;
            if (light.Kind == SceneLightKind.Spot)
            {
                lightDef["spot"] = new Dictionary<string, object?>
                {
                    ["innerConeAngle"] = Math.Max(0.0, light.InnerConeAngle),
                    ["outerConeAngle"] = Math.Max(light.InnerConeAngle, light.OuterConeAngle)
                };
            }
            lightDefs.Add(lightDef);

            int nodeIndex = nodes.Count;
            Dictionary<string, object?> node = new()
            {
                ["name"] = light.Id,
                ["extensions"] = new Dictionary<string, object?>
                {
                    ["KHR_lights_punctual"] = new Dictionary<string, object?> { ["light"] = lightIndex }
                }
            };
            if (light.Kind != SceneLightKind.Directional)
                node["translation"] = new[] { light.Position.X, light.Position.Y, light.Position.Z };
            double[]? rotation = RotationFromMinusZ(light.Direction);
            if (rotation != null)
                node["rotation"] = rotation;
            nodes.Add(node);
            rootNodes.Add(nodeIndex);
        }

        Dictionary<string, object?> root = new()
        {
            ["asset"] = new Dictionary<string, object?> { ["version"] = "2.0", ["generator"] = "LightingShowcase" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object?> { ["name"] = scene.Description, ["nodes"] = rootNodes } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["buffers"] = new[] { new Dictionary<string, object?> { ["byteLength"] = bin.Count } },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["extensionsUsed"] = materialExtensionsUsed
                .Concat(lightDefs.Count > 0 ? new[] { "KHR_lights_punctual" } : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ["extensions"] = lightDefs.Count > 0
                ? new Dictionary<string, object?> { ["KHR_lights_punctual"] = new Dictionary<string, object?> { ["lights"] = lightDefs } }
                : null
        };
        if (images.Count > 0)
        {
            root["images"] = images;
            root["textures"] = textureDefs;
            root["samplers"] = samplers;
        }
        return new ExportBuild(root, bin.ToArray(), optimizeGeometry);
    }

    // WriteGltf writes gltf to the external stream/document in the format’s required order, using stable
    // indices/references so another reader can reconstruct the same relationships. Serializer-specific handling
    // stays at this boundary rather than leaking into the live scene model.
    private static void WriteGltf(ExportBuild build, string filePath, string? bufferFileName)
    {
        string binName = string.IsNullOrWhiteSpace(bufferFileName)
            ? Path.GetFileNameWithoutExtension(filePath) + ".bin"
            : Path.GetFileName(bufferFileName);
        byte[] bin = build.Bin;
        Dictionary<string, object?> root = new(build.Root)
        {
            ["buffers"] = new[] { new Dictionary<string, object?> { ["byteLength"] = bin.Length, ["uri"] = binName } }
        };
        string json = JsonSerializer.Serialize(root, JsonOptions(build.CompactJson));
        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, binName), bin);
    }

    // WriteGlb writes glb to the external stream/document in the format’s required order, using stable
    // indices/references so another reader can reconstruct the same relationships. Binary field order is explicit;
    // changing it requires the corresponding reader/writer to remain symmetrical. Serializer-specific handling
    // stays at this boundary rather than leaking into the live scene model.
    private static void WriteGlb(ExportBuild build, string filePath, string? bufferFileName)
    {
        bool useExternalBuffer = !string.IsNullOrWhiteSpace(bufferFileName);
        string? binName = useExternalBuffer ? Path.GetFileName(bufferFileName) : null;
        Dictionary<string, object?> root = useExternalBuffer
            ? new Dictionary<string, object?>(build.Root)
            {
                ["buffers"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["byteLength"] = build.Bin.Length,
                        ["uri"] = binName
                    }
                }
            }
            : build.Root;

        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root, JsonOptions(build.CompactJson)));
        jsonBytes = Pad(jsonBytes, 0x20);
        byte[] binBytes = useExternalBuffer ? Array.Empty<byte>() : Pad(build.Bin, 0x00);
        uint length = (uint)(12 + 8 + jsonBytes.Length + (binBytes.Length > 0 ? 8 + binBytes.Length : 0));
        using BinaryWriter writer = new(File.Create(filePath), Encoding.UTF8);
        writer.Write(GlbMagic);
        writer.Write((uint)2);
        writer.Write(length);
        writer.Write((uint)jsonBytes.Length);
        writer.Write(JsonChunkType);
        writer.Write(jsonBytes);
        if (binBytes.Length > 0)
        {
            writer.Write((uint)binBytes.Length);
            writer.Write(BinChunkType);
            writer.Write(binBytes);
        }

        if (useExternalBuffer)
        {
            string outputDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
            File.WriteAllBytes(Path.Combine(outputDirectory, binName!), build.Bin);
        }
    }

    // ReadDocument reads document from the external stream/document, advancing through the format in the order
    // required to resolve references and produce valid internal data. Binary field order is explicit; changing it
    // requires the corresponding reader/writer to remain symmetrical.
    private static GltfDocument ReadDocument(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".gltf")
            return new GltfDocument(File.ReadAllBytes(filePath), null);

        using BinaryReader reader = new(File.OpenRead(filePath), Encoding.UTF8);
        uint magic = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint length = reader.ReadUInt32();
        if (magic != GlbMagic || version != 2 || length > reader.BaseStream.Length)
            throw new InvalidDataException("Invalid GLB header.");

        byte[]? jsonUtf8 = null;
        byte[]? bin = null;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int chunkLength = checked((int)reader.ReadUInt32());
            uint chunkType = reader.ReadUInt32();
            byte[] chunk = reader.ReadBytes(chunkLength);
            if (chunk.Length != chunkLength)
                throw new EndOfStreamException("Unexpected end of GLB chunk.");
            if (chunkType == JsonChunkType)
                jsonUtf8 = TrimJsonPadding(chunk);
            else if (chunkType == BinChunkType)
                bin = chunk;
        }
        return new GltfDocument(jsonUtf8 ?? throw new InvalidDataException("GLB JSON chunk missing."), bin);
    }

    private static byte[] TrimJsonPadding(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] is 0 or 0x20 or 0x09 or 0x0A or 0x0D)
            length--;
        if (length == bytes.Length)
            return bytes;
        byte[] result = new byte[length];
        Buffer.BlockCopy(bytes, 0, result, 0, length);
        return result;
    }

    private static List<byte[]> LoadBuffers(JsonElement root, GltfDocument doc, string filePath)
    {
        List<byte[]> result = new();
        JsonElement buffers = GetArray(root, "buffers");
        string baseDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
        for (int i = 0; i < buffers.GetArrayLength(); i++)
        {
            JsonElement buffer = buffers[i];
            if (i == 0 && doc.BinaryChunk != null && !buffer.TryGetProperty("uri", out _))
            {
                result.Add(doc.BinaryChunk);
                continue;
            }

            string? uri = buffer.TryGetProperty("uri", out JsonElement uriEl) ? uriEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(uri))
                throw new InvalidDataException("External glTF buffer URI is missing.");
            result.Add(ReadUriBytes(uri, baseDirectory));
        }
        return result;
    }

    private static byte[] ReadUriBytes(string uri, string baseDirectory)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0) throw new InvalidDataException("Invalid data URI.");
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        return File.ReadAllBytes(Path.Combine(baseDirectory, Uri.UnescapeDataString(uri)));
    }

    // ReadMaterials reads materials from the external stream/document, advancing through the format in the order
    // required to resolve references and produce valid internal data.
    private static List<GltfMaterial> ReadMaterials(JsonElement root, List<byte[]> buffers, string sceneFilePath, Material fallback)
    {
        List<GltfMaterial> result = new();
        JsonElement materials = GetArray(root, "materials");
        Dictionary<int, TextureMap?> textureCache = new();
        for (int i = 0; i < materials.GetArrayLength(); i++)
        {
            Vec3 color = fallback.Color;
            double emission = fallback.Emission;
            Vec3 emissionColor = new(1.0, 1.0, 1.0);
            double alpha = 1.0;
            bool alphaBlend = false;
            MaterialAlphaMode alphaMode = MaterialAlphaMode.Opaque;
            double alphaCutoff = 0.5;
            bool doubleSided = false;
            double metallic = 1.0;
            double roughness = 1.0;
            double transmission = 0.0;
            double ior = 1.5;
            double thickness = 0.0;
            Vec3 attenuationColor = new(1.0, 1.0, 1.0);
            double attenuationDistance = 0.0;
            double clearcoat = 0.0;
            double clearcoatRoughness = 0.0;
            bool clearcoatUsesTransmissionTexture = false;
            int transmissionTextureIndex = -1;
            int clearcoatTextureIndex = -1;
            TextureMap? texture = null;
            TextureMap? emissiveTexture = null;
            TextureMap? metallicRoughnessTexture = null;
            TextureMap? normalTexture = null;
            TextureMap? occlusionTexture = null;
            TextureMap? transmissionTexture = null;
            double normalScale = 1.0;
            double occlusionStrength = 1.0;
            int baseColorTexCoord = 0;
            JsonElement mat = materials[i];
            string alphaModeName = mat.TryGetProperty("alphaMode", out JsonElement alphaModeEl) ? alphaModeEl.GetString() ?? "OPAQUE" : "OPAQUE";
            alphaMode = alphaModeName.ToUpperInvariant() switch
            {
                "MASK" => MaterialAlphaMode.Mask,
                "BLEND" => MaterialAlphaMode.Blend,
                _ => MaterialAlphaMode.Opaque
            };
            alphaBlend = alphaMode == MaterialAlphaMode.Blend;
            if (mat.TryGetProperty("alphaCutoff", out JsonElement alphaCutoffEl))
                alphaCutoff = alphaCutoffEl.GetDouble();
            doubleSided = mat.TryGetProperty("doubleSided", out JsonElement doubleSidedEl) && doubleSidedEl.GetBoolean();
            if (mat.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out JsonElement baseColor) && baseColor.GetArrayLength() >= 3)
                {
                    color = new Vec3(baseColor[0].GetDouble(), baseColor[1].GetDouble(), baseColor[2].GetDouble());
                    if (baseColor.GetArrayLength() >= 4)
                    {
                        alpha = baseColor[3].GetDouble();
                    }
                }

                metallic = pbr.TryGetProperty("metallicFactor", out JsonElement metallicEl) ? metallicEl.GetDouble() : metallic;
                roughness = pbr.TryGetProperty("roughnessFactor", out JsonElement roughnessEl) ? roughnessEl.GetDouble() : roughness;

                if (pbr.TryGetProperty("metallicRoughnessTexture", out JsonElement mrTexture) &&
                    mrTexture.TryGetProperty("index", out JsonElement mrTextureIndexEl))
                {
                    int textureIndex = mrTextureIndexEl.GetInt32();
                    if (!textureCache.TryGetValue(textureIndex, out metallicRoughnessTexture))
                    {
                        metallicRoughnessTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                        textureCache[textureIndex] = metallicRoughnessTexture;
                    }
                    metallicRoughnessTexture = ApplyTextureTransform(metallicRoughnessTexture, mrTexture);
                }

                // Most real glTF samples, including DamagedHelmet/Sponza-style assets,
                // carry their visible color in baseColorTexture rather than only in
                // baseColorFactor.  Earlier builds ignored this, so those files loaded
                // as flat grey/white geometry even though their UVs were present.
                if (pbr.TryGetProperty("baseColorTexture", out JsonElement baseColorTexture) &&
                    baseColorTexture.TryGetProperty("index", out JsonElement textureIndexEl))
                {
                    baseColorTexCoord = ReadTextureCoordSet(baseColorTexture);
                    int textureIndex = textureIndexEl.GetInt32();
                    if (!textureCache.TryGetValue(textureIndex, out texture))
                    {
                        texture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                        textureCache[textureIndex] = texture;
                    }
                    texture = ApplyTextureTransform(texture, baseColorTexture);

                    // The current renderer treats a texture as the material's visible
                    // color source.  Avoid accidentally tinting a loaded texture by an
                    // arbitrary fallback material color when the file has no explicit
                    // factor.  Keep explicit baseColorFactor above when supplied.
                    if (!pbr.TryGetProperty("baseColorFactor", out _))
                        color = new Vec3(1, 1, 1);
                }
            }
            if (mat.TryGetProperty("emissiveFactor", out JsonElement emissive) && emissive.ValueKind == JsonValueKind.Array && emissive.GetArrayLength() >= 3)
            {
                emissionColor = new Vec3(emissive[0].GetDouble(), emissive[1].GetDouble(), emissive[2].GetDouble());
                emission = Math.Max(emissionColor.X, Math.Max(emissionColor.Y, emissionColor.Z)) > 0.0 ? 1.0 : 0.0;
            }

            if (mat.TryGetProperty("emissiveTexture", out JsonElement emissiveTextureEl) &&
                emissiveTextureEl.TryGetProperty("index", out JsonElement emissiveTextureIndexEl))
            {
                int textureIndex = emissiveTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out emissiveTexture))
                {
                    emissiveTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = emissiveTexture;
                }
                emissiveTexture = ApplyTextureTransform(emissiveTexture, emissiveTextureEl);

            }

            if (mat.TryGetProperty("normalTexture", out JsonElement normalTextureEl) &&
                normalTextureEl.TryGetProperty("index", out JsonElement normalTextureIndexEl))
            {
                int textureIndex = normalTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out normalTexture))
                {
                    normalTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = normalTexture;
                }
                normalTexture = ApplyTextureTransform(normalTexture, normalTextureEl);
                if (normalTextureEl.TryGetProperty("scale", out JsonElement normalScaleEl))
                    normalScale = normalScaleEl.GetDouble();
            }

            if (mat.TryGetProperty("occlusionTexture", out JsonElement occlusionTextureEl) &&
                occlusionTextureEl.TryGetProperty("index", out JsonElement occlusionTextureIndexEl))
            {
                int textureIndex = occlusionTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out occlusionTexture))
                {
                    occlusionTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = occlusionTexture;
                }
                occlusionTexture = ApplyTextureTransform(occlusionTexture, occlusionTextureEl);
                if (occlusionTextureEl.TryGetProperty("strength", out JsonElement occlusionStrengthEl))
                    occlusionStrength = occlusionStrengthEl.GetDouble();
            }

            if (mat.TryGetProperty("extensions", out JsonElement matExt))
            {
                if (matExt.TryGetProperty("KHR_materials_transmission", out JsonElement transmissionExt))
                {
                    if (transmissionExt.TryGetProperty("transmissionFactor", out JsonElement transmissionEl))
                        transmission = transmissionEl.GetDouble();

                    if (transmissionExt.TryGetProperty("transmissionTexture", out JsonElement transmissionTextureEl) &&
                        transmissionTextureEl.TryGetProperty("index", out JsonElement transmissionTextureIndexEl))
                    {
                        int textureIndex = transmissionTextureIndexEl.GetInt32();
                        transmissionTextureIndex = textureIndex;
                        if (!textureCache.TryGetValue(textureIndex, out transmissionTexture))
                        {
                            transmissionTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                            textureCache[textureIndex] = transmissionTexture;
                        }
                        transmissionTexture = ApplyTextureTransform(transmissionTexture, transmissionTextureEl);
                    }
                }
                if (matExt.TryGetProperty("KHR_materials_ior", out JsonElement iorExt) &&
                    iorExt.TryGetProperty("ior", out JsonElement iorEl))
                {
                    ior = iorEl.GetDouble();
                }

                if (matExt.TryGetProperty("KHR_materials_volume", out JsonElement volumeExt))
                {
                    if (volumeExt.TryGetProperty("thicknessFactor", out JsonElement thicknessEl))
                        thickness = thicknessEl.GetDouble();
                    if (volumeExt.TryGetProperty("attenuationDistance", out JsonElement attenuationDistanceEl))
                        attenuationDistance = attenuationDistanceEl.GetDouble();
                    if (volumeExt.TryGetProperty("attenuationColor", out JsonElement attenuationColorEl) &&
                        attenuationColorEl.ValueKind == JsonValueKind.Array && attenuationColorEl.GetArrayLength() >= 3)
                    {
                        attenuationColor = new Vec3(
                            attenuationColorEl[0].GetDouble(),
                            attenuationColorEl[1].GetDouble(),
                            attenuationColorEl[2].GetDouble());
                    }
                }

                if (matExt.TryGetProperty("KHR_materials_clearcoat", out JsonElement clearcoatExt))
                {
                    if (clearcoatExt.TryGetProperty("clearcoatFactor", out JsonElement clearcoatEl))
                        clearcoat = clearcoatEl.GetDouble();
                    if (clearcoatExt.TryGetProperty("clearcoatRoughnessFactor", out JsonElement clearcoatRoughnessEl))
                        clearcoatRoughness = clearcoatRoughnessEl.GetDouble();
                    if (clearcoatExt.TryGetProperty("clearcoatTexture", out JsonElement clearcoatTextureEl) &&
                        clearcoatTextureEl.TryGetProperty("index", out JsonElement clearcoatTextureIndexEl))
                    {
                        clearcoatTextureIndex = clearcoatTextureIndexEl.GetInt32();
                    }
                }

                clearcoatUsesTransmissionTexture =
                    transmissionTextureIndex >= 0 && clearcoatTextureIndex == transmissionTextureIndex;
            }

            result.Add(new GltfMaterial(new Material(
                color, emission, texture: texture, emissionColor: emissionColor, emissiveTexture: emissiveTexture,
                alpha: alphaMode == MaterialAlphaMode.Opaque ? 1.0 : alpha, alphaBlend: alphaBlend, metallic: metallic, roughness: roughness, transmission: transmission,
                metallicRoughnessTexture: metallicRoughnessTexture, normalTexture: normalTexture, occlusionTexture: occlusionTexture,
                normalScale: normalScale, occlusionStrength: occlusionStrength, alphaMode: alphaMode,
                alphaCutoff: alphaCutoff, doubleSided: doubleSided,
                transmissionTexture: transmissionTexture, ior: ior, thickness: thickness,
                attenuationColor: attenuationColor, attenuationDistance: attenuationDistance,
                clearcoat: clearcoat, clearcoatRoughness: clearcoatRoughness,
                clearcoatUsesTransmissionTexture: clearcoatUsesTransmissionTexture), baseColorTexCoord));
        }
        return result;
    }

    private static TextureMap? TryReadTexture(JsonElement root, List<byte[]> buffers, string sceneFilePath, int textureIndex)
    {
        try
        {
            JsonElement textures = GetArray(root, "textures");
            JsonElement images = GetArray(root, "images");
            if (textureIndex < 0 || textureIndex >= textures.GetArrayLength())
                return null;

            JsonElement texture = textures[textureIndex];
            if (!texture.TryGetProperty("source", out JsonElement sourceEl))
                return null;
            TextureAddressMode wrapS = TextureAddressMode.Repeat;
            TextureAddressMode wrapT = TextureAddressMode.Repeat;
            if (texture.TryGetProperty("sampler", out JsonElement samplerEl))
            {
                JsonElement samplers = GetArray(root, "samplers");
                int samplerIndex = samplerEl.GetInt32();
                if (samplerIndex >= 0 && samplerIndex < samplers.GetArrayLength())
                {
                    JsonElement sampler = samplers[samplerIndex];
                    wrapS = sampler.TryGetProperty("wrapS", out JsonElement wrapSEl) ? ToTextureAddressMode(wrapSEl.GetInt32()) : TextureAddressMode.Repeat;
                    wrapT = sampler.TryGetProperty("wrapT", out JsonElement wrapTEl) ? ToTextureAddressMode(wrapTEl.GetInt32()) : TextureAddressMode.Repeat;
                }
            }

            int imageIndex = sourceEl.GetInt32();
            if (imageIndex < 0 || imageIndex >= images.GetArrayLength())
                return null;

            JsonElement image = images[imageIndex];
            string baseDirectory = Path.GetDirectoryName(sceneFilePath) ?? string.Empty;
            string name = image.TryGetProperty("name", out JsonElement nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                ? nameEl.GetString()!.Trim()
                : $"gltf_texture_{textureIndex + 1}";

            if (image.TryGetProperty("uri", out JsonElement uriEl))
            {
                string? uri = uriEl.GetString();
                if (string.IsNullOrWhiteSpace(uri))
                    return null;

                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    // A glTF buffer/image URI may embed bytes directly as a data URI. Decoding that path locally
                    // avoids treating the base64 payload as a filesystem path.
                    return TextureMap.FromBytes(name, ReadDataUriBytes(uri), null).WithAddressing(wrapS, wrapT);

                string imagePath = Path.Combine(baseDirectory, Uri.UnescapeDataString(uri));
                return File.Exists(imagePath) ? TextureMap.FromFile(imagePath).WithAddressing(wrapS, wrapT) : null;
            }

            if (image.TryGetProperty("bufferView", out JsonElement viewEl))
            {
                byte[] bytes = ReadBufferViewBytes(root, buffers, viewEl.GetInt32());
                return TextureMap.FromBytes(name, bytes, null).WithAddressing(wrapS, wrapT);
            }
        }
        catch
        {
            // Keep loading the mesh if one texture is unsupported or corrupt.
        }
        return null;
    }

    private static TextureMap? ApplyTextureTransform(TextureMap? texture, JsonElement textureInfo)
    {
        if (texture == null ||
            !textureInfo.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty("KHR_texture_transform", out JsonElement transform))
            return texture;

        double offsetU = 0.0;
        double offsetV = 0.0;
        double scaleU = 1.0;
        double scaleV = 1.0;
        double rotation = 0.0;

        if (transform.TryGetProperty("offset", out JsonElement offset) && offset.ValueKind == JsonValueKind.Array && offset.GetArrayLength() >= 2)
        {
            offsetU = offset[0].GetDouble();
            offsetV = offset[1].GetDouble();
        }
        if (transform.TryGetProperty("scale", out JsonElement scale) && scale.ValueKind == JsonValueKind.Array && scale.GetArrayLength() >= 2)
        {
            scaleU = scale[0].GetDouble();
            scaleV = scale[1].GetDouble();
        }
        if (transform.TryGetProperty("rotation", out JsonElement rotationEl))
            rotation = rotationEl.GetDouble();

        return texture.WithTextureTransform(offsetU, offsetV, scaleU, scaleV, rotation);
    }

    // ReadTextureCoordSet reads texture coord set from the external stream/document, advancing through the format
    // in the order required to resolve references and produce valid internal data.
    private static int ReadTextureCoordSet(JsonElement textureInfo)
    {
        int texCoord = textureInfo.TryGetProperty("texCoord", out JsonElement texCoordEl)
            ? Math.Max(0, texCoordEl.GetInt32())
            : 0;

        if (textureInfo.TryGetProperty("extensions", out JsonElement extensions) &&
            extensions.TryGetProperty("KHR_texture_transform", out JsonElement transform) &&
            transform.TryGetProperty("texCoord", out JsonElement transformTexCoordEl))
        {
            texCoord = Math.Max(0, transformTexCoordEl.GetInt32());
        }

        return texCoord;
    }

    private static int ToGltfWrap(TextureAddressMode mode) => mode switch
    {
        TextureAddressMode.ClampToEdge => 33071,
        TextureAddressMode.MirroredRepeat => 33648,
        _ => 10497
    };

    private static TextureAddressMode ToTextureAddressMode(int gltfWrap) => gltfWrap switch
    {
        33071 => TextureAddressMode.ClampToEdge,
        33648 => TextureAddressMode.MirroredRepeat,
        _ => TextureAddressMode.Repeat
    };

    private static byte[] ReadDataUriBytes(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
            throw new InvalidDataException("Invalid data URI.");
        return Convert.FromBase64String(uri[(comma + 1)..]);
    }

    private static byte[] ReadBufferViewBytes(JsonElement root, List<byte[]> buffers, int bufferViewIndex)
    {
        JsonElement bufferViews = GetArray(root, "bufferViews");
        if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.GetArrayLength())
            throw new InvalidDataException("Invalid glTF image bufferView index.");
        JsonElement view = bufferViews[bufferViewIndex];
        int buffer = view.TryGetProperty("buffer", out JsonElement bufferEl) ? bufferEl.GetInt32() : 0;
        // glTF accessor data can start at both a buffer-view offset and an accessor offset, with an optional byte
        // stride between elements. Combining those values correctly is essential for interleaved vertex buffers.
        int offset = view.TryGetProperty("byteOffset", out JsonElement offsetEl) ? offsetEl.GetInt32() : 0;
        int length = view.GetProperty("byteLength").GetInt32();
        byte[] bytes = new byte[length];
        Buffer.BlockCopy(buffers[buffer], offset, bytes, 0, length);
        return bytes;
    }

    // ReadLights reads lights from the external stream/document, advancing through the format in the order required
    // to resolve references and produce valid internal data.
    private static List<ImportedLight> ReadLights(JsonElement root)
    {
        List<ImportedLight> result = new();
        if (!root.TryGetProperty("extensions", out JsonElement ext) || !ext.TryGetProperty("KHR_lights_punctual", out JsonElement lightExt) || !lightExt.TryGetProperty("lights", out JsonElement lights))
            return result;

        int index = 0;
        foreach (JsonElement light in lights.EnumerateArray())
        {
            string id = light.TryGetProperty("name", out JsonElement nameEl) ? SanitizeName(nameEl.GetString(), $"gltf_light_{index + 1}") : $"gltf_light_{index + 1}";
            string type = light.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "point" : "point";
            SceneLightKind kind = type switch
            {
                "directional" => SceneLightKind.Directional,
                "spot" => SceneLightKind.Spot,
                _ => SceneLightKind.Point
            };

            Vec3 color = new(1, 1, 1);
            if (light.TryGetProperty("color", out JsonElement colorEl) && colorEl.ValueKind == JsonValueKind.Array && colorEl.GetArrayLength() >= 3)
                color = new Vec3(colorEl[0].GetDouble(), colorEl[1].GetDouble(), colorEl[2].GetDouble());

            double intensity = light.TryGetProperty("intensity", out JsonElement intensityEl) ? intensityEl.GetDouble() : 1.0;
            double range = light.TryGetProperty("range", out JsonElement rangeEl) ? Math.Max(0.0, rangeEl.GetDouble()) : 0.0;
            double innerConeAngle = 0.0;
            double outerConeAngle = Math.PI / 4.0;
            if (kind == SceneLightKind.Spot && light.TryGetProperty("spot", out JsonElement spotEl))
            {
                if (spotEl.TryGetProperty("innerConeAngle", out JsonElement innerEl))
                    innerConeAngle = Math.Max(0.0, innerEl.GetDouble());
                if (spotEl.TryGetProperty("outerConeAngle", out JsonElement outerEl))
                    outerConeAngle = Math.Max(innerConeAngle, outerEl.GetDouble());
            }

            result.Add(new ImportedLight(id, kind, Vec3.Zero, new Vec3(0.0, 0.0, -1.0), color, intensity, range, innerConeAngle, outerConeAngle, true));
            index++;
        }
        return result;
    }

    private static bool TryReadVec3AccessorBounds(JsonElement root, int accessorIndex, out Vec3 min, out Vec3 max)
    {
        min = Vec3.Zero;
        max = Vec3.Zero;
        JsonElement accessors = GetArray(root, "accessors");
        if (accessorIndex < 0 || accessorIndex >= accessors.GetArrayLength())
            return false;

        JsonElement accessor = accessors[accessorIndex];
        if (!accessor.TryGetProperty("type", out JsonElement typeEl) ||
            !string.Equals(typeEl.GetString(), "VEC3", StringComparison.Ordinal) ||
            !accessor.TryGetProperty("min", out JsonElement minEl) ||
            !accessor.TryGetProperty("max", out JsonElement maxEl) ||
            minEl.ValueKind != JsonValueKind.Array || maxEl.ValueKind != JsonValueKind.Array ||
            minEl.GetArrayLength() < 3 || maxEl.GetArrayLength() < 3)
        {
            return false;
        }

        min = new Vec3(minEl[0].GetDouble(), minEl[1].GetDouble(), minEl[2].GetDouble());
        max = new Vec3(maxEl[0].GetDouble(), maxEl[1].GetDouble(), maxEl[2].GetDouble());
        return double.IsFinite(min.X) && double.IsFinite(min.Y) && double.IsFinite(min.Z) &&
               double.IsFinite(max.X) && double.IsFinite(max.Y) && double.IsFinite(max.Z) &&
               min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;
    }

    // ReadVec3Accessor reads vec3 accessor from the external stream/document, advancing through the format in the
    // order required to resolve references and produce valid internal data.
    private static List<Vec3> ReadVec3Accessor(JsonElement root, List<byte[]> buffers, int accessorIndex, Matrix4x4 transform)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            float x = ReadFloat(buffers[info.Buffer], offset);
            float y = ReadFloat(buffers[info.Buffer], offset + 4);
            float z = ReadFloat(buffers[info.Buffer], offset + 8);
            Vector3 v = Vector3.Transform(new Vector3(x, y, z), transform);
            values.Add(new Vec3(v.X, v.Y, v.Z));
        }
        return values;
    }

    private static List<Vec3> ReadNormalizedPositionAccessor(
        JsonElement root,
        List<byte[]> buffers,
        int accessorIndex,
        Matrix4x4 transform,
        Vec3 sourceCenter,
        double importScale,
        Vec3 importOffset)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        if (info.Type != "VEC3" || info.ComponentType != 5126)
            throw new NotSupportedException($"Expected float VEC3 glTF positions, got component {info.ComponentType} and type {info.Type}.");

        List<Vec3> values = new(info.Count);
        byte[] data = buffers[info.Buffer];
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            Vector3 transformed = Vector3.Transform(
                new Vector3(ReadFloat(data, offset), ReadFloat(data, offset + 4), ReadFloat(data, offset + 8)),
                transform);
            Vec3 position = new Vec3(transformed.X, transformed.Y, transformed.Z);
            values.Add((position - sourceCenter) * importScale + importOffset);
        }
        return values;
    }

    private static List<Vec3> ReadNormalAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex, Matrix4x4 transform)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        if (info.Type != "VEC3")
            throw new NotSupportedException($"Expected VEC3 glTF normals, got {info.Type}.");

        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
            inverse = Matrix4x4.Identity;
        // Normals use the inverse-transpose of the node transform rather than the position transform, which is
        // required to keep them perpendicular to surfaces under non-uniform scaling.
        Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);

        int componentSize = ComponentByteSize(info.ComponentType);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            float x = (float)ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            float y = (float)ReadAccessorComponent(buffers[info.Buffer], offset + componentSize, info.ComponentType, normalized: true);
            float z = (float)ReadAccessorComponent(buffers[info.Buffer], offset + componentSize * 2, info.ComponentType, normalized: true);
            Vector3 transformed = Vector3.TransformNormal(new Vector3(x, y, z), normalTransform);
            if (transformed.LengthSquared() > 1e-20f)
                transformed = Vector3.Normalize(transformed);
            values.Add(new Vec3(transformed.X, transformed.Y, transformed.Z));
        }
        return values;
    }

    private static List<Vec2> ReadVec2Accessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        if (info.Type != "VEC2")
            throw new NotSupportedException($"Expected VEC2 glTF texture coordinates, got {info.Type}.");

        int componentSize = ComponentByteSize(info.ComponentType);
        List<Vec2> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            double u = ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            double v = ReadAccessorComponent(buffers[info.Buffer], offset + componentSize, info.ComponentType, normalized: true);
            values.Add(new Vec2(u, v));
        }
        return values;
    }

    private static List<Vec3> ReadColorAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            double r = ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            double g = ReadAccessorComponent(buffers[info.Buffer], offset + ComponentByteSize(info.ComponentType), info.ComponentType, normalized: true);
            double b = ReadAccessorComponent(buffers[info.Buffer], offset + ComponentByteSize(info.ComponentType) * 2, info.ComponentType, normalized: true);
            values.Add(new Vec3(r, g, b));
        }
        return values;
    }

    private static List<int> ReadIndexAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<int> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            int value = info.ComponentType switch
            {
                5121 => buffers[info.Buffer][offset],
                5123 => BitConverter.ToUInt16(buffers[info.Buffer], offset),
                5125 => checked((int)BitConverter.ToUInt32(buffers[info.Buffer], offset)),
                _ => throw new NotSupportedException($"Unsupported glTF index component type {info.ComponentType}.")
            };
            values.Add(value);
        }
        return values;
    }

    // A glTF accessor is not a raw array pointer: its byte address combines the selected buffer, buffer-view
    // offset, accessor offset, component size, element width, and optional stride. Centralizing that calculation
    // keeps every attribute reader consistent.
    private static AccessorInfo GetAccessorInfo(JsonElement root, int accessorIndex)
    {
        JsonElement accessors = GetArray(root, "accessors");
        JsonElement bufferViews = GetArray(root, "bufferViews");
        JsonElement accessor = accessors[accessorIndex];
        int bufferViewIndex = accessor.GetProperty("bufferView").GetInt32();
        JsonElement view = bufferViews[bufferViewIndex];
        int componentType = accessor.GetProperty("componentType").GetInt32();
        string type = accessor.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "SCALAR" : "SCALAR";
        int componentCount = type switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 1 };
        int componentSize = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new NotSupportedException($"Unsupported glTF component type {componentType}.") };
        int stride = view.TryGetProperty("byteStride", out JsonElement strideEl) ? strideEl.GetInt32() : componentSize * componentCount;
        // glTF accessor data can start at both a buffer-view offset and an accessor offset, with an optional byte
        // stride between elements. Combining those values correctly is essential for interleaved vertex buffers.
        int offset = (view.TryGetProperty("byteOffset", out JsonElement viewOffset) ? viewOffset.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out JsonElement accessorOffset) ? accessorOffset.GetInt32() : 0);
        int buffer = view.TryGetProperty("buffer", out JsonElement bufferEl) ? bufferEl.GetInt32() : 0;
        return new AccessorInfo(buffer, offset, stride, accessor.GetProperty("count").GetInt32(), componentType, type);
    }

    private static Matrix4x4 GetNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixEl) && matrixEl.GetArrayLength() == 16)
        {
            float[] m = matrixEl.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        }

        Vector3 translation = Vector3.Zero;
        Vector3 scale = Vector3.One;
        Quaternion rotation = Quaternion.Identity;
        if (node.TryGetProperty("translation", out JsonElement t) && t.GetArrayLength() >= 3)
            translation = new Vector3((float)t[0].GetDouble(), (float)t[1].GetDouble(), (float)t[2].GetDouble());
        if (node.TryGetProperty("scale", out JsonElement s) && s.GetArrayLength() >= 3)
            scale = new Vector3((float)s[0].GetDouble(), (float)s[1].GetDouble(), (float)s[2].GetDouble());
        if (node.TryGetProperty("rotation", out JsonElement r) && r.GetArrayLength() >= 4)
            rotation = new Quaternion((float)r[0].GetDouble(), (float)r[1].GetDouble(), (float)r[2].GetDouble(), (float)r[3].GetDouble());
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static int AddFloatAccessor(List<byte> bin, List<object> views, List<object> accessors, float[] values, string type, Vec3? min, Vec3? max)
    {
        // glTF buffer views are aligned to 4-byte boundaries. Padding before appending a new attribute keeps
        // offsets valid for readers and GPU-friendly binary layouts.
        Align(bin, 4);
        int offset = bin.Count;
        foreach (float value in values)
            bin.AddRange(BitConverter.GetBytes(value));
        int view = views.Count;
        views.Add(new Dictionary<string, object?> { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 });
        int accessor = accessors.Count;
        Dictionary<string, object?> acc = new()
        {
            ["bufferView"] = view,
            ["componentType"] = 5126,
            ["count"] = values.Length / (type == "VEC3" ? 3 : type == "VEC2" ? 2 : 1),
            ["type"] = type
        };
        if (type == "VEC3" && min.HasValue && max.HasValue)
        {
            acc["min"] = new[] { min.Value.X, min.Value.Y, min.Value.Z };
            acc["max"] = new[] { max.Value.X, max.Value.Y, max.Value.Z };
        }
        accessors.Add(acc);
        return accessor;
    }

    private static int AddIndexAccessor(
        List<byte> bin,
        List<object> views,
        List<object> accessors,
        IReadOnlyList<uint> values,
        int vertexCount)
    {
        // glTF buffer views are aligned to 4-byte boundaries. Padding before appending a new attribute keeps
        // offsets valid for readers and GPU-friendly binary layouts.
        Align(bin, 4);
        int offset = bin.Count;
        int componentType;
        int byteLength;

        if (vertexCount <= ushort.MaxValue)
        {
            componentType = 5123;
            byteLength = checked(values.Count * 2);
            foreach (uint value in values)
                bin.AddRange(BitConverter.GetBytes(checked((ushort)value)));
        }
        else
        {
            componentType = 5125;
            byteLength = checked(values.Count * 4);
            foreach (uint value in values)
                bin.AddRange(BitConverter.GetBytes(value));
        }

        int view = views.Count;
        views.Add(new Dictionary<string, object?>
        {
            ["buffer"] = 0,
            ["byteOffset"] = offset,
            ["byteLength"] = byteLength
        });
        int accessor = accessors.Count;
        accessors.Add(new Dictionary<string, object?>
        {
            ["bufferView"] = view,
            ["componentType"] = componentType,
            ["count"] = values.Count,
            ["type"] = "SCALAR"
        });
        return accessor;
    }

    private static double ReadAccessorComponent(byte[] data, int offset, int componentType, bool normalized)
    {
        return componentType switch
        {
            5120 => normalized ? Math.Max(-1.0, (sbyte)data[offset] / 127.0) : (sbyte)data[offset],
            5121 => normalized ? data[offset] / 255.0 : data[offset],
            5122 => normalized ? Math.Max(-1.0, BitConverter.ToInt16(data, offset) / 32767.0) : BitConverter.ToInt16(data, offset),
            5123 => normalized ? BitConverter.ToUInt16(data, offset) / 65535.0 : BitConverter.ToUInt16(data, offset),
            5125 => normalized ? BitConverter.ToUInt32(data, offset) / 4294967295.0 : BitConverter.ToUInt32(data, offset),
            5126 => BitConverter.ToSingle(data, offset),
            _ => throw new NotSupportedException($"Unsupported glTF accessor component type {componentType}.")
        };
    }

    private static int ComponentByteSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new NotSupportedException($"Unsupported glTF accessor component type {componentType}.")
    };

    private static JsonElement GetArray(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value : EmptyArrayDocument.RootElement;
    private static float ReadFloat(byte[] data, int offset) => BitConverter.ToSingle(data, offset);
    private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;
    private static string SanitizeName(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
    private static string MaterialKey(Material m) => string.Create(CultureInfo.InvariantCulture, $"{m.Color.X:F6},{m.Color.Y:F6},{m.Color.Z:F6},{m.Emission:F6},{m.LightId}");
    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    private static void Align(List<byte> bytes, int boundary) { while (bytes.Count % boundary != 0) bytes.Add(0); }
    private static byte[] Pad(byte[] bytes, byte pad) { int padded = (bytes.Length + 3) & ~3; if (padded == bytes.Length) return bytes; byte[] result = new byte[padded]; Array.Copy(bytes, result, bytes.Length); for (int i = bytes.Length; i < result.Length; i++) result[i] = pad; return result; }
    private static JsonSerializerOptions JsonOptions(bool compact = false) => new() { WriteIndented = !compact, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static string LightTypeName(SceneLightKind kind) => kind switch
    {
        SceneLightKind.Directional => "directional",
        SceneLightKind.Spot => "spot",
        _ => "point"
    };

    private static double[]? RotationFromMinusZ(Vec3 direction)
    {
        Vec3 target = direction.Normalize();
        if (target.Length() < 1e-8)
            return null;

        Vec3 source = new(0.0, 0.0, -1.0);
        double dot = Math.Clamp(source.Dot(target), -1.0, 1.0);
        if (dot > 0.999999)
            return null;

        Vec3 axis = source.Cross(target);
        if (axis.Length() < 1e-8)
            axis = new Vec3(0.0, 1.0, 0.0);
        axis = axis.Normalize();
        double angle = Math.Acos(dot);
        double s = Math.Sin(angle / 2.0);
        return new[] { axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(angle / 2.0) };
    }

    private sealed record GltfDocument(byte[] JsonUtf8, byte[]? BinaryChunk);
    private sealed record GltfMaterial(Material Material, int BaseColorTexCoord);
    private sealed record ImportedLight(string Id, SceneLightKind Kind, Vec3 Position, Vec3 Direction, Vec3 Color, double Intensity, double Range, double InnerConeAngle, double OuterConeAngle, bool Enabled);
    private sealed record AccessorInfo(int Buffer, int Offset, int Stride, int Count, int ComponentType, string Type);
    private readonly record struct ExportVertex(
        float Px, float Py, float Pz,
        float Nx, float Ny, float Nz,
        float U, float V);
    private sealed record ExportBuild(Dictionary<string, object?> Root, byte[] Bin, bool CompactJson);
}
