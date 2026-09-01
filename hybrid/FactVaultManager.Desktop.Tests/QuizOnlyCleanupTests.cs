namespace FactVaultManager.Desktop.Tests;

public sealed class QuizOnlyCleanupTests
{
    [Fact]
    public void Build143_RemovesLegacyStockProvidersFromActiveApiConnections()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizOnlyCleanup.cs");

        Assert.Contains("RemoveSettingsSection(page, \"Image providers\")", source, StringComparison.Ordinal);
        Assert.Contains("_apiConnectionTests.Remove(\"pixabay\")", source, StringComparison.Ordinal);
        Assert.Contains("_apiConnectionTests.Remove(\"pexels\")", source, StringComparison.Ordinal);
        Assert.Contains("PexelsKeyPasswordBox.Password = \"\"", source, StringComparison.Ordinal);
        Assert.Contains("PixabayKeyPasswordBox.Password = \"\"", source, StringComparison.Ordinal);
        Assert.Contains("node.Remove(\"images\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build143_RemovesLegacyFactVideoSettingsAndWorkspaceEntryPoints()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizOnlyCleanup.cs");

        Assert.Contains("_settingsPages.Remove(\"images\")", source, StringComparison.Ordinal);
        Assert.Contains("_settingsNavButtons.Remove(\"images\")", source, StringComparison.Ordinal);
        Assert.Contains("⌂ Dashboard", source, StringComparison.Ordinal);
        Assert.Contains("▤ Projects", source, StringComparison.Ordinal);
        Assert.Contains("▷ Production", source, StringComparison.Ordinal);
        Assert.Contains("□ Media Library", source, StringComparison.Ordinal);
        Assert.Contains("◉ Asset Review", source, StringComparison.Ordinal);
        Assert.Contains("Math.Min(5, MainTabs.Items.Count)", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex = _quizTabIndex", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build143_InitializesQuizOnlyCleanupWithoutRemovingQuizProductionDependencies()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var version = ReadRepositoryFile("version.json");

        Assert.Contains("CurrentBuildNumber = 143", buildInfo, StringComparison.Ordinal);
        Assert.Contains("InitializeQuizOnlyCleanup();", buildInfo, StringComparison.Ordinal);
        Assert.Contains("\"latest_version\": \"1.0.118\"", version, StringComparison.Ordinal);
        Assert.Contains("Resolve", version, StringComparison.Ordinal);
        Assert.Contains("FFmpeg", version, StringComparison.Ordinal);
        Assert.Contains("OpenAI", version, StringComparison.Ordinal);
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
