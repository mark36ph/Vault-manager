namespace FactVaultManager.Desktop.Tests;

public sealed class NavigationPerformanceDiagnosticsCurrentSidebarTests
{
    [Fact]
    public void Build207_HotspotProfilerUsesCurrentFactburstSidebar()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");

        Assert.Contains("GetCurrentFactburstNavigationButtons()", source, StringComparison.Ordinal);
        Assert.Contains("_autopilotNavButtons.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("\"Autopilot\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Library\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Web Analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Users\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Comments\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SEO\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Website\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Settings\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryNavigationPanel.Children", source, StringComparison.Ordinal);
        Assert.DoesNotContain("int.TryParse(button.Tag?.ToString(), out var parsed)", source, StringComparison.Ordinal);
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
