namespace FactVaultManager.Desktop;

public static class QuizQuestionDisplayOrder
{
    public static IReadOnlyList<QuizQuestion> Preserve(
        IEnumerable<QuizQuestion> currentQuestions,
        IEnumerable<int> previousQuestionIds)
    {
        var current = currentQuestions.ToList();
        var positions = previousQuestionIds
            .Where(id => id > 0)
            .Select((id, index) => new { id, index })
            .GroupBy(item => item.id)
            .ToDictionary(group => group.Key, group => group.First().index);

        if (positions.Count == 0 || current.Count <= 1)
            return current;

        return current
            .OrderBy(question => positions.TryGetValue(question.Id, out var index) ? index : int.MaxValue)
            .ThenBy(question => question.Id)
            .ToList();
    }
}
