namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPromoShortTests
{
    [Fact]
    public void Planner_SelectsFirstInsaneSceneUsingSavedTimelineTimestamp()
    {
        var timeline = Timeline(
            Scene("Question 1", 2, 11, "easy", 101),
            Scene("Question 2", 13, 12, "insane", 202),
            Scene("Question 3", 25, 12, "insane", 303));

        var plan = QuizPromoShortPlanner.Create(timeline, sourceVideoDuration: 60, endCardDuration: 4);

        Assert.Equal(13, plan.SourceStart);
        Assert.Equal(12, plan.SourceDuration);
        Assert.Equal(4, plan.EndCardDuration);
        Assert.Equal("Question 2", plan.SceneTitle);
        Assert.Equal(202, plan.QuestionId);
        Assert.Equal(16, plan.TotalDuration);
    }

    [Fact]
    public void Planner_CapsCombinedVideoAtFortyFiveSeconds()
    {
        var timeline = Timeline(Scene("Insane marathon", 10, 60, "insane", 404));

        var plan = QuizPromoShortPlanner.Create(timeline, sourceVideoDuration: 90, endCardDuration: 6);

        Assert.Equal(39, plan.SourceDuration);
        Assert.Equal(QuizPromoShortPlanner.MaximumDuration, plan.TotalDuration);
    }

    [Fact]
    public void Planner_RequiresAnInsaneRound()
    {
        var timeline = Timeline(Scene("Question 1", 2, 11, "hard", 101));

        var error = Assert.Throws<InvalidOperationException>(() =>
            QuizPromoShortPlanner.Create(timeline, sourceVideoDuration: 30, endCardDuration: 4));

        Assert.Contains("Insane", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_ReframesLandscapeVideoAndConcatenatesEndCardAudio()
    {
        var plan = new QuizPromoShortPlan(20, 12, 4, "Question 10", 10);

        var filter = QuizPromoShortRenderer.BuildFilter(plan, hasSourceAudio: true);

        Assert.Contains("scale=1080:1920:force_original_aspect_ratio=increase", filter, StringComparison.Ordinal);
        Assert.Contains("crop=1080:1920", filter, StringComparison.Ordinal);
        Assert.Contains("force_original_aspect_ratio=decrease", filter, StringComparison.Ordinal);
        Assert.Contains("[0:a]atrim=duration=12", filter, StringComparison.Ordinal);
        Assert.Contains("concat=n=2:v=1:a=1", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_UsesSilenceWhenRenderedVideoHasNoAudioTrack()
    {
        var plan = new QuizPromoShortPlan(20, 12, 4, "Question 10", 10);

        var filter = QuizPromoShortRenderer.BuildFilter(plan, hasSourceAudio: false);

        Assert.Contains("anullsrc=r=48000:cl=stereo:d=12[a0]", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("[0:a]", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesDirectRelatedVideoCallToAction()
    {
        Assert.Equal(
            QuizPromoShortScript.DefaultCallToAction,
            QuizPromoShortScript.Normalize(""));
        Assert.Contains("related video", QuizPromoShortScript.DefaultCallToAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("welcome", QuizPromoShortScript.DefaultCallToAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndCardBranding_UsesConfiguredQuizLogo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-logo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logo = Path.Combine(root, "quiz_logo.png");
            var pixels = Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray();
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(logo)) encoder.Save(stream);

            var branding = QuizPromoShortEndCardRenderer.BuildBranding(logo);

            var image = Assert.IsType<System.Windows.Controls.Image>(branding);
            Assert.NotNull(image.Source);
            Assert.Equal(260, image.Height);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static NativeTimeline Timeline(params NativeTimelineScene[] scenes)
    {
        var timeline = new NativeTimeline();
        foreach (var scene in scenes) timeline.AddScene(scene);
        return timeline;
    }

    private static NativeTimelineScene Scene(
        string title,
        double start,
        double duration,
        string difficulty,
        int questionId) => new()
    {
        Title = title,
        Start = start,
        Duration = duration,
        Metadata = new Dictionary<string, object?>
        {
            ["difficulty"] = difficulty,
            ["question_id"] = questionId,
        },
    };
}
