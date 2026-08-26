namespace FactVaultManager.Desktop;

public sealed record QuizThumbnailRecommendation(
    QuizQuestion Question,
    int QuestionNumber,
    double Score,
    string Hook,
    string Subtitle,
    string Badge,
    string Teaser,
    bool HasArtwork);

public static class QuizThumbnailIntelligence
{
    private static readonly string[] HighImpactTerms =
    [
        "largest", "smallest", "fastest", "slowest", "oldest", "youngest",
        "first", "last", "only", "most", "least", "highest", "lowest",
        "farthest", "closest", "deepest", "brightest", "deadliest", "rarest",
        "biggest", "longest", "shortest", "strongest", "weakest", "never",
        "impossible", "record", "extreme"
    ];

    public static QuizThumbnailRecommendation Recommend(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        bool logoQuiz = false)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required for thumbnail intelligence.", nameof(questions));

        var ranked = questions
            .Select((question, index) => new
            {
                Question = question,
                Index = index,
                Score = Score(question, index, questions.Count, logoQuiz),
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Index)
            .ThenBy(item => item.Question.Id)
            .First();

        var difficulty = QuizDifficultyCatalog.Normalize(ranked.Question.Difficulty);
        var category = QuizQuestionCategoryNormalizer.Normalize(ranked.Question.Category);
        var hook = HookFor(ranked.Question, difficulty, category, logoQuiz);
        var subtitle = SubtitleFor(metadata, ranked.Question, difficulty, logoQuiz);
        var badge = BadgeFor(ranked.Question, difficulty, logoQuiz);
        var teaser = TeaserFor(ranked.Question.Question);
        var hasArtwork = !string.IsNullOrWhiteSpace(ranked.Question.ImagePath) && File.Exists(ranked.Question.ImagePath);

        return new QuizThumbnailRecommendation(
            ranked.Question,
            ranked.Index + 1,
            ranked.Score,
            hook,
            subtitle,
            badge,
            teaser,
            hasArtwork);
    }

    public static string DefaultHook(int questionCount, bool logoQuiz)
    {
        if (questionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(questionCount));
        if (logoQuiz)
            return "NAME THIS LOGO";
        if (questionCount >= 10)
            return "FINAL BOSS QUESTION";
        if (questionCount >= 5)
            return "ONLY EXPERTS?";
        return "CAN YOU SOLVE IT?";
    }

    public static double Score(
        QuizQuestion question,
        int index,
        int total,
        bool logoQuiz = false)
    {
        ArgumentNullException.ThrowIfNull(question);
        var difficulty = QuizDifficultyCatalog.Parse(question.Difficulty);
        double score = difficulty switch
        {
            QuizDifficulty.Insane => 100,
            QuizDifficulty.Hard => 58,
            QuizDifficulty.Medium => 24,
            _ => 8,
        };

        if (!string.IsNullOrWhiteSpace(question.ImagePath))
            score += logoQuiz ? 90 : 34;
        if (QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            score += 45;

        var text = (question.Question ?? "").ToLowerInvariant();
        var impactHits = HighImpactTerms.Count(term => text.Contains(term, StringComparison.Ordinal));
        score += Math.Min(impactHits, 3) * 8;
        if (text.StartsWith("which ", StringComparison.Ordinal) ||
            text.StartsWith("what ", StringComparison.Ordinal) ||
            text.StartsWith("who ", StringComparison.Ordinal))
        {
            score += 5;
        }
        if (text.Any(char.IsDigit))
            score += 5;
        if (question.Answers.Any(answer => (answer ?? "").Any(char.IsDigit)))
            score += 5;

        var normalizedCategory = QuizQuestionCategoryNormalizer.Normalize(question.Category);
        if (string.Equals(normalizedCategory, "Space", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedCategory, "Technology", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedCategory, "Nature & Animals", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (total > 1)
            score += Math.Clamp(index / (double)(total - 1), 0, 1) * 3;
        return score;
    }

    private static string HookFor(
        QuizQuestion question,
        string difficulty,
        string category,
        bool logoQuiz)
    {
        if (logoQuiz || QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            return "NAME THIS LOGO";
        if (string.Equals(difficulty, "insane", StringComparison.OrdinalIgnoreCase))
            return "FINAL BOSS QUESTION";
        if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
            return "HARDER THAN IT LOOKS";
        if (string.Equals(category, "Space", StringComparison.OrdinalIgnoreCase))
            return "SPACE IQ TEST";
        if (string.Equals(category, "Technology", StringComparison.OrdinalIgnoreCase))
            return "TECH IQ TEST";
        return "CAN YOU SOLVE IT?";
    }

    private static string SubtitleFor(
        QuizPublishMetadata metadata,
        QuizQuestion question,
        string difficulty,
        bool logoQuiz)
    {
        if (logoQuiz || QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            return "LOGOS";

        var category = QuizQuestionCategoryNormalizer.Normalize(question.Category).ToUpperInvariant();
        var difficultyLabel = difficulty.ToUpperInvariant();
        if (category.Length > 0 && difficultyLabel.Length > 0)
            return $"{category} • {difficultyLabel}";

        return QuizPublishMetadataGenerator.DisplayName(metadata.SeriesName).ToUpperInvariant();
    }

    private static string BadgeFor(QuizQuestion question, string difficulty, bool logoQuiz)
    {
        if (logoQuiz || QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            return "LOGO CHALLENGE";
        return string.Equals(difficulty, "insane", StringComparison.OrdinalIgnoreCase)
            ? "ROUND 4 • INSANE"
            : $"{difficulty.ToUpperInvariant()} QUESTION";
    }

    private static string TeaserFor(string value)
    {
        var text = (value ?? "").Trim();
        if (text.Length <= 76)
            return text;
        return text[..73].TrimEnd() + "...";
    }
}
