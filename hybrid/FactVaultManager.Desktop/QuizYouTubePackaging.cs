using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record QuizYouTubePackagingVariant(
    string Key,
    string Purpose,
    string Title,
    QuizThumbnailSettings Thumbnail,
    string ThumbnailFileName);

public sealed record QuizYouTubePackagingResult(
    string ProjectFolder,
    string ManifestPath,
    IReadOnlyList<QuizYouTubePackagingVariant> Variants);

public static class QuizYouTubePackaging
{
    public const string ManifestFileName = "YouTube Packaging.json";
    public const string TitlesFileName = "YouTube Titles A-B-C.txt";

    public static bool Exists(string? projectFolder)
    {
        var folder = (projectFolder ?? "").Trim();
        if (folder.Length == 0)
            return false;
        try
        {
            return File.Exists(Path.Combine(Path.GetFullPath(folder), ManifestFileName));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static IReadOnlyList<QuizYouTubePackagingVariant> BuildVariants(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required for YouTube packaging.", nameof(questions));

        var topic = TopicName(metadata, questions);
        var topicUpper = topic.ToUpperInvariant();
        var perfect = $"{questions.Count}/{questions.Count}";
        var episode = metadata.EpisodeLabel;
        var series = metadata.SeriesName;

        var titles = new[]
        {
            LimitTitle($"{topic} Quiz: {questions.Count} Questions | {series} {episode}"),
            LimitTitle($"Can You Get {perfect}? | {series} {episode}"),
            LimitTitle($"Only {ExpertTopic(topic)} Experts Get {perfect} | {series} {episode}"),
        };
        EnsureDistinctTitles(titles, topic, perfect, episode);

        var subtitles = new[]
        {
            topicUpper,
            topicUpper,
            topicUpper,
        };
        var categoryHook = CategoryHook(topic);
        return
        [
            new QuizYouTubePackagingVariant(
                "A",
                "Score challenge",
                titles[0],
                new QuizThumbnailSettings($"CAN YOU GET {perfect}?", subtitles[0]).Normalize(),
                "Thumbnail A - Score.png"),
            new QuizYouTubePackagingVariant(
                "B",
                "Expert challenge",
                titles[1],
                new QuizThumbnailSettings("ONLY EXPERTS", subtitles[1]).Normalize(),
                "Thumbnail B - Experts.png"),
            new QuizYouTubePackagingVariant(
                "C",
                "Category challenge",
                titles[2],
                new QuizThumbnailSettings(categoryHook, subtitles[2]).Normalize(),
                "Thumbnail C - Category.png"),
        ];
    }

    public static QuizYouTubePackagingResult Write(
        string projectFolder,
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizVisualRenderSettings visual,
        string? logoPath,
        bool vertical = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (vertical)
            throw new InvalidOperationException("YouTube A/B packaging is intended for long-form 16:9 quizzes.");

        var folder = Path.GetFullPath(projectFolder.Trim());
        Directory.CreateDirectory(folder);
        var variants = BuildVariants(metadata, questions);
        var renderer = new QuizThumbnailRenderer();

        foreach (var variant in variants)
        {
            var bitmap = renderer.RenderPreview(
                metadata,
                questions,
                variant.Thumbnail,
                visual,
                logoPath,
                vertical: false);
            SavePng(bitmap, Path.Combine(folder, variant.ThumbnailFileName));
            WriteAtomic(Path.Combine(folder, $"YouTube Title {variant.Key}.txt"), variant.Title);
        }

        var titlesText = string.Join(
            Environment.NewLine + Environment.NewLine,
            variants.Select(variant => $"{variant.Key} — {variant.Purpose}{Environment.NewLine}{variant.Title}"));
        WriteAtomic(Path.Combine(folder, TitlesFileName), titlesText);

        var manifestPath = Path.Combine(folder, ManifestFileName);
        var manifest = JsonSerializer.Serialize(new
        {
            generated_at = DateTimeOffset.Now.ToString("O"),
            purpose = "YouTube Test & Compare title and thumbnail candidates",
            variants = variants.Select(variant => new
            {
                key = variant.Key,
                purpose = variant.Purpose,
                title = variant.Title,
                thumbnail = variant.ThumbnailFileName,
                thumbnail_headline = variant.Thumbnail.Headline,
                thumbnail_subtitle = variant.Thumbnail.Subtitle,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomic(manifestPath, manifest);

        return new QuizYouTubePackagingResult(folder, manifestPath, variants);
    }

    private static string TopicName(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions)
    {
        var categories = questions
            .Select(question => QuizQuestionCategoryNormalizer.Normalize(question.Category))
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (categories.Length == 1)
            return categories[0];
        return QuizPublishMetadataGenerator.DisplayName(metadata.SeriesName);
    }

    private static string ExpertTopic(string topic)
    {
        if (string.Equals(topic, "General Knowledge", StringComparison.OrdinalIgnoreCase))
            return "Quiz";
        if (string.Equals(topic, "Nature & Animals", StringComparison.OrdinalIgnoreCase))
            return "Nature";
        if (string.Equals(topic, "Arts & Literature", StringComparison.OrdinalIgnoreCase))
            return "Arts & Literature";
        return topic;
    }

    private static string CategoryHook(string topic)
    {
        if (QuizTypeCatalog.FromCategory(topic) == QuizTypeCatalog.Logo)
            return "NAME THESE LOGOS";
        if (string.Equals(topic, "Space", StringComparison.OrdinalIgnoreCase))
            return "SPACE IQ TEST";
        if (string.Equals(topic, "Technology", StringComparison.OrdinalIgnoreCase))
            return "TECH IQ TEST";
        if (string.Equals(topic, "Film", StringComparison.OrdinalIgnoreCase))
            return "MOVIE IQ TEST";
        if (string.Equals(topic, "Music", StringComparison.OrdinalIgnoreCase))
            return "MUSIC IQ TEST";
        if (string.Equals(topic, "Sports", StringComparison.OrdinalIgnoreCase))
            return "SPORTS IQ TEST";

        var candidate = $"{topic.ToUpperInvariant()} IQ TEST";
        return candidate.Length <= QuizThumbnailSettings.MaxHeadlineLength
            ? candidate
            : "HOW GOOD IS YOUR KNOWLEDGE?";
    }

    private static string LimitTitle(string value)
    {
        var text = string.Join(' ', (value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (text.Length <= QuizPublishMetadataGenerator.MaxTitleLength)
            return text;
        return text[..QuizPublishMetadataGenerator.MaxTitleLength].TrimEnd(' ', '|', '-', ':');
    }

    private static void EnsureDistinctTitles(string[] titles, string topic, string perfect, string episode)
    {
        for (var index = 1; index < titles.Length; index++)
        {
            if (!titles.Take(index).Contains(titles[index], StringComparer.OrdinalIgnoreCase))
                continue;
            titles[index] = index == 1
                ? LimitTitle($"Can You Beat This {topic} Quiz? | Score {perfect} {episode}")
                : LimitTitle($"{topic} Challenge: Only Experts Score {perfect} {episode}");
        }
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var temporary = path + ".tmp";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(stream);
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, (content ?? "").Trim() + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
