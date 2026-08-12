/*
 * Surface appearance is normalized here so importers, editor controls, and every renderer use the same meaning
 * for colors, PBR values, alpha behavior, texture slots, UV transforms, and resource identity. That shared model
 * is what makes a material edited in the UI render consistently across backends.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Root scene container for editable objects, lights, and acceleration structures.</summary>
public sealed class SceneMaterials
{
    public Material WhiteWall { get; } = new(new Vec3(0.78, 0.76, 0.72));
    public Material RedWall { get; } = new(new Vec3(0.75, 0.18, 0.14));
    public Material BlueWall { get; } = new(new Vec3(0.16, 0.28, 0.75));
    public Material Floor { get; } = new(new Vec3(0.55, 0.50, 0.43));
    public Material Ceiling { get; } = new(new Vec3(0.72, 0.72, 0.70));
    public Material LightPanel { get; } = new(new Vec3(1.0, 0.92, 0.72), 2.2, "ceiling");
    public Material Wood { get; } = new(new Vec3(0.46, 0.28, 0.13));
    public Material DarkWood { get; } = new(new Vec3(0.23, 0.13, 0.07));
    public Material Sofa { get; } = new(new Vec3(0.16, 0.38, 0.58));
    public Material Cushion { get; } = new(new Vec3(0.90, 0.72, 0.42));
    public Material Rug { get; } = new(new Vec3(0.55, 0.18, 0.22));
    public Material Plant { get; } = new(new Vec3(0.12, 0.50, 0.18));
    public Material Pot { get; } = new(new Vec3(0.50, 0.28, 0.16));
    public Material LampGlow { get; } = new(new Vec3(1.0, 0.82, 0.48), 2.8, "lamp");
    public Material ScreenFrame { get; } = new(new Vec3(0.23, 0.13, 0.07));
    public Material Screen { get; } = new(new Vec3(0.08, 0.09, 0.11));
}
