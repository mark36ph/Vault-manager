using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizPublishMetadata(
    string SeriesName,
    int EpisodeNumber,
    string YouTubeTitle,
    string Description,
    string Hashtags,
    string PinnedComment)
{
    public string EpisodeLabel => EpisodeNumber > 0 ? $"#{EpisodeNumber:000}" : "";
}

public static class QuizPublishMetadataGenerator
{
    public const int MaxTitleLength = 100;
    public const int MaxDescriptionLength = 5_000;
    public const int MaxHashtagsLength = 500;
    public const int MaxPinnedCommentLength = 10_000;

    public static string SuggestSeriesName(string? selectedCategory)
    {
        var category = (selectedCategory ?? "").Trim();
        if (category.Length == 0 || category.StartsWith("All ", StringComparison.OrdinalIgnoreCase))
            return "General Knowledge Quiz";
        if (category.EndsWith("Quiz", StringComparison.OrdinalIgnoreCase) ||
            category.EndsWith("Trivia", StringComparison.OrdinalIgnoreCase) ||
            category.EndsWith("Challenge", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeSeriesName(category);
        }
        return NormalizeSeriesName($"{category} Quiz");
    }

    public static QuizPublishMetadata Generate(
        string? seriesName,
        int episodeNumber,
        IReadOnlyList<QuizQuestion> questions,
        bool vertical)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required to generate publishing metadata.", nameof(questions));

        var series = NormalizeSeriesName(seriesName);
        ValidateEpisode(episodeNumber);

        var categories = questions
            .Select(question => (question.Category ?? "").Trim())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var episode = $"#{episodeNumber:000}";
        var formatLabel = vertical ? "Quiz Shorts" : "Quiz";
        var title = Limit($"{series} {episode} | {questions.Count} Question {formatLabel}", MaxTitleLength);

        var categoryText = categories.Length == 0
            ? "Mixed topics"
            : string.Join(", ", categories);
        var hashtags = BuildHashtags(categories, vertical);
        var description = Limit(
            $"Test your knowledge with {questions.Count} questions in {series} {episode}.\n\n" +
            $"Categories: {categoryText}\n\n" +
            "Keep track of your score as you go, then share your result in the comments.\n\n" +
            hashtags,
            MaxDescriptionLength);
        var pinned = Limit(
            $"How many did you get right out of {questions.Count}? Share your score below. {series} {episode}",
            MaxPinnedCommentLength);

        return Validate(new QuizPublishMetadata(series, episodeNumber, title, description, hashtags, pinned));
    }

    public static QuizPublishMetadata Validate(QuizPublishMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var series = NormalizeSeriesName(metadata.SeriesName);
        ValidateEpisode(metadata.EpisodeNumber);
        var title = Required(metadata.YouTubeTitle, "YouTube title", MaxTitleLength);
        var description = Required(metadata.Description, "YouTube description", MaxDescriptionLength);
        var hashtags = NormalizeHashtags(metadata.Hashtags);
        if (hashtags.Length > MaxHashtagsLength)
            throw new ArgumentException($"YouTube hashtags must be {MaxHashtagsLength} characters or fewer.", nameof(metadata));
        var pinned = Required(metadata.PinnedComment, "Pinned comment", MaxPinnedCommentLength);
        return new QuizPublishMetadata(series, metadata.EpisodeNumber, title, description, hashtags, pinned);
    }

    public static string NormalizeSeriesName(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            text = "General Knowledge Quiz";
        if (text.Length > 100)
            throw new ArgumentException("Quiz series name must be 100 characters or fewer.", nameof(value));
        return text;
    }

    private static void ValidateEpisode(int episodeNumber)
    {
        if (episodeNumber is < 1 or > 9_999)
            throw new ArgumentOutOfRangeException(nameof(episodeNumber), "Episode number must be between 1 and 9999.");
    }

    private static string BuildHashtags(IEnumerable<string> categories, bool vertical)
    {
        var tags = new List<string> { "#Quiz", "#Trivia" };
        foreach (var category in categories)
        {
            var slug = new string(category.Where(char.IsLetterOrDigit).ToArray());
            if (slug.Length > 1)
                tags.Add("#" + slug);
        }
        tags.Add("#GeneralKnowledge");
        if (vertical)
            tags.Add("#Shorts");

        return NormalizeHashtags(string.Join(' ', tags));
    }

    private static string NormalizeHashtags(string? value)
    {
        var tokens = (value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.StartsWith('#') ? token : "#" + token)
            .Select(token => "#" + new string(token[1..].Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tokens.Length == 0)
            throw new ArgumentException("Add at least one YouTube hashtag.", nameof(value));
        return string.Join(' ', tokens);
    }

    private static string Required(string? value, string label, int maxLength)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException($"{label} is required.");
        if (text.Length > maxLength)
            throw new ArgumentException($"{label} must be {maxLength} characters or fewer.");
        return text;
    }

    private static string Limit(string value, int maxLength)
    {
        value = value.Trim();
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength].TrimEnd();
    }
}

public static class QuizPublishMetadataFiles
{
    public static string Write(string projectFolder, QuizPublishMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("Quiz project folder is required.", nameof(projectFolder));
        metadata = QuizPublishMetadataGenerator.Validate(metadata);

        var folder = Path.GetFullPath(projectFolder.Trim());
        Directory.CreateDirectory(folder);
        WriteAtomic(Path.Combine(folder, "YouTube Title.txt"), metadata.YouTubeTitle);
        WriteAtomic(Path.Combine(folder, "Description.txt"), metadata.Description);
        WriteAtomic(Path.Combine(folder, "Hashtags.txt"), metadata.Hashtags);
        WriteAtomic(Path.Combine(folder, "Pinned Comment.txt"), metadata.PinnedComment);

        var jsonPath = Path.Combine(folder, "Publish Metadata.json");
        var json = JsonSerializer.Serialize(new
        {
            series = metadata.SeriesName,
            episode = metadata.EpisodeNumber,
            youtube_title = metadata.YouTubeTitle,
            description = metadata.Description,
            hashtags = metadata.Hashtags,
            pinned_comment = metadata.PinnedComment,
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomic(jsonPath, json);
        return jsonPath;
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content.Trim() + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
