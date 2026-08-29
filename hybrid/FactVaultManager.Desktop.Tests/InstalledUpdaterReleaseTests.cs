using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledUpdaterReleaseTests
{
    [Fact]
    public void BootstrapInstaller_UsesStableLatestSetupAsset()
    {
        Assert.Equal(
            "https://github.com/mark36ph/Vault-manager/releases/latest/download/FactVaultManager-win-x64-stable-Setup.exe",
            AppUpdateService.StableSetupDownloadUrl);
    }

    [Fact]
    public void ReleaseWorkflow_PublishesVersionJsonChangesFromMain()
    {
        var source = ReadRepositoryFile(".github/workflows/release-hybrid.yml");

        Assert.Contains("branches:\n      - main", Normalize(source), StringComparison.Ordinal);
        Assert.Contains("- \"version.json\"", source, StringComparison.Ordinal);
        Assert.Contains("Get-Content version.json -Raw | ConvertFrom-Json", source, StringComparison.Ordinal);
        Assert.Contains("\"--packId\", \"FactVaultManager\"", source, StringComparison.Ordinal);
        Assert.Contains("\"--packTitle\", \"Factburst Quiz Manager\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_AllowsUnsignedPublishingWhenCertificateIsMissing()
    {
        var source = ReadRepositoryFile(".github/workflows/release-hybrid.yml");

        Assert.Contains("available=false", source, StringComparison.Ordinal);
        Assert.Contains("Publishing an unsigned installer; Velopack updates remain enabled", source, StringComparison.Ordinal);
        Assert.Contains("steps.signing.outputs.available", source, StringComparison.Ordinal);
        Assert.Contains("Validate Windows installer output", source, StringComparison.Ordinal);
        Assert.Contains("releases.win-x64-stable.json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Release publishing requires WINDOWS_SIGNING_PFX_BASE64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_StillSignsAndVerifiesWhenCertificateExists()
    {
        var source = ReadRepositoryFile(".github/workflows/release-hybrid.yml");

        Assert.Contains("available=true", source, StringComparison.Ordinal);
        Assert.Contains("--signParams", source, StringComparison.Ordinal);
        Assert.Contains("if: steps.signing.outputs.available == 'true'", source, StringComparison.Ordinal);
        Assert.Contains("signtool.FullName verify /pa /v", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceCopy_RegistersOneTimeInstallerBootstrapOnUpdatesButton()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstalledUpdaterUi.cs");

        Assert.Contains("window._updates.IsInstalled", source, StringComparison.Ordinal);
        Assert.Contains("BootstrapInstallAsync", source, StringComparison.Ordinal);
        Assert.Contains("Application.Current?.Shutdown()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("current signed Factburst", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionHeaderGuard_CollapsesImmediatelyIfAnotherRefreshMakesItVisible()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotShellActivationFix.cs");

        Assert.Contains("production.IsVisibleChanged", source, StringComparison.Ordinal);
        Assert.Contains("production.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

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
