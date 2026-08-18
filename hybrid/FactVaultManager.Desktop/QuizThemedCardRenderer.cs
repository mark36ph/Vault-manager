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
        int total,
        int? countdownValue = null)
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
            QuizPreviewCardKind.Countdown => BuildQuestionCard(question, number, total, options, visual, false, countdownValue ?? Math.Min(3, options.QuestionSeconds), false, false),
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

        if (!string.IsNullOrWhiteSpace(options.QuizLogoPath))
        {
            var logoPath = QuizBranding.ValidateLogoPath(options.QuizLogoPath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            content.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Height = options.Vertical ? 260 : 210,
                MaxWidth = options.Vertical ? 720 : 620,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, options.Vertical ? 44 : 30),
                SnapsToDevicePixels = true,
            });
        }

        var badge = new Border
        {
            Background = Brush(Color.FromArgb(95, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(options.Vertical ? 34 : 30, options.Vertical ? 13 : 10, options.Vertical ? 34 : 30, options.Vertical ? 13 : 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, options.Vertical ? 34 : 24),
            Child = new TextBlock
            {
                Text = "QUIZ TIME",
                Foreground = Brush(theme.Countdown),
                FontSize = options.Vertical ? 36 : 30,
                FontWeight = FontWeights.Bold,
            },
        };
        content.Children.Add(badge);

        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(theme.Text),
            FontSize = options.Vertical ? 78 : 72,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        content.Children.Add(new TextBlock
        {
            Text = "ARE YOU READY?",
            Foreground = Brush(theme.AccentSoft),
            FontSize = options.Vertical ? 34 : 28,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, options.Vertical ? 34 : 24, 0, 0),
        });

        root.Child = content;
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
            Text = "👍  Like this quiz? Subscribe for more!",
            Foreground = Brush(theme.Text),
            FontSize = options.Vertical ? 64 : 60,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Like the video and share your score in the comments",
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
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, options.Vertical ? 24 : 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = $"Q{number:00}",
            Foreground = Brush(theme.Countdown),
            FontSize = options.Vertical ? 42 : 38,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"  /  {total:00}",
            Foreground = Brush(theme.Muted),
            FontSize = options.Vertical ? 24 : 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(heading);

        var phaseText = revealAnswer
            ? "✓ CORRECT"
            : countdownValue is int countdown
                ? countdown.ToString()
                : narrating
                    ? "LISTEN"
                    : options.ShowCountdown
                        ? ""
                        : $"{options.QuestionSeconds}s";
        var phaseColor = revealAnswer
            ? theme.CorrectBorder
            : countdownValue.HasValue
                ? theme.Countdown
                : narrating
                    ? theme.Narration
                    : theme.Muted;
        if (!string.IsNullOrWhiteSpace(phaseText))
        {
            var phase = new TextBlock
            {
                Text = phaseText,
                Foreground = Brush(phaseColor),
                FontSize = countdownValue.HasValue ? (options.Vertical ? 58 : 52) : (options.Vertical ? 28 : 25),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(phase, 1);
            header.Children.Add(phase);
        }
        page.Children.Add(header);

        var questionPanel = new Border
        {
            MinHeight = options.Vertical ? 0 : 170,
            Background = Brush(Color.FromArgb(228, theme.Panel.R, theme.Panel.G, theme.Panel.B)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(options.Vertical ? 5 : 6, 0, 0, 0),
            CornerRadius = new CornerRadius(options.Vertical ? 18 : 16),
            Padding = new Thickness(options.Vertical ? 34 : 34, options.Vertical ? 28 : 28, options.Vertical ? 34 : 34, options.Vertical ? 28 : 28),
            Margin = new Thickness(0, 0, 0, options.Vertical ? 34 : 16),
            Child = new TextBlock
            {
                Text = question.Question,
                Foreground = Brush(theme.Text),
                FontSize = options.Vertical ? 54 : 50,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(questionPanel, 1);
        page.Children.Add(questionPanel);

        FrameworkElement answerArea;
        if (options.Vertical)
        {
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            for (var index = 0; index < 4; index++)
            {
                stack.Children.Add(BuildAnswerRow(
                    question.Answers[index],
                    index,
                    revealAnswer && index == question.CorrectIndex,
                    emphasizeReveal && index == question.CorrectIndex,
                    options,
                    theme));
            }
            answerArea = stack;
        }
        else
        {
            var grid = new Grid
            {
                MinHeight = 470,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            for (var index = 0; index < 4; index++)
            {
                var card = BuildAnswerRow(
                    question.Answers[index],
                    index,
                    revealAnswer && index == question.CorrectIndex,
                    emphasizeReveal && index == question.CorrectIndex,
                    options,
                    theme);
                card.Margin = new Thickness(0);
                Grid.SetColumn(card, index % 2 == 0 ? 0 : 2);
                Grid.SetRow(card, index < 2 ? 0 : 2);
                grid.Children.Add(card);
            }
            answerArea = grid;
        }
        Grid.SetRow(answerArea, 2);
        page.Children.Add(answerArea);

        var footer = new Grid { Margin = new Thickness(0, options.Vertical ? 24 : 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (revealAnswer)
        {
            footer.Children.Add(new TextBlock
            {
                Text = emphasizeReveal || string.IsNullOrWhiteSpace(question.Explanation)
                    ? $"{question.CorrectLetter}. {question.CorrectAnswer}"
                    : question.Explanation,
                Foreground = Brush(theme.CorrectBorder),
                FontSize = emphasizeReveal ? (options.Vertical ? 38 : 32) : (options.Vertical ? 30 : 26),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (narrating)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "Listen, then choose your answer",
                Foreground = Brush(theme.Narration),
                FontSize = options.Vertical ? 28 : 24,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (!countdownValue.HasValue)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "Choose A, B, C or D",
                Foreground = Brush(theme.Muted),
                FontSize = options.Vertical ? 28 : 24,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (!revealAnswer && !narrating)
        {
            var timerWidth = options.Vertical ? 500.0 : 520.0;
            var timerFraction = countdownValue is int timerRemaining
                ? Math.Clamp(timerRemaining / (double)options.QuestionSeconds, 0.0, 1.0)
                : 1.0;
            var timerTrack = new Border
            {
                Width = timerWidth,
                Height = options.Vertical ? 12 : 12,
                Background = Brush(Color.FromArgb(125, theme.PanelBorder.R, theme.PanelBorder.G, theme.PanelBorder.B)),
                CornerRadius = new CornerRadius(999),
                VerticalAlignment = VerticalAlignment.Center,
            };
            timerTrack.Child = new Border
            {
                Width = Math.Max(1, timerWidth * timerFraction),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush(countdownValue.HasValue ? theme.Countdown : theme.Accent),
                CornerRadius = new CornerRadius(999),
            };
            Grid.SetColumn(timerTrack, 1);
            footer.Children.Add(timerTrack);
        }

        Grid.SetRow(footer, 3);
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
        var accent = index switch
        {
            0 => theme.Accent,
            1 => theme.Narration,
            2 => theme.Countdown,
            _ => Blend(theme.Accent, theme.Narration, 0.55),
        };
        var background = correct
            ? Blend(theme.Correct, theme.CorrectBorder, 0.10)
            : Blend(theme.Panel, accent, 0.045);
        var border = correct ? theme.CorrectBorder : Blend(theme.PanelBorder, accent, 0.42);

        var row = new Border
        {
            MinHeight = options.Vertical ? 0 : 205,
            Background = Brush(background),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(emphasized ? 5 : correct ? 3 : 2),
            CornerRadius = new CornerRadius(options.Vertical ? 18 : 18),
            Padding = new Thickness(options.Vertical ? 20 : 24, options.Vertical ? 16 : 22, options.Vertical ? 24 : 28, options.Vertical ? 16 : 22),
            Margin = new Thickness(0, 0, 0, options.Vertical ? 18 : 0),
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(options.Vertical ? 72 : 78) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new Border
        {
            Width = options.Vertical ? 52 : 56,
            Height = options.Vertical ? 52 : 56,
            Background = Brush(correct ? theme.CorrectBorder : accent),
            CornerRadius = new CornerRadius(options.Vertical ? 12 : 14),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = correct ? "✓" : ((char)('A' + index)).ToString(),
                Foreground = Brush(theme.Background),
                FontSize = options.Vertical ? 27 : 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        content.Children.Add(marker);

        var answerText = new TextBlock
        {
            Text = answer,
            Foreground = Brush(correct ? theme.Text : theme.PanelText),
            FontSize = emphasized ? (options.Vertical ? 42 : 38) : (options.Vertical ? 37 : 34),
            FontWeight = correct ? FontWeights.Bold : FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(answerText, 1);
        content.Children.Add(answerText);
        row.Child = content;
        return row;
    }

    private static Border CardRoot(QuizVideoBuildOptions options, QuizVisualTheme theme)
    {
        var gradient = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Blend(theme.Background, theme.Accent, 0.13), 0),
                new(theme.Background, 0.34),
                new(theme.Background, 0.66),
                new(Blend(theme.Background, theme.Narration, 0.15), 1),
            },
            new Point(0, 0),
            new Point(1, 1));
        return new Border
        {
            Width = options.Width,
            Height = options.Height,
            Background = gradient,
            BorderBrush = Brush(Color.FromArgb(95, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
            BorderThickness = new Thickness(options.Vertical ? 8 : 6),
        };
    }

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
        var baseHeight = options.Vertical ? 72.0 : 62.0;
        var baseWidth = options.Vertical ? 240.0 : 225.0;
        var horizontalMargin = options.Vertical ? 56.0 : 42.0;
        var verticalMargin = options.Vertical ? 24.0 : 18.0;

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
        : new Thickness(86, 46, 86, 46);

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(left.R + ((right.R - left.R) * amount)),
            (byte)Math.Round(left.G + ((right.G - left.G) * amount)),
            (byte)Math.Round(left.B + ((right.B - left.B) * amount)));
    }

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
