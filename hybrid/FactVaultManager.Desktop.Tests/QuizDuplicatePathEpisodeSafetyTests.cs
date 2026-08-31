using System.Reflection;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDuplicatePathEpisodeSafetyTests
{
    private static readonly MethodInfo EvaluateMethod = typeof(DesktopDataService).GetMethod(
        "EvaluateDuplicateRepairCandidate",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Duplicate path evaluator was not found.");

    [Fact]
    public void TrailingSequenceMismatch_IsHardRejected_ForLogosEpisodeThree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-duplicate-logos-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "Logos - 001");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "quiz.json"), "{\"title\":\"Logos Quiz\",\"series\":\"Logos Quiz\"}");

            var result = Evaluate(
                History(83, "Logos Quiz", "Logos Quiz", 3, "16:9"),
                QuizArchiveDeepMatcher.InspectProjectFolder(folder));

            Assert.Equal(QuizArchiveMatchConfidence.NoMatch, result.Confidence);
            Assert.Equal(0, result.Score);
            Assert.Contains(result.Evidence, item =>
                item.Contains("sequence 001 conflicts with episode #003", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MatchingTrailingSequence_RemainsHighConfidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-duplicate-history-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "History Quiz - 001");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "quiz.json"), "{\"title\":\"History Quiz\",\"series\":\"History Quiz\"}");

            var result = Evaluate(
                History(48, "History Quiz", "History Quiz", 1, "16:9"),
                QuizArchiveDeepMatcher.InspectProjectFolder(folder));

            Assert.True(result.Confidence >= QuizArchiveMatchConfidence.High);
            Assert.Contains(result.Evidence, item =>
                item.Contains("sequence 001 matches episode #001", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitHashEpisode_TakesPrecedenceOverTrailingExportSequence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-duplicate-film-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "Film Quiz #002 - Short - 001");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "quiz.json"), "{\"title\":\"Film Quiz\",\"series\":\"Film Quiz\"}");

            var result = Evaluate(
                History(68, "Film Quiz", "Film Quiz", 2, "9:16"),
                QuizArchiveDeepMatcher.InspectProjectFolder(folder));

            Assert.True(result.Confidence >= QuizArchiveMatchConfidence.High);
            Assert.Contains(result.Evidence, item =>
                item.Contains("explicit episode #002 matches", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Evidence, item =>
                item.Contains("trailing project sequence", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static QuizArchiveDeepCandidate Evaluate(
        QuizHistorySummary history,
        QuizArchiveFolderFingerprint fingerprint) =>
        Assert.IsType<QuizArchiveDeepCandidate>(EvaluateMethod.Invoke(null, [history, fingerprint]));

    private static QuizHistorySummary History(
        int id,
        string title,
        string series,
        int episode,
        string format) =>
        new(
            Id: id,
            Title: title,
            Created: "2026-08-31",
            QuestionCount: 10,
            Categories: title.Replace(" Quiz", "", StringComparison.Ordinal),
            Format: format,
            QuestionSeconds: 5,
            ShuffleAnswers: true,
            ProjectFolder: "",
            SeriesName: series,
            EpisodeNumber: episode,
            YouTubeTitle: $"Can You Beat It? | {series} #{episode:000}",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: false,
            YouTubeUrl: "");
}
