using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record QuizMarathonSection(
    int QuestionNumber,
    int QuestionId,
    string Label,
    string Theme,
    QuizDifficulty Difficulty);

public static class QuizMarathonPlanner
{
    public static IReadOnlyList<int> SupportedQuestionCounts { get; } = [30, 50, 100];

    public static bool IsMarathonPreset(QuizBuilderModePreset? preset) => preset?.IsMarathon == true;

    public static bool IsSupportedQuestionCount(int count) => SupportedQuestionCounts.Contains(count);

    public static bool IsSupportedTheme(string? category)
    {
        var value = (category ?? "").Trim();
        return value.Length == 0 ||
               string.Equals(value, "Space", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Technology", StringComparison.OrdinalIgnoreCase);
    }

    public static string ThemeDisplayName(string? category)
    {
        var value = (category ?? "").Trim();
        if (value.Length == 0)
            return "Space + Technology";
        if (string.Equals(value, "Space", StringComparison.OrdinalIgnoreCase))
            return "Space";
        if (string.Equals(value, "Technology", StringComparison.OrdinalIgnoreCase))
            return "Technology";
        throw new ArgumentException("Marathon Mode supports Space, Technology, or All categories for a combined Space + Technology marathon.", nameof(category));
    }

    public static IReadOnlyList<QuizQuestion> Select(
        IEnumerable<QuizQuestion> questions,
        int count,
        string? category,
        bool preferLeastUsed = false,
        IReadOnlySet<int>? recentlyUsedQuestionIds = null,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (!IsSupportedQuestionCount(count))
            throw new ArgumentOutOfRangeException(nameof(count), "Marathon Mode supports 30, 50, or 100 questions.");
        if (!IsSupportedTheme(category))
            throw new ArgumentException("Marathon Mode supports Space, Technology, or All categories for a combined Space + Technology marathon.", nameof(category));

        var requestedTheme = (category ?? "").Trim();
        var combined = requestedTheme.Length == 0;
        var pool = questions
            .Where(question => question.IsEnabled)
            .Where(question => combined
                ? IsSpace(question.Category) || IsTechnology(question.Category)
                : string.Equals(question.Category, requestedTheme, StringComparison.OrdinalIgnoreCase))
            .GroupBy(question => question.Id)
            .Select(group => group.First())
            .ToList();

        if (pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Only {pool.Count} enabled {ThemeDisplayName(category)} questions are available, but {count} are required for this marathon.");
        }

        random ??= Random.Shared;
        recentlyUsedQuestionIds ??= new HashSet<int>();
        var selected = new List<QuizQuestion>(count);
        var targets = QuizDifficultyProgressionSelector.TargetsFor(count);

        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var target = targets[targetIndex];
            var beforeDifficulty = selected.Count;
            if (combined)
            {
                var extraToSpace = target.Count % 2 == 1 && targetIndex % 2 == 0;
                var spaceTarget = target.Count / 2 + (extraToSpace ? 1 : 0);
                var technologyTarget = target.Count - spaceTarget;
                TakeBucket(pool, selected, target.Difficulty, "Space", spaceTarget,
                    preferLeastUsed, recentlyUsedQuestionIds, random);
                TakeBucket(pool, selected, target.Difficulty, "Technology", technologyTarget,
                    preferLeastUsed, recentlyUsedQuestionIds, random);
            }
            else
            {
                TakeBucket(pool, selected, target.Difficulty, requestedTheme, target.Count,
                    preferLeastUsed, recentlyUsedQuestionIds, random);
            }

            var selectedForDifficulty = selected.Count - beforeDifficulty;
            if (selectedForDifficulty < target.Count)
            {
                var remainingSameDifficulty = Remaining(pool, selected)
                    .Where(question => question.DifficultyLevel == target.Difficulty)
                    .ToList();
                var needed = Math.Min(target.Count - selectedForDifficulty, remainingSameDifficulty.Count);
                if (needed > 0)
                {
                    selected.AddRange(QuizRotationSelector.Select(
                        remainingSameDifficulty,
                        needed,
                        preferLeastUsed,
                        recentlyUsedQuestionIds,
                        random));
                }
            }
        }

        if (selected.Count < count)
        {
            var remaining = Remaining(pool, selected).ToList();
            selected.AddRange(QuizRotationSelector.Select(
                remaining,
                count - selected.Count,
                preferLeastUsed,
                recentlyUsedQuestionIds,
                random));
        }

        return OrderForSections(selected, combined);
    }

