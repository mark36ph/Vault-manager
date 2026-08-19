namespace FactVaultManager.Desktop;

public static class QuizQuestionTopicCategorizer
{
    public static IReadOnlyList<string> Categories { get; } = QuizQuestionCategoryNormalizer.CanonicalCategories;

    private static readonly TopicRule[] Rules =
    [
        new("Space",
        [
            "solar system", "planet", "sun", "moon", "galaxy", "astronaut", "apollo",
            "jupiter", "saturn", "mars", "venus", "mercury", "neptune", "uranus",
            "astronomical", "orbit", "milky way",
        ]),
        new("Nature & Animals",
        [
            "animal", "whale", "cheetah", "elephant", "bird", "ostrich", "octopus",
            "insect", "spider", "frog", "mammal", "dolphin", "fish", "species", "wildlife",
            "legs", "hearts",
        ]),
        new("Technology",
        [
            "world wide web", "invented", "inventor", "invention", "morse", "telephone",
            "binary", "cpu", "html", "gps", "usb", "computer", "internet", "wright brothers",
            "powered flight", "printing press",
        ]),
        new("Arts & Literature",
        [
            "wrote", "novel", "play", "poet", "shakespeare", "austen", "orwell", "odyssey",
            "hamlet", "pride and prejudice", "book", "author", "painted", "painting", "artist",
            "mona lisa", "starry night", "sculpture", "michelangelo", "guernica", "the scream", "museum",
        ]),
        new("Music",
        [
            "music", "song", "album", "singer", "musician", "composer", "symphony", "opera",
            "piano", "violin", "guitar", "orchestra", "band", "beatles", "mozart", "beethoven",
        ]),
        new("Film",
        [
            "film", "movie", "cinema", "oscar", "academy award", "best picture", "screenplay",
            "box office", "film director",
        ]),
        new("Entertainment",
        [
            "television", "tv show", "series", "sitcom", "actor", "actress", "celebrity", "emmy",
        ]),
        new("Sports",
        [
            "football", "basketball", "tennis", "wimbledon", "olympic", "marathon",
            "free throw", "fifa", "cricket", "rugby", "golf", "players on the field",
        ]),
        new("Mathematics",
        [
            "square root", "percent", "triangle", "prime number", "roman numeral", "decimal number",
            "metre", "kilometre", "millilitre", "litre", "multiplied", "degrees", "dozen",
        ]),
        new("Science",
        [
            "chemical", "atomic", "atom", "oxygen", "hydrogen", "electric current", "si unit",
            "photosynthesis", "gravity", "particle", "electron", "dna", "boils", "celsius",
            "formula", "molecule", "ph value", "human body", "organ",
        ]),
        new("Geography",
        [
            "capital", "country", "continent", "ocean", "mountain", "desert", "river", "sea",
            "city", "border", "located", "machu picchu", "great wall", "taj mahal", "eiffel tower",
            "petra", "angkor wat", "stonehenge", "colosseum", "rio de janeiro", "canberra",
            "tokyo", "everest", "sahara", "danube", "pyrenees",
        ]),
        new("History",
        [
            "magna carta", "great fire", "revolution", "world war", "declaration of independence",
            "emperor", "renaissance", "ancient civilization", "pompeii", "roman empire", "century",
            "bc", "ad", "historical",
        ]),
    ];

    public static string Categorize(QuizQuestion question) =>
        Categorize(question.Question, question.Answers, question.Explanation);

    public static string Categorize(
        string question,
        IEnumerable<string>? answers = null,
        string? explanation = null)
    {
        var parts = new List<string> { question ?? "" };
        if (answers is not null)
            parts.AddRange(answers);
        if (!string.IsNullOrWhiteSpace(explanation))
            parts.Add(explanation);

        var text = " " + Normalize(string.Join(" ", parts)) + " ";
        var bestCategory = "General Knowledge";
        var bestScore = 0;

        foreach (var rule in Rules)
        {
            var score = 0;
            foreach (var keyword in rule.Keywords)
            {
                var normalizedKeyword = Normalize(keyword);
                if (normalizedKeyword.Length == 0)
                    continue;
                if (text.Contains(" " + normalizedKeyword + " ", StringComparison.Ordinal))
                    score += normalizedKeyword.Contains(' ') ? 3 : 1;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestCategory = rule.Category;
            }
        }

        return bestCategory;
    }

    private static string Normalize(string value)
    {
        var chars = (value ?? "")
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record TopicRule(string Category, string[] Keywords);
}
