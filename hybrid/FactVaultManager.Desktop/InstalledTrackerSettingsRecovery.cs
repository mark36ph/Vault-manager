using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

internal sealed record InstalledTrackerSettingsRecoveryResult(
    bool Recovered,
    bool ExistingConfigured,
    bool SuppressedByMarker,
    string Source);

public static class InstalledTrackerSettingsRecovery
{
    private const int RecoveryVersion = 1;
    private const string RecoveryMarkerName = "installed-tracker-settings-recovery-v1.json";
    private const string TrackerFileName = "factburst-link-tracker.json";

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(
                appDataRoot,
                CandidateSettingsPaths(appDataRoot),
                Environment.GetEnvironmentVariable("TRACKER_API_KEY"),
                Environment.GetEnvironmentVariable("FACTBURST_TRACKER_BASE_URL"));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Website tracker recovery is best-effort and must never prevent startup.
            Debug.WriteLine($"Installed tracker settings recovery could not complete: {error}");
        }
    }

    internal static InstalledTrackerSettingsRecoveryResult Run(
        string appDataRoot,
        IEnumerable<string> sourceSettingsPaths,
        string? environmentApiKey = null,
        string? environmentBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationSettingsPath = Path.Combine(appDataRoot, "data", "settings.json");
        var destinationTrackerPath = FactburstTrackerSettingsStore.PathFor(destinationSettingsPath);
        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);

        var existing = FactburstTrackerSettingsStore.Load(destinationSettingsPath);
        if (existing.IsConfigured)
        {
            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: false,
                ExistingConfigured: true,
                SuppressedByMarker: false,
                Source: "installed");
        }

        // If recovery succeeded once and the installed tracker is now missing/blank,
        // treat that as an intentional clear. Do not resurrect a legacy secret.
        if (WasRecovered(markerPath))
        {
            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: false,
                ExistingConfigured: false,
                SuppressedByMarker: true,
                Source: "marker");
        }

        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var candidateSettingsPath in sourceSettingsPaths)
        {
            if (string.IsNullOrWhiteSpace(candidateSettingsPath))
                continue;

            string sourceSettings;
            string sourceTracker;
            try
            {
                sourceSettings = Path.GetFullPath(candidateSettingsPath);
                sourceTracker = FactburstTrackerSettingsStore.PathFor(sourceSettings);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (PathsEqual(sourceTracker, destinationTrackerPath) ||
                !seen.Add(sourceTracker) ||
                !File.Exists(sourceTracker))
            {
                continue;
            }

            var source = FactburstTrackerSettingsStore.Load(sourceSettings);
            if (!source.IsConfigured)
                continue;

            BackupInvalidDestination(destinationTrackerPath, appDataRoot);
            FactburstTrackerSettingsStore.Save(
                destinationSettingsPath,
                source.BaseUrl,
                source.ApiKey);
            WriteMarker(markerPath, "legacy-file");

            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: true,
                ExistingConfigured: false,
                SuppressedByMarker: false,
                Source: "legacy-file");
        }

        var environmentKey = (environmentApiKey ?? "").Trim();
        if (environmentKey.Length >= 16)
        {
            BackupInvalidDestination(destinationTrackerPath, appDataRoot);
            FactburstTrackerSettingsStore.Save(
                destinationSettingsPath,
                FactburstTrackerSettingsStore.PreferredBaseUrl(environmentBaseUrl),
                environmentKey);
            WriteMarker(markerPath, "environment");

            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: true,
                ExistingConfigured: false,
                SuppressedByMarker: false,
                Source: "environment");
        }

        return new InstalledTrackerSettingsRecoveryResult(
            Recovered: false,
            ExistingConfigured: false,
            SuppressedByMarker: false,
            Source: "none");
    }

    internal static IEnumerable<string> CandidateSettingsPaths(string appDataRoot)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var candidate in RawCandidateSettingsPaths(appDataRoot))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (seen.Add(fullPath))
                yield return fullPath;
        }
    }

    private static IEnumerable<string> RawCandidateSettingsPaths(string appDataRoot)
    {
        var developmentRootMarker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(developmentRootMarker))
        {
            string root;
            try { root = File.ReadAllText(developmentRootMarker).Trim(); }
            catch { root = ""; }
            if (root.Length > 0)
                yield return Path.Combine(root, "data", "settings.json");
        }

        var migrationMarker = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (File.Exists(migrationMarker))
        {
            JsonObject? marker = null;
            try { marker = JsonNode.Parse(File.ReadAllText(migrationMarker)) as JsonObject; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }

            var sourceData = marker?["source_data"]?.GetValue<string>()?.Trim() ?? "";
            if (sourceData.Length > 0)
                yield return Path.Combine(sourceData, "settings.json");
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = null;
            try { directory = new DirectoryInfo(start); }
            catch { }
            while (directory is not null)
            {
                if (LooksLikeRepositoryRoot(directory.FullName))
                    yield return Path.Combine(directory.FullName, "data", "settings.json");
                directory = directory.Parent;
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in CommonCheckoutRoots(profile, documents))
            yield return Path.Combine(root, "data", "settings.json");

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in CommonCheckoutRoots(oneDrive, Path.Combine(oneDrive, "Documents")))
            yield return Path.Combine(root, "data", "settings.json");

        if (Directory.Exists(appDataRoot))
        {
            string[] topLevel;
            try { topLevel = Directory.GetDirectories(appDataRoot, "*", SearchOption.TopDirectoryOnly); }
            catch { topLevel = Array.Empty<string>(); }

            foreach (var directory in topLevel)
            {
                yield return Path.Combine(directory, "settings.json");
                yield return Path.Combine(directory, "data", "settings.json");
            }
        }

        foreach (var backupRootName in new[] { "migration-backup", "credential-recovery-backup" })
        {
            var backupRoot = Path.Combine(appDataRoot, backupRootName);
            if (!Directory.Exists(backupRoot))
                continue;

            string[] backups;
            try { backups = Directory.GetDirectories(backupRoot, "*", SearchOption.TopDirectoryOnly); }
            catch { backups = Array.Empty<string>(); }

            foreach (var backup in backups)
            {
                yield return Path.Combine(backup, "settings.json");
                yield return Path.Combine(backup, "data", "settings.json");
            }
        }
    }

    private static IEnumerable<string> CommonCheckoutRoots(params string[] roots)
    {
        foreach (var rawRoot in roots)
        {
            var root = (rawRoot ?? "").Trim();
            if (root.Length == 0)
                continue;

            foreach (var relative in new[]
            {
                "Vault-manager",
                Path.Combine("GitHub", "Vault-manager"),
                Path.Combine("source", "repos", "Vault-manager"),
                Path.Combine("repos", "Vault-manager"),
                Path.Combine("Desktop", "Vault-manager"),
                Path.Combine("Documents", "Vault-manager"),
                Path.Combine("Documents", "GitHub", "Vault-manager"),
            })
            {
                yield return Path.Combine(root, relative);
            }
        }
    }

    private static bool LooksLikeRepositoryRoot(string path) =>
        Directory.Exists(Path.Combine(path, "hybrid", "FactVaultManager.Desktop")) &&
        (File.Exists(Path.Combine(path, "version.json")) || Directory.Exists(Path.Combine(path, ".git")));

    private static bool WasRecovered(string markerPath)
    {
        if (!File.Exists(markerPath))
            return false;

        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            return marker?["recovered"]?.GetValue<bool>() == true;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not read tracker recovery marker: {error.Message}");
            return false;
        }
    }

    private static void WriteMarker(string markerPath, string source)
    {
        var marker = new JsonObject
        {
            ["version"] = RecoveryVersion,
            ["recovered"] = true,
            ["source"] = source,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var temporary = markerPath + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static void BackupInvalidDestination(string destinationTrackerPath, string appDataRoot)
    {
        if (!File.Exists(destinationTrackerPath))
            return;

        var backupRoot = Path.Combine(appDataRoot, "tracker-recovery-backup");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(
            backupRoot,
            $"{TrackerFileName}.{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
        File.Copy(destinationTrackerPath, backup, overwrite: false);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
