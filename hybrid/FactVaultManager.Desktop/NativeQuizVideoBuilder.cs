using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record QuizVideoBuildOptions(
    string Title,
    int QuestionSeconds = 8,
    int AnswerSeconds = 3,
    bool Vertical = false,
    double FrameRate = 30,
    string QuizLogoPath = "")
{
    public int Width => Vertical ? 1080 : 1920;
    public int Height => Vertical ? 1920 : 1080;

    public void Validate()
    {
        ProjectPathSecurity.ValidateSegment(Title, "Quiz title");
        if (QuestionSeconds is < 2 or > 60)
            throw new ArgumentOutOfRangeException(nameof(QuestionSeconds), "Seconds per question must be between 2 and 60.");
        if (AnswerSeconds is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(AnswerSeconds), "Answer reveal must be between 1 and 15 seconds.");
        if (FrameRate <= 0 || FrameRate > 120)
            throw new ArgumentOutOfRangeException(nameof(FrameRate), "Frame rate must be greater than zero and no more than 120.");
        if (!string.IsNullOrWhiteSpace(QuizLogoPath))
            QuizBranding.ValidateLogoPath(QuizLogoPath);
    }

    public double EstimatedDuration(int questionCount) =>
        2.0 + (Math.Max(0, questionCount) * (QuestionSeconds + AnswerSeconds)) + 2.0;
}

public sealed record QuizVideoBuildResult(
    string ProjectFolder,
    string QuizJson,
    NativeTimeline Timeline,
    NativeResolveFreeExportResult ResolveExport);

public sealed class NativeQuizVideoBuilder
{
    public QuizVideoBuildResult BuildAndExport(
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options,
        string projectsRoot)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (questions.Count == 0)
            throw new ArgumentException("Pick at least one quiz question before exporting.", nameof(questions));
        if (questions.Select(question => question.Id).Distinct().Count() != questions.Count)
            throw new ArgumentException("A quiz cannot contain the same question more than once.", nameof(questions));
        if (string.IsNullOrWhiteSpace(projectsRoot))
            throw new InvalidOperationException("Set the Projects Folder in Settings before creating a quiz video.");
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            throw new InvalidOperationException("Quiz cards must be rendered on the desktop UI thread.");

        projectsRoot = Path.GetFullPath(projectsRoot.Trim());
        Directory.CreateDirectory(projectsRoot);
        var safeTitle = ProjectPathSecurity.ValidateSegment(options.Title, "Quiz title");
        var quizRoot = ProjectPathSecurity.CombineContained(projectsRoot, "Quizzes");
        var projectFolder = ProjectPathSecurity.CombineContained(projectsRoot, "Quizzes", safeTitle);
        Directory.CreateDirectory(quizRoot);
        Directory.CreateDirectory(projectFolder);

        var cardsFolder = ProjectPathSecurity.CombineContained(projectsRoot, "Quizzes", safeTitle, "Cards");
        if (Directory.Exists(cardsFolder))
            Directory.Delete(cardsFolder, recursive: true);
        Directory.CreateDirectory(cardsFolder);

