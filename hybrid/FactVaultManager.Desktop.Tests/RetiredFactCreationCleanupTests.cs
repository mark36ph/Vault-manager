namespace FactVaultManager.Desktop.Tests;

public sealed class RetiredFactCreationCleanupTests
{
    [Fact]
    public void Build148_RemovesRetiredGenericFactCreationFiles()
    {
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/DesktopDataService.NewFact.cs"));
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/HybridProject.cs"));
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/MainShellWindow.LegacyShellCompatibility.cs"));
    }

    [Fact]
    public void Build148_RemovesRetiredProjectWorkflowHooksFromShellCodeBehind()
    {
        var shell = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.xaml.cs");
        Assert.DoesNotContain("_projectsWorkflowInitialized", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeProjectsWorkflow(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshProjects(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyProjectsFilter(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderProjects(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyProjectProductionMetadata(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProjectOnScreenText(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProjectVisualPlan(", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredWorkspaceCleanupRemainsUntilLegacyXamlIsRemoved()
    {
        var cleanup = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizOnlyCleanup.cs");
        Assert.Contains("RemoveLegacyFactVideoWorkspaceSurfaces", cleanup, StringComparison.Ordinal);
        Assert.Contains("retired generic Dashboard/Projects/Production/Media/Asset Review workspace", cleanup, StringComparison.Ordinal);
    }

    private static bool RepositoryFileExists(string relativePath) =>
        FindRepositoryFile(relativePath) is not null;

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = FindRepositoryFile(relativePath) ?? throw new FileNotFoundException(relativePath);
        return File.ReadAllText(path);
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
