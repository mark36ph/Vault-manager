using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public QuizQuestion UpdateQuizQuestion(int id, QuizQuestionEditRequest request)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Quiz question ID must be greater than zero.");

        var edited = QuizQuestionEditValidator.Validate(request);
        var fingerprint = QuizQuestionFingerprint.Create(edited.Question, edited.Answers);
        EnsureQuizSchema();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE quiz_questions
            SET question = $question,
                option_a = $a,
                option_b = $b,
                option_c = $c,
                option_d = $d,
                correct_index = $correct,
                explanation = $explanation,
                category = $category,
                difficulty = $difficulty,
                fingerprint = $fingerprint,
                enabled = $enabled
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$question", edited.Question);
        command.Parameters.AddWithValue("$a", edited.OptionA);
        command.Parameters.AddWithValue("$b", edited.OptionB);
        command.Parameters.AddWithValue("$c", edited.OptionC);
        command.Parameters.AddWithValue("$d", edited.OptionD);
        command.Parameters.AddWithValue("$correct", edited.CorrectIndex);
        command.Parameters.AddWithValue("$explanation", edited.Explanation);
        command.Parameters.AddWithValue("$category", edited.Category);
        command.Parameters.AddWithValue("$difficulty", edited.Difficulty);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$enabled", edited.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);

        int affected;
        try
        {
            affected = command.ExecuteNonQuery();
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "Another question in the bank already has the same question text and answer choices.",
                error);
        }

        if (affected == 0)
            throw new KeyNotFoundException($"Quiz question #{id} no longer exists.");

        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, question, option_a, option_b, option_c, option_d,
                   correct_index, explanation, category, difficulty, source, times_used, enabled
            FROM quiz_questions
            WHERE id = $id
            """;
        select.Parameters.AddWithValue("$id", id);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
            throw new KeyNotFoundException($"Quiz question #{id} could not be reloaded after editing.");
        return ReadQuizQuestion(reader);
    }
}

public sealed record QuizQuestionEditRequest(
    string Question,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    int CorrectIndex,
    string Explanation,
    string Category,
    string Difficulty,
    bool IsEnabled)
{
    public IReadOnlyList<string> Answers => [OptionA, OptionB, OptionC, OptionD];
}

public static class QuizQuestionEditValidator
{
    public static QuizQuestionEditRequest Validate(QuizQuestionEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var question = Required(request.Question, "Question", 500);
        var answers = new[]
        {
            Required(request.OptionA, "Answer A", 300),
            Required(request.OptionB, "Answer B", 300),
            Required(request.OptionC, "Answer C", 300),
            Required(request.OptionD, "Answer D", 300),
        };
        if (answers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new InvalidDataException("All four answer choices must be different.");
        if (request.CorrectIndex is < 0 or > 3)
            throw new InvalidDataException("Correct answer must be A, B, C, or D.");

        var explanation = Optional(request.Explanation, "Explanation", 2_000);
        var category = Required(request.Category, "Category", 100);
        var difficulty = Required(request.Difficulty, "Difficulty", 50).ToLowerInvariant() switch
        {
            "beginner" => "easy",
            "normal" or "intermediate" => "medium",
            "difficult" or "expert" => "hard",
            var value => value,
        };
        if (difficulty is not ("easy" or "medium" or "hard"))
            throw new InvalidDataException("Difficulty must be easy, medium, or hard.");

        return new QuizQuestionEditRequest(
            question,
            answers[0], answers[1], answers[2], answers[3],
            request.CorrectIndex,
            explanation,
            category,
            difficulty,
            request.IsEnabled);
    }

    private static string Required(string? value, string field, int maximumLength)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidDataException($"{field} cannot be empty.");
        if (text.Length > maximumLength)
            throw new InvalidDataException($"{field} is too long.");
        return text;
    }

    private static string Optional(string? value, string field, int maximumLength)
    {
        var text = (value ?? "").Trim();
        if (text.Length > maximumLength)
            throw new InvalidDataException($"{field} is too long.");
        return text;
    }
}
