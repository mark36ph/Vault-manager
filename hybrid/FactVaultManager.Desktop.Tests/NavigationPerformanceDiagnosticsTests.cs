using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class NavigationPerformanceDiagnosticsTests
{
    [Fact]
    public void Build202_NavigationHotspotProfileWaitsForStableWpfLayout()
    {
        var profiler = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");

        Assert.Contains("NavigateAndWaitForStableLayoutAsync", profiler, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", profiler, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateAndWaitAsync(button)", profiler, StringComparison.Ordinal);
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
