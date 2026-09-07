using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class PerformanceDiagnosticsDashboardTests
{
    [Fact]
    public void Build214_DeferredStartupQueuesIndividualSteps()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("Startup.DeferredStep.", buildInfo, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredShellPhase(FinalizeApiConnectionsYouTubeButton);", buildInfo, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredShellPhase(InitializeWebsiteManagerPage);", buildInfo, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredShellPhase(InitializeQuizHistoryPage);", buildInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Build214_DiagnosticsDashboardProvidesCurrentSidebarProfileAndSafeActions()
    {
        var dashboard = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnosticsDashboard.cs");

        Assert.Contains("Diagnostic Dashboard", dashboard, StringComparison.Ordinal);
        Assert.Contains("Profile current sidebar", dashboard, StringComparison.Ordinal);
        Assert.Contains("RunNavigationHotspotProfileAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("Quick UI health scan", dashboard, StringComparison.Ordinal);
        Assert.Contains("Clear measurements", dashboard, StringComparison.Ordinal);
        Assert.Contains("Copy results", dashboard, StringComparison.Ordinal);
        Assert.Contains("Write report", dashboard, StringComparison.Ordinal);
        Assert.Contains("UniformGrid", dashboard, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 42", dashboard, StringComparison.Ordinal);
        Assert.Contains("_performanceDiagnosticsDashboardBusy", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void Build214_HidesLegacyNavigationBenchmarkInFavourOfCurrentSidebarProfiler()
    {
        var dashboard = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnosticsDashboard.cs");
        Assert.Contains("\"Benchmark navigation\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("button.Visibility = Visibility.Collapsed", dashboard, StringComparison.Ordinal);
        Assert.Contains("\"Profile current sidebar\"", dashboard, StringComparison.Ordinal);
    }

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
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