    public static bool IsMarathonQuestionSet(IReadOnlyList<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        return IsSupportedQuestionCount(questions.Count) &&
               questions.Count > 0 &&
               questions.All(question => IsSpace(question.Category) || IsTechnology(question.Category));
    }

    public static string ThemeDisplayName(IReadOnlyList<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var hasSpace = questions.Any(question => IsSpace(question.Category));
        var hasTechnology = questions.Any(question => IsTechnology(question.Category));
        if (hasSpace && hasTechnology)
            return "Space + Technology";
        if (hasSpace)
            return "Space";
        if (hasTechnology)
            return "Technology";
        return "Marathon";
    }

    public static IReadOnlyList<QuizMarathonSection> Sections(IReadOnlyList<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (!IsMarathonQuestionSet(questions))
            return Array.Empty<QuizMarathonSection>();

        var combined = questions.Any(question => IsSpace(question.Category)) &&
                       questions.Any(question => IsTechnology(question.Category));
        var sections = new List<QuizMarathonSection>();
        string? previousKey = null;

        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var difficulty = question.DifficultyLevel;
            var theme = IsTechnology(question.Category) ? "Technology" : "Space";
            var key = difficulty == QuizDifficulty.Insane
                ? "insane"
                : combined
                    ? $"{difficulty}:{theme}"
                    : difficulty.ToString();
            if (string.Equals(key, previousKey, StringComparison.Ordinal))
                continue;

            previousKey = key;
            var label = difficulty == QuizDifficulty.Insane
                ? "FINAL INSANE ROUND"
                : $"{(theme == "Technology" ? "TECH" : "SPACE")} ROUND • {difficulty.ToString().ToUpperInvariant()}";
            sections.Add(new QuizMarathonSection(
                index + 1,
                question.Id,
                label,
                theme,
                difficulty));
        }

