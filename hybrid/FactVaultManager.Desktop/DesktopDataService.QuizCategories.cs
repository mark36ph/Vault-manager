using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public IReadOnlyList<QuizQuestionCategorySummary> GetQuizCategorySummaries()
    {
        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT category,
                   COUNT(*) AS question_count,
                   SUM(CASE WHEN enabled = 1 THEN 1 ELSE 0 END) AS enabled_count,
                   COALESCE(SUM(times_used), 0) AS times_used
            FROM quiz_questions
            GROUP BY category COLLATE NOCASE
            ORDER BY category COLLATE NOCASE
            """;

        var stored = new Dictionary<string, QuizQuestionCategorySummary>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var category = QuizQuestionCategoryNormalizer.Normalize(reader.IsDBNull(0) ? "" : reader.GetString(0));
            var total = reader.GetInt32(1);
            var enabled = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var timesUsed = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            stored[category] = new QuizQuestionCategorySummary(category, total, enabled, total - enabled, timesUsed);
        }

        var results = new List<QuizQuestionCategorySummary>();
        foreach (var category in QuizQuestionTopicCategorizer.Categories)
        {
            if (stored.Remove(category, out var summary))
                results.Add(summary);
            else
                results.Add(new QuizQuestionCategorySummary(category, 0, 0, 0, 0));
        }

        results.AddRange(stored.Values.OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase));
        return results;
    }

    public void SetQuizQuestionCategory(int questionId, string category)
    {
        category = QuizQuestionCategoryNormalizer.Normalize(category);
        if (category.Length > 100)
            throw new ArgumentException("Quiz category cannot be longer than 100 characters.", nameof(category));

        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE quiz_questions
            SET category = $category,
                enabled = CASE
                    WHEN $category = 'Icons' COLLATE NOCASE
                     AND TRIM(image_path) = '' THEN 0
                    ELSE enabled
                END
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$id", questionId);
        if (command.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("The selected quiz question no longer exists.");
    }

    public QuizQuestionCategorizationResult AutoCategorizeLegacyMusicQuestions()
    {
        var questions = GetQuizQuestions(category: "Entertainment", limit: 10_000)
            .Where(question => string.Equals(
                QuizQuestionTopicCategorizer.Categorize(question),
                "Music",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (questions.Length == 0)
            return new QuizQuestionCategorizationResult(0, 0);

        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var updated = 0;

        foreach (var question in questions)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE quiz_questions
                SET category = 'Music'
                WHERE id = $id
                  AND category = 'Entertainment' COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("$id", question.Id);
            updated += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new QuizQuestionCategorizationResult(questions.Length, updated);
    }

    public QuizQuestionCategorizationResult AutoCategorizeGeneralKnowledgeQuestions()
    {
        var questions = GetQuizQuestions(category: "General Knowledge", limit: 10_000);
        if (questions.Count == 0)
            return new QuizQuestionCategorizationResult(0, 0);

        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var updated = 0;

        foreach (var question in questions)
        {
            var category = QuizQuestionTopicCategorizer.Categorize(question);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE quiz_questions
                SET category = $category
                WHERE id = $id
                  AND category = 'General Knowledge' COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("$category", category);
            command.Parameters.AddWithValue("$id", question.Id);
            updated += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new QuizQuestionCategorizationResult(questions.Count, updated);
    }
}

public sealed record QuizQuestionCategorizationResult(int Found, int Updated);

public sealed record QuizQuestionCategorySummary(
    string Category,
    int QuestionCount,
    int EnabledCount,
    int DisabledCount,
    int TimesUsed);
