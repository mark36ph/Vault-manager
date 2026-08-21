using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record QuizPreset
{
    public string Name { get; init; } = "Quiz preset";
    public int QuestionCount { get; init; } = 10;
    public int QuestionSeconds { get; init; } = 8;
    public string Category { get; init; } = "";
    public string Difficulty { get; init; } = "";
    public bool PreferLeastUsed { get; init; } = true;
    public bool AvoidRecent { get; init; } = true;
    public int RecentQuizCount { get; init; } = 5;
    public string Format { get; init; } = "landscape";
    public string ThemeKey { get; init; } = "dark";
    public string LogoPath { get; init; } = "";
    public string LogoPosition { get; init; } = "Bottom right";
    public double LogoScale { get; init; } = 1.0;
    public bool ShuffleAnswers { get; init; } = true;
    public bool ShowCountdown { get; init; } = true;
    public bool AnimateAnswerReveal { get; init; } = true;
    public bool Narrate { get; init; } = true;
    public bool NarrateAnswers { get; init; }
    public string Voice { get; init; } = QuizVoiceCatalog.DefaultVoice;
    public bool CountdownTicks { get; init; } = true;
    public bool AnswerRevealSfx { get; init; } = true;
    public bool UseBackgroundMusic { get; init; }
    public string BackgroundMusicPath { get; init; } = "";

    public QuizPreset Normalize()
    {
        var name = (Name ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidDataException("Preset name cannot be blank.");
        if (name.Length > 80)
            throw new InvalidDataException("Preset name cannot exceed 80 characters.");
        if (QuestionCount is < 1 or > 100)
            throw new InvalidDataException("Preset question count must be between 1 and 100.");
        if (QuestionSeconds is < 2 or > 60)
            throw new InvalidDataException("Preset question time must be between 2 and 60 seconds.");
        if (RecentQuizCount is < 1 or > 50)
            throw new InvalidDataException("Preset recent-quiz count must be between 1 and 50.");
        if (LogoScale is < 0.5 or > 2.0 || double.IsNaN(LogoScale) || double.IsInfinity(LogoScale))
            throw new InvalidDataException("Preset logo size must be between 50% and 200%.");

        var difficulty = (Difficulty ?? "").Trim().ToLowerInvariant();
        if (difficulty is not ("" or "easy" or "medium" or "hard"))
            difficulty = "";

        string voice;
        try
        {
            voice = QuizVoiceCatalog.Validate(Voice ?? QuizVoiceCatalog.DefaultVoice);
        }
        catch
        {
            voice = QuizVoiceCatalog.DefaultVoice;
        }

        return this with
        {
            Name = name,
            QuestionCount = Math.Clamp(QuestionCount, 1, 100),
            QuestionSeconds = Math.Clamp(QuestionSeconds, 2, 60),
            Category = (Category ?? "").Trim(),
            Difficulty = difficulty,
            RecentQuizCount = Math.Clamp(RecentQuizCount, 1, 50),
            Format = string.Equals(Format, "vertical", StringComparison.OrdinalIgnoreCase) ? "vertical" : "landscape",
            ThemeKey = QuizVisualThemeCatalog.Normalize(ThemeKey),
            LogoPath = (LogoPath ?? "").Trim(),
            LogoPosition = QuizLogoPositionCatalog.Normalize(LogoPosition),
            LogoScale = Math.Clamp(LogoScale, 0.5, 2.0),
            Voice = voice,
            BackgroundMusicPath = (BackgroundMusicPath ?? "").Trim(),
        };
    }
}

internal static class QuizPresetBranding
{
    public static string ResolveLogoPath(string? currentLogoPath, string? presetLogoPath)
    {
        var current = (currentLogoPath ?? "").Trim();
        var preset = (presetLogoPath ?? "").Trim();
        return preset.Length > 0 && File.Exists(preset)
            ? Path.GetFullPath(preset)
            : current;
    }
}

public sealed partial class DesktopDataService
{
    public IReadOnlyList<QuizPreset> LoadQuizPresets()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var root = JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject;
                var presetsNode = root?["quiz"]?["presets"];
                if (presetsNode is not null)
                {
                    var loaded = JsonSerializer.Deserialize<List<QuizPreset>>(presetsNode.ToJsonString()) ?? [];
                    var normalized = loaded
                        .Select(TryNormalizePreset)
                        .Where(preset => preset is not null)
                        .Cast<QuizPreset>()
                        .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last())
                        .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (normalized.Count > 0)
                        return normalized;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz presets: {error.Message}");
        }

        return DefaultQuizPresets();
    }

    public void UpsertQuizPreset(QuizPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset = preset.Normalize();
        var presets = LoadQuizPresets().ToList();
        var index = presets.FindIndex(existing => string.Equals(existing.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            presets[index] = preset;
        else
            presets.Add(preset);
        SaveQuizPresets(presets);
    }

    public void DeleteQuizPreset(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
            return;
        var presets = LoadQuizPresets()
            .Where(preset => !string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SaveQuizPresets(presets);
    }

    private void SaveQuizPresets(IReadOnlyList<QuizPreset> presets)
    {
        var normalized = presets
            .Select(preset => preset.Normalize())
            .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var root = File.Exists(_settingsPath)
            ? JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var quiz = root["quiz"] as JsonObject ?? new JsonObject();
        root["quiz"] = quiz;
        quiz["presets"] = JsonSerializer.SerializeToNode(normalized);

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private static QuizPreset? TryNormalizePreset(QuizPreset preset)
    {
        try
        {
            return preset.Normalize();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<QuizPreset> DefaultQuizPresets() =>
    [
        new QuizPreset
        {
            Name = "General Knowledge 16:9",
            QuestionCount = 10,
            QuestionSeconds = 8,
            Format = "landscape",
            ThemeKey = "dark",
            LogoPosition = "Bottom right",
            LogoScale = 1.0,
        },
        new QuizPreset
        {
            Name = "Quiz Shorts 9:16",
            QuestionCount = 5,
            QuestionSeconds = 7,
            Format = "vertical",
            ThemeKey = "bright",
            LogoPosition = "Top right",
            LogoScale = 1.1,
        },
    ];
}
