using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public string LoadQuizLogoPath()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var node = JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject;
                var stored = node?["quiz"]?["logo_path"]?.GetValue<string>()?.Trim() ?? "";
                if (stored.Length > 0)
                    return stored;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz logo setting: {error.Message}");
        }

        var bundled = Path.Combine(_runtimeRoot, "assets", "quiz_logo.png");
        return File.Exists(bundled) ? bundled : "";
    }

    public void SaveQuizLogoPath(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length > 0)
            path = QuizBranding.ValidateLogoPath(path);

        var node = File.Exists(_settingsPath)
            ? JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var quiz = node["quiz"] as JsonObject ?? new JsonObject();
        node["quiz"] = quiz;
        quiz["logo_path"] = path;
        WriteSettingsNode(node);
    }
}

public static class QuizBranding
{
    private static readonly HashSet<string> SupportedLogoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp",
    };

    public static string ValidateLogoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Quiz logo file was not found.", fullPath);
        if (!SupportedLogoExtensions.Contains(Path.GetExtension(fullPath)))
            throw new InvalidDataException("Quiz logo must be a PNG, JPG, JPEG, or BMP image.");
        return fullPath;
    }
}
