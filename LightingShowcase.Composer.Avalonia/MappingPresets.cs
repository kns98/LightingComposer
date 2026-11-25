// Named mapping preset library for the Mapping tab: UV source plus transform
// and wrap values for common tiling setups. Hosts write the picked values
// into their mapping editors so validation and apply paths stay unchanged.
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record MappingPreset(
    string Name,
    bool BoxProjection,
    double TileMeters,
    double OffsetU,
    double OffsetV,
    double ScaleU,
    double ScaleV,
    double RotationDegrees,
    TextureAddressMode WrapU,
    TextureAddressMode WrapV,
    string Summary);

internal static class MappingPresets
{
    public static readonly IReadOnlyList<MappingPreset> All = new MappingPreset[]
    {
        new("Authored UVs", false, 0.25, 0.0, 0.0, 1.0, 1.0, 0.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat, "Mesh stored UVs with no transform."),
        new("Box 25 cm", true, 0.25, 0.0, 0.0, 1.0, 1.0, 0.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat, "Box projection with 25 cm tiles."),
        new("Box 50 cm", true, 0.50, 0.0, 0.0, 1.0, 1.0, 0.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat, "Box projection with 50 cm tiles."),
        new("Tile 2x", false, 0.25, 0.0, 0.0, 2.0, 2.0, 0.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat, "Repeat the image twice per UV tile."),
        new("Tile 4x", false, 0.25, 0.0, 0.0, 4.0, 4.0, 0.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat, "Repeat the image four times per UV tile."),
        new("Clamp Single", false, 0.25, 0.0, 0.0, 1.0, 1.0, 0.0, TextureAddressMode.ClampToEdge, TextureAddressMode.ClampToEdge, "One stretched copy, no tiling."),
    };
}
