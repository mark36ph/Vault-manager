namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailTests
{
    [Fact]
    public void Defaults_BuildsChallengeHeadlineAndCategorySubtitle()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Science Challenge",
            8,
            [Question(1), Question(2), Question(3)],
            vertical: false);

        var thumbnail = QuizThumbnailDefaults.Create(metadata, 3);

        Assert.Equal("CAN YOU GET 3/3?", thumbnail.Headline);
        Assert.Equal("SCIENCE CHALLENGE", thumbnail.Subtitle);
    }

    [Theory]
    [InlineData("", "GENERAL KNOWLEDGE QUIZ", true)]
    [InlineData("GENERAL KNOWLEDGE QUIZ", "GENERAL KNOWLEDGE QUIZ", true)]
    [InlineData("SCIENCE QUIZ", "SCIENCE QUIZ", true)]
    [InlineData("THE PAST AWAITS", "GENERAL KNOWLEDGE QUIZ", false)]
    public void Defaults_ReplacesOnlyBlankOrPreviouslyAutomaticSubtitle(
        string current,
        string previousAuto,
        bool expected)
    {
        Assert.Equal(
            expected,
            QuizThumbnailDefaults.ShouldReplaceSubtitle(current, previousAuto));
    }

    [Fact]
    public void Defaults_HistoryMetadataBuildsHistorySubtitle()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "History Quiz",
            1,
            [Question(1), Question(2)],
            vertical: false);

        var thumbnail = QuizThumbnailDefaults.Create(metadata, 2);

        Assert.Equal("HISTORY QUIZ", thumbnail.Subtitle);
    }

    [Fact]
    public void Settings_NormalizeRejectsMissingOrOverlongCopy()
    {
        Assert.Throws<ArgumentException>(() => new QuizThumbnailSettings("", "Subtitle").Normalize());
        Assert.Throws<ArgumentException>(() => new QuizThumbnailSettings("Headline", "").Normalize());
        Assert.Throws<ArgumentException>(() =>
            new QuizThumbnailSettings(new string('H', QuizThumbnailSettings.MaxHeadlineLength + 1), "Subtitle").Normalize());
    }

    [Fact]
    public void Renderer_UsesPortablePngFileName()
    {
        Assert.Equal("Thumbnail.png", QuizThumbnailRenderer.FileName);
        Assert.Equal(1280, QuizThumbnailRenderer.Width);
        Assert.Equal(720, QuizThumbnailRenderer.Height);
        Assert.Equal(1080, QuizThumbnailRenderer.ShortsWidth);
        Assert.Equal(1920, QuizThumbnailRenderer.ShortsHeight);
        Assert.Equal((1280, 720), QuizThumbnailRenderer.Dimensions(vertical: false));
        Assert.Equal((1080, 1920), QuizThumbnailRenderer.Dimensions(vertical: true));
        Assert.Equal(70d, QuizThumbnailRenderer.LogoHeight);
        Assert.Equal(104d, QuizThumbnailRenderer.BottomRightLogoBottomMargin);
    }

    [Theory]
    [InlineData(1, "1 QUESTION")]
    [InlineData(10, "10 QUESTIONS")]
    public void Renderer_UsesCorrectQuestionCountGrammar(int count, string expected)
    {
        Assert.Equal(expected, QuizThumbnailRenderer.QuestionCountLabel(count));
    }

    [Fact]
    public void Checklist_ShowsRequestedPublishingItemsAndPostExportStates()
    {
        var beforeExport = QuizPublishChecklist.Evaluate(
            draftQuestionCount: 10,
            youtubeTitleReady: true,
            descriptionReady: true,
            hashtagsReady: true,
            pinnedCommentReady: true,
            thumbnailReady: true,
            resolveExportReady: false,
            historyRecorded: false);
        var afterExport = QuizPublishChecklist.Evaluate(
            draftQuestionCount: 10,
            youtubeTitleReady: true,
            descriptionReady: true,
            hashtagsReady: true,
            pinnedCommentReady: true,
            thumbnailReady: true,
            resolveExportReady: true,
            historyRecorded: true);

        Assert.Equal(
            new[]
            {
                "Quiz draft",
                "YouTube title",
                "Description",
                "Hashtags",
                "Pinned comment",
                "Thumbnail",
                "Resolve export",
                "Quiz History entry",
            },
            beforeExport.Select(item => item.Label).ToArray());
        Assert.True(beforeExport.Take(6).All(item => item.IsComplete));
        Assert.False(beforeExport[6].IsComplete);
        Assert.False(beforeExport[7].IsComplete);
        Assert.True(afterExport.All(item => item.IsComplete));
        Assert.Contains("Quiz History entry", QuizPublishChecklist.Format(afterExport));
    }

    [Fact]
    public void Checklist_TracksMetadataFieldsIndependently()
    {
        var items = QuizPublishChecklist.Evaluate(
            draftQuestionCount: 10,
            youtubeTitleReady: true,
            descriptionReady: false,
            hashtagsReady: true,
            pinnedCommentReady: false,
            thumbnailReady: true,
            resolveExportReady: false,
            historyRecorded: false);

        Assert.True(items.Single(item => item.Label == "YouTube title").IsComplete);
        Assert.False(items.Single(item => item.Label == "Description").IsComplete);
        Assert.True(items.Single(item => item.Label == "Hashtags").IsComplete);
        Assert.False(items.Single(item => item.Label == "Pinned comment").IsComplete);
    }

    private static QuizQuestion Question(int id) => new(
        id,
        $"Question {id}?",
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Explanation",
        "Science",
        "medium",
        "Test",
        0);
}
