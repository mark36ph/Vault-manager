using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

/// <summary>
/// Finds legacy settings stores that live deeper inside the installed application's
/// LocalAppData tree (for example, older Velopack version directories) and feeds them
/// through the normal credential recovery path.
/// </summary>
internal static class InstalledCredentialDeepRecovery
{
    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");
        try
        {
            var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
            var destinationDatabase = Path.Combine(appDataRoot, "data", "factvault.db");
            if (!Directory.Exists(appDataRoot))
                return;

            var settingsCandidates = new List<string>();
            foreach (var path in EnumerateFiles(appDataRoot, "settings.json"))
            {
                if (!PathsEqual(path, destinationSettings))
                    settingsCandidates.Add(path);
            }

            var databaseDocuments = new List<JsonObject>();
            foreach (var databasePath in EnumerateFiles(appDataRoot, "factvault.db"))
            {
                if (PathsEqual(databasePath, destinationDatabase))
                    continue;

                var document = TryLoadAppSettings(databasePath);
                if (document is not null)
                    databaseDocuments.Add(document);
            }

            if (settingsCandidates.Count > 0 || databaseDocuments.Count > 0)
                _ = InstalledCredentialRecovery.Run(appDataRoot, settingsCandidates, databaseDocuments);
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            SqliteException)
        {
            Debug.WriteLine($"Deep installed credential recovery could not complete: {error}");
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string fileName)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not enumerate installed {fileName} files: {error.Message}");
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }

    private static JsonObject? TryLoadAppSettings(string databasePath)
    {
        try
        {
            var json = DatabaseSettingsStore.LoadJson(databasePath, DatabaseSettingsStore.MainSettingsKey);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidOperationException or
            SqliteException)
        {
            Debug.WriteLine($"Could not inspect legacy settings database '{databasePath}': {error.Message}");
            return null;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
