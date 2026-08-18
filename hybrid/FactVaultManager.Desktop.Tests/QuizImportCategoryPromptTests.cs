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
}
