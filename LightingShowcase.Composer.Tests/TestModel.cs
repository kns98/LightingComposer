/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 */
namespace LightingShowcase.Composer.Tests;

// TestModel is a caller/UI-facing snapshot of domain state; it deliberately avoids handing out the live mutable
// scene object that produced it.
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

    // Dispose ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
    // windows/renderers do not keep receiving work or retain unmanaged memory.
    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch { }
    }
}
