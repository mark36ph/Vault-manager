using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizQuestion(
    int Id,
    string Question,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    int CorrectIndex,
    string Explanation,
    string Category,
    string Difficulty,
    string Source,
    int TimesUsed,
    bool IsEnabled = true,
    string ImagePath = "")
{
    public IReadOnlyList<string> Answers => [OptionA, OptionB, OptionC, OptionD];

    public string CorrectAnswer => CorrectIndex switch
    {
        0 => OptionA,
        1 => OptionB,
        2 => OptionC,
        3 => OptionD,
        _ => "",
    };

    public string CorrectLetter => CorrectIndex is >= 0 and <= 3
        ? ((char)('A' + CorrectIndex)).ToString()
        : "";

    public string Availability => IsEnabled ? "Enabled" : "Disabled";

    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    public string QuizType => HasImage || QuizTypeCatalog.FromCategory(Category) == QuizTypeCatalog.Logo
        ? QuizTypeCatalog.Logo
        : QuizTypeCatalog.Standard;
}

public static class QuizTypeCatalog
{
    public const string Standard = "Standard";
    public const string Logo = "Logo";

    public static string Normalize(string? value) =>
        string.Equals((value ?? "").Trim(), Logo, StringComparison.OrdinalIgnoreCase)
            ? Logo
            : Standard;

    public static string FromCategory(string? category) =>
        string.Equals((category ?? "").Trim(), "Icons", StringComparison.OrdinalIgnoreCase)
            ? Logo
            : Standard;

    public static string ExcludedRandomCategory(string? selectedCategory) =>
        FromCategory(selectedCategory) == Logo ? "" : "Icons";
}

public static class QuizQuestionEnablement
{
    public static bool IsMissingRequiredImage(string? category, string? imagePath) =>
        QuizTypeCatalog.FromCategory(category) == QuizTypeCatalog.Logo &&
        string.IsNullOrWhiteSpace(imagePath);

    public static bool ForImport(string? category, string? imagePath) =>
        !IsMissingRequiredImage(category, imagePath);

    public static bool ForEdit(
        string? category,
        string? previousImagePath,
        string? newImagePath,
        bool requestedEnabled)
    {
        if (IsMissingRequiredImage(category, newImagePath))
            return false;

        var imageWasJustAttached = QuizTypeCatalog.FromCategory(category) == QuizTypeCatalog.Logo &&
                                   string.IsNullOrWhiteSpace(previousImagePath) &&
                                   !string.IsNullOrWhiteSpace(newImagePath);
        return imageWasJustAttached || requestedEnabled;
    }
}

