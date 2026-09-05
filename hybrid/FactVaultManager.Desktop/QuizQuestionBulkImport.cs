using System.Net;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizQuestionBulkImportParseResult(IReadOnlyList<QuizQuestionImportItem> Items, int Detected, int Invalid, int CategoryMappings, bool IsOpenTriviaDb);
public sealed record QuizQuestionBulkImportPreview(int Detected, int Valid, int Ready, int Duplicates, int Invalid, int CategoryMappings, bool IsOpenTriviaDb);
public sealed record QuizQuestionBulkImportResult(int Detected, int Valid, int Inserted, int Duplicates, int Invalid, int CategoryMappings, bool IsOpenTriviaDb);

public static class QuizQuestionBulkImportParser
{
    public const int MaximumImportCharacters = 15_000_000;
    public const int MaximumQuestionsPerImport = 20_000;

    public static QuizQuestionBulkImportParseResult Parse(string input, string defaultSource = "JSON import", Random? random = null)
    {
        var text = StripCodeFence(input ?? "").Trim();
        if (text.Length == 0) throw new InvalidDataException("Paste or load quiz questions as JSON first.");
        if (text.Length > MaximumImportCharacters) throw new InvalidDataException("Quiz import is too large. Import a smaller file.");
        JsonDocument document;
        try { var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text)); document = JsonDocument.ParseValue(ref reader); }
        catch (JsonException error) { throw new InvalidDataException($"Quiz JSON is invalid: {error.Message}", error); }
        using (document)
        {
            if (!TryOpenTriviaQuestionArray(document.RootElement, out var openTriviaQuestions))
            {
                var items = QuizQuestionImportParser.Parse(text, defaultSource);
                return new QuizQuestionBulkImportParseResult(items, items.Count, 0, 0, false);
            }
            var detected = openTriviaQuestions.GetArrayLength();
            if (detected > MaximumQuestionsPerImport) throw new InvalidDataException($"A single import can contain at most {MaximumQuestionsPerImport:N0} questions.");
            random ??= Random.Shared;
            var results = new List<QuizQuestionImportItem>(detected);
            var invalid = 0; var categoryMappings = 0;
            foreach (var element in openTriviaQuestions.EnumerateArray())
            {
                if (!TryParseOpenTriviaQuestion(element, random, out var item, out var categoryWasMapped)) { invalid++; continue; }
                if (categoryWasMapped) categoryMappings++;
                results.Add(item!);
            }
            if (results.Count == 0) throw new InvalidDataException(detected == 0 ? "The Open Trivia DB JSON did not contain any questions." : "The Open Trivia DB JSON did not contain any supported four-answer multiple-choice questions.");
            return new QuizQuestionBulkImportParseResult(results, detected, invalid, categoryMappings, true);
        }
    }

    private static bool TryOpenTriviaQuestionArray(JsonElement root, out JsonElement questions)
    {
        if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "results", out var results) && results.ValueKind == JsonValueKind.Array) { questions = results; return true; }
        JsonElement candidate;
        if (root.ValueKind == JsonValueKind.Array) candidate = root;
        else if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "questions", out var wrapped) && wrapped.ValueKind == JsonValueKind.Array) candidate = wrapped;
        else { questions = default; return false; }
        foreach (var item in candidate.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && TryProperty(item, "incorrect_answers", out _) && TryProperty(item, "correct_answer", out _)) { questions = candidate; return true; }
        }
        questions = default; return false;
    }

    private static bool TryParseOpenTriviaQuestion(JsonElement element, Random random, out QuizQuestionImportItem? item, out bool categoryWasMapped)
    {
        item = null; categoryWasMapped = false;
        if (element.ValueKind != JsonValueKind.Object) return false;
        var type = OptionalString(element, "type");
        if (type.Length > 0 && !string.Equals(type, "multiple", StringComparison.OrdinalIgnoreCase)) return false;
        if (!TryProperty(element, "question", out var questionValue) || questionValue.ValueKind != JsonValueKind.String || !TryProperty(element, "correct_answer", out var correctValue) || correctValue.ValueKind != JsonValueKind.String || !TryProperty(element, "incorrect_answers", out var incorrectValue) || incorrectValue.ValueKind != JsonValueKind.Array) return false;
        var incorrect = incorrectValue.EnumerateArray().ToArray();
        if (incorrect.Length != 3 || incorrect.Any(value => value.ValueKind != JsonValueKind.String)) return false;
        string question, correctAnswer; List<string> answers;
        try
        {
            question = CheckedText(Decode(questionValue.GetString()), "question", 500);
            correctAnswer = CheckedText(Decode(correctValue.GetString()), "answer", 300);
            answers = new List<string>(4) { correctAnswer };
            answers.AddRange(incorrect.Select(value => CheckedText(Decode(value.GetString()), "answer", 300)));
        }
        catch (InvalidDataException) { return false; }
        if (answers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4) return false;
        Shuffle(answers, random);
        var correctIndex = answers.FindIndex(answer => string.Equals(answer, correctAnswer, StringComparison.Ordinal));
        if (correctIndex < 0) return false;
        var originalCategory = Decode(OptionalString(element, "category")).Trim();
        var category = QuizOpenTriviaCategoryMapper.Map(originalCategory);
        categoryWasMapped = originalCategory.Length > 0 && !string.Equals(originalCategory, category, StringComparison.OrdinalIgnoreCase);
        var difficulty = QuizDifficultyCatalog.Normalize(Decode(OptionalString(element, "difficulty")));
        item = new QuizQuestionImportItem(question, answers[0], answers[1], answers[2], answers[3], correctIndex, "", category, difficulty, "Open Trivia DB");
        return true;
    }

    private static void Shuffle<T>(IList<T> values, Random random) { for (var index = values.Count - 1; index > 0; index--) { var swap = random.Next(index + 1); (values[index], values[swap]) = (values[swap], values[index]); } }
    private static string Decode(string? value) => WebUtility.HtmlDecode(value ?? "");
    private static string CheckedText(string? value, string fieldName, int maximumLength) { var text = (value ?? "").Trim(); if (text.Length == 0 || text.Length > maximumLength) throw new InvalidDataException($"Open Trivia DB field '{fieldName}' is invalid."); return text; }
    private static string OptionalString(JsonElement element, string propertyName) { if (!TryProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return ""; return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString(); }
    private static bool TryProperty(JsonElement element, string name, out JsonElement value) { foreach (var property in element.EnumerateObject()) if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
    private static string StripCodeFence(string value) { var text = value.Trim(); if (!text.StartsWith("```", StringComparison.Ordinal)) return text; var firstNewline = text.IndexOf('\n'); if (firstNewline < 0) return text; text = text[(firstNewline + 1)..]; var closing = text.LastIndexOf("```", StringComparison.Ordinal); return closing >= 0 ? text[..closing] : text; }
}

public static class QuizOpenTriviaCategoryMapper
{
    public static string Map(string? category)
    {
        var value = (category ?? "").Trim(); if (value.Length == 0) return "General Knowledge";
        var mapped = value.ToLowerInvariant() switch
        {
            "general knowledge" => "General Knowledge", "entertainment: books" => "Arts & Literature", "entertainment: film" => "Film", "entertainment: music" => "Music", "entertainment: musicals & theatres" => "Arts & Literature", "entertainment: television" => "Entertainment", "entertainment: video games" => "Entertainment", "entertainment: board games" => "Entertainment", "entertainment: comics" => "Arts & Literature", "entertainment: japanese anime & manga" => "Entertainment", "entertainment: cartoon & animations" => "Entertainment", "science & nature" => "Science", "science: computers" => "Technology", "science: mathematics" => "Mathematics", "science: gadgets" => "Technology", "mythology" => "Arts & Literature", "sports" => "Sports", "geography" => "Geography", "history" => "History", "politics" => "General Knowledge", "art" => "Arts & Literature", "celebrities" => "Entertainment", "animals" => "Nature & Animals", "vehicles" => "Technology", _ => ""
        };
        if (mapped.Length > 0) return mapped;
        if (value.Contains("astronomy", StringComparison.OrdinalIgnoreCase) || value.Contains("space", StringComparison.OrdinalIgnoreCase)) return "Space";
        if (value.StartsWith("science", StringComparison.OrdinalIgnoreCase)) return "Science";
        if (value.StartsWith("entertainment", StringComparison.OrdinalIgnoreCase)) return "Entertainment";
        var normalized = QuizQuestionCategoryNormalizer.Normalize(value);
        return QuizQuestionCategoryNormalizer.CanonicalCategories.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? QuizQuestionCategoryNormalizer.CanonicalCategories.First(categoryName => string.Equals(categoryName, normalized, StringComparison.OrdinalIgnoreCase)) : "General Knowledge";
    }
}
