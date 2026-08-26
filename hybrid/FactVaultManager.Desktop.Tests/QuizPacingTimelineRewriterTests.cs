namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPacingTimelineRewriterTests
{
    [Fact]
    public void Apply_AddsSuspenseAndAnswerPauseWithoutMovingNarration()
    {
        var timeline = BuildTimeline();
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8, AnswerSeconds: 3);

        var added = QuizPacingTimelineRewriter.Apply(timeline, options);

        Assert.Equal(0.85, added, 6);
        Assert.Equal(2, FindAudio(timeline, "Question 1 Narration").Start, 6);
        Assert.Equal(4.5, FindVideo(timeline, "Question 1").Start, 6);
        Assert.Equal(9.5, FindVideo(timeline, "Question 1 Countdown 3").Start, 6);
        Assert.Equal(12.85, FindVideo(timeline, "Answer 1 Reveal").Start, 6);
        Assert.Equal(15.85, FindVideo(timeline, "Quiz Outro").Start, 6);

        var suspense = FindVideo(timeline, "Question 1 Suspense Beat");
        Assert.Equal(4, suspense.Start, 6);
        Assert.Equal(0.5, suspense.Duration, 6);
        Assert.Equal("question.png", suspense.Source);
        Assert.Equal("suspense", Convert.ToString(suspense.Metadata["quiz_card"]));

        var answerPause = FindVideo(timeline, "Question 1 Answer Pause");
        Assert.Equal(12.5, answerPause.Start, 6);
        Assert.Equal(0.35, answerPause.Duration, 6);
        Assert.Equal("countdown-1.png", answerPause.Source);
        Assert.Equal("answer_pause", Convert.ToString(answerPause.Metadata["quiz_card"]));

        var scene = Assert.Single(timeline.Scenes);
        Assert.Equal(2, scene.Start, 6);
        Assert.Equal(13.85, scene.Duration, 6);
        Assert.Equal(0.5, Convert.ToDouble(scene.Metadata["narration_suspense_seconds"]), 6);
        Assert.Equal(0.35, Convert.ToDouble(scene.Metadata["answer_reveal_pause_seconds"]), 6);
        Assert.Equal(0.85, Convert.ToDouble(timeline.Metadata["quiz_pacing_added_seconds"]), 6);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var timeline = BuildTimeline();
        var options = new QuizVideoBuildOptions("Quiz");

        var first = QuizPacingTimelineRewriter.Apply(timeline, options);
        var duration = timeline.Duration;
        var second = QuizPacingTimelineRewriter.Apply(timeline, options);

        Assert.Equal(0.85, first, 6);
        Assert.Equal(0, second, 6);
        Assert.Equal(duration, timeline.Duration, 6);
        Assert.Single(AllVideo(timeline).Where(clip => clip.Name == "Question 1 Suspense Beat"));
        Assert.Single(AllVideo(timeline).Where(clip => clip.Name == "Question 1 Answer Pause"));
    }

    [Fact]
    public void Apply_LeavesStandaloneVerticalShortPacingUntouched()
    {
        var timeline = BuildTimeline();
        timeline.Width = 1080;
        timeline.Height = 1920;
        var options = new QuizVideoBuildOptions("Short", Vertical: true);
        var before = timeline.Duration;

        var added = QuizPacingTimelineRewriter.Apply(timeline, options);

        Assert.Equal(0, added, 6);
        Assert.Equal(before, timeline.Duration, 6);
        Assert.DoesNotContain(AllVideo(timeline), clip =>
            clip.Metadata.TryGetValue("pacing_hold", out var value) && value is true);
    }

    private static NativeTimeline BuildTimeline()
    {
        var timeline = new NativeTimeline { Name = "Quiz", Width = 1920, Height = 1080 };
        var video = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        var audio = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Narration",
            Kind = NativeTimelineTrackKind.Audio,
        });

        video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 2,
            Source = "intro.png",
            Name = "Quiz Intro",
            Metadata = new() { ["quiz_card"] = "intro" },
        });

        var narration = audio.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Audio,
            Start = 2,
            Duration = 2,
            Source = "narration.wav",
            Name = "Question 1 Narration",
        });
        var narrationCard = video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 2,
            Duration = 2,
            Source = "narration.png",
            Name = "Question 1 Narration Card",
            Metadata = new() { ["quiz_card"] = "narration", ["question_id"] = 1 },
        });
        var question = video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 4,
            Duration = 5,
            Source = "question.png",
            Name = "Question 1",
            Metadata = new() { ["quiz_card"] = "question", ["question_id"] = 1 },
        });
        var countdown3 = AddCountdown(video, 9, 3);
        var countdown2 = AddCountdown(video, 10, 2);
        var countdown1 = AddCountdown(video, 11, 1);
        var reveal = video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 12,
            Duration = 0.5,
            Source = "reveal.png",
            Name = "Answer 1 Reveal",
            Metadata = new() { ["quiz_card"] = "answer_reveal", ["question_id"] = 1 },
        });
        var answer = video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 12.5,
            Duration = 2.5,
            Source = "answer.png",
            Name = "Answer 1",
            Metadata = new() { ["quiz_card"] = "answer", ["question_id"] = 1 },
        });
        video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 15,
            Duration = 5,
            Source = "outro.png",
            Name = "Quiz Outro",
            Metadata = new() { ["quiz_card"] = "outro" },
        });

        timeline.AddScene(new NativeTimelineScene
        {
            Title = "Question 1",
            Start = 2,
            Duration = 13,
            ClipIds =
            [
                narration.Id,
                narrationCard.Id,
                question.Id,
                countdown3.Id,
                countdown2.Id,
                countdown1.Id,
                reveal.Id,
                answer.Id,
            ],
            Metadata = new()
            {
                ["question_id"] = 1,
                ["narration_seconds"] = 2d,
            },
        });
        return timeline;
    }

    private static NativeTimelineClip AddCountdown(NativeTimelineTrack video, double start, int remaining) =>
        video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = start,
            Duration = 1,
            Source = $"countdown-{remaining}.png",
            Name = $"Question 1 Countdown {remaining}",
            Metadata = new()
            {
                ["quiz_card"] = "countdown",
                ["question_id"] = 1,
                ["seconds_remaining"] = remaining,
            },
        });

    private static NativeTimelineClip FindVideo(NativeTimeline timeline, string name) =>
        AllVideo(timeline).Single(clip => clip.Name == name);

    private static NativeTimelineClip FindAudio(NativeTimeline timeline, string name) =>
        timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .Single(clip => clip.Name == name);

    private static IEnumerable<NativeTimelineClip> AllVideo(NativeTimeline timeline) =>
        timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips);
}