        return sections;
    }

    public static string ProgressionDescription(int count) =>
        QuizDifficultyProgressionSelector.DescriptionFor(count);

    private static IReadOnlyList<QuizQuestion> OrderForSections(
        IReadOnlyList<QuizQuestion> selected,
        bool combined) => selected
        .Select((question, index) => new { Question = question, Index = index })
        .OrderBy(item => item.Question.DifficultyLevel)
        .ThenBy(item => TopicOrder(item.Question, combined))
        .ThenBy(item => item.Index)
        .Select(item => item.Question)
        .ToList();

    private static int TopicOrder(QuizQuestion question, bool combined)
    {
        if (!combined || question.DifficultyLevel == QuizDifficulty.Insane)
            return 0;
        return IsTechnology(question.Category) ? 1 : 0;
    }

    private static void TakeBucket(
        IReadOnlyList<QuizQuestion> pool,
        List<QuizQuestion> selected,
        QuizDifficulty difficulty,
        string category,
        int requested,
        bool preferLeastUsed,
        IReadOnlySet<int> recentlyUsedQuestionIds,
        Random random)
    {
        if (requested <= 0)
            return;

        var candidates = Remaining(pool, selected)
            .Where(question => question.DifficultyLevel == difficulty)
            .Where(question => string.Equals(question.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var take = Math.Min(requested, candidates.Count);
        if (take <= 0)
            return;
        selected.AddRange(QuizRotationSelector.Select(
            candidates,
            take,
            preferLeastUsed,
            recentlyUsedQuestionIds,
            random));
    }

    private static IEnumerable<QuizQuestion> Remaining(
        IEnumerable<QuizQuestion> pool,
        IReadOnlyCollection<QuizQuestion> selected)
    {
        var selectedIds = selected.Select(question => question.Id).ToHashSet();
        return pool.Where(question => !selectedIds.Contains(question.Id));
    }

    private static bool IsSpace(string? category) =>
        string.Equals((category ?? "").Trim(), "Space", StringComparison.OrdinalIgnoreCase);

    private static bool IsTechnology(string? category) =>
        string.Equals((category ?? "").Trim(), "Technology", StringComparison.OrdinalIgnoreCase);
}

public static class QuizMarathonVisualOverlay
{
    public const double HeaderSeconds = 1.0;

    public static int Apply(
        NativeTimeline timeline,
        IReadOnlyList<QuizQuestion> questions,
        string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (!QuizMarathonPlanner.IsMarathonQuestionSet(questions))
            return 0;

        timeline.Validate();
        var sections = QuizMarathonPlanner.Sections(questions);
        if (sections.Count == 0)
            return 0;

        var applied = 0;
        foreach (var section in sections)
        {
            var pair = timeline.Tracks
                .Where(track => track.Kind == NativeTimelineTrackKind.Video &&
                                !string.Equals(track.Name, QuizAnimatedBackground.TrackName, StringComparison.Ordinal))
                .SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
                .Where(pair => pair.Clip.Kind == NativeTimelineClipKind.Image)
                .Where(pair => QuestionId(pair.Clip) == section.QuestionId)
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Clip.Source) && File.Exists(pair.Clip.Source))
                .OrderBy(pair => pair.Clip.Start)
                .FirstOrDefault();
            if (pair.Clip is null || pair.Track is null)
                continue;

            var originalSource = Path.GetFullPath(pair.Clip.Source!);
            var cardsFolder = Path.Combine(Path.GetFullPath(projectFolder), "Cards");
            Directory.CreateDirectory(cardsFolder);
            var destination = Path.Combine(cardsFolder, $"marathon_section_{section.QuestionNumber:000}.png");
            RenderOverlay(originalSource, destination, section.Label, timeline.Width, timeline.Height);

            var originalDuration = pair.Clip.Duration;
            var originalSourceIn = pair.Clip.SourceIn;
            var originalName = pair.Clip.Name;
            var originalMetadata = new Dictionary<string, object?>(pair.Clip.Metadata);
            var headerDuration = Math.Min(HeaderSeconds, originalDuration);
            pair.Clip.Source = destination;
            pair.Clip.Duration = headerDuration;
            pair.Clip.Metadata["marathon_section"] = section.Label;

            if (originalDuration > headerDuration + 0.0001)
            {
                var continuation = pair.Track.AddClip(new NativeTimelineClip
                {
                    Kind = pair.Clip.Kind,
                    Start = pair.Clip.Start + headerDuration,
                    Duration = originalDuration - headerDuration,
                    Source = originalSource,
                    SourceIn = originalSourceIn,
                    Name = string.IsNullOrWhiteSpace(originalName) ? "Marathon section continuation" : originalName + " Continued",
                    Metadata = originalMetadata,
                });
                var scene = timeline.Scenes.FirstOrDefault(candidate =>
                    candidate.ClipIds.Contains(pair.Clip.Id) || QuestionId(candidate) == section.QuestionId);
                scene?.ClipIds.Add(continuation.Id);
            }

            applied++;
        }

        timeline.Metadata["quiz_marathon"] = true;
        timeline.Metadata["quiz_marathon_theme"] = QuizMarathonPlanner.ThemeDisplayName(questions);
        timeline.Metadata["quiz_marathon_section_count"] = applied;
        WriteProjectMetadata(projectFolder, questions, sections);
        timeline.Validate();
        return applied;
    }

    private static void RenderOverlay(string source, string destination, string label, int width, int height)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(source, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var root = new Grid
        {
            Width = width,
            Height = height,
        };
        root.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Width = width,
            Height = height,
        });

        var banner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(244, 8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 202, 45)),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(36, 14, 36, 14),
            Margin = new Thickness(0, height > width ? 120 : 34, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = height > width ? 46 : 42,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
            },
        };
        root.Children.Add(banner);

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(root);
        rendered.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static int QuestionId(NativeTimelineClip clip)
    {
        if (!clip.Metadata.TryGetValue("question_id", out var value) || value is null)
            return 0;
        return int.TryParse(Convert.ToString(value), out var id) ? id : 0;
    }

    private static int QuestionId(NativeTimelineScene scene)
    {
        if (!scene.Metadata.TryGetValue("question_id", out var value) || value is null)
            return 0;
        return int.TryParse(Convert.ToString(value), out var id) ? id : 0;
    }

    private static void WriteProjectMetadata(
        string projectFolder,
        IReadOnlyList<QuizQuestion> questions,
        IReadOnlyList<QuizMarathonSection> sections)
    {
        var path = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        if (!File.Exists(path))
            return;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
                return;
            root["marathon"] = true;
            root["marathon_theme"] = QuizMarathonPlanner.ThemeDisplayName(questions);
            var array = new JsonArray();
            foreach (var section in sections)
            {
                array.Add(new JsonObject
                {
                    ["question_number"] = section.QuestionNumber,
                    ["question_id"] = section.QuestionId,
                    ["label"] = section.Label,
                    ["theme"] = section.Theme,
                    ["difficulty"] = section.Difficulty.ToString().ToLowerInvariant(),
                });
            }
            root["marathon_sections"] = array;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not write marathon metadata: {error.Message}");
        }
    }
}
