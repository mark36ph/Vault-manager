using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledYouTubeOAuthClientIdRecoveryTests
{
    [Fact]
    public void RecoversMissingClientIdWithoutChangingOtherSettingsOrSource()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(
            destinationSettings,
            """
            {
              "general": { "theme": "dark" },
              "youtube": {
                "oauth_client_id": "",
                "approved_channel_id": "channel-123"
              }
            }
            """);

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        var sourceText =
            """
            {
              "youtube": {
                "oauth_client_id": "1234567890-example.apps.googleusercontent.com"
              }
            }
            """;
        File.WriteAllText(sourceSettings, sourceText);

        var changed = InstalledYouTubeOAuthClientIdRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.True(changed);
        Assert.Equal(sourceText, File.ReadAllText(sourceSettings));

        var migrated = ReadObject(destinationSettings);
        Assert.Equal("dark", migrated["general"]!["theme"]!.GetValue<string>());
        Assert.Equal("channel-123", migrated["youtube"]!["approved_channel_id"]!.GetValue<string>());
        Assert.Equal(
            "1234567890-example.apps.googleusercontent.com",
            migrated["youtube"]!["oauth_client_id"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-youtube-oauth-client-id-recovery-v1.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(appDataRoot, "oauth-client-id-recovery-backup"), "*.json"));
    }

    [Fact]
    public void PreservesInstalledClientId()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(
            destinationSettings,
            """{ "youtube": { "oauth_client_id": "installed-client.apps.googleusercontent.com" } }""");

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(
            sourceSettings,
            """{ "youtube": { "oauth_client_id": "old-client.apps.googleusercontent.com" } }""");

        var changed = InstalledYouTubeOAuthClientIdRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.False(changed);
        Assert.Equal(
            "installed-client.apps.googleusercontent.com",
            ReadObject(destinationSettings)["youtube"]!["oauth_client_id"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(appDataRoot, "installed-youtube-oauth-client-id-recovery-v1.json")));
    }

    [Fact]
    public void DoesNotRestoreClientIdAfterUserClearsRecoveredValue()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(destinationSettings, """{ "youtube": { "oauth_client_id": "" } }""");

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(
            sourceSettings,
            """{ "youtube": { "oauth_client_id": "source-client.apps.googleusercontent.com" } }""");

        Assert.True(InstalledYouTubeOAuthClientIdRecovery.Run(appDataRoot, [sourceSettings]));

        var installed = ReadObject(destinationSettings);
        installed["youtube"]!["oauth_client_id"] = "";
        File.WriteAllText(destinationSettings, installed.ToJsonString());

        var changed = InstalledYouTubeOAuthClientIdRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.False(changed);
        Assert.Equal("", ReadObject(destinationSettings)["youtube"]!["oauth_client_id"]!.GetValue<string>());
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object.");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.YouTubeOAuthClientIdRecovery.Tests",
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
