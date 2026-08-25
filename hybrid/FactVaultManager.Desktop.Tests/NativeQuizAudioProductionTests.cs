using System.Text;
using System.Net;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeQuizAudioProductionTests
{
    [Fact]
    public void VoiceCatalog_NormalizesBuiltInVoice()
    {
        Assert.Equal("marin", QuizVoiceCatalog.Validate(" Marin "));
        Assert.Contains("alloy", QuizVoiceCatalog.BuiltInVoices);
        Assert.Contains("cedar", QuizVoiceCatalog.BuiltInVoices);
    }

    [Fact]
    public void VoiceCatalog_RejectsUnknownVoice()
    {
        Assert.Throws<ArgumentException>(() => QuizVoiceCatalog.Validate("not-a-voice"));
    }

    [Fact]
    public async Task SpeechProvider_UsesSelectedVoiceAndReusesIdenticalNarration()
    {
        var root = NewTempFolder();
        try
        {
            var handler = new SpeechRequestHandler();
            using var provider = new NativeQuizSpeechProvider(
                "test-key",
                voice: "nova",
                client: new HttpClient(handler));
            var first = Question(1);
            var second = Question(2) with { Question = first.Question };

            var firstPath = await provider.GenerateQuestionAsync(first, 1, includeAnswers: false, root);
            var secondPath = await provider.GenerateQuestionAsync(second, 2, includeAnswers: false, root);

            Assert.Equal(firstPath, secondPath);
            Assert.Contains("narration_nova_", Path.GetFileName(firstPath), StringComparison.Ordinal);
            Assert.Single(handler.RequestBodies);
            Assert.Contains("\"voice\":\"nova\"", handler.RequestBodies[0], StringComparison.Ordinal);
            Assert.Contains("\"input\":\"" + first.Question, handler.RequestBodies[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CountdownTick_WritesPcmWave()
    {
        var root = NewTempFolder();
        try
        {
            var cue = QuizAudioCueFactory.EnsureCountdownTick(root);
            var bytes = File.ReadAllBytes(cue.Path);

            Assert.True(bytes.Length > 44);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(0.14, cue.Duration, 2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnswerReveal_WritesPcmWave()
    {
        var root = NewTempFolder();
        try
        {
            var cue = QuizAudioCueFactory.EnsureAnswerReveal(root);
            var bytes = File.ReadAllBytes(cue.Path);

            Assert.True(bytes.Length > 44);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(0.46, cue.Duration, 2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NarrationPlanner_UsesActualNarrationBeforeAnswerTime()
    {
        var questions = new[] { Question(1), Question(2) };
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8, AnswerSeconds: 3);
        var narration = new Dictionary<int, QuizNarrationAsset>
        {
            [1] = new(1, "q1.mp3", 2),
            [2] = new(2, "q2.mp3", 3),
        };

        var windows = QuizAudioTimelinePlanner.BuildNarrationWindows(questions, options, narration);

        Assert.Equal(2, windows.Count);
        Assert.Equal(new QuizNarrationWindow(2, 4), windows[0]);
        Assert.Equal(new QuizNarrationWindow(15, 18), windows[1]);
    }

    [Fact]
    public void BackgroundFilter_DucksDuringNarrationWindows()
    {
        var filter = NativeQuizBackgroundMusicRenderer.BuildAudioFilter(
            30,
            [new QuizNarrationWindow(2, 4), new QuizNarrationWindow(15, 18)]);

        Assert.Contains("volume=0.20", filter, StringComparison.Ordinal);
        Assert.Contains("volume=0.32:enable='between(t,2,4)'", filter, StringComparison.Ordinal);
        Assert.Contains("volume=0.32:enable='between(t,15,18)'", filter, StringComparison.Ordinal);
        Assert.Contains("afade=t=in", filter, StringComparison.Ordinal);
        Assert.Contains("afade=t=out", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedAudio_UsesFormatSpecificGain()
    {
        Assert.Equal("4dB", QuizFcpXmlTimelineSynchronizer.AudioGainAmountFor(vertical: false));
        Assert.Equal("8dB", QuizFcpXmlTimelineSynchronizer.AudioGainAmountFor(vertical: true));
    }

    [Fact]
    public void MusicFile_RejectsUnsupportedExtension()
    {
        var root = NewTempFolder();
        var path = Path.Combine(root, "music.txt");
        File.WriteAllText(path, "not audio");
        try
        {
            Assert.Throws<InvalidDataException>(() => QuizMusicFile.Validate(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TimelineEndTrimmer_StopsAudioAtFinalVideoCard()
    {
        var timeline = new NativeTimeline { Name = "Quiz" };
        var video = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });
        video.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = 0,
            Duration = 12,
            Source = "card.png",
            Name = "Final card",
        });
        var audio = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Background Music",
            Kind = NativeTimelineTrackKind.Audio,
        });
        audio.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Audio,
            Start = 0,
            Duration = 30,
            Source = "music.wav",
            Name = "Music",
        });

        var end = QuizTimelineEndTrimmer.TrimToVideoEnd(timeline);

        Assert.Equal(12, end);
        Assert.Equal(12, audio.Clips.Single().Duration);
        Assert.Equal(12, timeline.Duration);
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
        0,
        true);

    private static string NewTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SpeechRequestHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
        }
    }
}
