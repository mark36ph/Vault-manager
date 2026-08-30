using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public QuizQuestionBulkImportPreview PreviewQuizQuestionImport(
        string json,
        string source = "JSON import")
    {
        var parsed = QuizQuestionBulkImportParser.Parse(json, source, new Random(104729));
        EnsureQuizSchema();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var duplicates = LoadQuizImportDuplicateIndex(connection, transaction);
        var duplicateCount = 0;
        var readyCount = 0;
        var categoryMappings = parsed.CategoryMappings;

        foreach (var parsedQuestion in parsed.Items)
        {
            var category = QuizQuestionTopicCategorizer.NormalizeImportedCategory(
                parsedQuestion.Category,
                parsedQuestion.Question,
                parsedQuestion.Answers,
                parsedQuestion.Explanation,
                parsedQuestion.ImagePath);
            if (!parsed.IsOpenTriviaDb &&
                !string.Equals(category, parsedQuestion.Category, StringComparison.OrdinalIgnoreCase))
            {
                categoryMappings++;
            }

            var question = parsedQuestion with { Category = category };
            if (duplicates.Contains(question))
            {
                duplicateCount++;
                continue;
            }

            readyCount++;
            duplicates.Add(question);
        }

        transaction.Rollback();
        return new QuizQuestionBulkImportPreview(
            parsed.Detected,
            parsed.Items.Count,
            readyCount,
            duplicateCount,
            parsed.Invalid,
            categoryMappings,
            parsed.IsOpenTriviaDb);
    }

    public QuizQuestionBulkImportResult ImportQuizQuestionsBulk(
        string json,
        string source = "JSON import")
    {
        var parsed = QuizQuestionBulkImportParser.Parse(json, source);
        EnsureQuizSchema();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var duplicates = LoadQuizImportDuplicateIndex(connection, transaction);
        var inserted = 0;
        var categoryMappings = parsed.CategoryMappings;

        foreach (var parsedQuestion in parsed.Items)
        {
            var managedImagePath = ManageQuizQuestionImage(parsedQuestion.ImagePath);
            var category = QuizQuestionTopicCategorizer.NormalizeImportedCategory(
                parsedQuestion.Category,
                parsedQuestion.Question,
                parsedQuestion.Answers,
                parsedQuestion.Explanation,
                managedImagePath);
            if (!parsed.IsOpenTriviaDb &&
                !string.Equals(category, parsedQuestion.Category, StringComparison.OrdinalIgnoreCase))
            {
                categoryMappings++;
            }

            var question = parsedQuestion with
            {
                Category = category,
                ImagePath = managedImagePath,
            };

            if (duplicates.Contains(question))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO quiz_questions(
                    question, option_a, option_b, option_c, option_d,
                    correct_index, explanation, category, difficulty,
                    source, fingerprint, created, times_used, enabled, image_path)
                VALUES(
                    $question, $a, $b, $c, $d,
                    $correct, $explanation, $category, $difficulty,
                    $source, $fingerprint, $created, 0, $enabled, $imagePath)
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
            command.Parameters.AddWithValue("$enabled", QuizQuestionEnablement.ForImport(question.Category, question.ImagePath) ? 1 : 0);
            command.Parameters.AddWithValue("$imagePath", question.ImagePath);

            var added = command.ExecuteNonQuery();
            if (added <= 0)
                continue;

            inserted += added;
            duplicates.Add(question);
        }

        transaction.Commit();
        return new QuizQuestionBulkImportResult(
            parsed.Detected,
            parsed.Items.Count,
            inserted,
            parsed.Items.Count - inserted,
            parsed.Invalid,
            categoryMappings,
            parsed.IsOpenTriviaDb);
    }

    private static QuizImportDuplicateIndex LoadQuizImportDuplicateIndex(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var index = new QuizImportDuplicateIndex();
        using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = "SELECT question, option_a, option_b, option_c, option_d, correct_index FROM quiz_questions";
        using var reader = existing.ExecuteReader();
        while (reader.Read())
        {
            var question = reader.GetString(0);
            var correctIndex = reader.GetInt32(5);
            var correctAnswer = reader.GetString(correctIndex + 1);
            index.Add(question, correctAnswer);
        }
        return index;
    }

    private sealed class QuizImportDuplicateIndex
    {
        private readonly HashSet<string> _questionKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(string Question, string CorrectAnswer)>> _byCorrectAnswer =
            new(StringComparer.Ordinal);

        public bool Contains(QuizQuestionImportItem question)
        {
            var questionKey = QuizQuestionDuplicateKey.Create(question.Question);
            if (QuizTypeCatalog.FromCategory(question.Category) != QuizTypeCatalog.Logo &&
                _questionKeys.Contains(questionKey))
            {
                return true;
            }

            var correctAnswer = question.Answers[question.CorrectIndex];
            var answerKey = NormalizeQuizImportAnswer(correctAnswer);
            if (!_byCorrectAnswer.TryGetValue(answerKey, out var candidates))
                return false;

            return candidates.Any(existing => QuizQuestionDuplicateDetector.IsLikelyDuplicate(
                question.Question,
                correctAnswer,
                existing.Question,
                existing.CorrectAnswer));
        }

        public void Add(QuizQuestionImportItem question) =>
            Add(question.Question, question.Answers[question.CorrectIndex]);

        public void Add(string question, string correctAnswer)
        {
            _questionKeys.Add(QuizQuestionDuplicateKey.Create(question));
            var answerKey = NormalizeQuizImportAnswer(correctAnswer);
            if (!_byCorrectAnswer.TryGetValue(answerKey, out var matches))
            {
                matches = new List<(string Question, string CorrectAnswer)>();
                _byCorrectAnswer[answerKey] = matches;
            }
            matches.Add((question, correctAnswer));
        }
    }

    private static string NormalizeQuizImportAnswer(string answer)
    {
        var chars = (answer ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