        var timeline = new NativeTimeline
        {
            Name = safeTitle,
            Width = options.Width,
            Height = options.Height,
            FrameRate = options.FrameRate,
            Metadata = new Dictionary<string, object?>
            {
                ["content_type"] = "quiz",
                ["question_count"] = questions.Count,
                ["question_seconds"] = options.QuestionSeconds,
                ["answer_seconds"] = options.AnswerSeconds,
                ["orientation"] = options.Vertical ? "vertical" : "landscape",
                ["quiz_logo"] = string.IsNullOrWhiteSpace(options.QuizLogoPath)
                    ? ""
                    : Path.GetFileName(options.QuizLogoPath),
            },
        };
        var videoTrack = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Quiz Cards",
            Kind = NativeTimelineTrackKind.Video,
        });

        var cursor = 0.0;
        var introPath = Path.Combine(cardsFolder, "000_intro.png");
        RenderCard(BuildTitleCard(safeTitle, options), introPath, options.Width, options.Height);
        videoTrack.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = cursor,
            Duration = 2,
            Source = introPath,
            Name = "Quiz Intro",
            Metadata = new() { ["quiz_card"] = "intro" },
        });
        cursor += 2;

        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var number = index + 1;
            var questionPath = Path.Combine(cardsFolder, $"{number:000}_question.png");
            var answerPath = Path.Combine(cardsFolder, $"{number:000}_answer.png");
            RenderCard(BuildQuestionCard(question, number, questions.Count, options, revealAnswer: false), questionPath, options.Width, options.Height);
            RenderCard(BuildQuestionCard(question, number, questions.Count, options, revealAnswer: true), answerPath, options.Width, options.Height);

            var questionClip = videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = cursor,
                Duration = options.QuestionSeconds,
                Source = questionPath,
                Name = $"Question {number}",
                Metadata = new()
                {
                    ["quiz_card"] = "question",
                    ["question_id"] = question.Id,
                    ["correct_index"] = question.CorrectIndex,
                },
            });
            var answerStart = cursor + options.QuestionSeconds;
            var answerClip = videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = answerStart,
                Duration = options.AnswerSeconds,
                Source = answerPath,
                Name = $"Answer {number}",
                Metadata = new()
                {
                    ["quiz_card"] = "answer",
                    ["question_id"] = question.Id,
                    ["correct_index"] = question.CorrectIndex,
                },
            });

            timeline.AddScene(new NativeTimelineScene
            {
                Title = $"Question {number}",
                Start = cursor,
                Duration = options.QuestionSeconds + options.AnswerSeconds,
                Narration = question.Question,
                ClipIds = [questionClip.Id, answerClip.Id],
                Metadata = new()
                {
                    ["question_id"] = question.Id,
                    ["category"] = question.Category,
                    ["difficulty"] = question.Difficulty,
                },
            });
            cursor += options.QuestionSeconds + options.AnswerSeconds;
        }

        var outroPath = Path.Combine(cardsFolder, "999_outro.png");
        RenderCard(BuildOutroCard(options), outroPath, options.Width, options.Height);
        videoTrack.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Image,
            Start = cursor,
            Duration = 2,
            Source = outroPath,
            Name = "Quiz Outro",
            Metadata = new() { ["quiz_card"] = "outro" },
        });

        timeline.Validate();
        var quizJson = WriteQuizProject(projectFolder, questions, options);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = safeTitle,
            ["description"] = $"{questions.Count}-question quiz generated by FactVaultManager.",
            ["script"] = BuildTextScript(questions),
            ["sources"] = "Quiz question bank",
        };
        var resolve = new NativeResolveFreeExportService().Export(
            timeline,
            projectFolder,
            metadata,
            strict: true,
            overwrite: true);

        return new QuizVideoBuildResult(projectFolder, quizJson, timeline, resolve);
    }

    private static FrameworkElement BuildTitleCard(string title, QuizVideoBuildOptions options)
    {
        var root = CardRoot(options);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
        };
        content.Children.Add(new TextBlock
        {
            Text = "QUIZ TIME",
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            FontSize = options.Vertical ? 38 : 32,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
        });
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 72 : 70,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        root.Child = WithQuizLogo(content, options);
        return root;
    }

    private static FrameworkElement BuildOutroCard(QuizVideoBuildOptions options)
    {
        var root = CardRoot(options);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
        };
        content.Children.Add(new TextBlock
        {
            Text = "How many did you get right?",
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 64 : 60,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Share your score in the comments",
            Foreground = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
            FontSize = options.Vertical ? 34 : 30,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 28, 0, 0),
        });
        root.Child = WithQuizLogo(content, options);
        return root;
    }

    private static FrameworkElement BuildQuestionCard(
        QuizQuestion question,
        int number,
        int total,
        QuizVideoBuildOptions options,
        bool revealAnswer)
    {
        var root = CardRoot(options);
        var page = new Grid { Margin = CardMargin(options) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, options.Vertical ? 34 : 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = $"QUESTION {number} OF {total}",
            Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            FontSize = options.Vertical ? 30 : 24,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var phase = new TextBlock
        {
            Text = revealAnswer ? "ANSWER" : $"{options.QuestionSeconds} SECONDS",
            Foreground = revealAnswer
                ? new SolidColorBrush(Color.FromRgb(134, 239, 172))
                : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = options.Vertical ? 26 : 22,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetColumn(phase, 1);
        header.Children.Add(phase);
        page.Children.Add(header);

        var questionText = new TextBlock
        {
            Text = question.Question,
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 54 : 46,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, options.Vertical ? 42 : 24),
        };
        Grid.SetRow(questionText, 1);
        page.Children.Add(questionText);

        var answers = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var index = 0; index < 4; index++)
            answers.Children.Add(BuildAnswerRow(question.Answers[index], index, revealAnswer && index == question.CorrectIndex, options));
        Grid.SetRow(answers, 2);
        page.Children.Add(answers);

        var footer = new TextBlock
        {
            Text = revealAnswer
                ? (string.IsNullOrWhiteSpace(question.Explanation)
                    ? $"Correct answer: {question.CorrectLetter}. {question.CorrectAnswer}"
                    : question.Explanation)
                : "Choose A, B, C, or D",
            Foreground = revealAnswer
                ? new SolidColorBrush(Color.FromRgb(220, 252, 231))
                : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = options.Vertical ? 30 : 24,
            FontWeight = revealAnswer ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, options.Vertical ? 34 : 20, 0, 0),
        };
        Grid.SetRow(footer, 3);
        page.Children.Add(footer);

        root.Child = WithQuizLogo(page, options);
        return root;
    }

    private static Border BuildAnswerRow(string answer, int index, bool correct, QuizVideoBuildOptions options)
    {
        var background = correct
            ? new SolidColorBrush(Color.FromRgb(22, 101, 52))
            : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        var border = correct
            ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
            : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        var row = new Border
        {
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(correct ? 3 : 1),
            CornerRadius = new CornerRadius(options.Vertical ? 18 : 14),
            Padding = new Thickness(options.Vertical ? 24 : 20, options.Vertical ? 22 : 15, options.Vertical ? 24 : 20, options.Vertical ? 22 : 15),
            Margin = new Thickness(0, 0, 0, options.Vertical ? 22 : 12),
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(options.Vertical ? 70 : 62) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = ((char)('A' + index)).ToString(),
            Foreground = correct ? new SolidColorBrush(Color.FromRgb(187, 247, 208)) : new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            FontSize = options.Vertical ? 38 : 32,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var answerText = new TextBlock
        {
            Text = answer,
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 38 : 32,
            FontWeight = correct ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(answerText, 1);
        content.Children.Add(answerText);
        row.Child = content;
        return row;
    }

    private static Border CardRoot(QuizVideoBuildOptions options) => new()
    {
        Width = options.Width,
        Height = options.Height,
        Background = new SolidColorBrush(Color.FromRgb(11, 18, 32)),
    };

    private static FrameworkElement WithQuizLogo(FrameworkElement content, QuizVideoBuildOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.QuizLogoPath))
            return content;

        var logoPath = QuizBranding.ValidateLogoPath(options.QuizLogoPath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var layout = new Grid();
        layout.Children.Add(content);
        layout.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = options.Vertical ? 72 : 46,
            MaxWidth = options.Vertical ? 240 : 180,
            Margin = options.Vertical
                ? new Thickness(0, 0, 56, 24)
                : new Thickness(0, 0, 70, 14),
            SnapsToDevicePixels = true,
        });
        return layout;
    }

    private static Thickness CardMargin(QuizVideoBuildOptions options) => options.Vertical
        ? new Thickness(76, 110, 76, 110)
        : new Thickness(120, 66, 120, 66);

    private static void RenderCard(FrameworkElement card, string destination, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        card.Measure(new Size(width, height));
        card.Arrange(new Rect(0, 0, width, height));
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static string WriteQuizProject(
        string projectFolder,
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options)
    {
        var path = Path.Combine(projectFolder, "quiz.json");
        var payload = new
        {
            title = options.Title,
            question_seconds = options.QuestionSeconds,
            answer_seconds = options.AnswerSeconds,
            orientation = options.Vertical ? "vertical" : "landscape",
            width = options.Width,
            height = options.Height,
            frame_rate = options.FrameRate,
            quiz_logo = string.IsNullOrWhiteSpace(options.QuizLogoPath) ? "" : Path.GetFileName(options.QuizLogoPath),
            questions = questions.Select((question, index) => new
            {
                number = index + 1,
                id = question.Id,
                question = question.Question,
                answers = question.Answers,
                correct_index = question.CorrectIndex,
                correct_answer = question.CorrectAnswer,
                explanation = question.Explanation,
                category = question.Category,
                difficulty = question.Difficulty,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private static string BuildTextScript(IReadOnlyList<QuizQuestion> questions)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            builder.AppendLine($"Question {index + 1}: {question.Question}");
            for (var answer = 0; answer < question.Answers.Count; answer++)
                builder.AppendLine($"{(char)('A' + answer)}. {question.Answers[answer]}");
            builder.AppendLine($"Correct: {question.CorrectLetter}. {question.CorrectAnswer}");
            if (!string.IsNullOrWhiteSpace(question.Explanation))
                builder.AppendLine(question.Explanation);
            builder.AppendLine();
        }
        return builder.ToString().Trim();
    }
}
