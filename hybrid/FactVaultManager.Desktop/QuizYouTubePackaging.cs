using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record QuizYouTubePackagingVariant(
    string Key,
    string Purpose,
    string Title,
    QuizThumbnailSettings Thumbnail,
    QuizYouTubeThumbnailLayout Layout,
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

        // A/B/C retain three different click hypotheses, while the wording rotates by
        // topic + episode so a batch does not look like 15 copies of the same title.
        var titles = BuildClickTitles(metadata, topic, questions.Count);
        EnsureDistinctTitles(titles, topic, perfect, episode);

        var expertLabel = ExpertThumbnailTopic(topic);
        var categoryLabel = CategoryQuizLabel(topic);
        return
        [
            new QuizYouTubePackagingVariant(
                "A",
                "Score challenge: direct score hook + featured question preview",
                titles[0],
                new QuizThumbnailSettings($"CAN YOU GET {perfect}?", topicUpper).Normalize(),
                QuizYouTubeThumbnailLayout.ScoreChallenge,
                "Thumbnail A - Score.png"),
            new QuizYouTubePackagingVariant(
                "B",
                "Expert challenge: identity/exclusivity hook + oversized challenge visual",
                titles[1],
                new QuizThumbnailSettings($"ONLY {expertLabel} EXPERTS", "PROVE IT").Normalize(),
                QuizYouTubeThumbnailLayout.ExpertChallenge,
                "Thumbnail B - Experts.png"),
            new QuizYouTubePackagingVariant(
                "C",
                "Category/search: category-first quiz packaging + clean question cluster",
                titles[2],
                new QuizThumbnailSettings(categoryLabel, $"{questions.Count} QUESTION CHALLENGE").Normalize(),
                QuizYouTubeThumbnailLayout.CategorySearch,
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
        var renderer = new QuizYouTubePackagingThumbnailRenderer();

        foreach (var variant in variants)
        {
            var bitmap = renderer.Render(
                metadata,
                questions,
                variant.Thumbnail,
                visual,
                variant.Layout,
                logoPath);
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
                thumbnail_layout = variant.Layout.ToString(),
                thumbnail_headline = variant.Thumbnail.Headline,
                thumbnail_subtitle = variant.Thumbnail.Subtitle,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomic(manifestPath, manifest);

        return new QuizYouTubePackagingResult(folder, manifestPath, variants);
    }

    private static string[] BuildClickTitles(QuizPublishMetadata metadata, string topic, int questionCount)
    {
        var perfect = $"{questionCount}/{questionCount}";
        var threshold = Math.Max(1, questionCount - 2);
        var episode = EpisodeSuffix(metadata.EpisodeLabel);
        var expert = ExpertTopic(topic);

        var scorePattern = TitlePatternIndex(metadata, topic, 0);
        var expertPattern = TitlePatternIndex(metadata, topic, 2);
        var searchPattern = TitlePatternIndex(metadata, topic, 4);

        var scoreTitle = scorePattern switch
        {
            0 => $"Can You Get {perfect}? | {topic} Quiz{episode}",
            1 => $"How Many Can You Get Right? | {topic} Quiz{episode}",
            2 => $"Can You Ace All {questionCount}? | {topic} Quiz{episode}",
            3 => $"{questionCount} Questions. Can You Get a Perfect Score? | {topic}{episode}",
            4 => $"What's Your Score Out of {questionCount}? | {topic} Quiz{episode}",
            _ => $"Can You Beat {threshold}/{questionCount}? | {topic} Quiz{episode}",
        };

        var expertTitle = expertPattern switch
        {
            0 => $"Think You're a {expert} Expert? Prove It | {topic}{episode}",
            1 => $"Are You a Real {expert} Expert? | {questionCount}-Question Challenge{episode}",
            2 => $"{expert} Fans: Can You Score {perfect}? | {topic}{episode}",
            3 => $"How Strong Is Your {topic} Knowledge? | {questionCount} Questions{episode}",
            4 => $"Know {topic}? This Quiz Will Test You | {questionCount} Questions{episode}",
            _ => $"{expert} Expert or Lucky Guesser? Try These {questionCount} Questions{episode}",
        };

        var searchTitle = searchPattern switch
        {
            0 => $"{topic} Quiz: {questionCount} Questions to Test Your Knowledge{episode}",
            1 => $"{questionCount} {topic} Questions — How Many Can You Answer?{episode}",
            2 => $"{topic} Trivia Challenge: {questionCount} Questions{episode}",
            3 => $"Test Your {topic} Knowledge: {questionCount}-Question Quiz{episode}",
            4 => $"{topic} Quiz Challenge — {questionCount} Questions, Keep Score{episode}",
            _ => $"Ultimate {topic} Quiz: {questionCount} Questions{episode}",
        };

        return
        [
            LimitTitle(scoreTitle),
            LimitTitle(expertTitle),
            LimitTitle(searchTitle),
        ];
    }

    private static int TitlePatternIndex(QuizPublishMetadata metadata, string topic, int offset)
    {
        const int patternCount = 6;
        var episodeNumber = ParseEpisodeNumber(metadata.EpisodeLabel);
        var topicOffset = StableTopicOffset(topic);
        return (episodeNumber + topicOffset + offset) % patternCount;
    }

    private static int ParseEpisodeNumber(string? episodeLabel)
    {
        var digits = new string((episodeLabel ?? "").Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? Math.Max(0, value) : 0;
    }

    private static int StableTopicOffset(string topic)
    {
        var hash = 17;
        foreach (var character in (topic ?? "").ToUpperInvariant())
            hash = unchecked((hash * 31) + character);
        return (hash & int.MaxValue) % 97;
    }

    private static string EpisodeSuffix(string? episodeLabel)
    {
        var episode = (episodeLabel ?? "").Trim();
        return episode.Length == 0 ? "" : " " + episode;
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
            return "Trivia";
        if (string.Equals(topic, "Nature & Animals", StringComparison.OrdinalIgnoreCase))
            return "Nature";
        if (string.Equals(topic, "Arts & Literature", StringComparison.OrdinalIgnoreCase))
            return "Arts & Literature";
        if (string.Equals(topic, "Mathematics", StringComparison.OrdinalIgnoreCase))
            return "Math";
        if (string.Equals(topic, "Film", StringComparison.OrdinalIgnoreCase))
            return "Movie";
        return topic;
    }

    private static string ExpertThumbnailTopic(string topic)
    {
        if (string.Equals(topic, "General Knowledge", StringComparison.OrdinalIgnoreCase))
            return "TRIVIA";
        if (string.Equals(topic, "Nature & Animals", StringComparison.OrdinalIgnoreCase))
            return "NATURE";
        if (string.Equals(topic, "Arts & Literature", StringComparison.OrdinalIgnoreCase))
            return "ARTS & LIT";
        if (string.Equals(topic, "Mathematics", StringComparison.OrdinalIgnoreCase))
            return "MATH";
        if (string.Equals(topic, "Film", StringComparison.OrdinalIgnoreCase))
            return "MOVIE";
        if (string.Equals(topic, "Technology", StringComparison.OrdinalIgnoreCase))
            return "TECH";
        if (QuizTypeCatalog.FromCategory(topic) == QuizTypeCatalog.Logo)
            return "LOGO";
        return topic.ToUpperInvariant();
    }

    private static string CategoryQuizLabel(string topic)
    {
        if (QuizTypeCatalog.FromCategory(topic) == QuizTypeCatalog.Logo)
            return "LOGO QUIZ";
        if (string.Equals(topic, "General Knowledge", StringComparison.OrdinalIgnoreCase))
            return "GENERAL KNOWLEDGE QUIZ";
        if (string.Equals(topic, "Nature & Animals", StringComparison.OrdinalIgnoreCase))
            return "NATURE & ANIMALS QUIZ";
        if (string.Equals(topic, "Arts & Literature", StringComparison.OrdinalIgnoreCase))
            return "ARTS & LITERATURE QUIZ";
        if (string.Equals(topic, "Film", StringComparison.OrdinalIgnoreCase))
            return "MOVIE QUIZ";
        if (string.Equals(topic, "Mathematics", StringComparison.OrdinalIgnoreCase))
            return "MATH QUIZ";
        return $"{topic.ToUpperInvariant()} QUIZ";
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
                ? LimitTitle($"Think You're a {topic} Expert? Score {perfect} | {episode}")
                : LimitTitle($"{topic} Quiz Challenge: {perfect} to Beat | {episode}");
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
