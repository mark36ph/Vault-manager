using System.Globalization;

namespace FactVaultManager.Desktop;

public sealed record QuizHistorySummary(
    int Id,
    string Title,
    string Created,
    int QuestionCount,
    string Categories,
    string Format,
    int QuestionSeconds,
    bool ShuffleAnswers,
    string ProjectFolder);

public sealed record QuizHistoryQuestion(
    int Position,
    int QuestionId,
    string Question,
    string Category,
    string Difficulty);

public sealed partial class DesktopDataService
{
    public int RecordQuizExport(
        string title,
        IReadOnlyList<QuizQuestion> questions,
        bool vertical,
        int questionSeconds,
        bool shuffleAnswers,
        string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Quiz title is required.", nameof(title));
        if (questions is null || questions.Count == 0)
            throw new ArgumentException("A quiz history entry must contain at least one question.", nameof(questions));
        if (questionSeconds is < 2 or > 60)
            throw new ArgumentOutOfRangeException(nameof(questionSeconds));

        EnsureQuizHistorySchema();
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var categories = string.Join(", ", questions
            .Select(question => question.Category.Trim())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase));
        var format = vertical ? "9:16" : "16:9";

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        int historyId;
        using (var insertHistory = connection.CreateCommand())
        {
            insertHistory.Transaction = transaction;
            insertHistory.CommandText = """
                INSERT INTO quiz_history(
                    title, created, question_count, categories, format,
                    question_seconds, shuffle_answers, project_folder)
                VALUES(
                    $title, $created, $questionCount, $categories, $format,
                    $questionSeconds, $shuffleAnswers, $projectFolder);
                SELECT last_insert_rowid();
                """;
            insertHistory.Parameters.AddWithValue("$title", title.Trim());
            insertHistory.Parameters.AddWithValue("$created", created);
            insertHistory.Parameters.AddWithValue("$questionCount", questions.Count);
            insertHistory.Parameters.AddWithValue("$categories", categories);
            insertHistory.Parameters.AddWithValue("$format", format);
            insertHistory.Parameters.AddWithValue("$questionSeconds", questionSeconds);
            insertHistory.Parameters.AddWithValue("$shuffleAnswers", shuffleAnswers ? 1 : 0);
            insertHistory.Parameters.AddWithValue("$projectFolder", (projectFolder ?? "").Trim());
            historyId = Convert.ToInt32((long)(insertHistory.ExecuteScalar() ?? 0L));
        }

        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            using var insertQuestion = connection.CreateCommand();
            insertQuestion.Transaction = transaction;
            insertQuestion.CommandText = """
                INSERT INTO quiz_history_questions(
                    history_id, position, question_id, question, category, difficulty)
                VALUES($historyId, $position, $questionId, $question, $category, $difficulty)
                """;
            insertQuestion.Parameters.AddWithValue("$historyId", historyId);
            insertQuestion.Parameters.AddWithValue("$position", index + 1);
            insertQuestion.Parameters.AddWithValue("$questionId", question.Id);
            insertQuestion.Parameters.AddWithValue("$question", question.Question);
            insertQuestion.Parameters.AddWithValue("$category", question.Category);
            insertQuestion.Parameters.AddWithValue("$difficulty", question.Difficulty);
            insertQuestion.ExecuteNonQuery();
        }

        foreach (var questionId in questions.Select(question => question.Id).Where(id => id > 0).Distinct())
        {
            using var updateUsage = connection.CreateCommand();
            updateUsage.Transaction = transaction;
            updateUsage.CommandText = """
                UPDATE quiz_questions
                SET times_used = times_used + 1, last_used = $lastUsed
                WHERE id = $id
                """;
            updateUsage.Parameters.AddWithValue("$lastUsed", created);
            updateUsage.Parameters.AddWithValue("$id", questionId);
            updateUsage.ExecuteNonQuery();
        }

        transaction.Commit();
        return historyId;
    }

    public IReadOnlySet<int> GetRecentQuizQuestionIds(int recentQuizCount)
    {
        if (recentQuizCount <= 0)
            return new HashSet<int>();

        EnsureQuizHistorySchema();
        recentQuizCount = Math.Clamp(recentQuizCount, 1, 100);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT q.question_id
            FROM quiz_history_questions q
            WHERE q.history_id IN (
                SELECT id
                FROM quiz_history
                ORDER BY id DESC
                LIMIT $limit
            )
            """;
        command.Parameters.AddWithValue("$limit", recentQuizCount);

        using var reader = command.ExecuteReader();
        var ids = new HashSet<int>();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    public IReadOnlyList<QuizHistorySummary> GetQuizHistory(int limit = 500)
    {
        EnsureQuizHistorySchema();
        limit = Math.Clamp(limit, 1, 2_000);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, created, question_count, categories, format,
                   question_seconds, shuffle_answers, project_folder
            FROM quiz_history
            ORDER BY id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var results = new List<QuizHistorySummary>();
        while (reader.Read())
        {
            results.Add(new QuizHistorySummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7) != 0,
                reader.GetString(8)));
        }
        return results;
    }

    public IReadOnlyList<QuizHistoryQuestion> GetQuizHistoryQuestions(int historyId)
    {
        if (historyId <= 0)
            return [];
        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT position, question_id, question, category, difficulty
            FROM quiz_history_questions
            WHERE history_id = $historyId
            ORDER BY position ASC
            """;
        command.Parameters.AddWithValue("$historyId", historyId);

        using var reader = command.ExecuteReader();
        var results = new List<QuizHistoryQuestion>();
        while (reader.Read())
        {
            results.Add(new QuizHistoryQuestion(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }
        return results;
    }

    private void EnsureQuizHistorySchema()
    {
        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quiz_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                created TEXT NOT NULL,
                question_count INTEGER NOT NULL,
                categories TEXT NOT NULL DEFAULT '',
                format TEXT NOT NULL DEFAULT '16:9',
                question_seconds INTEGER NOT NULL,
                shuffle_answers INTEGER NOT NULL DEFAULT 0,
                project_folder TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS quiz_history_questions (
                history_id INTEGER NOT NULL,
                position INTEGER NOT NULL,
                question_id INTEGER NOT NULL,
                question TEXT NOT NULL,
                category TEXT NOT NULL DEFAULT '',
                difficulty TEXT NOT NULL DEFAULT '',
                PRIMARY KEY(history_id, position),
                FOREIGN KEY(history_id) REFERENCES quiz_history(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_quiz_history_created
                ON quiz_history(id DESC);
            CREATE INDEX IF NOT EXISTS ix_quiz_history_questions_question
                ON quiz_history_questions(question_id, history_id DESC);
            """;
        command.ExecuteNonQuery();
    }
}
