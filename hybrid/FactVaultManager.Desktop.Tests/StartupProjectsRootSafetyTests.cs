namespace FactVaultManager.Desktop.Tests;

public sealed class StartupProjectsRootSafetyTests
{
    [Fact]
    public void StartupFolderCleanup_OnlySkipsMissingProjectsFolderConfiguration()
    {
        var shell = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.xaml.cs");
        var safety = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/DesktopDataService.StartupFolderCleanupSafety.cs");

        Assert.Contains("_data.ResumeQuizFolderCleanupSafely();", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("_data.ResumeQuizFolderCleanup();", shell, StringComparison.Ordinal);
        Assert.Contains("public void ResumeQuizFolderCleanupSafely()", safety, StringComparison.Ordinal);
        Assert.Contains("ResumeQuizFolderCleanup();", safety, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException error) when", safety, StringComparison.Ordinal);
        Assert.Contains("Set the Projects Folder in Settings first.", safety, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Exception error)", safety, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictProjectsRootGuard_RemainsForOperationsThatRequireIt()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/DesktopDataService.cs");
        Assert.Contains("Set the Projects Folder in Settings first.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build133_IsTheStartupProjectsRootHotfix()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("CurrentBuildNumber = 133", build, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
