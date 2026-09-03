using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

/// <summary>
/// Imports only application credentials from an explicitly named database backup.
/// The live database is never replaced or copied over.
/// </summary>
internal static class InstalledCredentialBackupRecovery
{
    private const string RecoveryMarkerName = "installed-credential-backup-recovery-v1.json";
    private const string ExplicitBackupName = "credential-recovery-backup.db";
    private const string DatabaseBackupPrefix = "factvault-";

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot);
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            SqliteException)
        {
            Debug.WriteLine($"Installed credential backup recovery could not complete: {error}");
        }
    }

    internal static InstalledCredentialRecoveryResult Run(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        appDataRoot = Path.GetFullPath(appDataRoot);

        var destinationDatabase = Path.Combine(appDataRoot, "data", "factvault.db");
        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);
        var importedSources = ReadImportedSources(markerPath);
        var candidates = FindBackupCandidates(appDataRoot, destinationDatabase, importedSources);

        var recoveredCount = 0;
        var clearedInvalidCount = 0;
        var settingsChanged = false;
        var imported = new List<string>();

        foreach (var candidate in candidates)
        {
            var document = TryLoadAppSettings(candidate);
            if (document is null)
                continue;

            var result = InstalledCredentialRecovery.Run(
                appDataRoot,
                Array.Empty<string>(),
                [document]);

            recoveredCount += result.RecoveredCount;
            clearedInvalidCount += result.ClearedInvalidCount;
            settingsChanged |= result.SettingsChanged;
            imported.Add(candidate);
        }

        if (imported.Count > 0)
            WriteRecoveryMarker(markerPath, importedSources.Concat(imported));

        return new InstalledCredentialRecoveryResult(
            recoveredCount,
            clearedInvalidCount,
            settingsChanged);
    }

    private static IReadOnlyList<string> FindBackupCandidates(
        string appDataRoot,
        string destinationDatabase,
        IReadOnlySet<string> importedSources)
    {
        var dataDirectory = Path.Combine(appDataRoot, "data");
        if (!Directory.Exists(dataDirectory))
            return Array.Empty<string>();

        var candidates = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        void AddCandidate(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (PathsEqual(fullPath, destinationDatabase) ||
                    importedSources.Contains(fullPath) ||
                    !File.Exists(fullPath) ||
                    !seen.Add(fullPath))
                    return;

                candidates.Add(fullPath);
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        AddCandidate(Path.Combine(dataDirectory, ExplicitBackupName));

        IEnumerable<string> backups;
        try
        {
            backups = Directory.EnumerateFiles(dataDirectory, DatabaseBackupPrefix + "*.db", SearchOption.TopDirectoryOnly);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not enumerate database backup candidates: {error.Message}");
            return candidates;
        }

        foreach (var backup in backups)
            AddCandidate(backup);

        return candidates;
    }

    private static JsonObject? TryLoadAppSettings(string databasePath)
    {
        try
        {
            var json = DatabaseSettingsStore.LoadJson(
                databasePath,
                DatabaseSettingsStore.MainSettingsKey);
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
            Debug.WriteLine($"Could not inspect credential backup '{databasePath}': {error.Message}");
            return null;
        }
    }

    private static HashSet<string> ReadImportedSources(string markerPath)
    {
        var result = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        if (!File.Exists(markerPath))
            return result;

        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            if (marker?["sources"] is not JsonArray sources)
                return result;

            foreach (var item in sources)
            {
                var source = item?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                try
                {
                    result.Add(Path.GetFullPath(source));
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidOperationException)
        {
            Debug.WriteLine($"Could not read credential backup recovery marker: {error.Message}");
        }

        return result;
    }

    private static void WriteRecoveryMarker(string markerPath, IEnumerable<string> sources)
    {
        var sourceArray = new JsonArray();
        foreach (var source in sources
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(OperatingSystem.IsWindows()
                         ? StringComparer.OrdinalIgnoreCase
                         : StringComparer.Ordinal)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            sourceArray.Add(source);
        }

        var marker = new JsonObject
        {
            ["version"] = 1,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sources"] = sourceArray
        };

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var temporary = markerPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, marker.ToJsonString());
            File.Move(temporary, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
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
