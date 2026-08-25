namespace FactVaultManager.Desktop;

public static class QuizQuestionCategoryNormalizer
{
    public static IReadOnlyList<string> CanonicalCategories { get; } =
    [
        "Science",
        "History",
        "Geography",
        "Space",
        "Nature & Animals",
        "Technology",
        "Arts & Literature",
        "Music",
        "Film",
        "Logos",
        "Sports",
        "Entertainment",
        "Mathematics",
        "General Knowledge",
    ];

    public static string Normalize(string? category)
    {
        var value = (category ?? "").Trim();
        if (value.Length == 0)
            return "General Knowledge";

        return value.ToLowerInvariant() switch
        {
            "science" => "Science",
            "history" => "History",
            "geography" => "Geography",
            "space" => "Space",
            "nature" or "animals" or "wildlife" or "nature & animals" => "Nature & Animals",
            "technology" => "Technology",
            "literature" or "art & culture" or "arts & literature" or "art" => "Arts & Literature",
            "music" or "classical music" or "pop music" => "Music",
            "film" or "movie" or "movies" or "cinema" => "Film",
            "icon" or "icons" or "logo" or "logos" or "brand logo" or "company logo" => "Logos",
            "sport" or "sports" => "Sports",
            "film & tv" or "tv" or "television" or "entertainment" => "Entertainment",
            "math" or "maths" or "mathematics" => "Mathematics",
            "games & puzzles" or "food & drink" or "language" or "miscellaneous" or "general" or "general knowledge" => "General Knowledge",
            _ => value,
        };
    }
}

public static class QuizQuestionDuplicateDetector
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "what", "which", "who", "where", "when", "why", "how",
        "is", "are", "was", "were", "do", "does", "did", "has", "have", "had",
        "of", "to", "in", "on", "for", "from", "with", "by", "as", "at", "into",
        "it", "its", "this", "that", "these", "those", "called", "known", "name", "city",
    };

    public static bool IsLikelyDuplicate(
        string firstQuestion,
        string firstCorrectAnswer,
        string secondQuestion,
        string secondCorrectAnswer)
    {
        if (!string.Equals(NormalizeAnswer(firstCorrectAnswer), NormalizeAnswer(secondCorrectAnswer), StringComparison.Ordinal))
            return false;

        if (string.Equals(
                QuizQuestionDuplicateKey.Create(firstQuestion),
                QuizQuestionDuplicateKey.Create(secondQuestion),
                StringComparison.Ordinal))
        {
            return true;
        }

        var first = SignificantTokens(firstQuestion);
        var second = SignificantTokens(secondQuestion);
        if (first.Count < 2 || second.Count < 2)
            return false;

        var intersection = first.Intersect(second, StringComparer.Ordinal).Count();
        var union = first.Union(second, StringComparer.Ordinal).Count();
        var containment = intersection / (double)Math.Min(first.Count, second.Count);
        var jaccard = intersection / (double)union;
        return containment >= 0.80 && jaccard >= 0.60;
    }

    private static HashSet<string> SignificantTokens(string question)
    {
        var tokens = QuizQuestionDuplicateKey.Create(question)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        return tokens;
    }

    private static string NormalizeAnswer(string answer)
    {
        var chars = (answer ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
