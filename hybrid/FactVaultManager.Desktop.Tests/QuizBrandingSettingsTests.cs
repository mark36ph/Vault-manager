using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBrandingSettingsTests
{
    [Fact]
    public void SaveLogoPath_PersistsSelectionAcrossReloads_AndPreservesOtherSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "data", "settings.json");
        var logoPath = Path.Combine(root, "quiz-logo.png");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            Directory.CreateDirectory(root);
            File.WriteAllText(settingsPath, "{\"general\":{\"theme\":\"light\"}}");
            File.WriteAllBytes(logoPath, [0x89, 0x50, 0x4E, 0x47]);

            QuizBranding.SaveLogoPath(settingsPath, logoPath);

            var reloaded = QuizBranding.LoadLogoPath(settingsPath, root);
            Assert.Equal(Path.GetFullPath(logoPath), reloaded);

            var settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
            Assert.Equal("light", settings?["general"]?["theme"]?.GetValue<string>());
            Assert.Equal(Path.GetFullPath(logoPath), settings?["quiz"]?["logo_path"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveLogoPath_Clear_RemovesRememberedSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "data", "settings.json");
        var logoPath = Path.Combine(root, "quiz-logo.png");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(logoPath, [0x89, 0x50, 0x4E, 0x47]);

            QuizBranding.SaveLogoPath(settingsPath, logoPath);
            QuizBranding.SaveLogoPath(settingsPath, "");

            Assert.Equal("", QuizBranding.LoadLogoPath(settingsPath, root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
