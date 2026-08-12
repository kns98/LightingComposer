/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Nearest-hit record filled during ray traversal.</summary>
public sealed class Hit
{
    public double T { get; }
    public Vec3 Point { get; }
    public Vec3 Normal { get; }
    public Material Material { get; }
    public int GroupId { get; }
    public double TextureU { get; }
    public double TextureV { get; }
    public Vec3 Tangent { get; }
    public Vec3 Bitangent { get; }
    public Hit(
        double t,
        Vec3 point,
        Vec3 normal,
        Material material,
        int groupId = -1,
        double textureU = 0.0,
        double textureV = 0.0,
        Vec3? tangent = null,
        Vec3? bitangent = null)
    {
        T = t;
        Point = point;
        Normal = normal;
        Material = material;
        GroupId = groupId;
        TextureU = textureU;
        TextureV = textureV;
        Tangent = tangent ?? Vec3.Zero;
        Bitangent = bitangent ?? Vec3.Zero;
    }
}
