using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizHistoricalThumbnailPlan(
    string ProjectFolder,
    QuizPublishMetadata Metadata,
    IReadOnlyList<QuizQuestion> Questions,
    QuizThumbnailSettings Thumbnail,
    QuizVisualRenderSettings Visual,
    string LogoPath,
    bool Vertical,
    QuizThumbnailRecommendation Recommendation);

public sealed record QuizHistoricalThumbnailResult(
    int HistoryId,
    string ThumbnailPath,
    int QuestionCount,
    int FeaturedQuestionNumber,
    string Hook);

public static class QuizHistoricalThumbnailRegenerator
{
    public static bool IsBatchEligible(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return string.Equals(history.VideoType, "Video", StringComparison.Ordinal);
    }

    public static QuizHistoricalThumbnailPlan BuildPlan(
        QuizHistorySummary history,
        IReadOnlyList<QuizHistoryQuestion> historyQuestions,
        Func<int, QuizQuestion?> questionLookup,
        string? currentLogoPath = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(historyQuestions);
        ArgumentNullException.ThrowIfNull(questionLookup);

        var projectFolder = NormalizeProjectFolder(history.ProjectFolder);
        JsonDocument? quizDocument = null;
        try
        {
            quizDocument = TryLoadJson(Path.Combine(projectFolder, "quiz.json"));
            var questions = LoadQuestions(quizDocument?.RootElement, projectFolder, historyQuestions, questionLookup);
            if (questions.Count == 0)
                throw new InvalidDataException("No saved quiz questions were available to regenerate the thumbnail.");

            var metadata = LoadMetadata(projectFolder, history, questions);
            var vertical = string.Equals(history.Format.Trim(), "9:16", StringComparison.OrdinalIgnoreCase);
            var visual = LoadVisualSettings(quizDocument?.RootElement, questions);
            var logoPath = NormalizeOptionalFile(currentLogoPath);
            var logoQuiz = string.Equals(visual.QuizType, QuizTypeCatalog.Logo, StringComparison.OrdinalIgnoreCase);
            var recommendation = QuizThumbnailIntelligence.Recommend(metadata, questions, logoQuiz);
            var thumbnail = new QuizThumbnailSettings(recommendation.Hook, recommendation.Subtitle).Normalize();

            return new QuizHistoricalThumbnailPlan(
                projectFolder,
                metadata,
                questions,
                thumbnail,
                visual,
                logoPath,
                vertical,
                recommendation);
        }
        finally
        {
            quizDocument?.Dispose();
        }
    }

    public static QuizHistoricalThumbnailResult Regenerate(
        QuizHistorySummary history,
        IReadOnlyList<QuizHistoryQuestion> historyQuestions,
        Func<int, QuizQuestion?> questionLookup,
        string? currentLogoPath = null)
    {
        var plan = BuildPlan(history, historyQuestions, questionLookup, currentLogoPath);
        var path = new QuizThumbnailRenderer().Write(
            plan.ProjectFolder,
            plan.Metadata,
            plan.Questions,
            plan.Thumbnail,
            plan.Visual,
            plan.LogoPath,
            plan.Vertical);

        return new QuizHistoricalThumbnailResult(
            history.Id,
            path,
            plan.Questions.Count,
            plan.Recommendation.QuestionNumber,
            plan.Recommendation.Hook);
    }

