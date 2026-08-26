namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailTests
{
    [Fact]
    public void Defaults_BuildsShortChallengeHeadlineAndCategorySubtitle()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Science Challenge",
            8,
            [Question(1), Question(2), Question(3)],
            vertical: false);

        var thumbnail = QuizThumbnailDefaults.Create(metadata, 3);

        Assert.Equal("CAN YOU SOLVE IT?", thumbnail.Headline);
        Assert.Equal("SCIENCE CHALLENGE", thumbnail.Subtitle);
    }

    [Theory]
    [InlineData(10, false, "FINAL BOSS QUESTION")]
    [InlineData(6, false, "ONLY EXPERTS?")]
    [InlineData(1, false, "CAN YOU SOLVE IT?")]
    [InlineData(10, true, "NAME THIS LOGO")]
    public void Defaults_UsesTwoToFourWordHighFrictionHooks(int count, bool logoQuiz, string expected)
    {
        var hook = QuizThumbnailIntelligence.DefaultHook(count, logoQuiz);

        Assert.Equal(expected, hook);
        var words = hook.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.InRange(words.Length, 2, 4);
    }

    [Theory]
    [InlineData("", "GENERAL KNOWLEDGE QUIZ", true)]
    [InlineData("GENERAL KNOWLEDGE QUIZ", "GENERAL KNOWLEDGE QUIZ", true)]
    [InlineData("SCIENCE QUIZ", "SCIENCE QUIZ", true)]
    [InlineData("ICONS", "GENERAL KNOWLEDGE QUIZ", true)]
    [InlineData("ICONS QUIZ", "GENERAL KNOWLEDGE QUIZ", true)]
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

        Assert.Equal("HISTORY", thumbnail.Subtitle);
    }

    [Fact]
    public void Defaults_LogoQuizUsesLogoSubtitle()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Logos",
            1,
            [Question(1), Question(2)],
            vertical: false,
            logoQuiz: true);

        var thumbnail = QuizThumbnailDefaults.Create(metadata, 2, logoQuiz: true);

        Assert.Equal("NAME THIS LOGO", thumbnail.Headline);
        Assert.Equal("LOGOS", thumbnail.Subtitle);
    }

    [Fact]
    public void Intelligence_PrefersInsaneQuestionAndBuildsRoundFourHook()
    {
        var questions = new[]
        {
            Question(1, difficulty: "easy"),
            Question(2, difficulty: "hard", question: "Which planet is the largest?"),
            Question(3, difficulty: "insane", question: "What is the shortest day in the Solar System?", category: "Space"),
        };
        var metadata = Metadata(questions, "Space Quiz");

        var recommendation = QuizThumbnailIntelligence.Recommend(metadata, questions);

        Assert.Equal(3, recommendation.Question.Id);
        Assert.Equal(3, recommendation.QuestionNumber);
        Assert.Equal("FINAL BOSS QUESTION", recommendation.Hook);
        Assert.Equal("ROUND 4 • INSANE", recommendation.Badge);
        Assert.Equal("SPACE • INSANE", recommendation.Subtitle);
        Assert.Contains("shortest day", recommendation.Teaser, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Intelligence_LogoQuizStronglyPrefersQuestionWithArtwork()
    {
        var root = Path.Combine(Path.GetTempPath(), "thumbnail-intelligence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artwork = Path.Combine(root, "logo.png");
        File.WriteAllText(artwork, "test");
        try
        {
            var questions = new[]
            {
                Question(1, difficulty: "insane", category: "Logos"),
                Question(2, difficulty: "hard", category: "Logos", imagePath: artwork),
            };
            var metadata = Metadata(questions, "Logos", logoQuiz: true);

            var recommendation = QuizThumbnailIntelligence.Recommend(metadata, questions, logoQuiz: true);

            Assert.Equal(2, recommendation.Question.Id);
            Assert.True(recommendation.HasArtwork);
            Assert.Equal("NAME THIS LOGO", recommendation.Hook);
            Assert.Equal("LOGO CHALLENGE", recommendation.Badge);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Intelligence_UsesImpactTermsAsTieBreakerWithinDifficulty()
    {
        var ordinary = Question(1, difficulty: "hard", question: "Which object is shown here?");
        var highImpact = Question(2, difficulty: "hard", question: "Which planet has the fastest rotation and shortest day?", category: "Space");
        var questions = new[] { ordinary, highImpact };

        var recommendation = QuizThumbnailIntelligence.Recommend(Metadata(questions, "Space Quiz"), questions);

        Assert.Equal(highImpact.Id, recommendation.Question.Id);
        Assert.True(
            QuizThumbnailIntelligence.Score(highImpact, 1, 2) >
            QuizThumbnailIntelligence.Score(ordinary, 0, 2));
    }

    [Fact]
    public void Intelligence_IsDeterministicForSameQuestionSet()
    {
        var questions = new[]
        {
            Question(4, difficulty: "hard"),
            Question(7, difficulty: "hard"),
            Question(9, difficulty: "hard"),
        };
        var metadata = Metadata(questions, "Science Quiz");

        var first = QuizThumbnailIntelligence.Recommend(metadata, questions);
        var second = QuizThumbnailIntelligence.Recommend(metadata, questions);

        Assert.Equal(first.Question.Id, second.Question.Id);
        Assert.Equal(first.Hook, second.Hook);
        Assert.Equal(first.Score, second.Score);
    }

    [Fact]
    public void Intelligence_TeaserIsCappedForMobileReadability()
    {
        var longQuestion = Question(
            10,
            difficulty: "insane",
            question: "Which extraordinarily distant object in the observable universe demonstrates the most surprising property in this deliberately long quiz question?",
            category: "Space");
        var questions = new[] { longQuestion };

        var recommendation = QuizThumbnailIntelligence.Recommend(Metadata(questions, "Space Quiz"), questions);

        Assert.InRange(recommendation.Teaser.Length, 1, 76);
        Assert.EndsWith("...", recommendation.Teaser, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_UpgradesLegacyAutomaticHeadlineButPreservesManualCopy()
    {
        var questions = new[] { Question(1, difficulty: "insane", category: "Space") };
        var recommendation = QuizThumbnailIntelligence.Recommend(Metadata(questions, "Space Quiz"), questions);

        var legacy = QuizThumbnailRenderer.UpgradeLegacyAutomaticCopy(
            new QuizThumbnailSettings("CAN YOU GET 10/10?", "SPACE"),
            recommendation);
        var manual = QuizThumbnailRenderer.UpgradeLegacyAutomaticCopy(
            new QuizThumbnailSettings("THE UNIVERSE'S SECRET", "SPACE"),
            recommendation);

        Assert.Equal("FINAL BOSS QUESTION", legacy.Headline);
        Assert.Equal("THE UNIVERSE'S SECRET", manual.Headline);
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
    public void Renderer_BrandLabelDoesNotIncludeEpisodeNumber()
    {
        Assert.Equal("FACTBURST QUIZ", QuizThumbnailRenderer.BrandLabel());
        Assert.DoesNotContain("#", QuizThumbnailRenderer.BrandLabel());
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

    private static QuizPublishMetadata Metadata(
        IReadOnlyList<QuizQuestion> questions,
        string series,
        bool logoQuiz = false) =>
        QuizPublishMetadataGenerator.Generate(series, 1, questions, vertical: false, logoQuiz: logoQuiz);

    private static QuizQuestion Question(
        int id,
        string difficulty = "medium",
        string? question = null,
        string category = "Science",
        string imagePath = "") => new(
        id,
        question ?? $"Question {id}?",
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Explanation",
        category,
        difficulty,
        "Test",
        0,
        true,
        imagePath);
}
