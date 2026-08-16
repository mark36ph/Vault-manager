namespace FactVaultManager.Desktop;

public static class QuizDraftOperations
{
    public const int MaximumQuestions = 100;

    public static IReadOnlyList<QuizQuestion> Move(
        IReadOnlyList<QuizQuestion> questions,
        int questionId,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var list = questions.ToList();
        var index = list.FindIndex(question => question.Id == questionId);
        if (index < 0)
            throw new InvalidOperationException("The selected question is no longer in the quiz draft.");
        if (list.Count < 2 || offset == 0)
            return list;

        var target = Math.Clamp(index + offset, 0, list.Count - 1);
        if (target == index)
            return list;

        (list[index], list[target]) = (list[target], list[index]);
        return list;
    }

    public static IReadOnlyList<QuizQuestion> Remove(
        IReadOnlyList<QuizQuestion> questions,
        int questionId)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var list = questions.ToList();
        var removed = list.RemoveAll(question => question.Id == questionId);
        if (removed == 0)
            throw new InvalidOperationException("The selected question is no longer in the quiz draft.");
        return list;
    }

    public static IReadOnlyList<QuizQuestion> Replace(
        IReadOnlyList<QuizQuestion> questions,
        int questionId,
        QuizQuestion replacement)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!replacement.IsEnabled)
            throw new InvalidOperationException("Disabled questions cannot be added to a quiz draft.");

        var list = questions.ToList();
        var index = list.FindIndex(question => question.Id == questionId);
        if (index < 0)
            throw new InvalidOperationException("The selected question is no longer in the quiz draft.");
        if (list.Any(question => question.Id == replacement.Id && question.Id != questionId))
            throw new InvalidOperationException("That question is already in the quiz draft.");

        list[index] = replacement;
        return list;
    }

    public static IReadOnlyList<QuizQuestion> Add(
        IReadOnlyList<QuizQuestion> questions,
        QuizQuestion question)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(question);
        if (!question.IsEnabled)
            throw new InvalidOperationException("Disabled questions cannot be added to a quiz draft.");
        if (questions.Count >= MaximumQuestions)
            throw new InvalidOperationException($"A quiz draft can contain at most {MaximumQuestions} questions.");
        if (questions.Any(existing => existing.Id == question.Id))
            throw new InvalidOperationException("That question is already in the quiz draft.");

        var list = questions.ToList();
        list.Add(question);
        return list;
    }
}

public static class QuizAnswerShuffler
{
    public static IReadOnlyList<QuizQuestion> Shuffle(
        IReadOnlyList<QuizQuestion> questions,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        random ??= Random.Shared;
        return questions.Select(question => ShuffleQuestion(question, random)).ToList();
    }

    public static QuizQuestion ShuffleQuestion(QuizQuestion question, Random random)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(random);

        var answers = question.Answers
            .Select((answer, originalIndex) => (Answer: answer, OriginalIndex: originalIndex))
            .ToArray();

        for (var index = answers.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (answers[index], answers[swap]) = (answers[swap], answers[index]);
        }

        var correctIndex = Array.FindIndex(answers, answer => answer.OriginalIndex == question.CorrectIndex);
        if (correctIndex < 0)
            throw new InvalidOperationException("Could not preserve the correct answer while shuffling.");

        return question with
        {
            OptionA = answers[0].Answer,
            OptionB = answers[1].Answer,
            OptionC = answers[2].Answer,
            OptionD = answers[3].Answer,
            CorrectIndex = correctIndex,
        };
    }
}
