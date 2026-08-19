namespace FactVaultManager.Desktop.Tests;

public sealed class QuizImportCategoryPromptTests
{
    [Fact]
    public void ChatGptPrompt_GeneralKnowledge_RequestsMixedSpecificCategories()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(100, "General Knowledge");

        Assert.Contains("balanced mix", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Science", prompt);
        Assert.Contains("History", prompt);
        Assert.Contains("Geography", prompt);
        Assert.Contains("Music", prompt);
        Assert.Contains("Film", prompt);
        Assert.Contains("specific broad category", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not label most questions as General Knowledge", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatGptPrompt_SpecificCategory_KeepsThatCategory()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(50, "Science");

        Assert.Contains("about Science", prompt);
        Assert.Contains("Set the category field to 'Science' for every question", prompt);
    }

    [Fact]
    public void ChatGptPrompt_SpecificDifficulty_RequestsSelectedCountCategoryAndDifficulty()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(25, "Paranormal", "hard");

        Assert.Contains("Create 25 accurate", prompt);
        Assert.Contains("about Paranormal", prompt);
        Assert.Contains("Set the category field to 'Paranormal' for every question", prompt);
        Assert.Contains("Set the difficulty field to 'hard' for every question", prompt);
        Assert.Contains("\"difficulty\": \"hard\"", prompt);
        Assert.DoesNotContain("Mix easy, medium, and hard difficulty", prompt);
    }

    [Fact]
    public void ChatGptPrompt_MixedDifficulty_RequestsDifficultyMix()
    {
        var prompt = QuizQuestionImportParser.ChatGptPrompt(50, "History", "mixed");

        Assert.Contains("Mix easy, medium, and hard difficulty", prompt);
        Assert.Contains("\"difficulty\": \"easy\"", prompt);
    }

    [Fact]
    public void Parse_SkipsSameQuestionDespitePunctuationAndAnswerChanges()
    {
        const string json = """
        {
          "questions": [
            {
              "question": "What is the capital of France?",
              "answers": ["Paris", "Rome", "Berlin", "Madrid"],
              "correct_answer": "A",
              "category": "Geography"
            },
            {
              "question": "WHAT IS THE CAPITAL OF FRANCE",
              "answers": ["Lyon", "Paris", "Nice", "Marseille"],
              "correct_answer": "B",
              "category": "Geography"
            }
          ]
        }
        """;

        var questions = QuizQuestionImportParser.Parse(json);

        Assert.Single(questions);
    }

    [Fact]
    public void DuplicateKey_IgnoresCaseWhitespaceAndPunctuation()
    {
        var first = QuizQuestionDuplicateKey.Create(" What is   2 + 2? ");
        var second = QuizQuestionDuplicateKey.Create("WHAT IS 2 + 2");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DuplicateKey_PreservesDifferentMathOperators()
    {
        var addition = QuizQuestionDuplicateKey.Create("What is 2 + 2?");
        var subtraction = QuizQuestionDuplicateKey.Create("What is 2 - 2?");

        Assert.NotEqual(addition, subtraction);
    }
}
