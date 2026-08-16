using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public QuizQuestionImportResult ImportQuizQuestions(string json, string source = "ChatGPT import")
    {
        var questions = QuizQuestionImportParser.Parse(json, source);
        EnsureQuizSchema();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var inserted = 0;

        foreach (var question in questions)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO quiz_questions(
                    question, option_a, option_b, option_c, option_d,
                    correct_index, explanation, category, difficulty,
                    source, fingerprint, created, times_used)
                VALUES(
                    $question, $a, $b, $c, $d,
                    $correct, $explanation, $category, $difficulty,
                    $source, $fingerprint, $created, 0)
                """;
            command.Parameters.AddWithValue("$question", question.Question);
            command.Parameters.AddWithValue("$a", question.OptionA);
            command.Parameters.AddWithValue("$b", question.OptionB);
            command.Parameters.AddWithValue("$c", question.OptionC);
            command.Parameters.AddWithValue("$d", question.OptionD);
            command.Parameters.AddWithValue("$correct", question.CorrectIndex);
            command.Parameters.AddWithValue("$explanation", question.Explanation);
            command.Parameters.AddWithValue("$category", question.Category);
            command.Parameters.AddWithValue("$difficulty", question.Difficulty);
            command.Parameters.AddWithValue("$source", question.Source);
            command.Parameters.AddWithValue("$fingerprint", question.Fingerprint);
            command.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            inserted += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new QuizQuestionImportResult(questions.Count, inserted, questions.Count - inserted);
    }

    public IReadOnlyList<QuizQuestion> GetQuizQuestions(
        string? search = null,
        string? category = null,
        string? difficulty = null,
        int limit = 2_000,
        bool enabledOnly = false)
    {
        EnsureQuizSchema();
        limit = Math.Clamp(limit, 1, 10_000);
        search = (search ?? "").Trim();
        category = NormalizeQuizFilter(category);
        difficulty = NormalizeQuizFilter(difficulty);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, question, option_a, option_b, option_c, option_d,
                   correct_index, explanation, category, difficulty, source, times_used, enabled
            FROM quiz_questions
            WHERE ($search = '' OR question LIKE $searchLike OR category LIKE $searchLike)
              AND ($category = '' OR category = $category COLLATE NOCASE)
              AND ($difficulty = '' OR difficulty = $difficulty COLLATE NOCASE)
              AND ($enabledOnly = 0 OR enabled <> 0)
            ORDER BY category COLLATE NOCASE, question COLLATE NOCASE
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$searchLike", $"%{EscapeLike(search)}%");
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$difficulty", difficulty);
        command.Parameters.AddWithValue("$enabledOnly", enabledOnly ? 1 : 0);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var results = new List<QuizQuestion>();
        while (reader.Read())
        {
            results.Add(ReadQuizQuestion(reader));
        }
        return results;
    }

    public IReadOnlyList<QuizQuestion> GetRandomQuizQuestions(
        int count,
        string? category = null,
        string? difficulty = null,
        Random? random = null)
    {
        if (count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(count), "Choose between 1 and 100 questions per quiz.");

        var matching = GetQuizQuestions(
            category: category,
            difficulty: difficulty,
            limit: 10_000,
            enabledOnly: true);
        return QuizQuestionSelector.SelectRandom(matching, count, random);
    }

    public IReadOnlyList<string> GetQuizCategories()
    {
        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT category
            FROM quiz_questions
            WHERE TRIM(category) <> ''
            ORDER BY category COLLATE NOCASE
            """;
        using var reader = command.ExecuteReader();
        var categories = new List<string>();
        while (reader.Read())
            categories.Add(reader.GetString(0));
        return categories;
    }

    public int GetQuizQuestionCount(
        string? category = null,
        string? difficulty = null,
        bool enabledOnly = false)
    {
        EnsureQuizSchema();
        category = NormalizeQuizFilter(category);
        difficulty = NormalizeQuizFilter(difficulty);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM quiz_questions
            WHERE ($category = '' OR category = $category COLLATE NOCASE)
              AND ($difficulty = '' OR difficulty = $difficulty COLLATE NOCASE)
              AND ($enabledOnly = 0 OR enabled <> 0)
            """;
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$difficulty", difficulty);
        command.Parameters.AddWithValue("$enabledOnly", enabledOnly ? 1 : 0);
        return Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L));
    }

    public void RecordQuizQuestionsUsed(IEnumerable<int> questionIds)
    {
        var ids = questionIds.Distinct().Where(id => id > 0).ToArray();
        if (ids.Length == 0)
            return;
        EnsureQuizSchema();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE quiz_questions
                SET times_used = times_used + 1, last_used = $lastUsed
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$lastUsed", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void SetQuizQuestionEnabled(int id, bool enabled)
    {
        if (id <= 0)
            return;
        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE quiz_questions SET enabled=$enabled WHERE id=$id";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteQuizQuestion(int id)
    {
        if (id <= 0)
            return;
        EnsureQuizSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM quiz_questions WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private void EnsureQuizSchema()
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS quiz_questions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    question TEXT NOT NULL,
                    option_a TEXT NOT NULL,
                    option_b TEXT NOT NULL,
                    option_c TEXT NOT NULL,
                    option_d TEXT NOT NULL,
                    correct_index INTEGER NOT NULL CHECK(correct_index BETWEEN 0 AND 3),
                    explanation TEXT NOT NULL DEFAULT '',
                    category TEXT NOT NULL DEFAULT 'General Knowledge',
                    difficulty TEXT NOT NULL DEFAULT 'medium',
                    source TEXT NOT NULL DEFAULT 'Imported',
                    fingerprint TEXT NOT NULL UNIQUE,
                    created TEXT NOT NULL,
                    times_used INTEGER NOT NULL DEFAULT 0,
                    last_used TEXT NULL,
                    enabled INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ix_quiz_questions_category
                    ON quiz_questions(category COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS ix_quiz_questions_difficulty
                    ON quiz_questions(difficulty COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS ix_quiz_questions_usage
                    ON quiz_questions(times_used, last_used);
                """;
            command.ExecuteNonQuery();
        }

        EnsureQuizColumn(connection, "enabled", "INTEGER NOT NULL DEFAULT 1");
        using var enabledIndex = connection.CreateCommand();
        enabledIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_quiz_questions_enabled
                ON quiz_questions(enabled, times_used);
            """;
        enabledIndex.ExecuteNonQuery();
    }

    private static void EnsureQuizColumn(SqliteConnection connection, string columnName, string definition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(quiz_questions)";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE quiz_questions ADD COLUMN {columnName} {definition}";
        alter.ExecuteNonQuery();
    }

    private static QuizQuestion ReadQuizQuestion(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt32(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.GetInt32(11),
        reader.GetInt32(12) != 0);

    private static string NormalizeQuizFilter(string? value)
    {
        var text = (value ?? "").Trim();
        return text.StartsWith("All ", StringComparison.OrdinalIgnoreCase) ? "" : text;
    }

    private static string EscapeLike(string value) => value
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("_", "[_]", StringComparison.Ordinal);
}
