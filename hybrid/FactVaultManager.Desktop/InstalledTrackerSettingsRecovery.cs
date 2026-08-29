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
    private const int RecoveryVersion = 2;
    private const string RecoveryMarkerName = "installed-tracker-settings-recovery-v2.json";
    private const string PreviousRecoveryMarkerName = "installed-tracker-settings-recovery-v1.json";
    private const string TrackerFileName = "factburst-link-tracker.json";
    private const int MaxSearchDirectories = 4_000;

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
                CandidateTrackerPaths(appDataRoot),
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
        string? environmentBaseUrl = null) =>
        Run(
            appDataRoot,
            sourceSettingsPaths,
            Array.Empty<string>(),
            environmentApiKey,
            environmentBaseUrl);

    internal static InstalledTrackerSettingsRecoveryResult Run(
        string appDataRoot,
        IEnumerable<string> sourceSettingsPaths,
        IEnumerable<string> sourceTrackerPaths,
        string? environmentApiKey = null,
        string? environmentBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);
        ArgumentNullException.ThrowIfNull(sourceTrackerPaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationSettingsPath = Path.Combine(appDataRoot, "data", "settings.json");
        var destinationTrackerPath = FactburstTrackerSettingsStore.PathFor(destinationSettingsPath);
        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);
        var previousMarkerPath = Path.Combine(appDataRoot, PreviousRecoveryMarkerName);

        var existing = FactburstTrackerSettingsStore.Load(destinationSettingsPath);
        if (existing.IsConfigured)
        {
            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: false,
                ExistingConfigured: true,
                SuppressedByMarker: false,
                Source: "installed");
        }

        // Respect either recovery generation. If recovery succeeded previously and the
        // installed tracker is now missing/blank, treat that as an intentional clear.
        if (WasRecovered(markerPath) || WasRecovered(previousMarkerPath))
        {
            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: false,
                ExistingConfigured: false,
                SuppressedByMarker: true,
                Source: "marker");
        }

        var seen = new HashSet<string>(PathComparer());

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

            SaveRecoveredTracker(
                appDataRoot,
                destinationSettingsPath,
                destinationTrackerPath,
                markerPath,
                source,
                "legacy-settings-sibling");

            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: true,
                ExistingConfigured: false,
                SuppressedByMarker: false,
                Source: "legacy-settings-sibling");
        }

        // Build 61 only searched for settings.json and then inferred the tracker path.
        // Older development/runtime layouts can leave the tracker file elsewhere, so
        // Build 62 also searches for the tracker file itself and reads it directly.
        foreach (var candidateTrackerPath in sourceTrackerPaths)
        {
            if (string.IsNullOrWhiteSpace(candidateTrackerPath))
                continue;

            string sourceTracker;
            try { sourceTracker = Path.GetFullPath(candidateTrackerPath); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { continue; }

            if (PathsEqual(sourceTracker, destinationTrackerPath) ||
                !seen.Add(sourceTracker) ||
                !File.Exists(sourceTracker))
            {
                continue;
            }

            var source = ReadTrackerFile(sourceTracker);
            if (!source.IsConfigured)
                continue;

            SaveRecoveredTracker(
                appDataRoot,
                destinationSettingsPath,
                destinationTrackerPath,
                markerPath,
                source,
                "legacy-tracker-file");

            return new InstalledTrackerSettingsRecoveryResult(
                Recovered: true,
                ExistingConfigured: false,
                SuppressedByMarker: false,
                Source: "legacy-tracker-file");
        }

        var environmentKey = (environmentApiKey ?? "").Trim();
        if (environmentKey.Length >= 16)
        {
            var source = new FactburstTrackerSettings(
                FactburstTrackerSettingsStore.PreferredBaseUrl(environmentBaseUrl),
                environmentKey);
            SaveRecoveredTracker(
                appDataRoot,
                destinationSettingsPath,
                destinationTrackerPath,
                markerPath,
                source,
                "environment");

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
        var seen = new HashSet<string>(PathComparer());

        foreach (var candidate in RawCandidateSettingsPaths(appDataRoot))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string fullPath;
            try { fullPath = Path.GetFullPath(candidate); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { continue; }

            if (seen.Add(fullPath))
                yield return fullPath;
        }
    }

    internal static IEnumerable<string> CandidateTrackerPaths(string appDataRoot)
    {
        var seen = new HashSet<string>(PathComparer());
        var destination = Path.GetFullPath(Path.Combine(appDataRoot, "data", TrackerFileName));

        foreach (var candidate in RawCandidateTrackerPaths(appDataRoot))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string fullPath;
            try { fullPath = Path.GetFullPath(candidate); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { continue; }

            if (!PathsEqual(fullPath, destination) && seen.Add(fullPath))
                yield return fullPath;
        }
    }

    private static IEnumerable<string> RawCandidateSettingsPaths(string appDataRoot)
    {
        var developmentRoot = ReadDevelopmentRoot(appDataRoot);
        if (developmentRoot.Length > 0)
            yield return Path.Combine(developmentRoot, "data", "settings.json");

        var sourceData = ReadMigrationSourceData(appDataRoot);
        if (sourceData.Length > 0)
            yield return Path.Combine(sourceData, "settings.json");

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

        foreach (var backupRootName in new[]
                 {
                     "migration-backup",
                     "credential-recovery-backup",
                     "tracker-recovery-backup",
                 })
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

    private static IEnumerable<string> RawCandidateTrackerPaths(string appDataRoot)
    {
        var searchRoots = new List<(string Root, int Depth)>();

        var developmentRoot = ReadDevelopmentRoot(appDataRoot);
        if (developmentRoot.Length > 0)
        {
            yield return Path.Combine(developmentRoot, "data", TrackerFileName);
            yield return Path.Combine(developmentRoot, TrackerFileName);
            yield return Path.Combine(developmentRoot, "hybrid", "FactVaultManager.Desktop", "data", TrackerFileName);
            searchRoots.Add((developmentRoot, 6));
        }

        var sourceData = ReadMigrationSourceData(appDataRoot);
        if (sourceData.Length > 0)
        {
            yield return Path.Combine(sourceData, TrackerFileName);
            var sourceParent = Path.GetDirectoryName(sourceData);
            if (!string.IsNullOrWhiteSpace(sourceParent))
                searchRoots.Add((sourceParent, 4));
        }

        // Look through FactVaultManager's own persistent folders, including migration
        // and credential backups created by earlier installed builds.
        searchRoots.Add((appDataRoot, 6));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in CommonCheckoutRoots(profile, documents))
        {
            yield return Path.Combine(root, "data", TrackerFileName);
            yield return Path.Combine(root, "hybrid", "FactVaultManager.Desktop", "data", TrackerFileName);
            searchRoots.Add((root, 4));
        }

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in CommonCheckoutRoots(oneDrive, Path.Combine(oneDrive, "Documents")))
        {
            yield return Path.Combine(root, "data", TrackerFileName);
            yield return Path.Combine(root, "hybrid", "FactVaultManager.Desktop", "data", TrackerFileName);
            searchRoots.Add((root, 4));
        }

        foreach (var (root, depth) in searchRoots)
        {
            foreach (var found in SearchTrackerFiles(root, depth))
                yield return found;
        }
    }

    private static IEnumerable<string> SearchTrackerFiles(string root, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var queue = new Queue<(string Directory, int Depth)>();
        var visited = new HashSet<string>(PathComparer());
        queue.Enqueue((Path.GetFullPath(root), 0));
        var directoriesVisited = 0;

        while (queue.Count > 0 && directoriesVisited < MaxSearchDirectories)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;
            directoriesVisited++;

            string[] files;
            try { files = Directory.GetFiles(directory, TrackerFileName + "*", SearchOption.TopDirectoryOnly); }
            catch { files = Array.Empty<string>(); }

            foreach (var file in files.OrderByDescending(SafeLastWriteUtc))
                yield return file;

            if (depth >= maxDepth)
                continue;

            string[] children;
            try { children = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly); }
            catch { children = Array.Empty<string>(); }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ShouldSkipDirectory(name))
                    continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool ShouldSkipDirectory(string name) =>
        name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(".venv", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("venv", StringComparison.OrdinalIgnoreCase);

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string ReadDevelopmentRoot(string appDataRoot)
    {
        var marker = Path.Combine(appDataRoot, "development-root.txt");
        if (!File.Exists(marker))
            return "";
        try { return File.ReadAllText(marker).Trim(); }
        catch { return ""; }
    }

    private static string ReadMigrationSourceData(string appDataRoot)
    {
        var markerPath = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (!File.Exists(markerPath))
            return "";

        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            return marker?["source_data"]?.GetValue<string>()?.Trim() ?? "";
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return "";
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

    private static FactburstTrackerSettings ReadTrackerFile(string trackerPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(trackerPath)) as JsonObject ?? new JsonObject();
            return new FactburstTrackerSettings(
                FactburstTrackerSettingsStore.PreferredBaseUrl(root["base_url"]?.GetValue<string>()),
                LocalSecretProtector.Unprotect(root["api_key"]?.GetValue<string>() ?? ""));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not read legacy tracker file '{trackerPath}': {error.Message}");
            return new FactburstTrackerSettings(FactburstTrackerSettingsStore.DefaultBaseUrl, "");
        }
    }

    private static void SaveRecoveredTracker(
        string appDataRoot,
        string destinationSettingsPath,
        string destinationTrackerPath,
        string markerPath,
        FactburstTrackerSettings source,
        string sourceKind)
    {
        BackupInvalidDestination(destinationTrackerPath, appDataRoot);
        FactburstTrackerSettingsStore.Save(destinationSettingsPath, source.BaseUrl, source.ApiKey);
        WriteMarker(markerPath, sourceKind);
    }

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

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
