namespace FactVaultManager.Desktop.Tests;

public sealed class QuizFinalRenderTimelineTests
{
    [Fact]
    public void Prepare_KeepsCanonicalQuizCardsAndRemovesAuxiliaryVideoTracks()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiz-final-render-normalization");
        var timeline = new NativeTimeline
        {
            Name = "Quiz",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
        };
        var background = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = QuizAnimatedBackground.TrackName,
            Kind = NativeTimelineTrackKind.Video,
        });
        background.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Video,
            Start = 0,
            Duration = 2,
            Source = Path.Combine(root, "background.mp4"),
        });
        var cards = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = QuizFinalRenderTimeline.PrimaryVideoTrackName,
            Kind = NativeTimelineTrackKind.Video,
        });
        var firstCard = cards.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 1,
            Source = Path.Combine(root, "card-1.png"),
        });
        var secondCard = cards.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 1,
            Duration = 1,
            Source = Path.Combine(root, "card-2.png"),
        });
        var auxiliary = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Video 1",
            Kind = NativeTimelineTrackKind.Video,
        });
        var auxiliaryClip = auxiliary.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 2,
            Source = Path.Combine(root, "legacy-overlay.png"),
        });
        var narration = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Narration",
            Kind = NativeTimelineTrackKind.Audio,
        });
        narration.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Audio,
            Start = 0,
            Duration = 1,
            Source = Path.Combine(root, "voice.wav"),
        });
        timeline.AddScene(new NativeTimelineScene
        {
            Title = "Question 1",
            Start = 0,
            Duration = 2,
            ClipIds = new List<string> { firstCard.Id, secondCard.Id, auxiliaryClip.Id },
        });

        var prepared = QuizFinalRenderTimeline.Prepare(timeline);

        var videoNames = prepared.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .Select(track => track.Name)
            .ToArray();
        Assert.Equal(new[] { QuizAnimatedBackground.TrackName, QuizFinalRenderTimeline.PrimaryVideoTrackName }, videoNames);
        Assert.DoesNotContain(auxiliaryClip.Id, prepared.Scenes.Single().ClipIds);
        Assert.Single(prepared.Tracks, track => track.Kind == NativeTimelineTrackKind.Audio);

        var plan = NativeQuizFinalRenderer.CreatePlan(prepared);
        Assert.Equal(2, plan.ForegroundClips.Count);
    }

    [Fact]
    public void Prepare_DoesNotRelaxNonQuizTimelinesWithoutCanonicalCardsTrack()
    {
        var timeline = new NativeTimeline
        {
            Name = "Other",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
        };
        foreach (var name in new[] { "Video 1", "Video 2" })
        {
            var track = timeline.AddTrack(new NativeTimelineTrack
            {
                Name = name,
                Kind = NativeTimelineTrackKind.Video,
            });
            track.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = 0,
                Duration = 2,
                Source = Path.Combine(Path.GetTempPath(), name + ".png"),
            });
        }

        var prepared = QuizFinalRenderTimeline.Prepare(timeline);

        Assert.Equal(2, prepared.Tracks.Count(track => track.Kind == NativeTimelineTrackKind.Video));
        Assert.Throws<InvalidOperationException>(() => NativeQuizFinalRenderer.CreatePlan(prepared));
    }
}
