using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizLongFormZeroIntroTests
{
    [Fact]
    public void TrimTimeline_StartsQuestionNarrationAndEffectsAtZeroBasedTiming()
    {
        var builder = new NativeTimelineBuilder("Quiz");
        var intro = builder.AddClip(
            "Quiz Cards",
            NativeTimelineTrackKind.Video,
            NativeTimelineClipKind.Image,
            0,
            QuizOpeningSequence.IntroSeconds,
            "intro.png",
            "Quiz Intro");
        intro.Metadata["quiz_card"] = "intro";

        var question = builder.AddClip(
            "Quiz Cards",
            NativeTimelineTrackKind.Video,
            NativeTimelineClipKind.Image,
            2,
            5,
            "question.png",
            "Question 1");
        var narration = builder.AddClip(
            "Quiz Narration",
            NativeTimelineTrackKind.Audio,
            NativeTimelineClipKind.Audio,
            2,
            1.5,
            "question.mp3",
            "Question 1 Narration");
        var tick = builder.AddClip(
            "Quiz SFX",
            NativeTimelineTrackKind.Audio,
            NativeTimelineClipKind.Audio,
            6,
            0.14,
            "tick.wav",
            "Countdown Tick");
        var music = builder.AddClip(
            "Quiz Background Music",
            NativeTimelineTrackKind.Audio,
            NativeTimelineClipKind.Audio,
            0,
            12,
            "music.wav",
            "Quiz Background Music");
        music.Metadata["quiz_audio"] = "background_music";
        builder.AddScene("Question 1", 2, 5, "Question", new[] { question.Id, narration.Id, tick.Id });

        var trimmed = QuizLongFormZeroIntro.TrimTimeline(builder.Timeline);

        Assert.Equal(2, trimmed, precision: 6);
        Assert.Equal(0, question.Start, precision: 6);
        Assert.Equal(0, narration.Start, precision: 6);
        Assert.Equal(4, tick.Start, precision: 6);
        Assert.Equal(0, builder.Timeline.Scenes.Single().Start, precision: 6);
        Assert.DoesNotContain(builder.Timeline.GetTrack("Quiz Cards")!.Clips,
            clip => clip.Metadata.TryGetValue("quiz_card", out var value) &&
                    string.Equals(Convert.ToString(value), "intro", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, music.Start, precision: 6);
        Assert.Equal(2, music.SourceIn, precision: 6);
        Assert.Equal(10, music.Duration, precision: 6);
        builder.Timeline.Validate();
    }

    [Fact]
    public void TrimTimeline_IsIdempotentAfterQuestionOneStartsAtZero()
    {
        var builder = new NativeTimelineBuilder("Quiz");
        var intro = builder.AddClip(
            "Quiz Cards",
            NativeTimelineTrackKind.Video,
            NativeTimelineClipKind.Image,
            0,
            2,
            "intro.png",
            "Quiz Intro");
        intro.Metadata["quiz_card"] = "intro";
        var question = builder.AddClip(
            "Quiz Cards",
            NativeTimelineTrackKind.Video,
            NativeTimelineClipKind.Image,
            2,
            5,
            "question.png",
            "Question 1");
        builder.AddScene("Question 1", 2, 5, "Question", new[] { question.Id });

        Assert.Equal(2, QuizLongFormZeroIntro.TrimTimeline(builder.Timeline), precision: 6);
        Assert.Equal(0, QuizLongFormZeroIntro.TrimTimeline(builder.Timeline), precision: 6);
        Assert.Equal(0, question.Start, precision: 6);
        Assert.Equal(0, builder.Timeline.Scenes.Single().Start, precision: 6);
    }

    [Fact]
    public void RenderAndApply_RejectsVerticalShorts()
    {
        var builder = new NativeTimelineBuilder("Short Quiz");
        var options = new QuizVideoBuildOptions("Short Quiz", Vertical: true);

        Assert.Throws<ArgumentException>(() =>
            QuizLongFormZeroIntro.RenderAndApply(builder.Timeline, Path.GetTempPath(), options));
    }
}
