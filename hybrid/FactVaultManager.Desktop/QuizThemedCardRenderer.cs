using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public enum QuizPreviewCardKind
{
    Intro,
    Question,
    Countdown,
    AnswerReveal,
    Explanation,
    Outro,
}

public sealed record QuizVisualRenderSettings(
    string ThemeKey = "dark",
    string LogoPosition = "Bottom right",
    double LogoScale = 1.0)
{
    public QuizVisualRenderSettings Normalize()
    {
        if (LogoScale is < 0.5 or > 2.0 || double.IsNaN(LogoScale) || double.IsInfinity(LogoScale))
            throw new ArgumentOutOfRangeException(nameof(LogoScale), "Quiz logo size must be between 50% and 200%.");
        return this with
        {
            ThemeKey = QuizVisualThemeCatalog.Normalize(ThemeKey),
            LogoPosition = QuizLogoPositionCatalog.Normalize(LogoPosition),
            LogoScale = Math.Clamp(LogoScale, 0.5, 2.0),
        };
    }
}

public sealed class QuizThemedCardRenderer
{
    public void OverwriteCards(
        string projectFolder,
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options,
        IReadOnlyDictionary<int, QuizNarrationAsset> narrationByQuestion,
        QuizVisualRenderSettings visual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(narrationByQuestion);
        options.Validate();
        visual = visual.Normalize();

        var cardsFolder = Path.Combine(Path.GetFullPath(projectFolder), "Cards");
        Directory.CreateDirectory(cardsFolder);

        RenderCard(BuildTitleCard(options.Title, options, visual), Path.Combine(cardsFolder, "000_intro.png"), options.Width, options.Height);
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var number = index + 1;
            narrationByQuestion.TryGetValue(question.Id, out var narration);
            var narrationSeconds = narration?.Duration ?? 0;
            var countdownSeconds = options.CountdownSeconds;
            var answerLeadSeconds = options.QuestionSeconds - countdownSeconds;

            if (narrationSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, false, null, false, true),
                    Path.Combine(cardsFolder, $"{number:000}_narration.png"),
                    options.Width,
                    options.Height);
            }

            if (answerLeadSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, false, null, false, false),
                    Path.Combine(cardsFolder, $"{number:000}_question.png"),
                    options.Width,
                    options.Height);
            }

            for (var remaining = countdownSeconds; remaining >= 1; remaining--)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, false, remaining, false, false),
                    Path.Combine(cardsFolder, $"{number:000}_countdown_{remaining}.png"),
                    options.Width,
                    options.Height);
            }

            if (options.RevealEmphasisSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, true, null, true, false),
                    Path.Combine(cardsFolder, $"{number:000}_answer_reveal.png"),
                    options.Width,
                    options.Height);
            }

            if (options.AnswerSeconds - options.RevealEmphasisSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, true, null, false, false),
                    Path.Combine(cardsFolder, $"{number:000}_answer.png"),
                    options.Width,
                    options.Height);
            }
        }

        RenderCard(BuildOutroCard(options, visual), Path.Combine(cardsFolder, "999_outro.png"), options.Width, options.Height);
        WriteVisualMetadata(projectFolder, visual);
    }

    public BitmapSource RenderPreviewBitmap(
        QuizQuestion question,
        QuizVideoBuildOptions options,
        QuizVisualRenderSettings visual,
        QuizPreviewCardKind kind,
        int number,
        int total)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        visual = visual.Normalize();
        number = Math.Clamp(number, 1, Math.Max(1, total));
        total = Math.Max(1, total);

        FrameworkElement card = kind switch
        {
            QuizPreviewCardKind.Intro => BuildTitleCard(options.Title, options, visual),
            QuizPreviewCardKind.Countdown => BuildQuestionCard(question, number, total, options, visual, false, Math.Min(3, options.QuestionSeconds), false, false),
            QuizPreviewCardKind.AnswerReveal => BuildQuestionCard(question, number, total, options, visual, true, null, true, false),
            QuizPreviewCardKind.Explanation => BuildQuestionCard(question, number, total, options, visual, true, null, false, false),
            QuizPreviewCardKind.Outro => BuildOutroCard(options, visual),
            _ => BuildQuestionCard(question, number, total, options, visual, false, null, false, false),
        };

        card.Measure(new Size(options.Width, options.Height));
        card.Arrange(new Rect(0, 0, options.Width, options.Height));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(options.Width, options.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    private static FrameworkElement BuildTitleCard(string title, QuizVideoBuildOptions options, QuizVisualRenderSettings visual)
    {
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = CardRoot(options, theme);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
        };
        content.Children.Add(new TextBlock
        {
            Text = "QUIZ TIME",
            Foreground = Brush(theme.Accent),
            FontSize = options.Vertical ? 38 : 32,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
        });
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(theme.Text),
            FontSize = options.Vertical ? 72 : 70,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        root.Child = WithQuizLogo(content, options, visual);
        return root;
    }

    private static FrameworkElement BuildOutroCard(QuizVideoBuildOptions options, QuizVisualRenderSettings visual)
    {
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = CardRoot(options, theme);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
        };
        content.Children.Add(new TextBlock
        {
            Text = "How many did you get right?",
            Foreground = Brush(theme.Text),
            FontSize = options.Vertical ? 64 : 60,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Share your score in the comments",
            Foreground = Brush(theme.AccentSoft),
            FontSize = options.Vertical ? 34 : 30,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 28, 0, 0),
        });
        root.Child = WithQuizLogo(content, options, visual);
        return root;
    }

    private static FrameworkElement BuildQuestionCard(
        QuizQuestion question,
        int number,
        int total,
        QuizVideoBuildOptions options,
        QuizVisualRenderSettings visual,
        bool revealAnswer,
        int? countdownValue,
        bool emphasizeReveal,
        bool narrating)
    {
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = CardRoot(options, theme);
        var page = new Grid { Margin = CardMargin(options) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, options.Vertical ? 18 : 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = $"QUESTION {number} OF {total}",
            Foreground = Brush(theme.Accent),
            FontSize = options.Vertical ? 30 : 24,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var phaseText = revealAnswer
            ? (emphasizeReveal ? "✓ CORRECT!" : "ANSWER")
            : countdownValue is int countdown
                ? countdown.ToString()
                : narrating
                    ? "LISTEN"
                    : $"{options.QuestionSeconds} SECONDS";
        var phaseColor = revealAnswer
            ? theme.CorrectBorder
            : countdownValue.HasValue
                ? theme.Countdown
                : narrating
                    ? theme.Narration
                    : theme.Muted;
        var phase = new TextBlock
        {
            Text = phaseText,
            Foreground = Brush(phaseColor),
            FontSize = countdownValue.HasValue ? (options.Vertical ? 54 : 44) : (options.Vertical ? 26 : 22),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(phase, 1);
        header.Children.Add(phase);
        page.Children.Add(header);

        var progressTrack = new Border
        {
            Height = options.Vertical ? 10 : 8,
            Background = Brush(theme.Panel),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, options.Vertical ? 28 : 18),
        };
        var availableWidth = options.Width - CardMargin(options).Left - CardMargin(options).Right;
        progressTrack.Child = new Border
        {
            Width = Math.Max(1, availableWidth * number / (double)Math.Max(1, total)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush(revealAnswer ? theme.CorrectBorder : theme.Accent),
            CornerRadius = new CornerRadius(999),
        };
        Grid.SetRow(progressTrack, 1);
        page.Children.Add(progressTrack);

        var questionText = new TextBlock
        {
            Text = question.Question,
            Foreground = Brush(theme.Text),
            FontSize = options.Vertical ? 54 : 46,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, options.Vertical ? 42 : 24),
        };
        Grid.SetRow(questionText, 2);
        page.Children.Add(questionText);

        var answers = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var index = 0; index < 4; index++)
        {
            answers.Children.Add(BuildAnswerRow(
                question.Answers[index],
                index,
                revealAnswer && index == question.CorrectIndex,
                emphasizeReveal && index == question.CorrectIndex,
                options,
                theme));
        }
        Grid.SetRow(answers, 3);
        page.Children.Add(answers);

        var footer = new StackPanel { Margin = new Thickness(0, options.Vertical ? 28 : 16, 0, 0) };
        var footerText = revealAnswer
            ? emphasizeReveal
                ? $"{question.CorrectLetter}. {question.CorrectAnswer}"
                : (string.IsNullOrWhiteSpace(question.Explanation)
                    ? $"Correct answer: {question.CorrectLetter}. {question.CorrectAnswer}"
                    : question.Explanation)
            : countdownValue is int footerRemaining
                ? $"{footerRemaining} second{(footerRemaining == 1 ? "" : "s")} remaining"
                : narrating
                    ? "Answer time starts after the narration"
                    : "Choose A, B, C, or D";
        footer.Children.Add(new TextBlock
        {
            Text = footerText,
            Foreground = Brush(revealAnswer
                ? theme.CorrectBorder
                : countdownValue.HasValue
                    ? theme.Countdown
                    : narrating
                        ? theme.Narration
                        : theme.Muted),
            FontSize = emphasizeReveal ? (options.Vertical ? 38 : 32) : (options.Vertical ? 30 : 24),
            FontWeight = revealAnswer ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

        if (!revealAnswer && !narrating)
        {
            var timerWidth = availableWidth;
            var timerFraction = countdownValue is int timerRemaining
                ? Math.Clamp(timerRemaining / (double)options.QuestionSeconds, 0.0, 1.0)
                : 1.0;
            var timerTrack = new Border
            {
                Width = timerWidth,
                Height = options.Vertical ? 12 : 10,
                Background = Brush(theme.Panel),
                CornerRadius = new CornerRadius(999),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, options.Vertical ? 18 : 12, 0, 0),
            };
            timerTrack.Child = new Border
            {
                Width = Math.Max(1, timerWidth * timerFraction),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush(countdownValue.HasValue ? theme.Countdown : theme.Accent),
                CornerRadius = new CornerRadius(999),
            };
            footer.Children.Add(timerTrack);
        }

        Grid.SetRow(footer, 4);
        page.Children.Add(footer);
        root.Child = WithQuizLogo(page, options, visual);
        return root;
    }

    private static Border BuildAnswerRow(
        string answer,
        int index,
        bool correct,
        bool emphasized,
        QuizVideoBuildOptions options,
        QuizVisualTheme theme)
    {
        var background = correct ? theme.Correct : theme.Panel;
        var borderColor = correct ? theme.CorrectBorder : theme.PanelBorder;
        var row = new Border
        {
            Background = Brush(background),
            BorderBrush = Brush(borderColor),
            BorderThickness = new Thickness(emphasized ? 6 : correct ? 3 : 1),
            CornerRadius = new CornerRadius(options.Vertical ? 18 : 14),
            Padding = new Thickness(options.Vertical ? 24 : 20, options.Vertical ? 22 : 15, options.Vertical ? 24 : 20, options.Vertical ? 22 : 15),
            Margin = new Thickness(0, 0, 0, options.Vertical ? 22 : 12),
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(options.Vertical ? 70 : 62) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = correct ? $"✓ {(char)('A' + index)}" : ((char)('A' + index)).ToString(),
            Foreground = Brush(correct ? theme.CorrectBorder : theme.Accent),
            FontSize = options.Vertical ? 38 : 32,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var answerText = new TextBlock
        {
            Text = answer,
            Foreground = Brush(correct ? theme.Text : theme.PanelText),
            FontSize = emphasized ? (options.Vertical ? 42 : 36) : (options.Vertical ? 38 : 32),
            FontWeight = correct ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(answerText, 1);
        content.Children.Add(answerText);
        row.Child = content;
        return row;
    }

    private static Border CardRoot(QuizVideoBuildOptions options, QuizVisualTheme theme) => new()
    {
        Width = options.Width,
        Height = options.Height,
        Background = Brush(theme.Background),
    };

    private static FrameworkElement WithQuizLogo(
        FrameworkElement content,
        QuizVideoBuildOptions options,
        QuizVisualRenderSettings visual)
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

        var position = QuizLogoPositionCatalog.Normalize(visual.LogoPosition);
        var top = position.StartsWith("Top", StringComparison.OrdinalIgnoreCase);
        var left = position.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        var baseHeight = options.Vertical ? 72.0 : 46.0;
        var baseWidth = options.Vertical ? 240.0 : 180.0;
        var horizontalMargin = options.Vertical ? 56.0 : 70.0;
        var verticalMargin = options.Vertical ? 24.0 : 14.0;

        var layout = new Grid();
        layout.Children.Add(content);
        layout.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Height = baseHeight * visual.LogoScale,
            MaxWidth = baseWidth * visual.LogoScale,
            Margin = new Thickness(horizontalMargin, verticalMargin, horizontalMargin, verticalMargin),
            SnapsToDevicePixels = true,
        });
        return layout;
    }

    private static Thickness CardMargin(QuizVideoBuildOptions options) => options.Vertical
        ? new Thickness(76, 110, 76, 110)
        : new Thickness(120, 66, 120, 66);

    private static SolidColorBrush Brush(Color color) => new(color);

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

    private static void WriteVisualMetadata(string projectFolder, QuizVisualRenderSettings visual)
    {
        var path = Path.Combine(projectFolder, "quiz.json");
        if (!File.Exists(path))
            return;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
                return;
            root["theme"] = visual.ThemeKey;
            root["logo_position"] = visual.LogoPosition;
            root["logo_scale"] = visual.LogoScale;
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (JsonException)
        {
            // The base quiz builder owns quiz.json. A malformed file should not block the Resolve export.
        }
    }
}
