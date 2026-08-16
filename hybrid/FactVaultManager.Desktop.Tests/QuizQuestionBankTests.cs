namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionBankTests
{
    [Fact]
    public void Parse_AcceptsChatGptQuestionBankShape()
    {
        const string json = """
        {
          "questions": [
            {
              "question": "Which planet is the largest in the Solar System?",
              "answers": ["Earth", "Mars", "Jupiter", "Venus"],
              "correct_answer": "C",
              "explanation": "Jupiter is the largest planet in the Solar System.",
              "category": "Space",
              "difficulty": "easy"
            }
          ]
        }
        """;

        var question = Assert.Single(QuizQuestionImportParser.Parse(json));

        Assert.Equal("Which planet is the largest in the Solar System?", question.Question);
        Assert.Equal(2, question.CorrectIndex);
        Assert.Equal("Jupiter", question.OptionC);
        Assert.Equal("Space", question.Category);
        Assert.Equal("easy", question.Difficulty);
    }

    [Fact]
    public void Parse_StripsMarkdownCodeFence()
    {
        const string json = """
        ```json
        [
          {
            "question": "What is 2 + 2?",
            "answers": ["3", "4", "5", "6"],
            "correct_answer": "B"
          }
        ]
        ```
        """;

        var question = Assert.Single(QuizQuestionImportParser.Parse(json));

        Assert.Equal(1, question.CorrectIndex);
        Assert.Equal("General Knowledge", question.Category);
        Assert.Equal("medium", question.Difficulty);
    }

    [Fact]
    public void Parse_IgnoresTrailingChatGptTextAfterCompleteJson()
    {
        const string pasted = """
        {
          "questions": [
            {
              "question": "What is the capital of France?",
              "answers": ["Berlin", "Madrid", "Paris", "Rome"],
              "correct_answer": "C"
            }
          ]
        }
        [1] Sources and citations accidentally copied from ChatGPT
        """;

        var question = Assert.Single(QuizQuestionImportParser.Parse(pasted));

        Assert.Equal("What is the capital of France?", question.Question);
        Assert.Equal(2, question.CorrectIndex);
        Assert.Equal("Paris", question.OptionC);
    }

    [Fact]
    public void Parse_RejectsDuplicateAnswerChoices()
    {
        const string json = """
        [{
          "question": "Pick one",
          "answers": ["Same", "Same", "Other", "Another"],
          "correct_answer": "A"
        }]
        """;

        var error = Assert.Throws<InvalidDataException>(() => QuizQuestionImportParser.Parse(json));

        Assert.Contains("duplicate answer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fingerprint_NormalizesCaseAndWhitespace()
    {
        var first = QuizQuestionFingerprint.Create("  Biggest   planet? ", ["Earth", "Mars", "Jupiter", "Venus"]);
        var second = QuizQuestionFingerprint.Create("biggest planet?", [" earth ", "MARS", "Jupiter", "Venus"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SelectRandom_ReturnsRequestedUniqueQuestions()
    {
        var questions = Enumerable.Range(1, 20)
            .Select(index => Question(index))
            .ToList();

        var selected = QuizQuestionSelector.SelectRandom(questions, 10, new Random(12345));

        Assert.Equal(10, selected.Count);
        Assert.Equal(10, selected.Select(question => question.Id).Distinct().Count());
    }

    [Fact]
    public void SelectRandom_ExcludesDisabledQuestions()
    {
        var questions = new[]
        {
            Question(1) with { IsEnabled = false },
            Question(2),
            Question(3),
        };

        var selected = QuizQuestionSelector.SelectRandom(questions, 2, new Random(12345));

        Assert.Equal(new[] { 2, 3 }, selected.Select(question => question.Id).OrderBy(id => id));
        Assert.DoesNotContain(selected, question => question.Id == 1);
    }

    [Fact]
    public void SelectRandom_RejectsRequestLargerThanEnabledPool()
    {
        var questions = new[]
        {
            Question(1),
            Question(2) with { IsEnabled = false },
            Question(3),
        };

        var error = Assert.Throws<InvalidOperationException>(() => QuizQuestionSelector.SelectRandom(questions, 3));

        Assert.Contains("Only 2 enabled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Availability_ReflectsEnabledState()
    {
        Assert.Equal("Enabled", Question(1).Availability);
        Assert.Equal("Disabled", (Question(1) with { IsEnabled = false }).Availability);
    }

    [Fact]
    public void ChatGptPrompt_RequestsImportCompatibleJson()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(75, "World History");

        Assert.Contains("75", prompt);
        Assert.Contains("World History", prompt);
        Assert.Contains("correct_answer", prompt);
        Assert.Contains("JSON only", prompt);
        Assert.Contains("Do not include citations", prompt);
        Assert.Contains("after the final }", prompt);
    }

    private static QuizQuestion Question(int id) => new(
        id,
        $"Question {id}?",
        "A", "B", "C", "D",
        id % 4,
        "Explanation",
        "General Knowledge",
        "medium",
        "Test",
        0);
}
