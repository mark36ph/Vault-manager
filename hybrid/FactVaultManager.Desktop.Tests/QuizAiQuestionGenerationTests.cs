namespace FactVaultManager.Desktop.Tests;

public sealed class QuizAiQuestionGenerationTests
{
    [Fact]
    public void Request_NormalizesInputs()
    {
        var request = QuizAiGenerationRequest.Create(12, " Space ", "HARD", " Mars missions ");

        Assert.Equal(12, request.Count);
        Assert.Equal("Space", request.Category);
        Assert.Equal("hard", request.Difficulty);
        Assert.Equal("Mars missions", request.Topic);
    }

    [Fact]
    public void Request_RejectsOversizedBatch()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuizAiGenerationRequest.Create(51, "History", "mixed", ""));

        Assert.Contains("1 and 50", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPrompt_IncludesRequestedControlsAndJsonShape()
    {
        var request = QuizAiGenerationRequest.Create(8, "History", "medium", "Ancient Rome");

        var prompt = QuizAiQuestionGeneration.BuildPrompt(request);

        Assert.Contains("exactly 8", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Category: History", prompt);
        Assert.Contains("Ancient Rome", prompt);
        Assert.Contains("difficulty 'medium'", prompt);
        Assert.Contains("correct_answer", prompt);
        Assert.Contains("JSON only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EstimateProgress_RisesButNeverPretendsRequestIsComplete()
    {
        var samples = new[]
        {
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.Zero),
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.FromSeconds(5)),
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.FromSeconds(10)),
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.FromSeconds(30)),
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.FromMinutes(1)),
            QuizAiQuestionGeneration.EstimateProgress(TimeSpan.FromMinutes(10)),
        };

        Assert.Equal(new[] { 5, 25, 45, 85, 92, 92 }, samples);
        Assert.All(samples, value => Assert.InRange(value, 5, 92));
        Assert.Equal(samples.OrderBy(value => value), samples);
    }

    [Fact]
    public void ParseResponse_UsesRequestedCategoryDifficultyAndSource()
    {
        const string json = """
        {
          "questions": [
            {
              "question": "Which planet is closest to the Sun?",
              "answers": ["Venus", "Mercury", "Earth", "Mars"],
              "correct_answer": "B",
              "explanation": "Mercury is the closest planet to the Sun.",
              "category": "Wrong category",
              "difficulty": "easy"
            }
          ]
        }
        """;
        var request = QuizAiGenerationRequest.Create(1, "Space", "hard", "Solar System");

        var question = Assert.Single(QuizAiQuestionGeneration.ParseResponse(json, request));

        Assert.Equal("Space", question.Category);
        Assert.Equal("hard", question.Difficulty);
        Assert.Equal("OpenAI generation", question.Source);
        Assert.Equal(1, question.CorrectIndex);
    }

    [Fact]
    public void ParseResponse_TrimsUnexpectedExtraQuestions()
    {
        const string json = """
        {
          "questions": [
            {
              "question": "Question one?",
              "answers": ["A1", "B1", "C1", "D1"],
              "correct_answer": "A"
            },
            {
              "question": "Question two?",
              "answers": ["A2", "B2", "C2", "D2"],
              "correct_answer": "B"
            }
          ]
        }
        """;
        var request = QuizAiGenerationRequest.Create(1, "Miscellaneous", "mixed", "");

        var questions = QuizAiQuestionGeneration.ParseResponse(json, request);

        Assert.Single(questions);
        Assert.Equal("Question one?", questions[0].Question);
    }

    [Fact]
    public void SerializeForImport_RoundTripsGeneratedQuestions()
    {
        var original = new QuizQuestionImportItem(
            "What is 2 + 2?",
            "3", "4", "5", "6",
            1,
            "Two plus two equals four.",
            "Mathematics",
            "easy",
            "OpenAI generation");

        var json = QuizAiQuestionGeneration.SerializeForImport([original]);
        var parsed = Assert.Single(QuizQuestionImportParser.Parse(json, "OpenAI generation"));

        Assert.Equal(original.Question, parsed.Question);
        Assert.Equal(original.CorrectIndex, parsed.CorrectIndex);
        Assert.Equal(original.Category, parsed.Category);
        Assert.Equal(original.Difficulty, parsed.Difficulty);
    }
}
