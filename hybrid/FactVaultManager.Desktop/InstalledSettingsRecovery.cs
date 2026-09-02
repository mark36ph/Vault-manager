using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

/// <summary>
/// Restores non-secret application settings when an installed build starts against
/// a fresh LocalAppData data root. Secrets are handled by InstalledCredentialRecovery.
/// </summary>
public static class InstalledSettingsRecovery
{
    private const string MarkerName = "installed-settings-recovery-v1.json";

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot, CandidatePaths(appDataRoot));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Recovery must never prevent the desktop app from starting.
            Debug.WriteLine($"Installed settings recovery could not complete: {error}");
        }
    }

    internal static bool Run(string appDataRoot, IEnumerable<string> sourceSettingsPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);
        var destinationPath = Path.Combine(appDataRoot, "data", "settings.json");
        var destination = ReadObject(destinationPath);
        var source = FindBestSource(destinationPath, sourceSettingsPaths);
        if (source is null)
            return false;

        var changed = CopyMissingSettings(destination, source.Document);
        if (!changed)
            return false;

        AppSettingsDocumentStore.Save(destinationPath, destination);
        WriteMarker(appDataRoot, source.Path);
        return true;
    }

    private static bool CopyMissingSettings(JsonObject destination, JsonObject source)
    {
        var changed = false;

        changed |= CopyIfMissing(source, destination, "general", "projects_folder");
        changed |= CopyIfMissing(source, destination, "general", "nas_archive_folder");
        changed |= CopyIfMissing(source, destination, "general", "archive_after_upload");
        changed |= CopyIfMissing(source, destination, "general", "theme");
        changed |= CopyIfMissing(source, destination, "general", "check_updates");

        changed |= CopyIfMissing(source, destination, "youtube", "oauth_client_id");
        changed |= CopyIfMissing(source, destination, "youtube", "approved_channel_id");
        changed |= CopyIfMissing(source, destination, "youtube", "approved_channel_name");
        changed |= CopyIfMissing(source, destination, "facebook", "approved_page_id");
        changed |= CopyIfMissing(source, destination, "facebook", "approved_page_name");
        changed |= CopyIfMissing(source, destination, "ai", "model");
        changed |= CopyIfMissing(source, destination, "resolve", "application_path");
        changed |= CopyIfMissing(source, destination, "resolve", "timeline_width");
        changed |= CopyIfMissing(source, destination, "resolve", "timeline_height");
        changed |= CopyIfMissing(source, destination, "resolve", "frame_rate");

        return changed;
    }

    private static bool CopyIfMissing(JsonObject source, JsonObject destination, string sectionName, string key)
    {
        var sourceSection = source[sectionName] as JsonObject;
        if (sourceSection is null || sourceSection[key] is null)
            return false;

        var destinationSection = destination[sectionName] as JsonObject;
        if (destinationSection is null)
        {
            destinationSection = new JsonObject();
            destination[sectionName] = destinationSection;
        }

        var existing = destinationSection[key];
        if (existing is not null && !IsBlank(existing))
            return false;

        destinationSection[key] = sourceSection[key]!.DeepClone();
        return true;
    }

    private static bool IsBlank(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return string.IsNullOrWhiteSpace(text);
        return false;
    }

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static SourceSettings? FindBestSource(string destinationPath, IEnumerable<string> candidatePaths)
    {
        foreach (var candidate in candidatePaths)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (PathsEqual(full, destinationPath) || !File.Exists(full))
                    continue;

                if (JsonNode.Parse(File.ReadAllText(full)) is JsonObject root && HasUsefulSettings(root))
                    return new SourceSettings(full, root);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or NotSupportedException)
            {
                Debug.WriteLine($"Could not inspect settings source '{candidate}': {error.Message}");
            }
        }

        return null;
    }

    private static bool HasUsefulSettings(JsonObject root)
    {
        var projects = root["general"]?["projects_folder"]?.GetValue<string>() ?? "";
        var openAi = root["ai"]?["api_key"]?.GetValue<string>() ?? "";
        var youtube = root["youtube"]?["api_key"]?.GetValue<string>() ?? "";
        var facebook = root["facebook"]?["page_access_token"]?.GetValue<string>() ?? "";
        var instagram = root["instagram"]?["access_token"]?.GetValue<string>() ?? "";
        return projects.Length > 0 || openAi.Length > 0 || youtube.Length > 0 || facebook.Length > 0 || instagram.Length > 0;
    }

    private static IEnumerable<string> CandidatePaths(string appDataRoot)
    {
        var marker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(marker))
        {
            var root = File.ReadAllText(marker).Trim();
            if (root.Length > 0)
                yield return Path.Combine(root, "data", "settings.json");
        }

        var migrationMarker = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (File.Exists(migrationMarker))
        {
            string? sourceData = null;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(migrationMarker)) as JsonObject;
                sourceData = node?["source_data"]?.GetValue<string>()?.Trim();
            }
            catch (JsonException)
            {
            }

            if (!string.IsNullOrWhiteSpace(sourceData))
                yield return Path.Combine(sourceData, "settings.json");
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "hybrid", "FactVaultManager.Desktop", "FactVaultManager.Desktop.csproj")))
                    yield return Path.Combine(directory.FullName, "data", "settings.json");
                directory = directory.Parent;
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
        {
            Path.Combine(profile, "Vault-manager"),
            Path.Combine(profile, "FactVaultManager"),
            Path.Combine(profile, "source", "repos", "Vault-manager"),
            Path.Combine(profile, "repos", "Vault-manager"),
            Path.Combine(profile, "GitHub", "Vault-manager"),
            Path.Combine(profile, "Desktop", "Vault-manager"),
        })
            yield return Path.Combine(root, "data", "settings.json");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in new[]
        {
            Path.Combine(documents, "FactVaultManager"),
            Path.Combine(documents, "Fact Vault Manager"),
            Path.Combine(documents, "Vault-manager"),
            Path.Combine(documents, "GitHub", "Vault-manager"),
        })
            yield return Path.Combine(root, "data", "settings.json");
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void WriteMarker(string appDataRoot, string sourcePath)
    {
        var markerPath = Path.Combine(appDataRoot, MarkerName);
        var marker = new JsonObject
        {
            ["version"] = 1,
            ["source"] = sourcePath,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        File.WriteAllText(markerPath, marker.ToJsonString());
    }

    private sealed record SourceSettings(string Path, JsonObject Document);
}
