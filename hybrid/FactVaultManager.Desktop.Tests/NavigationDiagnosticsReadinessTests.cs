using System.IO;
using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class NavigationDiagnosticsReadinessTests
{
    [Fact]
    public void NavigationHotspotProfileSkipsRoutesThatAreStillStarting()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");

        Assert.Contains("IsNavigationReadyForDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("\"Performance\" => FindLegacyNavigationButton(\"YouTube Manager\") is not null", source, StringComparison.Ordinal);
        Assert.Contains("\"Library\" => FindLegacyNavigationButton(\"Quiz History\") is not null", source, StringComparison.Ordinal);
        Assert.Contains("Sections skipped", source, StringComparison.Ordinal);
        Assert.Contains("skipped rather than invoking normal navigation error dialogs", source, StringComparison.Ordinal);
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
