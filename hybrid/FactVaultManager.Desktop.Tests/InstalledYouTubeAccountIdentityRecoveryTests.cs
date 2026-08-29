using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledYouTubeAccountIdentityRecoveryTests
{
    [Fact]
    public void RecoversMissingApprovedChannelIdentityWithoutOverwritingOtherSettings()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(installedSettings)!);
        File.WriteAllText(installedSettings,
            """{ "general": { "theme": "dark" }, "youtube": { "oauth_client_id": "client.apps.googleusercontent.com" } }""");

        var legacySettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        File.WriteAllText(legacySettings,
            """{ "youtube": { "approved_channel_id": "UC123", "approved_channel_name": "FactBurst" } }""");

        var recovered = InstalledYouTubeAccountIdentityRecovery.Run(appDataRoot, [legacySettings]);

        Assert.Equal(2, recovered);
        var settings = JsonNode.Parse(File.ReadAllText(installedSettings))!;
        Assert.Equal("dark", settings["general"]!["theme"]!.GetValue<string>());
        Assert.Equal("client.apps.googleusercontent.com", settings["youtube"]!["oauth_client_id"]!.GetValue<string>());
        Assert.Equal("UC123", settings["youtube"]!["approved_channel_id"]!.GetValue<string>());
        Assert.Equal("FactBurst", settings["youtube"]!["approved_channel_name"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-youtube-account-identity-recovery-v1.json")));
    }

    [Fact]
    public void PreservesExistingInstalledChannelIdentity()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(installedSettings)!);
        File.WriteAllText(installedSettings,
            """{ "youtube": { "approved_channel_id": "UC-INSTALLED", "approved_channel_name": "Installed" } }""");

        var legacySettings = Path.Combine(sandbox.Path, "legacy", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        File.WriteAllText(legacySettings,
            """{ "youtube": { "approved_channel_id": "UC-OLD", "approved_channel_name": "Old" } }""");

        Assert.Equal(0, InstalledYouTubeAccountIdentityRecovery.Run(appDataRoot, [legacySettings]));
        var settings = JsonNode.Parse(File.ReadAllText(installedSettings))!;
        Assert.Equal("UC-INSTALLED", settings["youtube"]!["approved_channel_id"]!.GetValue<string>());
        Assert.Equal("Installed", settings["youtube"]!["approved_channel_name"]!.GetValue<string>());
    }

    [Fact]
    public void DoesNotRestoreARecoveredFieldAfterUserClearsIt()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var installedSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(installedSettings)!);
        File.WriteAllText(installedSettings, """{ "youtube": { "approved_channel_id": "" } }""");

        var legacySettings = Path.Combine(sandbox.Path, "legacy", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySettings)!);
        File.WriteAllText(legacySettings, """{ "youtube": { "approved_channel_id": "UC123" } }""");

        Assert.Equal(1, InstalledYouTubeAccountIdentityRecovery.Run(appDataRoot, [legacySettings]));
        var settings = JsonNode.Parse(File.ReadAllText(installedSettings)) as JsonObject ?? new JsonObject();
        settings["youtube"]!["approved_channel_id"] = "";
        File.WriteAllText(installedSettings, settings.ToJsonString());

        Assert.Equal(0, InstalledYouTubeAccountIdentityRecovery.Run(appDataRoot, [legacySettings]));
        Assert.Equal("", JsonNode.Parse(File.ReadAllText(installedSettings))!["youtube"]!["approved_channel_id"]!.GetValue<string>());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.YouTubeIdentityRecovery.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
