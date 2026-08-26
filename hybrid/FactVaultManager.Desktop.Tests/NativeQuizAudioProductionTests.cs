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
            Assert.Contains("\"instructions\":", handler.RequestBodies[0], StringComparison.Ordinal);
            Assert.Contains("light controlled suspense", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No greeting", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NarrationDelivery_ProgressesFromBriskToHighStakesWithoutAddingSpokenFiller()
    {
        const string text = "Which planet, despite its size, has the shortest day?";
        var easy = QuizNarrationScript.CreateDelivery(
            Question(1) with { Question = text, Difficulty = "easy" },
            includeAnswers: false);
        var medium = QuizNarrationScript.CreateDelivery(
            Question(2) with { Question = text, Difficulty = "medium" },
            includeAnswers: false);
        var hard = QuizNarrationScript.CreateDelivery(
            Question(3) with { Question = text, Difficulty = "hard" },
            includeAnswers: false);
        var insane = QuizNarrationScript.CreateDelivery(
            Question(4) with { Question = text, Difficulty = "insane" },
            includeAnswers: false);

        Assert.Equal(text, easy.Input);
        Assert.Equal(text, medium.Input);
        Assert.Equal("Which planet… despite its size, has the shortest day?", hard.Input);
        Assert.Equal("Which planet… despite its size… has the shortest day?", insane.Input);
        Assert.Contains("brisk", easy.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("light controlled suspense", medium.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high-stakes", hard.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maximum controlled suspense", insane.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.All(new[] { easy, medium, hard, insane }, delivery =>
        {
            Assert.Contains("Read exactly the supplied quiz text", delivery.Instructions, StringComparison.Ordinal);
            Assert.Contains("No greeting", delivery.Instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("Here is your next question", delivery.Input, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task SpeechProvider_DifficultyInstructionsProduceDifferentCachedAudio()
    {
        var root = NewTempFolder();
        try
        {
            var handler = new SpeechRequestHandler();
            using var provider = new NativeQuizSpeechProvider(
                "test-key",
                voice: "fable",
                client: new HttpClient(handler));
            const string text = "Which planet has the shortest day?";
            var easy = Question(1) with { Question = text, Difficulty = "easy" };
            var insane = Question(2) with { Question = text, Difficulty = "insane" };

            var easyPath = await provider.GenerateQuestionAsync(easy, 1, includeAnswers: false, root);
            var insanePath = await provider.GenerateQuestionAsync(insane, 2, includeAnswers: false, root);

            Assert.NotEqual(easyPath, insanePath);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("brisk", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("maximum controlled suspense", handler.RequestBodies[1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SpeechProvider_LegacyTtsModelsOmitUnsupportedInstructions()
    {
        var root = NewTempFolder();
        try
        {
            var handler = new SpeechRequestHandler();
            using var provider = new NativeQuizSpeechProvider(
                "test-key",
                model: "tts-1",
                voice: "fable",
                client: new HttpClient(handler));

            await provider.GenerateQuestionAsync(
                Question(1) with { Difficulty = "insane" },
                1,
                includeAnswers: false,
                root);

            Assert.Single(handler.RequestBodies);
            Assert.DoesNotContain("\"instructions\":", handler.RequestBodies[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SpeechProvider_PromoHookUsesFableAndCachesTheAudio()
    {
        var root = NewTempFolder();
        try
        {
            var handler = new SpeechRequestHandler();
            using var provider = new NativeQuizSpeechProvider(
                "test-key",
                voice: "fable",
                client: new HttpClient(handler));

            var firstPath = await provider.GeneratePromoCallToActionAsync(
                QuizPromoShortScript.DefaultCallToAction, root);
            var secondPath = await provider.GeneratePromoCallToActionAsync(
                QuizPromoShortScript.DefaultCallToAction, root);

            Assert.Equal(firstPath, secondPath);
            Assert.StartsWith("promo_cta_fable_", Path.GetFileName(firstPath), StringComparison.Ordinal);
            Assert.Single(handler.RequestBodies);
            Assert.Contains("\"voice\":\"fable\"", handler.RequestBodies[0], StringComparison.Ordinal);
            Assert.Contains("related video", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"instructions\":", handler.RequestBodies[0], StringComparison.Ordinal);
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
    public void NarrationPlanner_IncludesSuspenseAnswerPauseAndCountdownWindows()
    {
        var questions = new[] { Question(1), Question(2) };
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8, AnswerSeconds: 3);
        var narration = new Dictionary<int, QuizNarrationAsset>
        {
            [1] = new(1, "q1.mp3", 2),
            [2] = new(2, "q2.mp3", 3),
        };

        var plan = QuizAudioTimelinePlanner.BuildNarrationWindows(questions, options, narration);

        Assert.Equal(2, plan.Count);
        Assert.Equal(new QuizNarrationWindow(2, 4), plan[0]);
        Assert.Equal(new QuizNarrationWindow(15.85, 18.85), plan[1]);
        Assert.Equal(1.7, plan.AddedPacingSeconds, 6);
        Assert.Equal(
            new[]
            {
                new QuizCountdownWindow(4.5, 12.5),
                new QuizCountdownWindow(19.35, 27.35),
            },
            plan.CountdownWindows);
    }

    [Fact]
    public void NarrationPlanner_LeavesStandaloneShortTimingUnchanged()
    {
        var questions = new[] { Question(1) };
        var options = new QuizVideoBuildOptions("Short", QuestionSeconds: 8, AnswerSeconds: 3, Vertical: true);
        var narration = new Dictionary<int, QuizNarrationAsset>
        {
            [1] = new(1, "q1.mp3", 2),
        };

        var plan = QuizAudioTimelinePlanner.BuildNarrationWindows(questions, options, narration);

        Assert.Equal(0, plan.AddedPacingSeconds, 6);
        Assert.Equal(new QuizNarrationWindow(1.2, 3.2), Assert.Single(plan));
    }

    [Fact]
    public void BackgroundFilter_DucksNarrationHarderAndLiftsCountdown()
    {
        var plan = new QuizAudioMixPlan(
            [new QuizNarrationWindow(2, 4), new QuizNarrationWindow(15, 18)],
            [new QuizCountdownWindow(4.5, 12.5)],
            addedPacingSeconds: 0.85);

        var filter = NativeQuizBackgroundMusicRenderer.BuildAudioFilter(30, plan);

        Assert.Contains("volume=0.2", filter, StringComparison.Ordinal);
        Assert.Contains("volume=1.35:enable='between(t,4.5,12.5)'", filter, StringComparison.Ordinal);
        Assert.Contains("volume=0.25:enable='between(t,2,4)'", filter, StringComparison.Ordinal);
        Assert.Contains("volume=0.25:enable='between(t,15,18)'", filter, StringComparison.Ordinal);
        Assert.True(
            NativeQuizBackgroundMusicRenderer.BaseMusicVolume * NativeQuizBackgroundMusicRenderer.NarrationDuckMultiplier < 0.064,
            "Narration should be ducked more strongly than the previous mix.");
        Assert.True(NativeQuizBackgroundMusicRenderer.CountdownLiftMultiplier > 1);
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
