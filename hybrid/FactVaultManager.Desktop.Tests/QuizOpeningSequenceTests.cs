using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizOpeningSequenceTests
{
    [Fact]
    public void Planner_InsertsThreeTwoOneBeforeFirstQuestionAndKeepsAudioAligned()
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
        builder.AddScene("Question 1", 2, 5, "Question", new[] { question.Id, narration.Id });

        var spinPaths = Enumerable.Range(0, QuizOpeningSequence.SpinFrameCount)
            .Select(index => $"spin-{index}.png")
            .ToArray();
        var countdownPaths = new Dictionary<int, string>
        {
            [3] = "three.png",
            [2] = "two.png",
            [1] = "one.png",
        };

        QuizOpeningTimelinePlanner.Apply(builder.Timeline, spinPaths, countdownPaths);

        Assert.Equal(5, question.Start);
        Assert.Equal(5, narration.Start);
        Assert.Equal(5, builder.Timeline.Scenes.Single().Start);
        Assert.Equal(10, builder.Timeline.Duration);

        var countdown = builder.Timeline.GetTrack("Quiz Cards")!.Clips
            .Where(clip => clip.Metadata.TryGetValue("quiz_card", out var value) &&
                           string.Equals(Convert.ToString(value), "start_countdown", StringComparison.Ordinal))
            .OrderBy(clip => clip.Start)
            .ToArray();
        Assert.Equal(new[] { 2d, 3d, 4d }, countdown.Select(clip => clip.Start).ToArray());
        Assert.Equal(new[] { "3", "2", "1" }, countdown.Select(clip => Convert.ToString(clip.Metadata["seconds_remaining"])).ToArray());
        Assert.Equal(QuizOpeningSequence.SpinFrameCount,
            builder.Timeline.GetTrack("Quiz Cards")!.Clips.Count(clip =>
                clip.Metadata.TryGetValue("quiz_card", out var value) &&
                string.Equals(Convert.ToString(value), "intro_spin", StringComparison.Ordinal)));
    }
}
