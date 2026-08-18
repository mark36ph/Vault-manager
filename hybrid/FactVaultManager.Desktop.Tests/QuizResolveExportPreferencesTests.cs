using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizResolveExportPreferencesTests
{
    [Fact]
    public void FullCountdown_UsesEveryQuestionSecond()
    {
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1 }, QuizFullCountdownRewriter.Values(8));
    }

    [Fact]
    public void Defaults_NarrateQuestionWithoutReadingAnswers()
    {
        var preferences = new QuizResolveExportPreferences();

        Assert.True(preferences.Narrate);
        Assert.False(preferences.NarrateAnswers);
    }

    [Fact]
    public void PreferenceStore_RoundTripsResolveOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            var expected = new QuizResolveExportPreferences(
                FormatIndex: 1,
                ShowCountdown: true,
                AnimateReveal: false,
                Narrate: true,
                NarrateAnswers: false,
                Voice: "nova",
                CountdownTicks: false,
                AnswerRevealSfx: false,
                UseBackgroundMusic: true,
                BackgroundMusicPath: @"C:\Music\quiz.mp3");

            QuizResolveExportPreferenceStore.Save(settings, expected);
            var actual = QuizResolveExportPreferenceStore.Load(settings);

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferenceStore_PreservesOtherSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(settings, "{\"projects_folder\":\"D:\\\\Projects\",\"quiz\":{\"logo_path\":\"logo.png\"}}");

            QuizResolveExportPreferenceStore.Save(settings, new QuizResolveExportPreferences(FormatIndex: 1));
            var json = File.ReadAllText(settings);

            Assert.Contains("projects_folder", json, StringComparison.Ordinal);
            Assert.Contains("logo_path", json, StringComparison.Ordinal);
            Assert.Contains("resolve_export", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
