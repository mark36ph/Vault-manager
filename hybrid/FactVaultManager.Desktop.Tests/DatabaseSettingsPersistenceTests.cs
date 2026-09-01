using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class DatabaseSettingsPersistenceTests
{
    [Fact]
    public void MainSettings_MigrateToDatabase_AndSurviveMirrorDeletion()
    {
        var root = CreateTestRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            CreateDatabase(root);
            var legacy = new JsonObject
            {
                ["general"] = new JsonObject
                {
                    ["projects_folder"] = @"Z:\FactVaultManager\Quizzes",
                    ["start_maximized"] = true,
                },
            };
            File.WriteAllText(settingsPath, legacy.ToJsonString());

            var migrated = AppSettingsDocumentStore.Load(settingsPath);
            Assert.Equal(@"Z:\FactVaultManager\Quizzes", migrated["general"]?["projects_folder"]?.GetValue<string>());
            Assert.NotNull(DatabaseSettingsStore.LoadJson(
                Path.Combine(root, "factvault.db"),
                DatabaseSettingsStore.MainSettingsKey));

            File.Delete(settingsPath);
            var restored = AppSettingsDocumentStore.Load(settingsPath);
            Assert.Equal(@"Z:\FactVaultManager\Quizzes", restored["general"]?["projects_folder"]?.GetValue<string>());
            Assert.True(restored["general"]?["start_maximized"]?.GetValue<bool>());
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Fact]
    public void MainSettings_DatabaseWinsOverStaleLegacyMirror()
    {
        var root = CreateTestRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            CreateDatabase(root);
            AppSettingsDocumentStore.Save(settingsPath, new JsonObject
            {
                ["general"] = new JsonObject { ["projects_folder"] = "database-value" },
            });

            File.WriteAllText(settingsPath, new JsonObject
            {
                ["general"] = new JsonObject { ["projects_folder"] = "stale-file-value" },
            }.ToJsonString());

            var loaded = AppSettingsDocumentStore.Load(settingsPath);
            Assert.Equal("database-value", loaded["general"]?["projects_folder"]?.GetValue<string>());
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Fact]
    public void AutopilotPreferences_SurviveLegacyFileDeletion()
    {
        var root = CreateTestRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            CreateDatabase(root);
            AutopilotSchedulePreferencesStore.Save(settingsPath, new AutopilotSchedulePreferences
            {
                AutoFillEnabled = false,
                TargetDays = 21,
                LastAutomaticFillUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            });

            var mirror = AutopilotSchedulePreferencesStore.PathFor(settingsPath);
            Assert.True(File.Exists(mirror));
            File.Delete(mirror);

            var loaded = AutopilotSchedulePreferencesStore.Load(settingsPath);
            Assert.False(loaded.AutoFillEnabled);
            Assert.Equal(21, loaded.TargetDays);
            Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), loaded.LastAutomaticFillUtc);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Fact]
    public void TrackerSettings_AreProtectedInDatabase_AndSurviveLegacyFileDeletion()
    {
        var root = CreateTestRoot();
        try
        {
            const string apiKey = "0123456789abcdef0123456789abcdef";
            var settingsPath = Path.Combine(root, "settings.json");
            CreateDatabase(root);
            FactburstTrackerSettingsStore.Save(settingsPath, "https://go.factburstquiz.com", apiKey);

            var raw = DatabaseSettingsStore.LoadJson(
                Path.Combine(root, "factvault.db"),
                DatabaseSettingsStore.TrackerSettingsKey);
            Assert.NotNull(raw);
            Assert.DoesNotContain(apiKey, raw!, StringComparison.Ordinal);

            File.Delete(FactburstTrackerSettingsStore.PathFor(settingsPath));
            var loaded = FactburstTrackerSettingsStore.Load(settingsPath);
            Assert.Equal("https://go.factburstquiz.com", loaded.BaseUrl);
            Assert.Equal(apiKey, loaded.ApiKey);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Fact]
    public void ResolveExportPreferences_SurviveSettingsMirrorDeletion()
    {
        var root = CreateTestRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            CreateDatabase(root);
            QuizResolveExportPreferenceStore.Save(settingsPath, new QuizResolveExportPreferences(
                FormatIndex: 1,
                ShowCountdown: false,
                AnimateReveal: false,
                Narrate: true,
                NarrateAnswers: true,
                Voice: QuizVoiceCatalog.DefaultVoice,
                CountdownTicks: false,
                AnswerRevealSfx: false,
                UseBackgroundMusic: false,
                BackgroundMusicPath: ""));

            File.Delete(settingsPath);
            var loaded = QuizResolveExportPreferenceStore.Load(settingsPath);
            Assert.Equal(1, loaded.FormatIndex);
            Assert.False(loaded.ShowCountdown);
            Assert.False(loaded.AnimateReveal);
            Assert.True(loaded.NarrateAnswers);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Fact]
    public void SettingsWorkflow_DoesNotBypassDatabaseForExtendedPreferences()
    {
        var workflow = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.SettingsWorkflow.cs");
        var service = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/DesktopDataService.cs");

        Assert.Contains("_data.SaveSettingsDocument(node);", workflow, StringComparison.Ordinal);
        Assert.Contains("_data.LoadSettingsDocument()", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText(_data.SettingsPath", workflow, StringComparison.Ordinal);
        Assert.Contains("AppSettingsDocumentStore.Load(_settingsPath)", service, StringComparison.Ordinal);
        Assert.Contains("AppSettingsDocumentStore.Save(_settingsPath, node)", service, StringComparison.Ordinal);
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "factburst-db-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateDatabase(string root)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "factvault.db")}");
        connection.Open();
    }

    private static void DeleteTestRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
