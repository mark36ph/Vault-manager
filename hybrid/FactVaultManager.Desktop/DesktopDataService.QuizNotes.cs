using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public string LoadQuizNotes() => QuizNotesStore.Load(_settingsPath);

    public void SaveQuizNotes(string? notes) => QuizNotesStore.Save(_settingsPath, notes);
}

public static class QuizNotesStore
{
    public const int MaximumLength = 100_000;

    public static string Load(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return "";

            var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
            return root?["quiz"]?["notes"]?.GetValue<string>() ?? "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz notes: {error.Message}");
            return "";
        }
    }

    public static void Save(string settingsPath, string? notes)
    {
        notes ??= "";
        if (notes.Length > MaximumLength)
            throw new ArgumentException($"Quiz notes cannot exceed {MaximumLength:N0} characters.", nameof(notes));

        var root = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var quiz = root["quiz"] as JsonObject ?? new JsonObject();
        root["quiz"] = quiz;
        quiz["notes"] = notes;

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
    }
}
