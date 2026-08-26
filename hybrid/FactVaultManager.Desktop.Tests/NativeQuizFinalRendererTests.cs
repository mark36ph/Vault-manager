namespace FactVaultManager.Desktop.Tests;

public sealed class NativeQuizFinalRendererTests
{
    [Fact]
    public void CreatePlan_UsesAnimatedBackgroundCardsAndAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "native-render-plan");
        var background = Path.Combine(root, "background.mp4");
        var first = Path.Combine(root, "card-a.png");
        var second = Path.Combine(root, "card-b.png");
        var narration = Path.Combine(root, "voice.wav");
        var timeline = new NativeTimeline
        {
            Name = "Quiz",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
        };
        var backgroundTrack = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = QuizAnimatedBackground.TrackName,
            Kind = NativeTimelineTrackKind.Video,
        });
        backgroundTrack.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Video,
            Start = 0,
            Duration = 2,
            Source = background,
        });
        var cards = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        cards.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 0.75,
            Source = first,
        });
        cards.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0.75,
            Duration = 1.25,
            Source = second,
        });
        var audio = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Narration",
            Kind = NativeTimelineTrackKind.Audio,
        });
        audio.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Audio,
            Start = 0.2,
            Duration = 1.1,
            Source = narration,
        });

        var plan = NativeQuizFinalRenderer.CreatePlan(timeline);

        Assert.Equal(Path.GetFullPath(background), plan.BackgroundSource);
        Assert.Equal(new[] { first, second }, plan.ForegroundClips.Select(clip => clip.Source));
        Assert.Single(plan.AudioClips);
        Assert.Equal(2, plan.Duration, precision: 6);
        Assert.Equal(1920, plan.Width);
        Assert.Equal(1080, plan.Height);
        Assert.Equal(30, plan.FrameRate, precision: 6);
    }

    [Fact]
    public void CreatePlan_AllowsSolidBackgroundFallback()
    {
        var timeline = CardsOnlyTimeline();

        var plan = NativeQuizFinalRenderer.CreatePlan(timeline);

        Assert.Null(plan.BackgroundSource);
        Assert.Equal(2, plan.ForegroundClips.Count);
    }

    [Fact]
    public void CreatePlan_RejectsMultipleForegroundVideoTracks()
    {
        var timeline = CardsOnlyTimeline();
        var extra = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Unexpected Overlay",
            Kind = NativeTimelineTrackKind.Video,
        });
        extra.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 2,
            Source = Path.Combine(Path.GetTempPath(), "overlay.png"),
        });

        var error = Assert.Throws<InvalidOperationException>(() => NativeQuizFinalRenderer.CreatePlan(timeline));

        Assert.Contains("one quiz-card video track", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_RejectsCardTimelineGaps()
    {
        var timeline = CardsOnlyTimeline();
        timeline.GetTrack("Quiz Cards")!.Clips[1].Start = 1.25;

        var error = Assert.Throws<InvalidOperationException>(() => NativeQuizFinalRenderer.CreatePlan(timeline));

        Assert.Contains("gap or overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConcatManifest_PreservesExactCardDurationsAndRepeatsLastFrame()
    {
        var timeline = CardsOnlyTimeline();
        var clips = timeline.GetTrack("Quiz Cards")!.Clips;

        var manifest = NativeQuizFinalRenderer.BuildConcatManifest(clips);

        Assert.StartsWith("ffconcat version 1.0", manifest, StringComparison.Ordinal);
        Assert.Contains("duration 0.75", manifest, StringComparison.Ordinal);
        Assert.Contains("duration 1.25", manifest, StringComparison.Ordinal);
        Assert.Equal(3, manifest.Split('\n').Count(line => line.StartsWith("file ", StringComparison.Ordinal)));
    }

    [Fact]
    public void OutputPath_IsEasyForUploadManagerToRecognizeAsFinalVideo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "quiz-project");

        var path = NativeQuizFinalRenderer.OutputPath(folder);

        Assert.Equal(NativeQuizFinalRenderer.FinalFileName, Path.GetFileName(path));
        Assert.Contains("Final", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".mp4", Path.GetExtension(path));
    }

    private static NativeTimeline CardsOnlyTimeline()
    {
        var timeline = new NativeTimeline
        {
            Name = "Quiz",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
        };
        var track = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        track.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 0.75,
            Source = Path.Combine(Path.GetTempPath(), "card-1.png"),
        });
        track.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0.75,
            Duration = 1.25,
            Source = Path.Combine(Path.GetTempPath(), "card-2.png"),
        });
        return timeline;
    }
}
