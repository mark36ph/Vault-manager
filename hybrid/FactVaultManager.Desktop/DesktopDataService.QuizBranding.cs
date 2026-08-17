using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public string LoadQuizLogoPath() => QuizBranding.LoadLogoPath(_settingsPath, _runtimeRoot);

    public void SaveQuizLogoPath(string? path)
    {
        var sourcePath = (path ?? "").Trim();
        if (sourcePath.Length == 0)
        {
            QuizBranding.SaveLogoPath(_settingsPath, "");
            return;
        }

        sourcePath = QuizBranding.ValidateLogoPath(sourcePath);
        var managedPath = QuizBranding.ImportLogo(sourcePath, _dataRoot);
        QuizBranding.RegisterManagedAlias(sourcePath, managedPath);
        QuizBranding.SaveLogoPath(_settingsPath, managedPath);
    }

    public string ImportQuizLogo(string sourcePath)
    {
        sourcePath = QuizBranding.ValidateLogoPath(sourcePath);
        var managedPath = QuizBranding.ImportLogo(sourcePath, _dataRoot);
        QuizBranding.RegisterManagedAlias(sourcePath, managedPath);
        QuizBranding.SaveLogoPath(_settingsPath, managedPath);
        return managedPath;
    }
}

public static class QuizBranding
{
    private static readonly HashSet<string> SupportedLogoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp",
    };
    private static readonly object ManagedAliasLock = new();
    private static readonly Dictionary<string, string> ManagedAliases = new(StringComparer.OrdinalIgnoreCase);

    public static string LoadLogoPath(string settingsPath, string runtimeRoot)
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                var node = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
                var stored = node?["quiz"]?["logo_path"]?.GetValue<string>()?.Trim() ?? "";
                if (stored.Length > 0)
                    return stored;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz logo setting: {error.Message}");
        }

        var bundled = Path.Combine(runtimeRoot, "assets", "quiz_logo.png");
        return File.Exists(bundled) ? bundled : "";
    }

    public static void SaveLogoPath(string settingsPath, string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length > 0)
            path = ValidateLogoPath(path);

        var node = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var quiz = node["quiz"] as JsonObject ?? new JsonObject();
        node["quiz"] = quiz;
        quiz["logo_path"] = path;

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    public static string ImportLogo(string sourcePath, string dataRoot)
    {
        var source = ValidateLogoPath(sourcePath);
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("App data folder is required.", nameof(dataRoot));

        var managedDirectory = Path.Combine(Path.GetFullPath(dataRoot), "data", "quiz", "branding");
        Directory.CreateDirectory(managedDirectory);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.GetFullPath(Path.Combine(managedDirectory, "quiz_logo" + extension));
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(managedDirectory, $".quiz_logo_{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(source, temporary, overwrite: false);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        foreach (var supportedExtension in SupportedLogoExtensions)
        {
            var oldManagedPath = Path.GetFullPath(Path.Combine(managedDirectory, "quiz_logo" + supportedExtension));
            if (!string.Equals(oldManagedPath, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(oldManagedPath))
                File.Delete(oldManagedPath);
        }

        return destination;
    }

    internal static void RegisterManagedAlias(string sourcePath, string managedPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var managed = Path.GetFullPath(managedPath);
        lock (ManagedAliasLock)
            ManagedAliases[source] = managed;
    }

    public static string ValidateLogoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
        {
            string? managedPath;
            lock (ManagedAliasLock)
                ManagedAliases.TryGetValue(fullPath, out managedPath);
            if (!string.IsNullOrWhiteSpace(managedPath) && File.Exists(managedPath))
                return managedPath;
            throw new FileNotFoundException("Quiz logo file was not found.", fullPath);
        }
        if (!SupportedLogoExtensions.Contains(Path.GetExtension(fullPath)))
            throw new InvalidDataException("Quiz logo must be a PNG, JPG, JPEG, or BMP image.");
        return fullPath;
    }
}
