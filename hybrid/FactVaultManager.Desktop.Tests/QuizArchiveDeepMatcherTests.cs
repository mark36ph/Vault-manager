namespace FactVaultManager.Desktop.Tests;

public sealed class QuizArchiveDeepMatcherTests
{
    [Fact]
    public void Evaluate_ExactStoredFolderName_ProducesExactMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-archive-deep-exact-{Guid.NewGuid():N}");
        var archive = Path.Combine(root, "General Knowledge Quiz - 001");
        try
        {
            Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive, "quiz.json"), "{\"title\":\"General Knowledge Quiz\"}");
            File.WriteAllText(Path.Combine(archive, "timeline.json"), "{\"name\":\"General Knowledge Quiz\"}");

            var history = History(
                id: 41,
                title: "General Knowledge Quiz",
                series: "General Knowledge Quiz",
                episode: 1,
                format: "16:9",
                projectFolder: Path.Combine(root, "missing", "General Knowledge Quiz - 001"));

            var result = QuizArchiveDeepMatcher.Evaluate(
                history,
                QuizArchiveDeepMatcher.InspectProjectFolder(archive));

            Assert.Equal(QuizArchiveMatchConfidence.Exact, result.Confidence);
            Assert.Contains(result.Evidence, item => item.Contains("exact match", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_ExplicitEpisodeMismatch_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-archive-deep-episode-{Guid.NewGuid():N}");
        var archive = Path.Combine(root, "Film Quiz #002 - Short - 001");
        try
        {
            Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive, "quiz.json"), "{\"title\":\"Film Quiz\"}");

            var history = History(
                id: 68,
                title: "Film Quiz",
                series: "Film Quiz",
                episode: 3,
                format: "9:16",
                projectFolder: Path.Combine(root, "current", "Film Quiz #003 - Short - 001"));

            var result = QuizArchiveDeepMatcher.Evaluate(
                history,
                QuizArchiveDeepMatcher.InspectProjectFolder(archive));

            Assert.Equal(QuizArchiveMatchConfidence.NoMatch, result.Confidence);
            Assert.Contains(result.Evidence, item => item.Contains("conflicts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_FullVideoAgainstShortFolder_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-archive-deep-type-{Guid.NewGuid():N}");
        var archive = Path.Combine(root, "Sports Quiz - Short - 001");
        try
        {
            Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive, "quiz.json"), "{\"title\":\"Sports Quiz\"}");

            var history = History(
                id: 64,
                title: "Sports Quiz",
                series: "Sports Quiz",
                episode: 3,
                format: "16:9",
                projectFolder: Path.Combine(root, "current", "Sports Quiz - 001"));

            var result = QuizArchiveDeepMatcher.Evaluate(
                history,
                QuizArchiveDeepMatcher.InspectProjectFolder(archive));

            Assert.Equal(QuizArchiveMatchConfidence.NoMatch, result.Confidence);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_MatchingProjectFiles_StrengthenDuplicateCopyMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-archive-deep-files-{Guid.NewGuid():N}");
        var current = Path.Combine(root, "current", "Science Quiz - 001");
        var archive = Path.Combine(root, "archive", "Science Quiz - 009");
        try
        {
            Directory.CreateDirectory(current);
            Directory.CreateDirectory(archive);
            for (var index = 1; index <= 6; index++)
            {
                var name = $"asset-{index:00}.dat";
                var bytes = Enumerable.Range(0, index + 2).Select(value => (byte)value).ToArray();
                File.WriteAllBytes(Path.Combine(current, name), bytes);
                File.WriteAllBytes(Path.Combine(archive, name), bytes);
            }
            File.WriteAllText(Path.Combine(archive, "quiz.json"), "{\"title\":\"Science Quiz\"}");

            var history = History(
                id: 17,
                title: "Science Quiz",
                series: "Science Quiz",
                episode: 1,
                format: "16:9",
                projectFolder: current);

            var result = QuizArchiveDeepMatcher.Evaluate(
                history,
                QuizArchiveDeepMatcher.InspectProjectFolder(archive),
                QuizArchiveDeepMatcher.InspectProjectFolder(current));

            Assert.Equal(QuizArchiveMatchConfidence.Exact, result.Confidence);
            Assert.Contains(result.Evidence, item => item.Contains("identical sizes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_ExactFolderAndFileFingerprint_OutranksLooseSameSeriesCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-archive-deep-priority-{Guid.NewGuid():N}");
        var currentExact = Path.Combine(root, "current", "Technology - 001");
        var currentLoose = Path.Combine(root, "current", "Technology Backup - 004");
        var archive = Path.Combine(root, "archive", "Technology - 001");
        try
        {
            Directory.CreateDirectory(currentExact);
            Directory.CreateDirectory(currentLoose);
            Directory.CreateDirectory(archive);
            for (var index = 1; index <= 8; index++)
            {
                var name = $"asset-{index:00}.dat";
                var bytes = Enumerable.Range(0, index + 3).Select(value => (byte)value).ToArray();
                File.WriteAllBytes(Path.Combine(currentExact, name), bytes);
                File.WriteAllBytes(Path.Combine(archive, name), bytes);
            }
            File.WriteAllText(Path.Combine(archive, "quiz.json"), "{\"title\":\"Technology Quiz\",\"series\":\"Technology Quiz\"}");

            var exactHistory = History(
                id: 71,
                title: "Technology Quiz",
                series: "Technology Quiz",
                episode: 2,
                format: "16:9",
                projectFolder: currentExact);
            var looseHistory = History(
                id: 72,
                title: "Technology Quiz",
                series: "Technology Quiz",
                episode: 3,
                format: "16:9",
                projectFolder: currentLoose);
            var archiveFingerprint = QuizArchiveDeepMatcher.InspectProjectFolder(archive);

            var exact = QuizArchiveDeepMatcher.Evaluate(
                exactHistory,
                archiveFingerprint,
                QuizArchiveDeepMatcher.InspectProjectFolder(currentExact));
            var loose = QuizArchiveDeepMatcher.Evaluate(
                looseHistory,
                archiveFingerprint,
                QuizArchiveDeepMatcher.InspectProjectFolder(currentLoose));

            Assert.Equal(QuizArchiveMatchConfidence.Exact, exact.Confidence);
            Assert.True(exact.Score - loose.Score >= 200, $"Expected strong copy identity margin; exact={exact.Score}, loose={loose.Score}");
            Assert.Contains(exact.Evidence, item => item.Contains("confirms the same project copy", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static QuizHistorySummary History(
        int id,
        string title,
        string series,
        int episode,
        string format,
        string projectFolder) =>
        new(
            Id: id,
            Title: title,
            Created: "2026-08-30",
            QuestionCount: 10,
            Categories: title.Replace(" Quiz", "", StringComparison.Ordinal),
            Format: format,
            QuestionSeconds: 5,
            ShuffleAnswers: true,
            ProjectFolder: projectFolder,
            SeriesName: series,
            EpisodeNumber: episode,
            YouTubeTitle: $"Can You Beat It? | {series} #{episode:000}",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: false,
            YouTubeUrl: "");
}
