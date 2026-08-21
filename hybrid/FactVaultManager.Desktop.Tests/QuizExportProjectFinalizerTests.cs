namespace FactVaultManager.Desktop.Tests;

public sealed class QuizExportProjectFinalizerTests
{
    [Theory]
    [InlineData(false, "History Quiz")]
    [InlineData(true, "History Quiz - Short")]
    public void ExportFolderName_LabelsShortsWithoutChangingVideoNames(bool vertical, string expected)
    {
        Assert.Equal(expected, QuizExportFolderNaming.BaseName("History Quiz", vertical));
    }

    [Fact]
    public void Prepare_KeepsShortLabelInFinalNumberedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-finalize-{Guid.NewGuid():N}");
        try
        {
            var folderName = QuizExportFolderNaming.BaseName("History Quiz", vertical: true);

            var finalized = QuizExportProjectFinalizer.Prepare(CreateBuild(root, folderName));

            Assert.EndsWith("History Quiz - Short - 001", finalized.ProjectFolder, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_MovesExportToUniqueFolderAndRenamesStillsWithoutFrameNumbers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-finalize-{Guid.NewGuid():N}");
        try
        {
            var build = CreateBuild(root, "General Knowledge Quiz");
            var originalFolder = build.ProjectFolder;

            var finalized = QuizExportProjectFinalizer.Prepare(build);

            Assert.NotEqual(originalFolder, finalized.ProjectFolder);
            Assert.EndsWith("General Knowledge Quiz - 001", finalized.ProjectFolder, StringComparison.Ordinal);
            Assert.False(Directory.Exists(originalFolder));
            Assert.True(Directory.Exists(finalized.ProjectFolder));
            Assert.True(File.Exists(finalized.QuizJson));
            var imageSource = build.Timeline.Tracks.Single().Clips.Single().Source!;
            Assert.Matches("^quizcard_[a-z]{12}\\.png$", Path.GetFileName(imageSource));
            Assert.DoesNotMatch("[0-9]", Path.GetFileNameWithoutExtension(imageSource));
            Assert.True(File.Exists(imageSource));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_RebasesAudioClipsAddedBeforeFinalFolderMove()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-finalize-{Guid.NewGuid():N}");
        try
        {
            var build = CreateBuild(root, "Quiz With Audio");
            var audioFolder = Path.Combine(build.ProjectFolder, "Audio");
            Directory.CreateDirectory(audioFolder);
            var tick = Path.Combine(audioFolder, "countdown_tick.wav");
            File.WriteAllBytes(tick, new byte[] { 1, 2, 3, 4 });
            var audioTrack = build.Timeline.AddTrack(new NativeTimelineTrack
            {
                Name = "Quiz SFX",
                Kind = NativeTimelineTrackKind.Audio,
            });
            audioTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Audio,
                Start = 0,
                Duration = 0.14,
                Source = tick,
                Name = "Countdown Tick",
            });

            var finalized = QuizExportProjectFinalizer.Prepare(build);

            var audioSource = audioTrack.Clips.Single().Source!;
            Assert.StartsWith(finalized.ProjectFolder, audioSource, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("countdown_tick.wav", Path.GetFileName(audioSource));
            Assert.True(File.Exists(audioSource));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_AllocatesNextFolderInsteadOfReusingPreviousExport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-finalize-{Guid.NewGuid():N}");
        try
        {
            var first = QuizExportProjectFinalizer.Prepare(CreateBuild(root, "Quiz"));
            var second = QuizExportProjectFinalizer.Prepare(CreateBuild(root, "Quiz"));

            Assert.EndsWith("Quiz - 001", first.ProjectFolder, StringComparison.Ordinal);
            Assert.EndsWith("Quiz - 002", second.ProjectFolder, StringComparison.Ordinal);
            Assert.NotEqual(first.ProjectFolder, second.ProjectFolder);
            Assert.True(Directory.Exists(first.ProjectFolder));
            Assert.True(Directory.Exists(second.ProjectFolder));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HistoryRestorer_PreservesRecordedOrderAndReportsMissingBankQuestions()
    {
        var first = Question(11, "First");
        var second = Question(22, "Second");
        var history = new[]
        {
            new QuizHistoryQuestion(1, 22, "Second", "General", "medium"),
            new QuizHistoryQuestion(2, 99, "Deleted", "General", "medium"),
            new QuizHistoryQuestion(3, 11, "First", "General", "medium"),
        };

        var restored = QuizHistoryDraftRestorer.Restore(history, new[] { first, second });

        Assert.Equal(new[] { 22, 11 }, restored.Questions.Select(question => question.Id));
        Assert.Equal(new[] { 99 }, restored.MissingQuestionIds);
    }

    private static QuizVideoBuildResult CreateBuild(string root, string title)
    {
        var folder = Path.Combine(root, "Quizzes", title);
        var cards = Path.Combine(folder, "Cards");
        Directory.CreateDirectory(cards);
        var image = Path.Combine(cards, "000_intro.png");
        File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
        var quizJson = Path.Combine(folder, "quiz.json");
        File.WriteAllText(quizJson, "{}");

        var timeline = new NativeTimeline { Name = title };
        var track = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        track.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 1,
            Source = image,
            Name = "Intro",
        });
        return new QuizVideoBuildResult(folder, quizJson, timeline, null!);
    }

    private static QuizQuestion Question(int id, string text) => new(
        id,
        text,
        "A",
        "B",
        "C",
        "D",
        0,
        "",
        "General",
        "medium",
        "test",
        0);
}
