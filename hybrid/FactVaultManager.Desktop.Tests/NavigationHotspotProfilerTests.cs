using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class NavigationHotspotProfilerTests
{
    [Fact]
    public void Build189_AddsPerSectionNavigationHotspotProfiler()
    {
        var profiler = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");

        Assert.Contains("Profile navigation by section", profiler, StringComparison.Ordinal);
        Assert.Contains("RunNavigationHotspotProfileAsync", profiler, StringComparison.Ordinal);
        Assert.Contains("const int cycles = 10", profiler, StringComparison.Ordinal);
        Assert.Contains("NavigationHotspotRow", profiler, StringComparison.Ordinal);
        Assert.Contains("HOTSPOT", profiler, StringComparison.Ordinal);
        Assert.Contains("AverageMs", profiler, StringComparison.Ordinal);
        Assert.Contains("P95Ms", profiler, StringComparison.Ordinal);
        Assert.Contains("MaxMs", profiler, StringComparison.Ordinal);
        Assert.Contains("Samples50", profiler, StringComparison.Ordinal);
        Assert.Contains("Samples100", profiler, StringComparison.Ordinal);
    }

    [Fact]
    public void Build192_AttachesHotspotProfilerWhenDiagnosticsPageLoads()
    {
        var profiler = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");
        var recommendations = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnosticsRecommendations.cs");

        Assert.Contains("EnsureNavigationHotspotProfilerButton", profiler, StringComparison.Ordinal);
        Assert.Contains("window.EnsureNavigationHotspotProfilerButton();", recommendations, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterNavigationHotspotProfilerUi", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterClassHandler(\n            typeof(Button)", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("attempts >= 20", profiler, StringComparison.Ordinal);
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
