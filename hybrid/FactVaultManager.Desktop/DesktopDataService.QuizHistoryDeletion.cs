namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public bool DeleteQuizHistory(int historyId, bool deleteFolder = true)
    {
        if (historyId <= 0)
            return false;

        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        string projectFolder;
        using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = "SELECT project_folder FROM quiz_history WHERE id = $historyId";
            history.Parameters.AddWithValue("$historyId", historyId);
            var value = history.ExecuteScalar();
            if (value is null || value is DBNull)
            {
                transaction.Rollback();
                return false;
            }
            projectFolder = Convert.ToString(value)?.Trim() ?? "";
        }

        string? stagedFolder = null;
        if (deleteFolder && !string.IsNullOrWhiteSpace(projectFolder) && Directory.Exists(projectFolder))
        {
            var safeFolder = ProjectPathSecurity.EnsureContained(GetProjectsRoot(), projectFolder);
            var parent = Path.GetDirectoryName(safeFolder)
                ?? throw new InvalidOperationException("Quiz export folder has no parent folder.");
            stagedFolder = Path.Combine(parent, $".{Path.GetFileName(safeFolder)}.deleting-{Guid.NewGuid():N}");
            Directory.Move(safeFolder, stagedFolder);
        }

        try
        {
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
                RestoreStagedQuizFolder(stagedFolder, projectFolder);
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
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            RestoreStagedQuizFolder(stagedFolder, projectFolder);
            throw;
        }

        if (stagedFolder is not null && Directory.Exists(stagedFolder))
            Directory.Delete(stagedFolder, recursive: true);

        return true;
    }

    private static void RestoreStagedQuizFolder(string? stagedFolder, string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(stagedFolder) || !Directory.Exists(stagedFolder) || Directory.Exists(projectFolder))
            return;
        Directory.Move(stagedFolder, projectFolder);
    }
}
