namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionBulkImportTests
{
    [Fact]
    public void Parse_AcceptsOpenTriviaDbArray_DecodesHtml_AndSkipsBooleanQuestions()
    {
        const string json = """
        [
          {
            "type": "multiple",
            "difficulty": "medium",
            "category": "Entertainment: Books",
            "question": "Who wrote &quot;Nineteen Eighty-Four&quot;?",
            "correct_answer": "George Orwell",
            "incorrect_answers": ["Aldous Huxley", "Ray Bradbury", "H. G. Wells"]
          },
          {
            "type": "boolean",
            "difficulty": "easy",
            "category": "Science & Nature",
            "question": "The Earth is flat.",
            "correct_answer": "False",
            "incorrect_answers": ["True"]
          }
        ]
        """;

        var parsed = QuizQuestionBulkImportParser.Parse(json, random: new Random(12345));
        var question = Assert.Single(parsed.Items);

        Assert.True(parsed.IsOpenTriviaDb);
        Assert.Equal(2, parsed.Detected);
        Assert.Equal(1, parsed.Invalid);
        Assert.Equal(1, parsed.CategoryMappings);
        Assert.Equal("Who wrote \"Nineteen Eighty-Four\"?", question.Question);
        Assert.Equal("George Orwell", question.Answers[question.CorrectIndex]);
        Assert.Equal(4, question.Answers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Arts & Literature", question.Category);
        Assert.Equal("medium", question.Difficulty);
        Assert.Equal("Open Trivia DB", question.Source);
    }

    [Fact]
    public void Parse_AcceptsOpenTriviaDbApiResultsWrapper()
    {
        const string json = """
        {
          "response_code": 0,
          "results": [
            {
              "type": "multiple",
              "difficulty": "hard",
              "category": "Science: Computers",
              "question": "What does CPU stand for?",
              "correct_answer": "Central Processing Unit",
              "incorrect_answers": ["Computer Personal Unit", "Central Process Utility", "Core Processing User"]
            }
          ]
        }
        """;

        var parsed = QuizQuestionBulkImportParser.Parse(json, random: new Random(7));
        var question = Assert.Single(parsed.Items);

        Assert.True(parsed.IsOpenTriviaDb);
        Assert.Equal("Technology", question.Category);
        Assert.Equal("Central Processing Unit", question.Answers[question.CorrectIndex]);
    }

    [Fact]
    public void Parse_FallsBackToExistingFactburstJsonFormat()
    {
        const string json = """
        {
          "questions": [
            {
              "question": "Which planet is closest to the Sun?",
              "answers": ["Venus", "Mercury", "Mars", "Earth"],
              "correct_answer": "B",
              "category": "Space",
              "difficulty": "easy"
            }
          ]
        }
        """;

        var parsed = QuizQuestionBulkImportParser.Parse(json);
        var question = Assert.Single(parsed.Items);

        Assert.False(parsed.IsOpenTriviaDb);
        Assert.Equal(1, parsed.Detected);
        Assert.Equal(0, parsed.Invalid);
        Assert.Equal("Mercury", question.Answers[question.CorrectIndex]);
        Assert.Equal("Space", question.Category);
    }

    [Theory]
    [InlineData("General Knowledge", "General Knowledge")]
    [InlineData("Entertainment: Books", "Arts & Literature")]
    [InlineData("Entertainment: Film", "Film")]
    [InlineData("Entertainment: Music", "Music")]
    [InlineData("Entertainment: Television", "Entertainment")]
    [InlineData("Entertainment: Video Games", "Entertainment")]
    [InlineData("Science & Nature", "Science")]
    [InlineData("Science: Computers", "Technology")]
    [InlineData("Science: Mathematics", "Mathematics")]
    [InlineData("Science: Gadgets", "Technology")]
    [InlineData("Animals", "Nature & Animals")]
    [InlineData("Sports", "Sports")]
    [InlineData("Geography", "Geography")]
    [InlineData("History", "History")]
    [InlineData("Art", "Arts & Literature")]
    [InlineData("Celebrities", "Entertainment")]
    [InlineData("Vehicles", "Technology")]
    [InlineData("Politics", "General Knowledge")]
    [InlineData("Unknown OpenTDB Category", "General Knowledge")]
    public void OpenTriviaCategoryMapper_MapsToFactburstCategories(string source, string expected)
    {
        Assert.Equal(expected, QuizOpenTriviaCategoryMapper.Map(source));
    }

    [Fact]
    public void Parse_RejectsOpenTriviaEntryWithDuplicateAnswers_WithoutFailingWholeFile()
    {
        const string json = """
        [
          {
            "type": "multiple",
            "difficulty": "easy",
            "category": "Geography",
            "question": "Which city is in France?",
            "correct_answer": "Paris",
            "incorrect_answers": ["Paris", "Rome", "Berlin"]
          },
          {
            "type": "multiple",
            "difficulty": "easy",
            "category": "Geography",
            "question": "What is the capital of Italy?",
            "correct_answer": "Rome",
            "incorrect_answers": ["Paris", "Berlin", "Madrid"]
          }
        ]
        """;

        var parsed = QuizQuestionBulkImportParser.Parse(json, random: new Random(11));

        Assert.Equal(2, parsed.Detected);
        Assert.Equal(1, parsed.Invalid);
        Assert.Single(parsed.Items);
        Assert.Equal("What is the capital of Italy?", parsed.Items[0].Question);
    }
}
