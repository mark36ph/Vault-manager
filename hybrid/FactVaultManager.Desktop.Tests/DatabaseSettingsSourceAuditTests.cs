namespace FactVaultManager.Desktop.Tests;

public sealed class DatabaseSettingsSourceAuditTests
{
    [Fact]
    public void GlobalSettingsStores_AllUseDatabaseSettingsLayer()
    {
        var desktop = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/DesktopDataService.cs");
        var settingsUi = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.SettingsWorkflow.cs");
        var autopilot = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/AutopilotScheduleTarget.cs");
        var tracker = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/FactburstTrackerSettings.cs");
        var resolve = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/QuizResolveExportPreferences.cs");
        var database = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/DatabaseSettingsStore.cs");

        Assert.Contains("AppSettingsDocumentStore.Load(_settingsPath)", desktop, StringComparison.Ordinal);
        Assert.Contains("AppSettingsDocumentStore.Save(_settingsPath, node)", desktop, StringComparison.Ordinal);
        Assert.Contains("_data.SaveSettingsDocument(node);", settingsUi, StringComparison.Ordinal);
        Assert.Contains("DatabaseSettingsStore.AutopilotPreferencesKey", autopilot, StringComparison.Ordinal);
        Assert.Contains("DatabaseSettingsStore.TrackerSettingsKey", tracker, StringComparison.Ordinal);
        Assert.Contains("AppSettingsDocumentStore.Load(settingsPath)", resolve, StringComparison.Ordinal);
        Assert.Contains("AppSettingsDocumentStore.Save(settingsPath, root)", resolve, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS app_settings", database, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT(setting_key) DO UPDATE", database, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText(_data.SettingsPath", settingsUi, StringComparison.Ordinal);
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
