namespace FactVaultManager.Desktop;

public static class QuizRotationSelector
{
    public static IReadOnlyList<QuizQuestion> Select(
        IEnumerable<QuizQuestion> questions,
        int count,
        bool preferLeastUsed,
        IReadOnlySet<int>? recentlyUsedQuestionIds = null,
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
        recentlyUsedQuestionIds ??= new HashSet<int>();

        var ranked = pool
            .Select(question => new RankedQuestion(
                question,
                recentlyUsedQuestionIds.Contains(question.Id),
                preferLeastUsed ? question.TimesUsed : 0,
                random.NextDouble()))
            .OrderBy(item => item.WasRecentlyUsed)
            .ThenBy(item => item.UsageRank)
            .ThenBy(item => item.RandomRank)
            .Take(count)
            .Select(item => item.Question)
            .ToList();

        return ranked;
    }

    public static int CountRecentFallbacks(
        IEnumerable<QuizQuestion> selected,
        IReadOnlySet<int>? recentlyUsedQuestionIds)
    {
        if (recentlyUsedQuestionIds is null || recentlyUsedQuestionIds.Count == 0)
            return 0;
        return selected.Count(question => recentlyUsedQuestionIds.Contains(question.Id));
    }

    private sealed record RankedQuestion(
        QuizQuestion Question,
        bool WasRecentlyUsed,
        int UsageRank,
        double RandomRank);
}
