// File: Rendering/Raster/Gpu/RasterTexturePlacement.cs
// Purpose: Atlas UV transform plus wrap/page info for one texture slot.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterTexturePlacement
{
    public readonly Vector4 AtlasTransform; // xy=atlas UV offset, zw=atlas UV scale
    public readonly Vector4 TextureAddress; // x=wrapU, y=wrapV: 0 repeat, 1 clamp, 2 mirror
    public readonly Vector4 TextureTransform; // xy=offset, zw=scale; rotation in TextureAddress.z
    public readonly bool HasTexture;

    /// <summary>Initializes a texture atlas placement with transform, page, and wrap codes.</summary>
    public RasterTexturePlacement(float offsetX, float offsetY, float scaleX, float scaleY, int pageIndex, TextureMap texture)
    {
        AtlasTransform = new Vector4(offsetX, offsetY, scaleX, scaleY);
        // w stores the texture-array page. z remains the glTF texture-transform rotation.
        TextureAddress = new Vector4(AddressModeCode(texture.WrapU), AddressModeCode(texture.WrapV), (float)texture.Rotation, pageIndex);
        TextureTransform = new Vector4((float)texture.OffsetU, (float)texture.OffsetV, (float)texture.ScaleU, (float)texture.ScaleV);
        HasTexture = true;
    }

    public static RasterTexturePlacement None => new();

    /// <summary>Maps a texture address mode to its GPU code.</summary>
    internal static float AddressModeCode(TextureAddressMode mode) => mode switch
    {
        TextureAddressMode.ClampToEdge => 1.0f,
        TextureAddressMode.MirroredRepeat => 2.0f,
        _ => 0.0f
    };
}
