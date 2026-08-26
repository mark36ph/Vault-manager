using System.Text.Json;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPromoNativeShortTests
{
    [Fact]
    public void CardPhases_FollowShortQuestionCountdownAndAnswerRevealPacing()
    {
        var options = new QuizVideoBuildOptions(
            "Space",
            QuestionSeconds: 8,
            AnswerSeconds: 3,
            Vertical: true,
            ShowCountdown: true,
            AnimateAnswerReveal: true);

        var phases = QuizPromoNativeShortRenderer.BuildCardPhases(
            options,
            narrationSeconds: 2.5,
            targetDuration: 13.5);

        Assert.Collection(
            phases,
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.Question, phase.Kind);
                Assert.Null(phase.CountdownValue);
                Assert.Equal(7.5, phase.Duration, 6);
            },
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.Countdown, phase.Kind);
                Assert.Equal(3, phase.CountdownValue);
                Assert.Equal(1, phase.Duration, 6);
            },
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.Countdown, phase.Kind);
                Assert.Equal(2, phase.CountdownValue);
                Assert.Equal(1, phase.Duration, 6);
            },
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.Countdown, phase.Kind);
                Assert.Equal(1, phase.CountdownValue);
                Assert.Equal(1, phase.Duration, 6);
            },
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.AnswerReveal, phase.Kind);
                Assert.Equal(0.5, phase.Duration, 6);
            },
            phase =>
            {
                Assert.Equal(QuizPreviewCardKind.Explanation, phase.Kind);
                Assert.Equal(2.5, phase.Duration, 6);
            });
        Assert.Equal(13.5, phases.Sum(phase => phase.Duration), 6);
    }

    [Fact]
    public void CardPhases_AreTrimmedToPromoBodyDuration()
    {
        var options = new QuizVideoBuildOptions(
            "Technology",
            QuestionSeconds: 8,
            AnswerSeconds: 3,
            Vertical: true);

        var phases = QuizPromoNativeShortRenderer.BuildCardPhases(
            options,
            narrationSeconds: 5,
            targetDuration: 6);

        Assert.Single(phases);
        Assert.Equal(QuizPreviewCardKind.Question, phases[0].Kind);
        Assert.Equal(6, phases[0].Duration, 6);
    }

    [Fact]
    public void Filter_UsesNativeVerticalBodyAndOriginalInsaneSceneAudio()
    {
        var plan = new QuizPromoShortPlan(20, 12, 4.5, "Question 10", 10);

        var filter = QuizPromoNativeShortRenderer.BuildFilter(plan, hasSourceAudio: true);

        Assert.Contains("[0:v]trim=duration=12", filter, StringComparison.Ordinal);
        Assert.Contains("scale=1080:1920", filter, StringComparison.Ordinal);
        Assert.Contains("[1:a]atrim=start=20:duration=12", filter, StringComparison.Ordinal);
        Assert.Contains("concat=n=2:v=1:a=1", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("boxblur", filter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crop=iw", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filter_UsesSilenceWhenLongFormVideoHasNoAudio()
    {
        var plan = new QuizPromoShortPlan(20, 12, 4.5, "Question 10", 10);

        var filter = QuizPromoNativeShortRenderer.BuildFilter(plan, hasSourceAudio: false);

        Assert.Contains("anullsrc=r=48000:cl=stereo:d=12[a0]", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("[1:a]", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualSource_LoadsSavedQuestionNumberThemeAndShortSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var payload = new
            {
                title = "Ultimate Space IQ Test",
                question_seconds = 9,
                answer_seconds = 4,
                frame_rate = 30,
                show_countdown = true,
                animate_answer_reveal = true,
                theme = "game-show",
                logo_position = "Bottom right",
                logo_scale = 1.0,
                quiz_type = "Standard",
                questions = Enumerable.Range(1, 10).Select(number => new
                {
                    number,
                    id = 100 + number,
                    question = number == 10 ? "Which planet rotates fastest?" : $"Question {number}?",
                    answers = new[] { "Mercury", "Venus", "Earth", "Jupiter" },
                    correct_index = 3,
                    explanation = "Jupiter has the shortest day of the planets.",
                    category = "Space",
                    difficulty = number == 10 ? "insane" : "easy",
                    narration = number == 10 ? new { file = "q10.wav", duration = 2.75 } : null,
                }).ToArray(),
            };
            File.WriteAllText(
                Path.Combine(root, "quiz.json"),
                JsonSerializer.Serialize(payload));

            var source = QuizPromoNativeShortRenderer.LoadVisualSource(
                root,
                "Fallback title",
                "",
                questionId: 110);

            Assert.Equal("Which planet rotates fastest?", source.Question.Question);
            Assert.Equal("insane", source.Question.Difficulty);
            Assert.Equal(10, source.QuestionNumber);
            Assert.Equal(10, source.QuestionTotal);
            Assert.Equal(2.75, source.NarrationSeconds, 6);
            Assert.True(source.Options.Vertical);
            Assert.Equal(1080, source.Options.Width);
            Assert.Equal(1920, source.Options.Height);
            Assert.Equal(9, source.Options.QuestionSeconds);
            Assert.Equal(4, source.Options.AnswerSeconds);
            Assert.Equal("game-show", source.Visual.ThemeKey);
            Assert.Equal(QuizPromoNativeShortRenderer.VisualStyle, "native_factburst_short");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
