namespace LightingShowcase.Composer.Tests;

internal sealed class TestModel : IDisposable
{
    public TestModel()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "LightingShowcaseComposerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        ModelPath = Path.Combine(DirectoryPath, "two-parts.obj");
        File.WriteAllText(ModelPath, """
            o LeftPart
            v -1 0 0
            v 0 0 0
            v -1 1 0
            f 1 2 3
            o RightPart
            v 0.25 0 0
            v 1.25 0 0
            v 1.25 1 0
            f 4 5 6
            """);
    }

    public string DirectoryPath { get; }
    public string ModelPath { get; }

    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch { }
    }
}