    private static string NormalizeProjectFolder(string? value)
    {
        var folder = (value ?? "").Trim();
        if (folder.Length == 0)
            throw new DirectoryNotFoundException("This Quiz History entry does not have a saved project folder.");
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"The saved quiz project folder could not be found: {folder}");
        return folder;
    }

    private static IReadOnlyList<QuizQuestion> LoadQuestions(
        JsonElement? quizRoot,
        string projectFolder,
        IReadOnlyList<QuizHistoryQuestion> historyQuestions,
        Func<int, QuizQuestion?> questionLookup)
    {
        var saved = LoadSavedQuestions(quizRoot, projectFolder, questionLookup);
        if (saved.Count > 0)
            return saved;

        return historyQuestions
            .OrderBy(item => item.Position)
            .Select(item => FromHistory(item, questionLookup(item.QuestionId)))
            .ToList();
    }

    private static IReadOnlyList<QuizQuestion> LoadSavedQuestions(
        JsonElement? quizRoot,
        string projectFolder,
        Func<int, QuizQuestion?> questionLookup)
    {
        if (quizRoot is not JsonElement root || root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("questions", out var questionsElement) ||
            questionsElement.ValueKind != JsonValueKind.Array)
            return [];

        var saved = new List<(int Number, int Index, QuizQuestion Question)>();
        var index = 0;
        foreach (var element in questionsElement.EnumerateArray())
        {
            index++;
            if (element.ValueKind != JsonValueKind.Object)
                continue;
            var id = Int(element, "id", 0);
            var bankQuestion = id > 0 ? questionLookup(id) : null;
            var questionText = Text(element, "question", bankQuestion?.Question ?? "");
            if (questionText.Length == 0)
                continue;

            var answers = ReadAnswers(element, bankQuestion);
            var imagePath = ResolveSavedImagePath(element, projectFolder);
            if (imagePath.Length == 0)
                imagePath = NormalizeOptionalFile(bankQuestion?.ImagePath);

            var question = new QuizQuestion(
                id > 0 ? id : -(10_000 + index),
                questionText,
                answers[0],
                answers[1],
                answers[2],
                answers[3],
                Math.Clamp(Int(element, "correct_index", bankQuestion?.CorrectIndex ?? 0), 0, 3),
                Text(element, "explanation", bankQuestion?.Explanation ?? ""),
                Text(element, "category", bankQuestion?.Category ?? "General Knowledge"),
                Text(element, "difficulty", bankQuestion?.Difficulty ?? "medium"),
                "Saved quiz project",
                bankQuestion?.TimesUsed ?? 0,
                true,
                imagePath);
            saved.Add((Math.Max(1, Int(element, "number", index)), index, question));
        }

        return saved
            .OrderBy(item => item.Number)
            .ThenBy(item => item.Index)
            .Select(item => item.Question)
            .ToList();
    }

    private static string[] ReadAnswers(JsonElement element, QuizQuestion? bankQuestion)
    {
        if (element.TryGetProperty("answers", out var answersElement) && answersElement.ValueKind == JsonValueKind.Array)
        {
            var answers = answersElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? "" : "")
                .Take(4)
                .ToArray();
            if (answers.Length == 4 && answers.All(answer => answer.Length > 0) &&
                answers.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4)
                return answers;
        }

        if (bankQuestion is not null && bankQuestion.Answers.Count == 4 &&
            bankQuestion.Answers.All(answer => !string.IsNullOrWhiteSpace(answer)))
            return bankQuestion.Answers.ToArray();

        return ["Answer A", "Answer B", "Answer C", "Answer D"];
    }

    private static QuizQuestion FromHistory(QuizHistoryQuestion history, QuizQuestion? bankQuestion)
    {
        if (bankQuestion is not null)
        {
            return bankQuestion with
            {
                Question = history.Question.Trim().Length > 0 ? history.Question.Trim() : bankQuestion.Question,
                Category = history.Category.Trim().Length > 0 ? history.Category.Trim() : bankQuestion.Category,
                Difficulty = history.Difficulty.Trim().Length > 0 ? history.Difficulty.Trim() : bankQuestion.Difficulty,
                ImagePath = NormalizeOptionalFile(bankQuestion.ImagePath),
            };
        }

        return new QuizQuestion(
            history.QuestionId > 0 ? history.QuestionId : -(20_000 + history.Position),
            history.Question.Trim().Length > 0 ? history.Question.Trim() : $"Question {history.Position}",
            "Answer A",
            "Answer B",
            "Answer C",
            "Answer D",
            0,
            "",
            history.Category.Trim().Length > 0 ? history.Category.Trim() : "General Knowledge",
            history.Difficulty.Trim().Length > 0 ? history.Difficulty.Trim() : "medium",
            "Quiz History recovery",
            0,
            true,
            "");
    }

    private static QuizPublishMetadata LoadMetadata(
        string projectFolder,
        QuizHistorySummary history,
        IReadOnlyList<QuizQuestion> questions)
    {
        var metadataPath = Path.Combine(projectFolder, "Publish Metadata.json");
        using var document = TryLoadJson(metadataPath);
        var root = document?.RootElement;

        var series = root is JsonElement metadataRoot
            ? Text(metadataRoot, "series", history.SeriesName)
            : history.SeriesName;
        if (string.IsNullOrWhiteSpace(series))
            series = QuizPublishMetadataGenerator.SuggestSeriesNameForQuestions(questions);

        var episode = root is JsonElement episodeRoot
            ? Int(episodeRoot, "episode", history.EpisodeNumber)
            : history.EpisodeNumber;
        if (episode is < 1 or > 9_999)
            episode = 1;

        var youtubeTitle = root is JsonElement titleRoot
            ? Text(titleRoot, "youtube_title", history.YouTubeTitle)
            : history.YouTubeTitle;
        if (string.IsNullOrWhiteSpace(youtubeTitle))
            youtubeTitle = string.IsNullOrWhiteSpace(history.Title) ? series : history.Title.Trim();

        var description = root is JsonElement descriptionRoot
            ? Text(descriptionRoot, "description", history.YouTubeDescription)
            : history.YouTubeDescription;
        if (string.IsNullOrWhiteSpace(description))
            description = $"Factburst quiz with {questions.Count} {(questions.Count == 1 ? "question" : "questions")}.";

        var hashtags = root is JsonElement hashtagRoot
            ? Text(hashtagRoot, "hashtags", history.Hashtags)
            : history.Hashtags;
        if (string.IsNullOrWhiteSpace(hashtags))
            hashtags = "#Quiz #Trivia";

        var pinnedComment = root is JsonElement pinnedRoot
            ? Text(pinnedRoot, "pinned_comment", history.PinnedComment)
            : history.PinnedComment;
        if (string.IsNullOrWhiteSpace(pinnedComment))
            pinnedComment = "Share your score in the comments.";

        return QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
            series,
            episode,
            TrimTo(youtubeTitle, QuizPublishMetadataGenerator.MaxTitleLength),
            TrimTo(description, QuizPublishMetadataGenerator.MaxDescriptionLength),
            TrimTo(hashtags, QuizPublishMetadataGenerator.MaxHashtagsLength),
            TrimTo(pinnedComment, QuizPublishMetadataGenerator.MaxPinnedCommentLength)));
    }

    private static QuizVisualRenderSettings LoadVisualSettings(
        JsonElement? quizRoot,
        IReadOnlyList<QuizQuestion> questions)
    {
        var inferredType = questions.Count > 0 && questions.All(question =>
            QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            ? QuizTypeCatalog.Logo
            : QuizTypeCatalog.Standard;

        if (quizRoot is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            return new QuizVisualRenderSettings(QuizType: inferredType).Normalize();

        var requestedType = QuizTypeCatalog.Normalize(Text(root, "quiz_type", inferredType));
        return new QuizVisualRenderSettings(
            Text(root, "theme", "dark"),
            Text(root, "logo_position", "Bottom right"),
            Double(root, "logo_scale", 1.0),
            requestedType).Normalize();
    }

    private static string ResolveSavedImagePath(JsonElement question, string projectFolder)
    {
        var value = Text(question, "image_path", "");
        if (value.Length == 0)
            return "";
        try
        {
            var path = Path.IsPathRooted(value) ? value : Path.Combine(projectFolder, value);
            return NormalizeOptionalFile(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private static string NormalizeOptionalFile(string? value)
    {
        var path = (value ?? "").Trim();
        if (path.Length == 0)
            return "";
        try
        {
            path = Path.GetFullPath(path);
            return File.Exists(path) ? path : "";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private static JsonDocument? TryLoadJson(string path)
    {
        try
        {
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string property, string fallback)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return (fallback ?? "").Trim();
        return value.GetString()?.Trim() ?? (fallback ?? "").Trim();
    }

    private static int Int(JsonElement element, string property, int fallback)
    {
        if (!element.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
            return result;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result)
            ? result
            : fallback;
    }

    private static double Double(JsonElement element, string property, double fallback)
    {
        if (!element.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result))
            return result;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out result)
            ? result
            : fallback;
    }

    private static string TrimTo(string value, int maximum)
    {
        value = (value ?? "").Trim();
        return value.Length <= maximum ? value : value[..maximum].TrimEnd();
    }
}
