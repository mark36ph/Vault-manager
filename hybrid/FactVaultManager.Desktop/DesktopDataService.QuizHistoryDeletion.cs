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
    public void ResumeQuizFolderCleanup() =>
        QuizFolderCleanupQueue.ProcessDefaultInBackground(GetProjectsRoot());

    public bool DeleteQuizHistory(int historyId, bool deleteFolder = true)
    {
        if (historyId <= 0)
            return false;

        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        string projectFolder;
        using (var history = connection.CreateCommand())
        {
            history.CommandText = "SELECT project_folder FROM quiz_history WHERE id = $historyId";
            history.Parameters.AddWithValue("$historyId", historyId);
            var value = history.ExecuteScalar();
            if (value is null || value is DBNull)
                return false;
            projectFolder = Convert.ToString(value)?.Trim() ?? "";
        }

        var projectsRoot = GetProjectsRoot();
        string? safeFolder = null;
        string? stagedFolder = null;
        if (deleteFolder && !string.IsNullOrWhiteSpace(projectFolder))
        {
            safeFolder = ProjectPathSecurity.TryEnsureContained(projectsRoot, projectFolder);
            if (safeFolder is not null && Directory.Exists(safeFolder))
            {
                stagedFolder = ProjectPathSecurity.EnsureContained(
                    projectsRoot,
                    safeFolder + ".delete-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.Move(safeFolder, stagedFolder);
                try
                {
                    QuizFolderCleanupQueue.Enqueue(
                        QuizFolderCleanupQueue.DefaultQueuePath,
                        projectsRoot,
                        stagedFolder);
                }
                catch
                {
                    if (Directory.Exists(stagedFolder) && !Directory.Exists(safeFolder))
                        Directory.Move(stagedFolder, safeFolder);
                    throw;
                }
            }
        }

        try
        {
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
                RestoreStagedFolder();
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
            RestoreStagedFolder();
            throw;
        }

        if (stagedFolder is not null)
            QuizFolderCleanupQueue.ProcessDefaultInBackground(projectsRoot);

        return true;

        void RestoreStagedFolder()
        {
            if (stagedFolder is null || safeFolder is null) return;
            try { QuizFolderCleanupQueue.Remove(QuizFolderCleanupQueue.DefaultQueuePath, stagedFolder); }
            catch { }
            if (Directory.Exists(stagedFolder) && !Directory.Exists(safeFolder))
                Directory.Move(stagedFolder, safeFolder);
        }
    }
}
