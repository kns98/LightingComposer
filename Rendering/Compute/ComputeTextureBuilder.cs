// File: Rendering/Compute/ComputeTextureBuilder.cs
// Purpose: Collects scene textures into packed GPU upload metadata.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ComputeTextureBuilder
{
    /// <summary>Collects scene textures into GPU upload metadata.</summary>
    public ComputeTextureBuild Build(Scene scene)
    {
        List<TextureMap> textures = new();
        HashSet<TextureMap> seen = new();
        foreach (Triangle triangle in scene.Triangles)
        {
            AddUnique(triangle.Material.Texture);
            AddUnique(triangle.Material.EmissiveTexture);
            AddUnique(triangle.Material.MetallicRoughnessTexture);
            AddUnique(triangle.Material.NormalTexture);
            AddUnique(triangle.Material.OcclusionTexture);
            AddUnique(triangle.Material.TransmissionTexture);
        }

        if (textures.Count == 0)
        {
            return new ComputeTextureBuild
            {
                TextureIds = new Dictionary<TextureMap, int>(),
                Uploads = Array.Empty<ComputeTextureUpload>(),
                Infos = new[] { new GpuTextureInfo(0, 1, 1) },
                PixelCount = 1,
                TextureCount = 0
            };
        }

        GpuTextureInfo[] infos = new GpuTextureInfo[textures.Count];
        Dictionary<TextureMap, int> textureIds = new(textures.Count);
        List<ComputeTextureUpload> uploads = new(textures.Count);
        long pixelOffset = 0;

        foreach (TextureMap texture in textures)
        {
            int width = Math.Max(1, texture.Width);
            int height = Math.Max(1, texture.Height);
            long pixelLength = checked((long)width * height);
            if (pixelOffset > int.MaxValue || pixelOffset + pixelLength > int.MaxValue)
                throw new InvalidOperationException("Vulkan compute texture offsets exceed the shader's 32-bit indexing range.");

            int textureIndex = textureIds.Count;
            int offset = checked((int)pixelOffset);
            textureIds.Add(texture, textureIndex);
            infos[textureIndex] = new GpuTextureInfo(offset, width, height, texture);
            uploads.Add(new ComputeTextureUpload { Texture = texture, PixelOffset = offset });
            pixelOffset = checked(pixelOffset + pixelLength);
        }

        return new ComputeTextureBuild
        {
            TextureIds = textureIds,
            Uploads = uploads,
            Infos = infos,
            PixelCount = Math.Max(1, pixelOffset),
            TextureCount = textureIds.Count
        };

        void AddUnique(TextureMap? texture)
        {
            if (texture != null && seen.Add(texture))
                textures.Add(texture);
        }
    }
}
