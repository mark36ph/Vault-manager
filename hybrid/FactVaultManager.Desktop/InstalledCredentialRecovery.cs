using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

internal sealed record InstalledCredentialRecoveryResult(
    int RecoveredCount,
    int ClearedInvalidCount,
    bool SettingsChanged);

public static class InstalledCredentialRecovery
{
    private const int RecoveryVersion = 1;
    private const string RecoveryMarkerName = "installed-credential-recovery-v1.json";

    private static readonly CredentialSpec[] CredentialSpecs =
    [
        new("ai", "api_key", "OPENAI_API_KEY"),
        new("images", "pexels_api_key", "PEXELS_API_KEY"),
        new("images", "pixabay_api_key", "PIXABAY_API_KEY"),
        new("youtube", "api_key", "YOUTUBE_API_KEY"),
        new("youtube", "oauth_client_secret", "YOUTUBE_OAUTH_CLIENT_SECRET"),
        new("youtube", "oauth_refresh_token", "YOUTUBE_OAUTH_REFRESH_TOKEN"),
        new("facebook", "page_access_token", "FACEBOOK_PAGE_ACCESS_TOKEN"),
        new("instagram", "access_token", "INSTAGRAM_ACCESS_TOKEN"),
    ];

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot, CandidateSettingsPaths(appDataRoot));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Installed credential recovery could not complete: {error}");
        }
    }

    internal static InstalledCredentialRecoveryResult Run(
        string appDataRoot,
        IEnumerable<string> sourceSettingsPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);
        var destination = ReadSettingsOrEmpty(destinationSettings);
        var recoveredBefore = ReadRecoveredKeys(markerPath);
        var recoveredAfter = new HashSet<string>(recoveredBefore, StringComparer.OrdinalIgnoreCase);
        var sources = LoadSourceSettings(destinationSettings, sourceSettingsPaths);

        var settingsChanged = false;
        var recoveredCount = 0;
        var clearedInvalidCount = 0;

        foreach (var spec in CredentialSpecs)
        {
            var destinationState = ReadCredential(destination, spec);

            if (destinationState.IsValid)
            {
                if (!LocalSecretProtector.IsProtected(destinationState.Raw))
                {
                    SetCredential(destination, spec, LocalSecretProtector.Protect(destinationState.Clear));
                    settingsChanged = true;
                }
                continue;
            }

            var wasInvalid = destinationState.IsPresent && destinationState.Raw.Length > 0;
            var wasRecoveredPreviously = recoveredBefore.Contains(spec.Name);

            if (!wasInvalid && wasRecoveredPreviously)
                continue;

            if (TryFindSourceCredential(sources, spec, out var clear) ||
                TryReadEnvironmentCredential(spec, out clear))
            {
                SetCredential(destination, spec, LocalSecretProtector.Protect(clear));
                settingsChanged = true;
                recoveredCount++;
                recoveredAfter.Add(spec.Name);
                continue;
            }

            if (wasInvalid)
            {
                SetCredential(destination, spec, "");
                settingsChanged = true;
                clearedInvalidCount++;
            }
        }

        if (settingsChanged)
        {
            BackupDestinationSettings(destinationSettings, appDataRoot);
            AppSettingsDocumentStore.Save(destinationSettings, destination);
        }

        if (!recoveredAfter.SetEquals(recoveredBefore))
            WriteRecoveryMarker(markerPath, recoveredAfter);

        return new InstalledCredentialRecoveryResult(
            recoveredCount,
            clearedInvalidCount,
            settingsChanged);
    }

    private static IReadOnlyList<JsonObject> LoadSourceSettings(string destinationSettings, IEnumerable<string> sourceSettingsPaths)
    {
        var results = new List<JsonObject>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var candidate in sourceSettingsPaths)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string fullPath;
            try { fullPath = Path.GetFullPath(candidate); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { continue; }
            if (PathsEqual(fullPath, destinationSettings) || !seen.Add(fullPath) || !File.Exists(fullPath)) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(fullPath)) is JsonObject root) results.Add(root);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine($"Could not inspect credential source '{fullPath}': {error.Message}");
            }
        }
        return results;
    }

    private static bool TryFindSourceCredential(IReadOnlyList<JsonObject> sources, CredentialSpec spec, out string clear)
    {
        foreach (var source in sources)
        {
            var state = ReadCredential(source, spec);
            if (state.IsValid) { clear = state.Clear; return true; }
        }
        clear = "";
        return false;
    }

    private static bool TryReadEnvironmentCredential(CredentialSpec spec, out string clear)
    {
        clear = "";
        if (string.IsNullOrWhiteSpace(spec.EnvironmentVariable)) return false;
        clear = (Environment.GetEnvironmentVariable(spec.EnvironmentVariable) ?? "").Trim();
        return clear.Length > 0;
    }

    private static CredentialState ReadCredential(JsonObject root, CredentialSpec spec)
    {
        var section = root[spec.Section] as JsonObject;
        if (section is null || section[spec.Key] is null) return new CredentialState(false, "", "", false);
        var node = section[spec.Key];
        if (node is not JsonValue value || !value.TryGetValue<string>(out var rawValue))
            return new CredentialState(true, node?.ToJsonString() ?? "<invalid>", "", false);
        var raw = (rawValue ?? "").Trim();
        if (raw.Length == 0) return new CredentialState(true, "", "", false);
        try
        {
            var clear = LocalSecretProtector.Unprotect(raw).Trim();
            return new CredentialState(true, raw, clear, clear.Length > 0);
        }
        catch (InvalidOperationException) { return new CredentialState(true, raw, "", false); }
    }

    private static void SetCredential(JsonObject root, CredentialSpec spec, string value)
    {
        var section = root[spec.Section] as JsonObject;
        if (section is null) { section = new JsonObject(); root[spec.Section] = section; }
        section[spec.Key] = value;
    }

    private static JsonObject ReadSettingsOrEmpty(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { throw; }
    }

    private static HashSet<string> ReadRecoveredKeys(string markerPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(markerPath)) return result;
        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            if (marker?["recovered"] is not JsonArray recovered) return result;
            foreach (var item in recovered)
            {
                var value = item?.GetValue<string>()?.Trim() ?? "";
                if (value.Length > 0) result.Add(value);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not read credential recovery marker: {error.Message}");
        }
        return result;
    }

    private static void WriteRecoveryMarker(string markerPath, HashSet<string> recovered)
    {
        var recoveredArray = new JsonArray();
        foreach (var name in recovered.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)) recoveredArray.Add(name);
        var marker = new JsonObject
        {
            ["version"] = RecoveryVersion,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["recovered"] = recoveredArray,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var temporary = markerPath + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static void BackupDestinationSettings(string destinationSettings, string appDataRoot)
    {
        if (!File.Exists(destinationSettings)) return;
        var backupRoot = Path.Combine(appDataRoot, "credential-recovery-backup");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(backupRoot, $"settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Copy(destinationSettings, backup, overwrite: false);
    }

    private static IEnumerable<string> CandidateSettingsPaths(string appDataRoot)
    {
        foreach (var candidate in RawCandidateSettingsPaths(appDataRoot)) yield return candidate;
    }

    private static IEnumerable<string> RawCandidateSettingsPaths(string appDataRoot)
    {
        var developmentRootMarker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(developmentRootMarker))
        {
            var root = File.ReadAllText(developmentRootMarker).Trim();
            if (root.Length > 0) yield return Path.Combine(root, "data", "settings.json");
        }
        var migrationMarker = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (File.Exists(migrationMarker))
        {
            JsonObject? marker = null;
            try { marker = JsonNode.Parse(File.ReadAllText(migrationMarker)) as JsonObject; } catch (JsonException) { }
            var sourceData = marker?["source_data"]?.GetValue<string>()?.Trim() ?? "";
            if (sourceData.Length > 0) yield return Path.Combine(sourceData, "settings.json");
        }
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (IsDevelopmentRepositoryRoot(directory.FullName)) yield return Path.Combine(directory.FullName, "data", "settings.json");
                directory = directory.Parent;
            }
        }
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents)) yield return Path.Combine(root, "data", "settings.json");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var physicalDocuments = Path.Combine(userProfile, "Documents");
        foreach (var root in NamedDocumentRoots(physicalDocuments)) yield return Path.Combine(root, "data", "settings.json");
        foreach (var root in CommonCheckoutRoots(userProfile)) yield return Path.Combine(root, "data", "settings.json");
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in CommonCheckoutRoots(oneDrive)) yield return Path.Combine(root, "data", "settings.json");
        if (Directory.Exists(appDataRoot))
        {
            IEnumerable<string> topLevel;
            try { topLevel = Directory.EnumerateDirectories(appDataRoot, "*", SearchOption.TopDirectoryOnly).ToArray(); } catch { topLevel = Array.Empty<string>(); }
            foreach (var directory in topLevel)
            {
                yield return Path.Combine(directory, "settings.json");
                yield return Path.Combine(directory, "data", "settings.json");
            }
        }
        var migrationBackup = Path.Combine(appDataRoot, "migration-backup");
        if (Directory.Exists(migrationBackup))
        {
            IEnumerable<string> backups;
            try { backups = Directory.EnumerateDirectories(migrationBackup, "*", SearchOption.TopDirectoryOnly).ToArray(); } catch { backups = Array.Empty<string>(); }
            foreach (var backup in backups) yield return Path.Combine(backup, "settings.json");
        }
    }

    private static IEnumerable<string> NamedDocumentRoots(string documents)
    {
        if (string.IsNullOrWhiteSpace(documents)) yield break;
        yield return Path.Combine(documents, "FactVaultManager");
        yield return Path.Combine(documents, "Fact Vault Manager");
        yield return Path.Combine(documents, "Vault-manager");
        yield return Path.Combine(documents, "GitHub", "Vault-manager");
    }

    private static IEnumerable<string> CommonCheckoutRoots(string profileRoot)
    {
        if (string.IsNullOrWhiteSpace(profileRoot)) yield break;
        yield return Path.Combine(profileRoot, "Vault-manager");
        yield return Path.Combine(profileRoot, "FactVaultManager");
        yield return Path.Combine(profileRoot, "source", "repos", "Vault-manager");
        yield return Path.Combine(profileRoot, "repos", "Vault-manager");
        yield return Path.Combine(profileRoot, "GitHub", "Vault-manager");
        yield return Path.Combine(profileRoot, "Desktop", "Vault-manager");
    }

    private static bool IsDevelopmentRepositoryRoot(string root) => File.Exists(Path.Combine(root, "hybrid", "FactVaultManager.Desktop", "FactVaultManager.Desktop.csproj"));

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return false; }
    }

    private sealed record CredentialSpec(string Section, string Key, string? EnvironmentVariable = null)
    {
        public string Name => $"{Section}.{Key}";
    }

    private sealed record CredentialState(bool IsPresent, string Raw, string Clear, bool IsValid);
}
