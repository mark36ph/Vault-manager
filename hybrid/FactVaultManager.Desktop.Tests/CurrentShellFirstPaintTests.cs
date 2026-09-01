namespace FactVaultManager.Desktop.Tests;

public sealed class CurrentShellFirstPaintTests
{
    [Fact]
    public void Program_PreparesFactburstFirstPaintBeforeRunningMainWindow()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/Program.cs");
        var prepare = source.IndexOf("mainWindow.PrepareFactburstFirstPaint();", StringComparison.Ordinal);
        var run = source.IndexOf("application.Run(mainWindow);", StringComparison.Ordinal);

        Assert.True(prepare >= 0);
        Assert.True(run > prepare);
    }

    [Fact]
    public void FirstPaint_WaitsForCurrentFactburstNavigationBeforeReveal()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotShellActivationFix.cs");

        Assert.Contains("Opacity = 0;", source, StringComparison.Ordinal);
        Assert.Contains("IsFactburstFirstPaintReady()", source, StringComparison.Ordinal);
        Assert.Contains("FactburstFirstPaintNavigationKeys.All(_autopilotNavButtons.ContainsKey)", source, StringComparison.Ordinal);
        Assert.Contains("\"Website\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Web Analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Users\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Comments\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SEO\"", source, StringComparison.Ordinal);
        Assert.Contains("Opacity = 1;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationState_IsOwnedByLibraryInsteadOfHiddenUploadManager()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.PublicationState.cs");

        Assert.Contains("_quizHistoryGrid?.SelectedItem", source, StringComparison.Ordinal);
        Assert.Contains("Select a quiz in Library first.", source, StringComparison.Ordinal);
        Assert.Contains("\"Metadata\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uploadManagerGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Upload Queue\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Select a quiz in Upload Manager", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
