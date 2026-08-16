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
    public void SelectRandom_RejectsRequestLargerThanPool()
    {
        var questions = Enumerable.Range(1, 3).Select(Question).ToList();

        var error = Assert.Throws<InvalidOperationException>(() => QuizQuestionSelector.SelectRandom(questions, 4));

        Assert.Contains("Only 3", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatGptPrompt_RequestsImportCompatibleJson()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(75, "World History");

        Assert.Contains("75", prompt);
        Assert.Contains("World History", prompt);
        Assert.Contains("correct_answer", prompt);
        Assert.Contains("JSON only", prompt);
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
