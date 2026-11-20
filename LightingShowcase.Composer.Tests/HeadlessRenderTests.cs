using LightingShowcase.CommandLine;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class HeadlessRenderTests
{
    private const string QuadObj = """
        v -1 -1 4
        v 1 -1 4
        v 1 1 4
        v -1 1 4
        vn 0 0 -1
        vn 0 0 -1
        vn 0 0 -1
        vn 0 0 -1
        vt 0 0
        vt 1 0
        vt 1 1
        vt 0 1
        f 1/1/1 2/2/2 3/3/3
        f 1/1/1 3/3/3 4/4/4
        """;

    [Fact]
    public async Task Render_LightPositionAffectsPixels()
    {
        string directory = Directory.CreateTempSubdirectory("headless-light-test").FullName;
        try
        {
            string input = Path.Combine(directory, "quad.obj");
            await File.WriteAllTextAsync(input, QuadObj);
            string front = await RenderToFileAsync(input, Path.Combine(directory, "front.png"),
                LightRequest.Parse("position=0,2,-6;intensity=8", 0));
            string back = await RenderToFileAsync(input, Path.Combine(directory, "back.png"),
                LightRequest.Parse("position=0,2,14;intensity=8", 0));

            byte[] frontBytes = await File.ReadAllBytesAsync(front);
            byte[] backBytes = await File.ReadAllBytesAsync(back);
            Assert.True(frontBytes.Length > 0 && backBytes.Length > 0);
            Assert.False(frontBytes.SequenceEqual(backBytes));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> RenderToFileAsync(string input, string output, LightRequest light)
    {
        RenderRequest request = new()
        {
            Input = input,
            Output = output,
            Width = 64,
            Height = 48,
            Samples = 1,
            Bounces = 0,
            Backend = RenderBackend.ShadowRasterPreview,
            Lights = new[] { light }
        };
        RenderJobResult result = await new RenderJobRunner().RunAsync(request, CancellationToken.None);
        Assert.Equal(2, result.TriangleCount);
        return result.Output;
    }
}
