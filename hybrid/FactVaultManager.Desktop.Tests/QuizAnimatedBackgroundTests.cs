namespace FactVaultManager.Desktop.Tests;

public sealed class QuizAnimatedBackgroundTests
{
    [Fact]
    public void ApplyTimeline_InsertsLoopingBackgroundBelowQuizCards()
    {
        var timeline = new NativeTimeline
        {
            FrameRate = 30,
            Width = 1920,
            Height = 1080,
        };
        var cards = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        cards.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 25.5,
            Source = Path.GetFullPath("question.png"),
            Name = "Question",
        });

        var originalStart = cards.Clips[0].Start;
        var originalDuration = cards.Clips[0].Duration;
        QuizAnimatedBackground.ApplyTimeline(timeline, Path.GetFullPath("background.mp4"), 12);

        var background = timeline.GetTrack(QuizAnimatedBackground.TrackName);
        Assert.NotNull(background);
        Assert.Same(timeline.Tracks[0], background);
        Assert.Equal(NativeTimelineTrackKind.Video, background!.Kind);
        Assert.Equal(3, background.Clips.Count);
        Assert.Equal(new[] { 0.0, 12.0, 24.0 }, background.Clips.Select(clip => clip.Start).ToArray());
        Assert.Equal(new[] { 12.0, 12.0, 1.5 }, background.Clips.Select(clip => clip.Duration).ToArray());
        Assert.All(background.Clips, clip => Assert.Equal(NativeTimelineClipKind.Video, clip.Kind));
        Assert.Equal(originalStart, cards.Clips[0].Start);
        Assert.Equal(originalDuration, cards.Clips[0].Duration);
        Assert.True((bool)timeline.Metadata["animated_background_applied"]!);
    }
}
