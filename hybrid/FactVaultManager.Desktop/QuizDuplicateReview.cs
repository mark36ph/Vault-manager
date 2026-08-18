namespace FactVaultManager.Desktop;

public sealed record QuizDuplicateCandidate(
    int KeepId,
    string KeepQuestion,
    string KeepCategory,
    int DuplicateId,
    string DuplicateQuestion,
    string DuplicateCategory,
    string CorrectAnswer,
    string MatchType)
{
    public bool IsSelected { get; set; }
}

public static class QuizDuplicateReview
{
    public static IReadOnlyList<QuizDuplicateCandidate> FindCandidates(IEnumerable<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var ordered = questions
            .Where(question => question.Id > 0)
            .OrderBy(question => question.Id)
            .ToList();
        var claimedDuplicates = new HashSet<int>();
        var results = new List<QuizDuplicateCandidate>();

        for (var firstIndex = 0; firstIndex < ordered.Count; firstIndex++)
        {
            var first = ordered[firstIndex];
            if (claimedDuplicates.Contains(first.Id))
                continue;

            for (var secondIndex = firstIndex + 1; secondIndex < ordered.Count; secondIndex++)
            {
                var second = ordered[secondIndex];
                if (claimedDuplicates.Contains(second.Id))
                    continue;

                if (!QuizQuestionDuplicateDetector.IsLikelyDuplicate(
                        first.Question,
                        first.CorrectAnswer,
                        second.Question,
                        second.CorrectAnswer))
                {
                    continue;
                }

                var exact = string.Equals(
                    QuizQuestionDuplicateKey.Create(first.Question),
                    QuizQuestionDuplicateKey.Create(second.Question),
                    StringComparison.Ordinal);

                results.Add(new QuizDuplicateCandidate(
                    first.Id,
                    first.Question,
                    first.Category,
                    second.Id,
                    second.Question,
                    second.Category,
                    first.CorrectAnswer,
                    exact ? "Same wording" : "Reworded"));
                claimedDuplicates.Add(second.Id);
            }
        }

        return results;
    }
}
