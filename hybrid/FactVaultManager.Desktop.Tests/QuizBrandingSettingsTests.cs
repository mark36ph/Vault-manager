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

    [Fact]
    public void ImportLogo_CopiesIntoManagedAppStorage_AndSurvivesSourceDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "app-data");
        var sourcePath = Path.Combine(root, "downloads", "my-quiz-logo.png");
        var settingsPath = Path.Combine(dataRoot, "data", "settings.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02]);

            var managedPath = QuizBranding.ImportLogo(sourcePath, dataRoot);
            QuizBranding.SaveLogoPath(settingsPath, managedPath);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(dataRoot, "data", "quiz", "branding", "quiz_logo.png")),
                managedPath);
            Assert.True(File.Exists(managedPath));
            Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(managedPath));

            File.Delete(sourcePath);

            Assert.Equal(managedPath, QuizBranding.LoadLogoPath(settingsPath, root));
            Assert.Equal(managedPath, QuizBranding.ValidateLogoPath(managedPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportLogo_ReplacesPreviousManagedFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var jpgSource = Path.Combine(root, "sources", "first.jpg");
        var pngSource = Path.Combine(root, "sources", "second.png");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jpgSource)!);
            File.WriteAllBytes(jpgSource, [0xFF, 0xD8, 0xFF, 0x01]);
            File.WriteAllBytes(pngSource, [0x89, 0x50, 0x4E, 0x47, 0x02]);

            var oldManagedPath = QuizBranding.ImportLogo(jpgSource, root);
            var newManagedPath = QuizBranding.ImportLogo(pngSource, root);

            Assert.EndsWith("quiz_logo.png", newManagedPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(newManagedPath));
            Assert.False(File.Exists(oldManagedPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManagedAlias_UsesAppCopyIfOriginalIsDeletedBeforeRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source", "logo.bmp");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, [0x42, 0x4D, 0x01, 0x02]);

            var managedPath = QuizBranding.ImportLogo(sourcePath, root);
            QuizBranding.RegisterManagedAlias(sourcePath, managedPath);
            File.Delete(sourcePath);

            Assert.Equal(managedPath, QuizBranding.ValidateLogoPath(sourcePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void LoadManagedLogoPath_RecoversImportedLogoWhenSettingIsBlank()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "data", "settings.json");
        var managedPath = Path.Combine(root, "data", "quiz", "branding", "quiz_logo.png");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
            File.WriteAllBytes(managedPath, [0x89, 0x50, 0x4E, 0x47]);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{\"quiz\":{\"logo_path\":\"\"}}");

            var loaded = QuizBranding.LoadManagedLogoPath(settingsPath, root, root);

            Assert.Equal(Path.GetFullPath(managedPath), loaded);
            var settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
            Assert.Equal(
                Path.GetFullPath(managedPath),
                settings?["quiz"]?["logo_path"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadManagedLogoPath_ImportsExternalLogoAndSurvivesSourceDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "data", "settings.json");
        var sourcePath = Path.Combine(root, "source", "factburst.png");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, [0x89, 0x50, 0x4E, 0x47]);
            QuizBranding.SaveLogoPath(settingsPath, sourcePath);

            var managedPath = QuizBranding.LoadManagedLogoPath(settingsPath, root, root);
            File.Delete(sourcePath);

            Assert.True(File.Exists(managedPath));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "data", "quiz", "branding", "quiz_logo.png")),
                managedPath);
            Assert.Equal(managedPath, QuizBranding.LoadManagedLogoPath(settingsPath, root, root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeleteManagedLogos_RemovesStoredAppCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var managedPath = Path.Combine(root, "data", "quiz", "branding", "quiz_logo.png");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
            File.WriteAllBytes(managedPath, [0x89, 0x50, 0x4E, 0x47]);

            QuizBranding.DeleteManagedLogos(root);

            Assert.False(File.Exists(managedPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
