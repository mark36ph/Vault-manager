namespace FactVaultManager.Desktop;

public enum QuizPreflightSeverity
{
    Warning,
    Error,
}

public sealed record QuizPreflightIssue(
    QuizPreflightSeverity Severity,
    string Message,
    int? QuestionId = null);

public static class QuizPreflight
{
    public static IReadOnlyList<QuizPreflightIssue> Analyze(
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options,
        string quizType = QuizTypeCatalog.Standard)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(options);
        quizType = QuizTypeCatalog.Normalize(quizType);

        var issues = new List<QuizPreflightIssue>();
        try
        {
            options.Validate();
        }
        catch (Exception error)
        {
            issues.Add(new QuizPreflightIssue(QuizPreflightSeverity.Error, error.Message));
        }

        if (questions.Count == 0)
        {
            issues.Add(new QuizPreflightIssue(
                QuizPreflightSeverity.Error,
                "The quiz draft is empty. Add at least one question before export."));
            return issues;
        }

        var duplicateIds = questions
            .GroupBy(question => question.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var id in duplicateIds)
        {
            issues.Add(new QuizPreflightIssue(
                QuizPreflightSeverity.Error,
                $"Bank question #{id} appears more than once in the draft.",
                id));
        }

        var questionLimit = options.Vertical ? 105 : 155;
        var answerLimit = options.Vertical ? 58 : 82;
        var explanationLimit = options.Vertical ? 190 : 290;
        var titleLimit = options.Vertical ? 58 : 78;

        if (options.Title.Trim().Length > titleLimit)
        {
            issues.Add(new QuizPreflightIssue(
                QuizPreflightSeverity.Warning,
                $"The quiz title is long for {(options.Vertical ? "9:16" : "16:9")} and may wrap tightly in the intro card."));
        }

        foreach (var question in questions)
        {
            if (quizType == QuizTypeCatalog.Logo)
            {
                try
                {
                    QuizQuestionImage.ValidatePath(question.ImagePath, allowEmpty: false);
                }
                catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                {
                    issues.Add(new QuizPreflightIssue(
                        QuizPreflightSeverity.Error,
                        $"Question #{question.Id} needs a valid logo image: {error.Message}",
                        question.Id));
                }
            }

            if (question.Question.Trim().Length > questionLimit)
            {
                issues.Add(new QuizPreflightIssue(
                    QuizPreflightSeverity.Warning,
                    $"Question #{question.Id} is long ({question.Question.Trim().Length} characters) and may wrap tightly.",
                    question.Id));
            }

            for (var index = 0; index < question.Answers.Count; index++)
            {
                var answer = question.Answers[index].Trim();
                if (answer.Length > answerLimit)
                {
                    issues.Add(new QuizPreflightIssue(
                        QuizPreflightSeverity.Warning,
                        $"Question #{question.Id} answer {(char)('A' + index)} is long ({answer.Length} characters) and may need smaller text.",
                        question.Id));
                }
            }

            if (!string.IsNullOrWhiteSpace(question.Explanation) &&
                question.Explanation.Trim().Length > explanationLimit)
            {
                issues.Add(new QuizPreflightIssue(
                    QuizPreflightSeverity.Warning,
                    $"Question #{question.Id} has a long explanation ({question.Explanation.Trim().Length} characters) that may wrap tightly on the answer card.",
                    question.Id));
            }
        }

        return issues;
    }

    public static string Summary(IReadOnlyList<QuizPreflightIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0)
            return "Preflight passed — no layout warnings found.";

        var errors = issues.Count(issue => issue.Severity == QuizPreflightSeverity.Error);
        var warnings = issues.Count - errors;
        return errors > 0
            ? $"Preflight: {errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}."
            : $"Preflight: {warnings} warning{(warnings == 1 ? "" : "s")} — review before export.";
    }
}
