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
        var existingQuestionKeys = new HashSet<string>(StringComparer.Ordinal);
        var existingQuestions = new List<(string Question, string CorrectAnswer)>();

        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT question, option_a, option_b, option_c, option_d, correct_index FROM quiz_questions";
            using var reader = existing.ExecuteReader();
            while (reader.Read())
            {
                var text = reader.GetString(0);
                var correctIndex = reader.GetInt32(5);
                var correctAnswer = reader.GetString(correctIndex + 1);
                existingQuestionKeys.Add(QuizQuestionDuplicateKey.Create(text));
                existingQuestions.Add((text, correctAnswer));
            }
        }

        foreach (var parsedQuestion in questions)
        {
            var question = parsedQuestion with
            {
                Category = QuizQuestionCategoryNormalizer.Normalize(parsedQuestion.Category),
                ImagePath = ManageQuizQuestionImage(parsedQuestion.ImagePath),
            };
            var duplicateKey = QuizQuestionDuplicateKey.Create(question.Question);
            var correctAnswer = question.Answers[question.CorrectIndex];
            if (existingQuestionKeys.Contains(duplicateKey) ||
                existingQuestions.Any(existing => QuizQuestionDuplicateDetector.IsLikelyDuplicate(
                    question.Question,
                    correctAnswer,
                    existing.Question,
                    existing.CorrectAnswer)))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO quiz_questions(
                    question, option_a, option_b, option_c, option_d,
                    correct_index, explanation, category, difficulty,
                    source, fingerprint, created, times_used, image_path)
                VALUES(
                    $question, $a, $b, $c, $d,
                    $correct, $explanation, $category, $difficulty,
                    $source, $fingerprint, $created, 0, $imagePath)
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
            command.Parameters.AddWithValue("$imagePath", question.ImagePath);
            var added = command.ExecuteNonQuery();
            inserted += added;
            if (added > 0)
            {
                existingQuestionKeys.Add(duplicateKey);
                existingQuestions.Add((question.Question, correctAnswer));
            }
        }

        transaction.Commit();
        return new QuizQuestionImportResult(questions.Count, inserted, questions.Count - inserted);
    }

    public IReadOnlyList<QuizQuestion> GetQuizQuestions(
        string? search = null,
        string? category = null,
        string? difficulty = null,
        int limit = 2_000,
        bool enabledOnly = false,
        bool imageOnly = false,
        string? excludeCategory = null)
    {
        EnsureQuizSchema();
        limit = Math.Clamp(limit, 1, 10_000);
        search = (search ?? "").Trim();
        var searchId = QuizQuestionSearch.ExactId(search) ?? -1;
        category = NormalizeQuizFilter(category);
        difficulty = NormalizeQuizFilter(difficulty);
        excludeCategory = NormalizeQuizFilter(excludeCategory);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, question, option_a, option_b, option_c, option_d,
                   correct_index, explanation, category, difficulty, source, times_used, enabled, image_path
            FROM quiz_questions
            WHERE ($search = '' OR id = $searchId OR question LIKE $searchLike OR category LIKE $searchLike)
              AND ($category = '' OR category = $category COLLATE NOCASE)
              AND ($difficulty = '' OR difficulty = $difficulty COLLATE NOCASE)
              AND ($enabledOnly = 0 OR enabled <> 0)
              AND ($imageOnly = 0 OR TRIM(image_path) <> '')
              AND ($excludeCategory = '' OR category <> $excludeCategory COLLATE NOCASE)
            ORDER BY id ASC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$searchId", searchId);
        command.Parameters.AddWithValue("$searchLike", $"%{EscapeLike(search)}%");
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$difficulty", difficulty);
        command.Parameters.AddWithValue("$enabledOnly", enabledOnly ? 1 : 0);
        command.Parameters.AddWithValue("$imageOnly", imageOnly ? 1 : 0);
        command.Parameters.AddWithValue("$excludeCategory", excludeCategory);
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
        Random? random = null,
        bool imageOnly = false)
    {
        if (count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(count), "Choose between 1 and 100 questions per quiz.");

        var logoQuiz = QuizTypeCatalog.FromCategory(category) == QuizTypeCatalog.Logo;
        var matching = GetQuizQuestions(
            category: category,
            difficulty: difficulty,
            limit: 10_000,
            enabledOnly: true,
            imageOnly: imageOnly || logoQuiz,
            excludeCategory: QuizTypeCatalog.ExcludedRandomCategory(category));
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
                    enabled INTEGER NOT NULL DEFAULT 1,
                    image_path TEXT NOT NULL DEFAULT ''
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
        EnsureQuizColumn(connection, "image_path", "TEXT NOT NULL DEFAULT ''");
        using var enabledIndex = connection.CreateCommand();
        enabledIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_quiz_questions_enabled
                ON quiz_questions(enabled, times_used);
            """;
        enabledIndex.ExecuteNonQuery();
        NormalizeStoredQuizCategories(connection);
        MigrateStoredQuizQuestionImages(connection);
    }

    private string ManageQuizQuestionImage(string? imagePath) =>
        string.IsNullOrWhiteSpace(imagePath)
            ? ""
            : QuizQuestionImage.Import(imagePath, _dataRoot);

    private void MigrateStoredQuizQuestionImages(SqliteConnection connection)
    {
        var storedImages = new List<(int Id, string Path)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT id, image_path FROM quiz_questions WHERE TRIM(image_path) <> ''";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                storedImages.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        foreach (var stored in storedImages)
        {
            if (QuizQuestionImage.IsManagedPath(stored.Path, _dataRoot))
                continue;

            try
            {
                var managedPath = ManageQuizQuestionImage(stored.Path);
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE quiz_questions SET image_path = $path WHERE id = $id";
                update.Parameters.AddWithValue("$path", managedPath);
                update.Parameters.AddWithValue("$id", stored.Id);
                update.ExecuteNonQuery();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Could not migrate the image for quiz question #{stored.Id}: {error.Message}");
            }
        }
    }

    private static void NormalizeStoredQuizCategories(SqliteConnection connection)
    {
        var storedCategories = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT DISTINCT category FROM quiz_questions";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                storedCategories.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
        }

        foreach (var stored in storedCategories)
        {
            var normalized = QuizQuestionCategoryNormalizer.Normalize(stored);
            if (string.Equals(stored.Trim(), normalized, StringComparison.Ordinal))
                continue;

            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE quiz_questions SET category = $normalized WHERE category = $stored COLLATE NOCASE";
            update.Parameters.AddWithValue("$normalized", normalized);
            update.Parameters.AddWithValue("$stored", stored);
            update.ExecuteNonQuery();
        }
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
        reader.GetInt32(12) != 0,
        reader.IsDBNull(13) ? "" : reader.GetString(13));

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

internal static class QuizQuestionSearch
{
    public static int? ExactId(string? search)
    {
        var value = (search ?? "").Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
            value = value[1..].Trim();
        else if (value.StartsWith("No.", StringComparison.OrdinalIgnoreCase))
            value = value[3..].Trim();

        return int.TryParse(value, out var id) && id > 0 ? id : null;
    }
}
