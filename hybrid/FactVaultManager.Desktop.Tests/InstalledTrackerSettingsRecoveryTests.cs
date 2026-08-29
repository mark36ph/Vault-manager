using System.Text.Json.Nodes;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledTrackerSettingsRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FactVaultManager-TrackerRecovery-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecoversConfiguredTrackerFromLegacyData()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var legacySettings = Path.Combine(_root, "legacy", "data", "settings.json");
        const string key = "tracker-test-key-1234567890";
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        FactburstTrackerSettingsStore.Save(legacySettings, "https://go.factburstquiz.com", key);

        var result = InstalledTrackerSettingsRecovery.Run(appDataRoot, [legacySettings]);

        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var installed = FactburstTrackerSettingsStore.Load(installedSettings);
        Assert.True(result.Recovered);
        Assert.False(result.ExistingConfigured);
        Assert.True(installed.IsConfigured);
        Assert.Equal("https://go.factburstquiz.com", installed.BaseUrl);
        Assert.Equal(key, installed.ApiKey);
        Assert.True(File.Exists(FactburstTrackerSettingsStore.PathFor(legacySettings)));
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-tracker-settings-recovery-v2.json")));
    }

    [Fact]
    public void RecoversFromDirectTrackerFileInUnexpectedLegacyLayout()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var legacySettings = Path.Combine(_root, "legacy", "hybrid", "FactVaultManager.Desktop", "data", "placeholder.json");
        const string key = "tracker-direct-file-key-123456";
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        FactburstTrackerSettingsStore.Save(legacySettings, "https://go.factburstquiz.com", key);
        var directTracker = FactburstTrackerSettingsStore.PathFor(legacySettings);

        var result = InstalledTrackerSettingsRecovery.Run(
            appDataRoot,
            Array.Empty<string>(),
            [directTracker]);

        var installed = FactburstTrackerSettingsStore.Load(Path.Combine(appDataRoot, "data", "settings.json"));
        Assert.True(result.Recovered);
        Assert.Equal("legacy-tracker-file", result.Source);
        Assert.True(installed.IsConfigured);
        Assert.Equal(key, installed.ApiKey);
    }

    [Fact]
    public void CandidateTrackerPathsSearchRecordedDevelopmentRoot()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var developmentRoot = Path.Combine(_root, "custom-development-location");
        var trackerDirectory = Path.Combine(developmentRoot, "hybrid", "FactVaultManager.Desktop", "data");
        var legacySettings = Path.Combine(trackerDirectory, "placeholder.json");
        Directory.CreateDirectory(trackerDirectory);
        FactburstTrackerSettingsStore.Save(legacySettings, "https://go.factburstquiz.com", "tracker-search-key-123456789");
        var trackerPath = FactburstTrackerSettingsStore.PathFor(legacySettings);

        Directory.CreateDirectory(appDataRoot);
        File.WriteAllText(Path.Combine(appDataRoot, "development-root.txt"), developmentRoot);

        var candidates = InstalledTrackerSettingsRecovery.CandidateTrackerPaths(appDataRoot).ToList();

        Assert.Contains(candidates, candidate =>
            string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(trackerPath), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreservesExistingInstalledTracker()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var legacySettings = Path.Combine(_root, "legacy", "data", "settings.json");
        const string installedKey = "installed-tracker-key-123456";
        const string legacyKey = "legacy-tracker-key-123456789";
        Directory.CreateDirectory(Path.GetDirectoryName(installedSettings)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        FactburstTrackerSettingsStore.Save(installedSettings, "https://tracker.example.com", installedKey);
        FactburstTrackerSettingsStore.Save(legacySettings, "https://go.factburstquiz.com", legacyKey);

        var result = InstalledTrackerSettingsRecovery.Run(appDataRoot, [legacySettings]);

        var installed = FactburstTrackerSettingsStore.Load(installedSettings);
        Assert.False(result.Recovered);
        Assert.True(result.ExistingConfigured);
        Assert.Equal("https://tracker.example.com", installed.BaseUrl);
        Assert.Equal(installedKey, installed.ApiKey);
    }

    [Fact]
    public void DoesNotResurrectTrackerAfterRecoveredConfigIsCleared()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var legacySettings = Path.Combine(_root, "legacy", "data", "settings.json");
        const string key = "tracker-test-key-1234567890";
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        FactburstTrackerSettingsStore.Save(legacySettings, "https://go.factburstquiz.com", key);

        var first = InstalledTrackerSettingsRecovery.Run(appDataRoot, [legacySettings]);
        Assert.True(first.Recovered);

        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        File.Delete(FactburstTrackerSettingsStore.PathFor(installedSettings));

        var second = InstalledTrackerSettingsRecovery.Run(appDataRoot, [legacySettings]);
        var installed = FactburstTrackerSettingsStore.Load(installedSettings);

        Assert.False(second.Recovered);
        Assert.True(second.SuppressedByMarker);
        Assert.False(installed.IsConfigured);
    }

    [Fact]
    public void RespectsSuccessfulBuild61RecoveryMarker()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        var directSettings = Path.Combine(_root, "legacy", "unexpected", "placeholder.json");
        Directory.CreateDirectory(Path.GetDirectoryName(directSettings)!);
        FactburstTrackerSettingsStore.Save(directSettings, "https://go.factburstquiz.com", "tracker-legacy-key-123456789");
        var directTracker = FactburstTrackerSettingsStore.PathFor(directSettings);

        Directory.CreateDirectory(appDataRoot);
        var marker = new JsonObject
        {
            ["version"] = 1,
            ["recovered"] = true,
            ["source"] = "legacy-file",
        };
        File.WriteAllText(
            Path.Combine(appDataRoot, "installed-tracker-settings-recovery-v1.json"),
            marker.ToJsonString());

        var result = InstalledTrackerSettingsRecovery.Run(
            appDataRoot,
            Array.Empty<string>(),
            [directTracker]);

        Assert.False(result.Recovered);
        Assert.True(result.SuppressedByMarker);
        Assert.False(FactburstTrackerSettingsStore.Load(Path.Combine(appDataRoot, "data", "settings.json")).IsConfigured);
    }

    [Fact]
    public void RecoversFromEnvironmentWhenLegacyFileIsUnavailable()
    {
        var appDataRoot = Path.Combine(_root, "installed");
        const string key = "environment-tracker-key-123456";

        var result = InstalledTrackerSettingsRecovery.Run(
            appDataRoot,
            Array.Empty<string>(),
            key,
            "https://tracker.example.com/");

        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var installed = FactburstTrackerSettingsStore.Load(installedSettings);
        Assert.True(result.Recovered);
        Assert.Equal("environment", result.Source);
        Assert.True(installed.IsConfigured);
        Assert.Equal("https://tracker.example.com", installed.BaseUrl);
        Assert.Equal(key, installed.ApiKey);
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
