using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizAiGenerationRequest(
    int Count,
    string Category,
    string Difficulty,
    string Topic)
{
    public static QuizAiGenerationRequest Create(
        int count,
        string? category,
        string? difficulty,
        string? topic)
    {
        if (count is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(count), "Choose between 1 and 50 AI questions at a time.");

        var normalizedCategory = (category ?? "").Trim();
        if (normalizedCategory.Length == 0)
            normalizedCategory = "General Knowledge";
        if (normalizedCategory.Length > 100)
            throw new ArgumentException("Quiz category is too long.", nameof(category));

        var normalizedDifficulty = (difficulty ?? "mixed").Trim().ToLowerInvariant();
        if (normalizedDifficulty.Length == 0)
            normalizedDifficulty = "mixed";
        if (normalizedDifficulty is not ("mixed" or "easy" or "medium" or "hard" or "insane"))
            throw new ArgumentException("Difficulty must be mixed, easy, medium, hard, or insane.", nameof(difficulty));

        var normalizedTopic = (topic ?? "").Trim();
        if (normalizedTopic.Length > 200)
            throw new ArgumentException("Quiz topic is too long.", nameof(topic));

        return new QuizAiGenerationRequest(count, normalizedCategory, normalizedDifficulty, normalizedTopic);
    }
}

public enum QuizAiGenerationStage
{
    Preparing,
    WaitingForOpenAi,
    ResponseReceived,
    Validating,
    LoadingReview,
    Complete,
}

public static class QuizAiGenerationProgress
{
    public static int Percent(QuizAiGenerationStage stage) => stage switch
    {
        QuizAiGenerationStage.Preparing => 5,
        QuizAiGenerationStage.WaitingForOpenAi => 25,
        QuizAiGenerationStage.ResponseReceived => 70,
        QuizAiGenerationStage.Validating => 85,
        QuizAiGenerationStage.LoadingReview => 95,
        QuizAiGenerationStage.Complete => 100,
        _ => 0,
    };
}

public static class QuizAiQuestionGeneration
{
    public const string ProviderInstructions =
        "You create accurate, unambiguous multiple-choice quiz questions for short-form video. " +
        "Return only the requested JSON object. Never include Markdown, citations, URLs, commentary, or text outside the JSON.";

    public static string BuildPrompt(QuizAiGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var difficultyRule = request.Difficulty == "mixed"
            ? "Use a useful mix of easy, medium, hard, and insane questions."
            : $"Every question must have difficulty '{request.Difficulty}'.";
        var topicRule = string.IsNullOrWhiteSpace(request.Topic)
            ? ""
            : $"Focus specifically on this topic: {request.Topic}\n";

        return $$"""
Create exactly {{request.Count}} multiple-choice quiz questions.
Category: {{request.Category}}
{{topicRule}}Difficulty: {{request.Difficulty}}

Return JSON only in exactly this shape:
{
  "questions": [
    {
      "question": "Question text",
      "answers": ["Answer A", "Answer B", "Answer C", "Answer D"],
      "correct_answer": "A",
      "explanation": "One short factual explanation.",
      "category": "{{request.Category}}",
      "difficulty": "easy"
    }
  ]
}

Rules:
- Return exactly {{request.Count}} questions.
- Use exactly four distinct answer choices for every question.
- correct_answer must be A, B, C, or D and must match the actually correct choice.
- {{difficultyRule}}
- Keep every question in the requested category and topic.
- Avoid duplicate questions, trick questions, ambiguity, opinions, and time-sensitive claims unless the answer is stable.
- Verify factual accuracy before including a question.
- Keep wording concise and suitable for a YouTube quiz.
- Do not include citations, links, Markdown, or any text outside the JSON object.
""";
    }

    public static IReadOnlyList<QuizQuestionImportItem> ParseResponse(
        string response,
        QuizAiGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parsed = QuizQuestionImportParser.Parse(response, "OpenAI generation");

        return parsed
            .Take(request.Count)
            .Select(item => item with
            {
                Category = request.Category,
                Difficulty = request.Difficulty == "mixed"
                    ? NormalizeGeneratedDifficulty(item.Difficulty)
                    : request.Difficulty,
                Source = "OpenAI generation",
            })
            .ToArray();
    }

    public static string SerializeForImport(IEnumerable<QuizQuestionImportItem> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var items = questions.ToArray();
        if (items.Length == 0)
            throw new InvalidOperationException("Select at least one generated question first.");

        return JsonSerializer.Serialize(new
        {
            questions = items.Select(item => new
            {
                question = item.Question,
                answers = item.Answers,
                correct_answer = item.CorrectLetter(),
                explanation = item.Explanation,
                category = item.Category,
                difficulty = item.Difficulty,
            }),
        });
    }

    private static string NormalizeGeneratedDifficulty(string value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "easy" => "easy",
            "hard" => "hard",
            _ => "medium",
        };

    private static string CorrectLetter(this QuizQuestionImportItem item) =>
        item.CorrectIndex is >= 0 and <= 3
            ? ((char)('A' + item.CorrectIndex)).ToString()
            : throw new InvalidDataException("Generated quiz question has an invalid correct answer index.");
}
