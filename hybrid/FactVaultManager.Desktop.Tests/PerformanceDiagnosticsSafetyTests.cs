using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class PerformanceDiagnosticsSafetyTests
{
    [Fact]
    public void Build215_DiagnosticsSkipsUnreadyLegacyNavigationInsteadOfShowingPopups()
    {
        var profile = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnosticsSafeProfile.cs");
        var safety = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnosticsSafety.cs");

        Assert.Contains("IsNavigationReadyForDiagnostics", profile, StringComparison.Ordinal);
        Assert.Contains("SKIPPED — PAGE NOT READY", profile, StringComparison.Ordinal);
        Assert.Contains("no modal was shown", profile, StringComparison.Ordinal);
        Assert.Contains("Profile current sidebar", safety, StringComparison.Ordinal);
        Assert.Contains("Profile navigation by section", safety, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", safety, StringComparison.Ordinal);
        Assert.Contains("RunSafeNavigationHotspotProfileAsync", safety, StringComparison.Ordinal);
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
