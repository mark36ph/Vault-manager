using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
            var bitmap = LoadBitmap(logoPath);
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
        if (options.Vertical)
            return BuildVerticalQuestionCard(question, number, total, options, theme, revealAnswer, countdownValue, emphasizeReveal, narrating);

        var root = CardRoot(options, theme, transparent: true);
        var page = new Grid { Margin = new Thickness(72, 18, 72, 24) };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(224) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        var logo = BuildTopQuizLogo(options, theme);
        Grid.SetRow(logo, 0);
        page.Children.Add(logo);

        var titleBar = new Border
        {
            Height = 76,
            MinWidth = options.Width * 0.56,
            MaxWidth = options.Width * 0.72,
            Background = Brush(Color.FromArgb(242, 10, 17, 72)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(theme.Countdown, 18, 0.45),
            Child = new TextBlock
            {
                Text = options.Title.ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(34, 0, 34, 0),
            },
        };
        Grid.SetRow(titleBar, 1);
        page.Children.Add(titleBar);

        var statusRow = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

        var questionBadge = new Border
        {
            Width = 176,
            Height = 82,
            Background = Brush(Color.FromArgb(244, 9, 20, 78)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Glow(theme.Accent, 16, 0.45),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "QUESTION",
                        Foreground = Brush(theme.Countdown),
                        FontSize = 19,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"{number} / {total}",
                        Foreground = Brushes.White,
                        FontSize = 31,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        statusRow.Children.Add(questionBadge);

        var phaseText = revealAnswer
            ? "✓"
            : countdownValue is int countdown
                ? countdown.ToString()
                : narrating
                    ? "LISTEN"
                    : options.ShowCountdown ? "" : options.QuestionSeconds.ToString();
        var phaseColor = revealAnswer
            ? theme.CorrectBorder
            : countdownValue.HasValue
                ? theme.Countdown
                : narrating ? theme.Narration : theme.Accent;
        var phase = new Border
        {
            Width = 90,
            Height = 90,
            Background = Brush(Color.FromArgb(244, 9, 20, 78)),
            BorderBrush = Brush(phaseColor),
            BorderThickness = new Thickness(countdownValue.HasValue ? 6 : 3),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = string.IsNullOrWhiteSpace(phaseText) ? Visibility.Hidden : Visibility.Visible,
            Effect = Glow(phaseColor, 20, 0.55),
            Child = new TextBlock
            {
                Text = phaseText,
                Foreground = Brush(phaseColor),
                FontSize = countdownValue.HasValue ? 48 : 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Grid.SetColumn(phase, 2);
        statusRow.Children.Add(phase);
        Grid.SetRow(statusRow, 2);
        page.Children.Add(statusRow);

        var questionPanel = new Border
        {
            Height = 196,
            Background = Brush(Color.FromArgb(246, 8, 15, 65)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(30),
            Margin = new Thickness(170, 4, 170, 18),
            Padding = new Thickness(50, 26, 50, 26),
            Effect = Glow(theme.Accent, 24, 0.50),
            Child = new TextBlock
            {
                Text = question.Question,
                Foreground = Brushes.White,
                FontSize = 50,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(questionPanel, 3);
        page.Children.Add(questionPanel);

        var answers = new Grid
        {
            Margin = new Thickness(120, 0, 120, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
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
            answers.Children.Add(card);
        }
        Grid.SetRow(answers, 4);
        page.Children.Add(answers);

        var footer = new Grid { Margin = new Thickness(120, 8, 120, 0) };
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
                FontSize = emphasizeReveal ? 28 : 23,
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
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (!countdownValue.HasValue)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "Choose A, B, C or D",
                Foreground = Brushes.White,
                FontSize = 21,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (!revealAnswer && !narrating)
        {
            var timerWidth = 430.0;
            var timerFraction = countdownValue is int timerRemaining
                ? Math.Clamp(timerRemaining / (double)options.QuestionSeconds, 0.0, 1.0)
                : 1.0;
            var timerTrack = new Border
            {
                Width = timerWidth,
                Height = 12,
                Background = Brush(Color.FromArgb(150, 35, 45, 100)),
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

        Grid.SetRow(footer, 5);
        page.Children.Add(footer);
        root.Child = page;
        return root;
    }

    private static FrameworkElement BuildVerticalQuestionCard(
        QuizQuestion question,
        int number,
        int total,
        QuizVideoBuildOptions options,
        QuizVisualTheme theme,
        bool revealAnswer,
        int? countdownValue,
        bool emphasizeReveal,
        bool narrating)
    {
        var root = CardRoot(options, theme, transparent: true);
        var page = new Grid { Margin = QuestionCardMargin(options) };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = BuildTopQuizLogo(options, theme);
        Grid.SetRow(logo, 0);
        page.Children.Add(logo);

        var titleBar = new Border
        {
            Height = 64,
            Background = Brush(Color.FromArgb(225, theme.Panel.R, theme.Panel.G, theme.Panel.B)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(30, 6, 30, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = options.Width * 0.72,
            MaxWidth = options.Width * 0.78,
            Child = new TextBlock
            {
                Text = options.Title.ToUpperInvariant(),
                Foreground = Brush(theme.Text),
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(titleBar, 1);
        page.Children.Add(titleBar);

        var statusRow = new Grid { Height = 72, Margin = new Thickness(0, 0, 0, 12) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var questionBadge = new Border
        {
            Width = 176,
            Height = 66,
            Background = Brush(Color.FromArgb(232, theme.Panel.R, theme.Panel.G, theme.Panel.B)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 5, 14, 5),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "QUESTION", Foreground = Brush(theme.Countdown), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = $"{number} / {total}", Foreground = Brush(theme.Text), FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        statusRow.Children.Add(questionBadge);

        var phaseText = revealAnswer ? "✓" : countdownValue is int countdown ? countdown.ToString() : narrating ? "LISTEN" : options.ShowCountdown ? "" : $"{options.QuestionSeconds}s";
        var phaseColor = revealAnswer ? theme.CorrectBorder : countdownValue.HasValue ? theme.Countdown : narrating ? theme.Narration : theme.Muted;
        var phase = new Border
        {
            Width = 92,
            Height = 66,
            Background = Brush(Color.FromArgb(232, theme.Panel.R, theme.Panel.G, theme.Panel.B)),
            BorderBrush = Brush(phaseColor),
            BorderThickness = new Thickness(countdownValue.HasValue ? 5 : 2),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = string.IsNullOrWhiteSpace(phaseText) ? Visibility.Hidden : Visibility.Visible,
            Child = new TextBlock
            {
                Text = phaseText,
                Foreground = Brush(phaseColor),
                FontSize = countdownValue.HasValue ? 40 : 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Grid.SetColumn(phase, 2);
        statusRow.Children.Add(phase);
        Grid.SetRow(statusRow, 2);
        page.Children.Add(statusRow);

        var questionPanel = new Border
        {
            MinHeight = 170,
            Background = Brush(Color.FromArgb(235, theme.Panel.R, theme.Panel.G, theme.Panel.B)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(36, 26, 36, 26),
            Margin = new Thickness(34, 0, 34, 24),
            Child = new TextBlock
            {
                Text = question.Question,
                Foreground = Brush(theme.Text),
                FontSize = 50,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(questionPanel, 3);
        page.Children.Add(questionPanel);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        Grid.SetRow(stack, 4);
        page.Children.Add(stack);

        var footer = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(footer, 5);
        page.Children.Add(footer);

        root.Child = page;
        return root;
    }

    private static FrameworkElement BuildTopQuizLogo(QuizVideoBuildOptions options, QuizVisualTheme theme)
    {
        if (string.IsNullOrWhiteSpace(options.QuizLogoPath))
        {
            return new TextBlock
            {
                Text = "FACTBURST QUIZ",
                Foreground = Brush(theme.Countdown),
                FontSize = options.Vertical ? 42 : 50,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = Glow(theme.Countdown, 14, 0.35),
            };
        }

        var bitmap = LoadBitmap(QuizBranding.ValidateLogoPath(options.QuizLogoPath));
        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Height = options.Vertical ? 104 : 132,
            MaxWidth = options.Vertical ? 380 : 470,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            Effect = Glow(theme.Accent, 16, 0.38),
        };
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
            0 => Color.FromRgb(0, 209, 255),
            1 => Color.FromRgb(201, 72, 255),
            2 => Color.FromRgb(255, 201, 49),
            _ => Color.FromRgb(76, 235, 111),
        };
        var background = correct
            ? Blend(theme.Correct, theme.CorrectBorder, 0.10)
            : Color.FromRgb(9, 17, 68);
        var border = correct ? theme.CorrectBorder : accent;

        var row = new Border
        {
            MinHeight = options.Vertical ? 0 : 174,
            Background = Brush(Color.FromArgb(246, background.R, background.G, background.B)),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(emphasized ? 6 : correct ? 5 : 3),
            CornerRadius = new CornerRadius(options.Vertical ? 24 : 26),
            Padding = new Thickness(options.Vertical ? 22 : 30, options.Vertical ? 18 : 22, options.Vertical ? 28 : 32, options.Vertical ? 18 : 22),
            Margin = new Thickness(0, 0, 0, options.Vertical ? 18 : 0),
            Effect = Glow(border, emphasized ? 30 : 18, emphasized ? 0.75 : 0.48),
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(options.Vertical ? 86 : 92) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new Border
        {
            Width = options.Vertical ? 62 : 68,
            Height = options.Vertical ? 62 : 68,
            Background = Brush(correct ? theme.CorrectBorder : accent),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(correct ? theme.CorrectBorder : accent, 14, 0.6),
            Child = new TextBlock
            {
                Text = correct ? "✓" : ((char)('A' + index)).ToString(),
                Foreground = Brushes.White,
                FontSize = options.Vertical ? 31 : 34,
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
            Foreground = Brushes.White,
            FontSize = emphasized ? (options.Vertical ? 40 : 40) : (options.Vertical ? 36 : 35),
            FontWeight = correct ? FontWeights.Bold : FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(answerText, 1);
        content.Children.Add(answerText);
        row.Child = content;
        return row;
    }

    private static Border CardRoot(QuizVideoBuildOptions options, QuizVisualTheme theme, bool transparent = false)
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
            Background = transparent ? Brushes.Transparent : gradient,
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

        var bitmap = LoadBitmap(QuizBranding.ValidateLogoPath(options.QuizLogoPath));
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

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static DropShadowEffect Glow(Color color, double radius, double opacity) => new()
    {
        Color = color,
        BlurRadius = radius,
        ShadowDepth = 0,
        Opacity = opacity,
    };

    private static Thickness CardMargin(QuizVideoBuildOptions options) => options.Vertical
        ? new Thickness(76, 110, 76, 110)
        : new Thickness(86, 46, 86, 46);

    private static Thickness QuestionCardMargin(QuizVideoBuildOptions options) => options.Vertical
        ? new Thickness(62, 48, 62, 54)
        : new Thickness(64, 20, 64, 28);

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