public static class QuizQuestionImage
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp",
    };

    public static string ValidatePath(string? value, bool allowEmpty = true)
    {
        var path = (value ?? "").Trim();
        if (path.Length == 0)
        {
            if (allowEmpty)
                return "";
            throw new InvalidDataException("Choose a logo image for this question.");
        }

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("The question logo image could not be found.", path);
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
            throw new InvalidDataException("Question logo images must be PNG, JPG, JPEG, or BMP files.");
        return path;
    }

    public static string Import(string sourcePath, string dataRoot)
    {
        var source = ValidatePath(sourcePath, allowEmpty: false);
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("App data folder is required.", nameof(dataRoot));

        var managedDirectory = ManagedDirectory(dataRoot);
        Directory.CreateDirectory(managedDirectory);
        if (IsManagedPath(source, dataRoot))
            return source;

        string hash;
        using (var stream = File.OpenRead(source))
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var extension = Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(managedDirectory, hash + extension);
        if (File.Exists(destination))
            return destination;

        var temporary = Path.Combine(managedDirectory, $".{hash}_{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            try
            {
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another import of the same image completed first.
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return destination;
    }

    public static bool IsManagedPath(string? path, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(dataRoot))
            return false;

        var fullPath = Path.GetFullPath(path.Trim());
        var managedPrefix = ManagedDirectory(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(managedPrefix, comparison);
    }

    private static string ManagedDirectory(string dataRoot) =>
        Path.GetFullPath(Path.Combine(dataRoot, "data", "quiz", "question-images"));
}

public sealed record QuizQuestionImportItem(
    string Question,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    int CorrectIndex,
    string Explanation,
    string Category,
    string Difficulty,
    string Source,
    string ImagePath = "")
{
    public IReadOnlyList<string> Answers => [OptionA, OptionB, OptionC, OptionD];

    public string Fingerprint => QuizQuestionFingerprint.Create(Question, Answers);
}

public sealed record QuizQuestionImportResult(int Parsed, int Inserted, int Duplicates);

public static class QuizQuestionFingerprint
{
    public static string Create(string question, IEnumerable<string> answers)
    {
        var normalized = Normalize(question) + "\n" + string.Join("\n", answers.Select(Normalize));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string Normalize(string value) =>
        string.Join(" ", (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public static class QuizQuestionDuplicateKey
{
    public static string Create(string question)
    {
        var source = (question ?? "").Trim().ToLowerInvariant();
        var builder = new StringBuilder(source.Length);
        var pendingSpace = false;

        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(character);
                pendingSpace = false;
            }
            else if ("+-*/=%<>".Contains(character))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                builder.Append(character);
                pendingSpace = true;
            }
            else if (builder.Length > 0)
            {
                pendingSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}

public static class QuizQuestionImportParser
{
    public const int MaximumImportCharacters = 5_000_000;
    public const int MaximumQuestionsPerImport = 5_000;

    public static IReadOnlyList<QuizQuestionImportItem> Parse(string input, string defaultSource = "ChatGPT import")
    {
        var text = StripCodeFence(input ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidDataException("Paste quiz questions as JSON first.");
        if (text.Length > MaximumImportCharacters)
            throw new InvalidDataException("Quiz import is too large. Import smaller batches.");

        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Quiz JSON is invalid: {error.Message}", error);
        }

        using (document)
        {
            var questions = QuestionArray(document.RootElement);
            var results = new List<QuizQuestionImportItem>();
            var seenQuestions = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in questions.EnumerateArray())
            {
                if (results.Count >= MaximumQuestionsPerImport)
                    throw new InvalidDataException($"A single import can contain at most {MaximumQuestionsPerImport} questions.");
                if (element.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Each quiz question must be a JSON object.");

                var question = RequiredText(element, "question", 500);
                var answers = ReadAnswers(element);
                if (answers.Count != 4)
                    throw new InvalidDataException($"Question '{Short(question)}' must contain exactly four answers.");
                if (answers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                    throw new InvalidDataException($"Question '{Short(question)}' contains duplicate answer choices.");

                var correctIndex = ReadCorrectIndex(element, answers);
                var explanation = OptionalText(element, ["explanation", "reason"], 2_000);
                var category = OptionalText(element, ["category", "topic"], 100);
                var difficulty = NormalizeDifficulty(OptionalText(element, ["difficulty", "level"], 50));
                var imagePath = QuizQuestionImage.ValidatePath(
                    OptionalText(element, ["image_path", "image", "logo_path"], 2_000));
                var source = OptionalText(element, ["source"], 200);
                if (source.Length == 0)
                    source = defaultSource.Trim();
                if (source.Length == 0)
                    source = "Imported";

                var item = new QuizQuestionImportItem(
                    question,
                    answers[0], answers[1], answers[2], answers[3],
                    correctIndex,
                    explanation,
                    category.Length == 0 ? "General Knowledge" : category,
                    difficulty,
                    source,
                    imagePath);

                if (seenQuestions.Add(ImportDuplicateKey(item)))
                    results.Add(item);
            }

            if (results.Count == 0)
                throw new InvalidDataException("The JSON did not contain any quiz questions.");
            return results;
        }
    }

    public static string ChatGptPrompt(int count = 100, string category = "General Knowledge", string difficulty = "mixed")
    {
        count = Math.Clamp(count, 1, 500);
        category = string.IsNullOrWhiteSpace(category) ? "General Knowledge" : category.Trim();
        difficulty = difficulty?.Trim().ToLowerInvariant() ?? "mixed";
        if (difficulty is not ("easy" or "medium" or "hard"))
            difficulty = "mixed";
        var mixedCategories = string.Equals(category, "General Knowledge", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(category, "All categories", StringComparison.OrdinalIgnoreCase);
        var subjectLine = mixedCategories
            ? "across a balanced mix of popular quiz categories"
            : $"about {category}";
        var categoryExample = mixedCategories ? "Science" : category;
        var categoryRules = mixedCategories
            ? """
- Assign every question one specific broad category that matches its subject.
- Use a balanced mix from these stable category names: Science, History, Geography, Space, Nature & Animals, Technology, Arts & Literature, Music, Film, Icons, Sports, Entertainment, Mathematics, and General Knowledge.
- Spread the batch across as many of those categories as practical; do not label most questions as General Knowledge when a more specific category applies.
"""
            : $"- Set the category field to '{category}' for every question.\n";
        var difficultyExample = difficulty == "mixed" ? "easy" : difficulty;
        var difficultyRule = difficulty == "mixed"
            ? "- Mix easy, medium, and hard difficulty."
            : $"- Set the difficulty field to '{difficulty}' for every question.";

        var prompt = """
Create __COUNT__ accurate multiple-choice quiz questions __SUBJECT_LINE__.
Create a downloadable JSON file named quiz-questions.json and attach it to your response.
Do not display the questions as a list and do not paste the JSON into the chat response.
The downloaded file must contain JSON only, with no Markdown and no commentary.
Use exactly this shape:
{
  "questions": [
    {
      "question": "Question text",
      "answers": ["Answer A", "Answer B", "Answer C", "Answer D"],
      "correct_answer": "A",
      "explanation": "One short factual explanation.",
      "category": "__CATEGORY_EXAMPLE__",
      "difficulty": "__DIFFICULTY_EXAMPLE__"
    }
  ]
}
Rules:
- Exactly four distinct answer choices per question.
- correct_answer must be A, B, C, or D.
__DIFFICULTY_RULE__
__CATEGORY_RULES__- Avoid trick questions, ambiguous wording, duplicate questions, semantically repeated questions, and opinion-based answers.
- Do not ask the same fact again with slightly different wording.
- Keep questions suitable for a YouTube quiz.
- Verify factual accuracy before including each question.
- Do not include citations, source links, URLs, footnotes, references, Markdown, or source lists anywhere in the JSON.
- Inside quiz-questions.json, do not write anything before the opening { or after the final }.
""";

        return prompt
            .Replace("__COUNT__", count.ToString(), StringComparison.Ordinal)
            .Replace("__SUBJECT_LINE__", subjectLine, StringComparison.Ordinal)
            .Replace("__CATEGORY_EXAMPLE__", categoryExample, StringComparison.Ordinal)
            .Replace("__DIFFICULTY_EXAMPLE__", difficultyExample, StringComparison.Ordinal)
            .Replace("__DIFFICULTY_RULE__", difficultyRule, StringComparison.Ordinal)
            .Replace("__CATEGORY_RULES__", categoryRules, StringComparison.Ordinal);
    }

    private static JsonElement QuestionArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;
        if (root.ValueKind == JsonValueKind.Object &&
            TryProperty(root, ["questions", "items"], out var questions) &&
            questions.ValueKind == JsonValueKind.Array)
            return questions;
        throw new InvalidDataException("Quiz JSON must be an array or an object containing a 'questions' array.");
    }

    private static string ImportDuplicateKey(QuizQuestionImportItem item)
    {
        var questionKey = QuizQuestionDuplicateKey.Create(item.Question);
        if (QuizTypeCatalog.FromCategory(item.Category) != QuizTypeCatalog.Logo)
            return "standard\n" + questionKey;

        var correctAnswerKey = QuizQuestionDuplicateKey.Create(item.Answers[item.CorrectIndex]);
        return "icons\n" + questionKey + "\n" + correctAnswerKey;
    }

    private static List<string> ReadAnswers(JsonElement element)
    {
        if (TryProperty(element, ["answers", "options", "choices"], out var answers))
        {
            if (answers.ValueKind == JsonValueKind.Array)
            {
                var values = answers.EnumerateArray()
                    .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString())
                    .Select(value => CheckedText(value, "answer", 300))
                    .ToList();
                return values;
            }

            if (answers.ValueKind == JsonValueKind.Object)
            {
                var values = new List<string>();
                foreach (var key in new[] { "A", "B", "C", "D" })
                {
                    if (!TryProperty(answers, [key, key.ToLowerInvariant()], out var value))
                        return [];
                    values.Add(CheckedText(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString(), "answer", 300));
                }
                return values;
            }
        }

        var direct = new List<string>();
        foreach (var key in new[] { "A", "B", "C", "D" })
        {
            if (!TryProperty(element, [key, key.ToLowerInvariant()], out var value))
                return [];
            direct.Add(CheckedText(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString(), "answer", 300));
        }
        return direct;
    }

    private static int ReadCorrectIndex(JsonElement element, IReadOnlyList<string> answers)
    {
        if (!TryProperty(element, ["correct_answer", "correctAnswer", "correct", "answer", "correct_index", "correctIndex"], out var value))
            throw new InvalidDataException($"Question '{Short(RequiredText(element, "question", 500))}' is missing its correct answer.");

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            if (numeric is >= 0 and <= 3)
                return numeric;
            if (numeric is >= 1 and <= 4)
                return numeric - 1;
        }

        var text = (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())?.Trim() ?? "";
        if (text.Length == 1 && text[0] is >= 'A' and <= 'D')
            return text[0] - 'A';
        if (text.Length == 1 && text[0] is >= 'a' and <= 'd')
            return text[0] - 'a';
        if (int.TryParse(text, out numeric))
        {
            if (numeric is >= 0 and <= 3)
                return numeric;
            if (numeric is >= 1 and <= 4)
                return numeric - 1;
        }

        for (var index = 0; index < answers.Count; index++)
        {
            if (string.Equals(answers[index], text, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        throw new InvalidDataException($"Question '{Short(RequiredText(element, "question", 500))}' has an invalid correct answer '{text}'.");
    }

    private static string RequiredText(JsonElement element, string name, int maximumLength)
    {
        if (!TryProperty(element, [name], out var value))
            throw new InvalidDataException($"Quiz question is missing '{name}'.");
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        return CheckedText(text, name, maximumLength);
    }

    private static string OptionalText(JsonElement element, string[] names, int maximumLength)
    {
        if (!TryProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        text = text.Trim();
        if (text.Length > maximumLength)
            throw new InvalidDataException($"Quiz field '{names[0]}' is too long.");
        return text;
    }

    private static string CheckedText(string value, string name, int maximumLength)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidDataException($"Quiz field '{name}' cannot be empty.");
        if (text.Length > maximumLength)
            throw new InvalidDataException($"Quiz field '{name}' is too long.");
        return text;
    }

    private static string NormalizeDifficulty(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "medium",
            "easy" or "beginner" => "easy",
            "medium" or "normal" or "intermediate" => "medium",
            "hard" or "difficult" or "expert" => "hard",
            _ => normalized,
        };
    }

    private static bool TryProperty(JsonElement element, IEnumerable<string> names, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string StripCodeFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
            return text;
        text = text[(firstNewline + 1)..];
        var closing = text.LastIndexOf("```", StringComparison.Ordinal);
        if (closing >= 0)
            text = text[..closing];
        return text;
    }

    private static string Short(string value) => value.Length <= 70 ? value : value[..67] + "...";
}

public static class QuizQuestionSelector
{
    public static IReadOnlyList<QuizQuestion> SelectRandom(
        IEnumerable<QuizQuestion> questions,
        int count,
        Random? random = null)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Question count must be greater than zero.");

        var pool = questions
            .Where(question => question.IsEnabled)
            .GroupBy(question => question.Id)
            .Select(group => group.First())
            .ToList();
        if (count > pool.Count)
            throw new InvalidOperationException($"Only {pool.Count} enabled matching quiz questions are available, but {count} were requested.");

        random ??= Random.Shared;
        for (var index = pool.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (pool[index], pool[swap]) = (pool[swap], pool[index]);
        }
        return pool.Take(count).ToList();
    }
}
