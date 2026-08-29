using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledCredentialRecoveryTests
{
    [Fact]
    public void RecoversMissingCredentialsWithoutChangingSourceOrPreferences()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(
            destinationSettings,
            """
            {
              "general": {
                "theme": "dark",
                "projects_folder": "D:\\Quiz Projects"
              },
              "ai": {
                "model": "gpt-5-mini",
                "api_key": ""
              }
            }
            """);

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        var sourceText =
            """
            {
              "ai": { "api_key": "openai-source" },
              "images": {
                "pexels_api_key": "pexels-source",
                "pixabay_api_key": "pixabay-source"
              },
              "youtube": {
                "api_key": "youtube-source",
                "oauth_client_secret": "youtube-secret-source",
                "oauth_refresh_token": "youtube-refresh-source"
              },
              "facebook": { "page_access_token": "facebook-source" },
              "instagram": { "access_token": "instagram-source" }
            }
            """;
        File.WriteAllText(sourceSettings, sourceText);

        var result = InstalledCredentialRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.Equal(8, result.RecoveredCount);
        Assert.Equal(0, result.ClearedInvalidCount);
        Assert.True(result.SettingsChanged);
        Assert.Equal(sourceText, File.ReadAllText(sourceSettings));

        var migrated = ReadObject(destinationSettings);
        Assert.Equal("dark", migrated["general"]!["theme"]!.GetValue<string>());
        Assert.Equal("D:\\Quiz Projects", migrated["general"]!["projects_folder"]!.GetValue<string>());
        Assert.Equal("gpt-5-mini", migrated["ai"]!["model"]!.GetValue<string>());
        AssertCredential(migrated, "ai", "api_key", "openai-source");
        AssertCredential(migrated, "images", "pexels_api_key", "pexels-source");
        AssertCredential(migrated, "images", "pixabay_api_key", "pixabay-source");
        AssertCredential(migrated, "youtube", "api_key", "youtube-source");
        AssertCredential(migrated, "youtube", "oauth_client_secret", "youtube-secret-source");
        AssertCredential(migrated, "youtube", "oauth_refresh_token", "youtube-refresh-source");
        AssertCredential(migrated, "facebook", "page_access_token", "facebook-source");
        AssertCredential(migrated, "instagram", "access_token", "instagram-source");

        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-credential-recovery-v1.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(appDataRoot, "credential-recovery-backup"), "*.json"));
    }

    [Fact]
    public void PreservesValidInstalledCredentialWhileRecoveringMissingOnes()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(
            destinationSettings,
            $$"""
            {
              "ai": { "api_key": "{{JsonEscape(LocalSecretProtector.Protect("installed-openai"))}}" },
              "images": { "pexels_api_key": "" }
            }
            """);

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(
            sourceSettings,
            """
            {
              "ai": { "api_key": "old-openai" },
              "images": { "pexels_api_key": "source-pexels" }
            }
            """);

        var result = InstalledCredentialRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.Equal(1, result.RecoveredCount);
        var migrated = ReadObject(destinationSettings);
        AssertCredential(migrated, "ai", "api_key", "installed-openai");
        AssertCredential(migrated, "images", "pexels_api_key", "source-pexels");
    }

    [Fact]
    public void ClearsOnlyUnusableCiphertextWhenNoRecoverySourceExists()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(
            destinationSettings,
            $$"""
            {
              "ai": { "api_key": "{{JsonEscape(LocalSecretProtector.Protect("valid-openai"))}}" },
              "images": { "pexels_api_key": "dpapi:v1:not-base64" },
              "general": { "theme": "dark" }
            }
            """);

        var result = InstalledCredentialRecovery.Run(appDataRoot, Array.Empty<string>());

        Assert.Equal(0, result.RecoveredCount);
        Assert.Equal(1, result.ClearedInvalidCount);
        Assert.True(result.SettingsChanged);

        var migrated = ReadObject(destinationSettings);
        AssertCredential(migrated, "ai", "api_key", "valid-openai");
        Assert.Equal("", migrated["images"]!["pexels_api_key"]!.GetValue<string>());
        Assert.Equal("dark", migrated["general"]!["theme"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(appDataRoot, "installed-credential-recovery-v1.json")));
    }

    [Fact]
    public void DoesNotRestoreCredentialUserClearedAfterSuccessfulRecovery()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        File.WriteAllText(destinationSettings, """{ "ai": { "api_key": "" } }""");

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(sourceSettings, """{ "ai": { "api_key": "source-openai" } }""");

        var first = InstalledCredentialRecovery.Run(appDataRoot, [sourceSettings]);
        Assert.Equal(1, first.RecoveredCount);

        var installed = ReadObject(destinationSettings);
        installed["ai"]!["api_key"] = "";
        File.WriteAllText(destinationSettings, installed.ToJsonString());

        var second = InstalledCredentialRecovery.Run(appDataRoot, [sourceSettings]);

        Assert.Equal(0, second.RecoveredCount);
        Assert.False(second.SettingsChanged);
        Assert.Equal("", ReadObject(destinationSettings)["ai"]!["api_key"]!.GetValue<string>());
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object.");

    private static void AssertCredential(
        JsonObject root,
        string section,
        string key,
        string expected)
    {
        var stored = root[section]![key]!.GetValue<string>();
        Assert.StartsWith("dpapi:v1:", stored);
        Assert.Equal(expected, LocalSecretProtector.Unprotect(stored));
    }

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.CredentialRecovery.Tests",
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
