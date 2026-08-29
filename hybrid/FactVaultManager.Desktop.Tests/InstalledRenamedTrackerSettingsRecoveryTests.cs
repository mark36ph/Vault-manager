using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledRenamedTrackerSettingsRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FactVaultManager-RenamedTrackerRecovery-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecoversTrackerFromRenamedDevelopmentBackupSibling()
    {
        var documents = Path.Combine(_root, "Documents");
        var originalDevelopmentRoot = Path.Combine(documents, "FactVaultManager");
        var renamedBackupRoot = Path.Combine(documents, "FactVaultManager-backup");
        var appDataRoot = Path.Combine(_root, "installed");
        var backupSettings = Path.Combine(renamedBackupRoot, "data", "settings.json");
        var testValue = new string('x', 32);

        Directory.CreateDirectory(Path.GetDirectoryName(backupSettings)!);
        Directory.CreateDirectory(appDataRoot);
        FactburstTrackerSettingsStore.Save(
            backupSettings,
            "https://go.factburstquiz.com",
            testValue);
        File.WriteAllText(
            Path.Combine(appDataRoot, "development-root.txt"),
            originalDevelopmentRoot);

        var backupTrackerPath = FactburstTrackerSettingsStore.PathFor(backupSettings);
        Assert.True(File.Exists(backupTrackerPath));
        Assert.False(Directory.Exists(originalDevelopmentRoot));

        var candidates = InstalledRenamedTrackerSettingsRecovery
            .CandidateTrackerPaths(appDataRoot)
            .ToList();

        Assert.Contains(candidates, candidate =>
            string.Equals(
                Path.GetFullPath(candidate),
                Path.GetFullPath(backupTrackerPath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal));

        var result = InstalledRenamedTrackerSettingsRecovery.Run(appDataRoot);
        var installed = FactburstTrackerSettingsStore.Load(
            Path.Combine(appDataRoot, "data", "settings.json"));

        Assert.True(result.Recovered);
        Assert.True(installed.IsConfigured);
        Assert.Equal("https://go.factburstquiz.com", installed.BaseUrl);
        Assert.Equal(testValue, installed.ApiKey);
        Assert.True(File.Exists(backupTrackerPath));
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-tracker-settings-recovery-v2.json")));
    }

    [Fact]
    public void DoesNotTreatUnrelatedPrefixFolderAsRenamedDevelopmentRoot()
    {
        var documents = Path.Combine(_root, "Documents");
        var originalDevelopmentRoot = Path.Combine(documents, "FactVaultManager");
        var unrelatedRoot = Path.Combine(documents, "FactVaultManagerArchive");
        var appDataRoot = Path.Combine(_root, "installed");
        var unrelatedSettings = Path.Combine(unrelatedRoot, "data", "settings.json");

        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedSettings)!);
        Directory.CreateDirectory(appDataRoot);
        FactburstTrackerSettingsStore.Save(
            unrelatedSettings,
            "https://go.factburstquiz.com",
            new string('y', 32));
        File.WriteAllText(
            Path.Combine(appDataRoot, "development-root.txt"),
            originalDevelopmentRoot);

        var candidates = InstalledRenamedTrackerSettingsRecovery
            .CandidateTrackerPaths(appDataRoot)
            .ToList();

        Assert.DoesNotContain(candidates, candidate =>
            candidate.Contains("FactVaultManagerArchive", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
