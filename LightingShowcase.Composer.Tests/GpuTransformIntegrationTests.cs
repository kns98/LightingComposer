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


    [Fact]
    [Trait("Category", "Gpu")]
    public void Vulkan_raster_previews_pending_rotation_and_scale_without_committing_geometry()
    {
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
        Assert.True(session.SetSelectedObject(rootId));
        session.FrameObject(rootId);
        var camera = session.Camera.Snapshot();
        SceneCacheStamp beforeStamp = session.CaptureSceneCacheStampForTests();

        ComposerFrame before = session.Render(
            ComposerRendererKind.VulkanRaster,
            camera,
            192,
            144,
            interactive: true,
            CancellationToken.None,
            ComposerGizmoMode.Rotate);

        ComposerObjectState state = session.GetObjectState(rootId)!;
        Assert.True(session.UpdateTransformTarget(
            rootId,
            state.Position,
            state.Rotation + new Vec3(Math.PI / 7.0, Math.PI / 5.0, 0),
            new Vec3(1.35, 0.75, 1.1)));

        SceneCacheStamp pendingStamp = session.CaptureSceneCacheStampForTests();
        Assert.Equal(beforeStamp.Revision, pendingStamp.Revision);

        ComposerFrame preview = session.Render(
            ComposerRendererKind.VulkanRaster,
            camera,
            192,
            144,
            interactive: true,
            CancellationToken.None,
            ComposerGizmoMode.Rotate);

        Assert.Contains("live-transform=", preview.Details, StringComparison.Ordinal);
        Assert.NotEqual(HashPixels(before.Image), HashPixels(preview.Image));
        Assert.Equal(beforeStamp.Revision, session.CaptureSceneCacheStampForTests().Revision);
        Assert.True(session.CancelPendingTransform(rootId));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Vulkan_raster_previews_face_move_without_rebuilding_scene_geometry()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_RUN_GPU_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            output.WriteLine("GPU integration test not requested. Set LIGHTINGSHOWCASE_RUN_GPU_TESTS=1 to run it.");
            return;
        }

        using ComposerSceneSession session = new();
        int groupId = session.InsertPrimitive("Cube");
        Assert.True(session.SetSelectedTriangle(groupId, 0));
        session.FrameObject(groupId);
        var camera = session.Camera.Snapshot();
        SceneCacheStamp beforeStamp = session.CaptureSceneCacheStampForTests();

        ComposerFrame before = session.Render(
            ComposerRendererKind.VulkanRaster,
            camera,
            192,
            144,
            interactive: true,
            CancellationToken.None);

        Assert.True(session.UpdateMeshElementMovePreview(groupId, new Vec3(0.35, 0.2, 0.15)));
        Assert.Equal(beforeStamp.Revision, session.CaptureSceneCacheStampForTests().Revision);

        ComposerFrame preview = session.Render(
            ComposerRendererKind.VulkanRaster,
            camera,
            192,
            144,
            interactive: true,
            CancellationToken.None);

        Assert.Contains("live-mesh-edit=", preview.Details, StringComparison.Ordinal);
        Assert.NotEqual(HashPixels(before.Image), HashPixels(preview.Image));
        Assert.Equal(beforeStamp.Revision, session.CaptureSceneCacheStampForTests().Revision);
        Assert.True(session.CancelMeshElementMovePreview(groupId));
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
