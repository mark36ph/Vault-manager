namespace FactVaultManager.Desktop;

public sealed class QuizHistoryFolderCleanupException : IOException
{
    public QuizHistoryFolderCleanupException(string projectFolder, Exception inner)
        : base($"The quiz was removed from Quiz History, but Windows would not delete its export folder:\n{projectFolder}\n\nClose DaVinci Resolve or File Explorer windows using that folder, then delete the folder manually.", inner)
    {
        ProjectFolder = projectFolder;
    }

    public string ProjectFolder { get; }
}

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

        string? safeFolder = null;
        if (deleteFolder && !string.IsNullOrWhiteSpace(projectFolder) && Directory.Exists(projectFolder))
            safeFolder = ProjectPathSecurity.EnsureContained(GetProjectsRoot(), projectFolder);

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
            throw;
        }

        if (safeFolder is not null && Directory.Exists(safeFolder))
        {
            try
            {
                ClearReadOnlyAttributes(safeFolder);
                Directory.Delete(safeFolder, recursive: true);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                throw new QuizHistoryFolderCleanupException(safeFolder, error);
            }
        }

        return true;
    }

    private static void ClearReadOnlyAttributes(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);

        foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);

        File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
    }
}
