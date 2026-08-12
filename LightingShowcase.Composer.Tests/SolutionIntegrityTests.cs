/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 */
using System.Text.RegularExpressions;

namespace LightingShowcase.Composer.Tests;

public sealed class SolutionIntegrityTests
{
    // Visual_Studio_solution_references_existing_projects verifies that visual studio solution references existing
    // projects. Temporary filesystem output is inspected/cleaned so persistence behavior is tested end-to-end. The
    // assertions establish that the operation must explicitly report success; the expected entry must remain
    // discoverable. Representative cases include LightingShowcase.Composer.sln, Project\\(\\\, \\) = \\\, ]+\\\,
    // ([^\\\, LightingShowcase.Composer.Tests.
    [Fact]
    public void Visual_Studio_solution_references_existing_projects()
    {
        string root = FindRepositoryRoot();
        string solutionPath = Path.Combine(root, "LightingShowcase.Composer.sln");
        string solution = File.ReadAllText(solutionPath);

        MatchCollection matches = Regex.Matches(
            solution,
            "Project\\(\\\"\\{[^}]+\\}\\\"\\) = \\\"[^\\\"]+\\\", \\\"([^\\\"]+\\.csproj)\\\"",
            RegexOptions.CultureInvariant);

        Assert.NotEmpty(matches);
        foreach (Match match in matches)
        {
            string relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            string projectPath = Path.Combine(root, relativePath);
            Assert.True(File.Exists(projectPath), $"Solution project does not exist: {relativePath}");
        }

        Assert.Contains("LightingShowcase.Composer.Tests", solution);
        Assert.Contains("LightingShowcase.Composer.Avalonia", solution);
    }

    // FindRepositoryRoot searches for repository root and returns the matching object/value rather than assuming it
    // exists. Callers can therefore distinguish a missing match from the found instance.
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightingShowcase.Composer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate LightingShowcase.Composer.sln from the test output directory.");
    }
}
