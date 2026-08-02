using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using Xunit.Abstractions;

namespace LightingShowcase.Composer.Tests;

/// <summary>
/// Opt-in tests for a machine with a working Vulkan driver. These are excluded
/// from normal CI because hosted runners do not guarantee Vulkan availability.
/// Run with LIGHTINGSHOWCASE_RUN_GPU_TESTS=1.
/// </summary>
public sealed class GpuTransformIntegrationTests
{
    private readonly ITestOutputHelper output;

    public GpuTransformIntegrationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData("VulkanRaster")]
    [InlineData("VulkanCompute")]
    [Trait("Category", "Gpu")]
    public void Vulkan_preview_changes_after_model_transform(string rendererName)
    {
        ComposerRendererKind renderer = Enum.Parse<ComposerRendererKind>(rendererName);
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_RUN_GPU_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            output.WriteLine("GPU integration test not requested. Set LIGHTINGSHOWCASE_RUN_GPU_TESTS=1 to run it.");
            return;
        }

        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        session.FrameObject(rootId);
        var camera = session.Camera.Snapshot();

        ComposerFrame before = session.Render(renderer, camera, 160, 120, interactive: false, CancellationToken.None);
        ComposerObjectState state = session.GetObjectState(rootId)!;
        Assert.True(session.UpdateObject(
            rootId,
            state.Name,
            state.Visible,
            new Vec3(1.1, 0.25, 0),
            new Vec3(0, Math.PI / 10, 0),
            new Vec3(1, 1, 1)));
        if (renderer == ComposerRendererKind.VulkanRaster)
            Assert.Contains("refreshed in-place", session.LastGeometryRefreshDetails, StringComparison.OrdinalIgnoreCase);
        ComposerFrame after = session.Render(renderer, camera, 160, 120, interactive: false, CancellationToken.None);

        Assert.NotEqual(HashPixels(before.Image), HashPixels(after.Image));
    }

    private static ulong HashPixels(RenderImage image)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (uint pixel in image.PackedRgba32)
        {
            hash ^= pixel;
            hash *= prime;
        }
        return hash;
    }
}
