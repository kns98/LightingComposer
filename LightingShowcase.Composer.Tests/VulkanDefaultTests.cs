using LightingShowcase.Composer;
using LightingShowcase.Rendering;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class VulkanDefaultTests
{
    [Fact]
    public void LoaderProbe_IsDeterministic()
    {
        Assert.Equal(VulkanLoader.IsPresent(), VulkanLoader.IsPresent());
    }

    [Theory]
    [InlineData(true, 1)] // VulkanRaster
    [InlineData(false, 0)] // Raster
    public void DefaultKind_SelectsVulkanRasterWhenPresent(bool vulkanPresent, int expected)
    {
        Assert.Equal((ComposerRendererKind)expected, ComposerWindow.DefaultKind(vulkanPresent));
    }
}
