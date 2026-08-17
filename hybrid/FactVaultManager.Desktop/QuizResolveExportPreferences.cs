using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record QuizResolveExportPreferences(
    int FormatIndex = 0,
    bool ShowCountdown = true,
    bool AnimateReveal = true,
    bool Narrate = false,
    bool NarrateAnswers = true,
    string Voice = "alloy",
    bool CountdownTicks = true,
    bool AnswerRevealSfx = true,
    bool UseBackgroundMusic = false,
    string BackgroundMusicPath = "")
{
    public QuizResolveExportPreferences Normalize()
    {
        var voice = QuizVoiceCatalog.BuiltInVoices.Contains(Voice, StringComparer.OrdinalIgnoreCase)
            ? Voice.Trim().ToLowerInvariant()
            : "alloy";
        var musicPath = (BackgroundMusicPath ?? "").Trim();
        return this with
        {
            FormatIndex = Math.Clamp(FormatIndex, 0, 1),
            Voice = voice,
            BackgroundMusicPath = musicPath,
            UseBackgroundMusic = UseBackgroundMusic && musicPath.Length > 0,
        };
    }
}

public sealed partial class DesktopDataService
{
    public QuizResolveExportPreferences LoadQuizResolveExportPreferences() =>
        QuizResolveExportPreferenceStore.Load(_settingsPath);

    public void SaveQuizResolveExportPreferences(QuizResolveExportPreferences preferences) =>
        QuizResolveExportPreferenceStore.Save(_settingsPath, preferences);
}

public static class QuizResolveExportPreferenceStore
{
    public static QuizResolveExportPreferences Load(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return new QuizResolveExportPreferences();

            var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
            var node = root?["quiz"]?["resolve_export"] as JsonObject;
            if (node is null)
                return new QuizResolveExportPreferences();

            return new QuizResolveExportPreferences(
                FormatIndex: ReadInt(node, "format_index", 0),
                ShowCountdown: ReadBool(node, "show_countdown", true),
                AnimateReveal: ReadBool(node, "animate_reveal", true),
                Narrate: ReadBool(node, "narrate", false),
                NarrateAnswers: ReadBool(node, "narrate_answers", true),
                Voice: ReadString(node, "voice", "alloy"),
                CountdownTicks: ReadBool(node, "countdown_ticks", true),
                AnswerRevealSfx: ReadBool(node, "answer_reveal_sfx", true),
                UseBackgroundMusic: ReadBool(node, "use_background_music", false),
                BackgroundMusicPath: ReadString(node, "background_music_path", ""))
                .Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz Resolve export preferences: {error.Message}");
            return new QuizResolveExportPreferences();
        }
    }

    public static void Save(string settingsPath, QuizResolveExportPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var value = preferences.Normalize();

        var root = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var quiz = root["quiz"] as JsonObject ?? new JsonObject();
        root["quiz"] = quiz;
        quiz["resolve_export"] = new JsonObject
        {
            ["format_index"] = value.FormatIndex,
            ["show_countdown"] = value.ShowCountdown,
            ["animate_reveal"] = value.AnimateReveal,
            ["narrate"] = value.Narrate,
            ["narrate_answers"] = value.NarrateAnswers,
            ["voice"] = value.Voice,
            ["countdown_ticks"] = value.CountdownTicks,
            ["answer_reveal_sfx"] = value.AnswerRevealSfx,
            ["use_background_music"] = value.UseBackgroundMusic,
            ["background_music_path"] = value.BackgroundMusicPath,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    private static bool ReadBool(JsonObject node, string name, bool fallback) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static int ReadInt(JsonObject node, string name, int fallback) =>
        node[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;

    private static string ReadString(JsonObject node, string name, string fallback) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result ?? fallback : fallback;
}
