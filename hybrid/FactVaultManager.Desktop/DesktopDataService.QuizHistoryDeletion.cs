namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public bool DeleteQuizHistory(int historyId)
    {
        if (historyId <= 0)
            return false;

        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var questionIds = new List<int>();
        using (var questions = connection.CreateCommand())
        {
            questions.Transaction = transaction;
            questions.CommandText = """
                SELECT DISTINCT question_id
                FROM quiz_history_questions
                WHERE history_id = $historyId
                """;
            questions.Parameters.AddWithValue("$historyId", historyId);
            using var reader = questions.ExecuteReader();
            while (reader.Read())
            {
                var questionId = reader.GetInt32(0);
                if (questionId > 0)
                    questionIds.Add(questionId);
            }
        }

        using (var deleteQuestions = connection.CreateCommand())
        {
            deleteQuestions.Transaction = transaction;
            deleteQuestions.CommandText = "DELETE FROM quiz_history_questions WHERE history_id = $historyId";
            deleteQuestions.Parameters.AddWithValue("$historyId", historyId);
            deleteQuestions.ExecuteNonQuery();
        }

        int deleted;
        using (var deleteHistory = connection.CreateCommand())
        {
            deleteHistory.Transaction = transaction;
            deleteHistory.CommandText = "DELETE FROM quiz_history WHERE id = $historyId";
            deleteHistory.Parameters.AddWithValue("$historyId", historyId);
            deleted = deleteHistory.ExecuteNonQuery();
        }

        if (deleted == 0)
        {
            transaction.Rollback();
            return false;
        }

        foreach (var questionId in questionIds.Distinct())
        {
            using var updateUsage = connection.CreateCommand();
            updateUsage.Transaction = transaction;
            updateUsage.CommandText = """
                UPDATE quiz_questions
                SET times_used = CASE WHEN times_used > 0 THEN times_used - 1 ELSE 0 END,
                    last_used = COALESCE((
                        SELECT MAX(h.created)
                        FROM quiz_history_questions q
                        INNER JOIN quiz_history h ON h.id = q.history_id
                        WHERE q.question_id = $id
                    ), '')
                WHERE id = $id
                """;
            updateUsage.Parameters.AddWithValue("$id", questionId);
            updateUsage.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }
}
