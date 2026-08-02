using System.Text.RegularExpressions;

namespace LightingShowcase.Composer.Tests;

public sealed class SolutionIntegrityTests
{
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
