using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
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
