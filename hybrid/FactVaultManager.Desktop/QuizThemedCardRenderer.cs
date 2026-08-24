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
    double LogoScale = 1.0,
    string QuizType = QuizTypeCatalog.Standard)
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
            QuizType = QuizTypeCatalog.Normalize(QuizType),
        };
    }
}

public sealed class QuizThemedCardRenderer
{
    private static readonly Color NeonBlue = Color.FromRgb(0, 204, 255);
    private static readonly Color NeonPurple = Color.FromRgb(204, 70, 255);
    private static readonly Color NeonGold = Color.FromRgb(255, 202, 45);
    private static readonly Color NeonGreen = Color.FromRgb(70, 235, 115);
    private static readonly Color DeepPanel = Color.FromRgb(8, 14, 62);
    private static readonly Color DeepPanel2 = Color.FromRgb(13, 18, 78);

    internal static (double Width, double Height, Thickness Padding) QuestionBadgeLayout(bool vertical) =>
        vertical
            ? (220, 80, new Thickness(24, 10, 24, 10))
            : (210, 90, new Thickness(18, 6, 18, 6));

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

        RenderCard(
            BuildTitleCard(options.Title, options, visual),
            Path.Combine(cardsFolder, "000_intro.png"),
            options.Width,
            options.Height);

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
                    BuildQuestionCard(question, number, questions.Count, options, visual, revealAnswer: false, countdownValue: null, emphasizeReveal: false, narrating: true),
                    Path.Combine(cardsFolder, $"{number:000}_narration.png"),
                    options.Width,
                    options.Height);
            }

            if (answerLeadSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, revealAnswer: false, countdownValue: null, emphasizeReveal: false, narrating: false),
                    Path.Combine(cardsFolder, $"{number:000}_question.png"),
                    options.Width,
                    options.Height);
            }

            for (var remaining = countdownSeconds; remaining >= 1; remaining--)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, revealAnswer: false, countdownValue: remaining, emphasizeReveal: false, narrating: false),
                    Path.Combine(cardsFolder, $"{number:000}_countdown_{remaining}.png"),
                    options.Width,
                    options.Height);
            }

            if (options.RevealEmphasisSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, revealAnswer: true, countdownValue: null, emphasizeReveal: true, narrating: false),
                    Path.Combine(cardsFolder, $"{number:000}_answer_reveal.png"),
                    options.Width,
                    options.Height);
            }

            if (options.AnswerSeconds - options.RevealEmphasisSeconds > 0)
            {
                RenderCard(
                    BuildQuestionCard(question, number, questions.Count, options, visual, revealAnswer: true, countdownValue: null, emphasizeReveal: false, narrating: false),
                    Path.Combine(cardsFolder, $"{number:000}_answer.png"),
                    options.Width,
                    options.Height);
            }
        }

        RenderCard(
            BuildOutroCard(options, visual),
            Path.Combine(cardsFolder, "999_outro.png"),
            options.Width,
            options.Height);

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
        if (visual.QuizType == QuizTypeCatalog.Logo)
            return BuildLogoQuestionCard(question, number, total, options, theme, revealAnswer, countdownValue, emphasizeReveal, narrating);

        return options.Vertical
            ? BuildVerticalQuestionCard(question, number, total, options, theme, revealAnswer, countdownValue, emphasizeReveal, narrating)
            : BuildLandscapeGameShowCard(question, number, total, options, theme, revealAnswer, countdownValue, emphasizeReveal, narrating);
    }

    private static FrameworkElement BuildLogoQuestionCard(
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
        var imagePath = QuizQuestionImage.ValidatePath(question.ImagePath, allowEmpty: false);
        var root = CardRoot(options, theme, transparent: true);
        var page = new Grid
        {
            Margin = options.Vertical
                ? new Thickness(54, 38, 54, 46)
                : new Thickness(62, 24, 62, 28),
        };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(options.Vertical ? 118 : 150) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(options.Vertical ? 94 : 78) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(options.Vertical ? 540 : 304) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(options.Vertical ? 230 : 148) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(options.Vertical ? 58 : 42) });

        var brandLogo = BuildHeroLogo(options, theme);
        Grid.SetRow(brandLogo, 0);
        page.Children.Add(brandLogo);

        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Children.Add(new Border
        {
            Background = Brush(Color.FromArgb(242, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(24, 8, 24, 8),
            Child = new TextBlock
            {
                Text = $"LOGO {number} / {total}",
                Foreground = Brushes.White,
                FontSize = options.Vertical ? 28 : 24,
                FontWeight = FontWeights.Bold,
            },
        });
        var heading = new TextBlock
        {
            Text = QuizPublishMetadataGenerator.DisplayName(options.Title).ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 30 : 28,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 18, 0),
            Effect = Glow(NeonBlue, 16, 0.45),
        };
        Grid.SetColumn(heading, 1);
        status.Children.Add(heading);
        var phaseText = revealAnswer
            ? "✓"
            : countdownValue is int remaining
                ? remaining.ToString()
                : narrating
                    ? "LISTEN"
                    : options.ShowCountdown ? "" : options.QuestionSeconds.ToString();
        var phaseColor = revealAnswer ? NeonGreen : countdownValue.HasValue ? NeonGold : narrating ? NeonPurple : NeonBlue;
        var phase = BuildCountdownRing(phaseText, phaseColor, countdownValue.HasValue, narrating);
        phase.Width = options.Vertical ? 82 : 72;
        phase.Height = options.Vertical ? 82 : 72;
        Grid.SetColumn(phase, 2);
        status.Children.Add(phase);
        Grid.SetRow(status, 1);
        page.Children.Add(status);

        var image = new Image
        {
            Source = LoadBitmap(imagePath),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(18),
        };
        var imagePanel = new Border
        {
            Background = Brush(Color.FromArgb(242, 245, 247, 255)),
            BorderBrush = Brush(NeonGold),
            BorderThickness = new Thickness(options.Vertical ? 5 : 4),
            CornerRadius = new CornerRadius(34),
            Margin = new Thickness(options.Vertical ? 26 : 170, 8, options.Vertical ? 26 : 170, 8),
            Effect = Glow(NeonGold, 28, 0.55),
            Child = image,
        };
        Grid.SetRow(imagePanel, 2);
        page.Children.Add(imagePanel);

        var questionPanel = new Border
        {
            Background = Brush(Color.FromArgb(248, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(30),
            Padding = new Thickness(options.Vertical ? 34 : 54, 20, options.Vertical ? 34 : 54, 20),
            Margin = new Thickness(0, 8, 0, 8),
            Effect = Glow(NeonBlue, 24, 0.55),
            Child = BuildFittedQuestionText(
                question.Question,
                options.Vertical ? 48 : 44,
                options.Vertical ? 850 : 1320),
        };
        Grid.SetRow(questionPanel, 3);
        page.Children.Add(questionPanel);

        var answers = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        if (options.Vertical)
        {
            for (var row = 0; row < 4; row++)
                answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            answers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            answers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var index = 0; index < 4; index++)
        {
            var answer = BuildGameShowAnswer(
                question.Answers[index],
                index,
                revealAnswer && index == question.CorrectIndex,
                emphasizeReveal && index == question.CorrectIndex);
            if (options.Vertical)
            {
                answer.Margin = new Thickness(0, 5, 0, 5);
                Grid.SetRow(answer, index);
            }
            else
            {
                Grid.SetColumn(answer, index % 2 == 0 ? 0 : 2);
                Grid.SetRow(answer, index < 2 ? 0 : 2);
            }
            answers.Children.Add(answer);
        }
        Grid.SetRow(answers, 4);
        page.Children.Add(answers);

        var footer = BuildFooter(question, options, revealAnswer, emphasizeReveal, narrating, countdownValue);
        footer.Width = double.NaN;
        Grid.SetRow(footer, 5);
        page.Children.Add(footer);

        root.Child = page;
        return root;
    }

    private static FrameworkElement BuildLandscapeGameShowCard(
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
        var stage = new Grid
        {
            Margin = new Thickness(62, 16, 62, 24),
        };

        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        var logo = BuildHeroLogo(options, theme);
        Grid.SetRow(logo, 0);
        stage.Children.Add(logo);

        var title = new Border
        {
            Width = 1110,
            Height = 82,
            Background = Brush(Color.FromArgb(244, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonGold),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(NeonGold, 24, 0.55),
            Child = new TextBlock
            {
                Text = QuizPublishMetadataGenerator.DisplayName(options.Title).ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = 40,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 980,
            },
        };
        Grid.SetRow(title, 1);
        stage.Children.Add(title);

        var statusLayer = new Grid();
        statusLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        statusLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

        var questionBadgeLayout = QuestionBadgeLayout(vertical: false);
        var questionBadge = new Border
        {
            Width = questionBadgeLayout.Width,
            Height = questionBadgeLayout.Height,
            Padding = questionBadgeLayout.Padding,
            Background = Brush(Color.FromArgb(246, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(24),
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Glow(NeonBlue, 20, 0.55),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "QUESTION",
                        Foreground = Brush(NeonGold),
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"{number} / {total}",
                        Foreground = Brushes.White,
                        FontSize = 32,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        statusLayer.Children.Add(questionBadge);

        var phaseText = revealAnswer
            ? "✓"
            : countdownValue is int remaining
                ? remaining.ToString()
                : narrating
                    ? "LISTEN"
                    : options.ShowCountdown
                        ? ""
                        : options.QuestionSeconds.ToString();

        var phaseColor = revealAnswer
            ? NeonGreen
            : countdownValue.HasValue
                ? NeonGold
                : narrating
                    ? NeonPurple
                    : NeonBlue;

        var phase = BuildCountdownRing(phaseText, phaseColor, countdownValue.HasValue, narrating);
        Grid.SetColumn(phase, 2);
        statusLayer.Children.Add(phase);

        Grid.SetRow(statusLayer, 2);
        stage.Children.Add(statusLayer);

        var questionPanel = new Border
        {
            Width = 1240,
            Height = 198,
            Background = Brush(Color.FromArgb(248, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(34),
            Padding = new Thickness(60, 28, 60, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(NeonBlue, 30, 0.62),
            Child = BuildFittedQuestionText(question.Question, 54, 1090),
        };
        Grid.SetRow(questionPanel, 3);
        stage.Children.Add(questionPanel);

        var answerGrid = new Grid
        {
            Width = 1400,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        answerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        answerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        answerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        answerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        answerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        answerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < 4; index++)
        {
            var answer = BuildGameShowAnswer(
                question.Answers[index],
                index,
                revealAnswer && index == question.CorrectIndex,
                emphasizeReveal && index == question.CorrectIndex);

            Grid.SetColumn(answer, index % 2 == 0 ? 0 : 2);
            Grid.SetRow(answer, index < 2 ? 0 : 2);
            answerGrid.Children.Add(answer);
        }

        Grid.SetRow(answerGrid, 4);
        stage.Children.Add(answerGrid);

        var footer = BuildFooter(question, options, revealAnswer, emphasizeReveal, narrating, countdownValue);
        Grid.SetRow(footer, 5);
        stage.Children.Add(footer);

        root.Child = stage;
        return root;
    }

    internal static Viewbox BuildFittedQuestionText(string text, double fontSize, double maxWidth)
    {
        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth,
            },
        };
    }

    private static Border BuildGameShowAnswer(string answer, int index, bool correct, bool emphasized)
    {
        var accent = index switch
        {
            0 => NeonBlue,
            1 => NeonPurple,
            2 => NeonGold,
            _ => NeonGreen,
        };

        var borderColor = correct ? NeonGreen : accent;
        var fill = correct ? Color.FromRgb(9, 72, 55) : DeepPanel2;

        var card = new Border
        {
            Background = Brush(Color.FromArgb(248, fill.R, fill.G, fill.B)),
            BorderBrush = Brush(borderColor),
            BorderThickness = new Thickness(emphasized ? 7 : correct ? 5 : 3),
            CornerRadius = new CornerRadius(28),
            Padding = new Thickness(28, 20, 32, 20),
            Effect = Glow(borderColor, emphasized ? 34 : 22, emphasized ? 0.85 : 0.58),
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new Border
        {
            Width = 68,
            Height = 68,
            Background = Brush(borderColor),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(borderColor, 16, 0.70),
            Child = new TextBlock
            {
                Text = correct ? "✓" : ((char)('A' + index)).ToString(),
                Foreground = Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        content.Children.Add(marker);

        var text = new TextBlock
        {
            Text = answer,
            Foreground = Brushes.White,
            FontSize = emphasized ? 40 : 36,
            FontWeight = correct ? FontWeights.Bold : FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 44,
        };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        card.Child = content;
        return card;
    }

    private static Border BuildCountdownRing(string text, Color color, bool countdown, bool narrating)
    {
        var outer = new Border
        {
            Width = 98,
            Height = 98,
            Background = Brush(Color.FromArgb(150, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(color),
            BorderThickness = new Thickness(countdown ? 7 : 4),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Hidden : Visibility.Visible,
            Effect = Glow(color, 28, 0.75),
        };

        var inner = new Border
        {
            Margin = new Thickness(8),
            Background = Brush(Color.FromArgb(225, 7, 12, 54)),
            BorderBrush = Brush(Color.FromArgb(150, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush(color),
                FontSize = countdown ? 50 : narrating ? 19 : 30,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };

        outer.Child = inner;
        return outer;
    }

    private static Grid BuildFooter(
        QuizQuestion question,
        QuizVideoBuildOptions options,
        bool revealAnswer,
        bool emphasizeReveal,
        bool narrating,
        int? countdownValue)
    {
        var footer = new Grid
        {
            Width = 1400,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (revealAnswer)
        {
            footer.Children.Add(new TextBlock
            {
                Text = $"{question.CorrectLetter}. {question.CorrectAnswer}",
                Foreground = Brush(NeonGreen),
                FontSize = emphasizeReveal ? 28 : 22,
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
                Foreground = Brush(NeonPurple),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
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
            var timerFraction = countdownValue is int remaining
                ? Math.Clamp(remaining / (double)options.QuestionSeconds, 0.0, 1.0)
                : 1.0;

            var timer = new Border
            {
                Width = timerWidth,
                Height = 12,
                Background = Brush(Color.FromArgb(145, 29, 39, 104)),
                CornerRadius = new CornerRadius(999),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Border
                {
                    Width = Math.Max(1, timerWidth * timerFraction),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = Brush(countdownValue.HasValue ? NeonGold : NeonBlue),
                    CornerRadius = new CornerRadius(999),
                    Effect = Glow(countdownValue.HasValue ? NeonGold : NeonBlue, 10, 0.5),
                },
            };
            Grid.SetColumn(timer, 1);
            footer.Children.Add(timer);
        }

        return footer;
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
        var page = new Grid { Margin = new Thickness(64, 54, 64, 58) };

        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var logo = BuildHeroLogo(options, theme);
        Grid.SetRow(logo, 0);
        page.Children.Add(logo);

        var title = new Border
        {
            Background = Brush(Color.FromArgb(240, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonGold),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(24, 8, 24, 8),
            Child = new TextBlock
            {
                Text = QuizPublishMetadataGenerator.DisplayName(options.Title).ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        Grid.SetRow(title, 1);
        page.Children.Add(title);

        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var questionBadgeLayout = QuestionBadgeLayout(vertical: true);
        var badge = new Border
        {
            Width = questionBadgeLayout.Width,
            Height = questionBadgeLayout.Height,
            Padding = questionBadgeLayout.Padding,
            Background = Brush(DeepPanel),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(22),
            Child = new TextBlock
            {
                Text = $"QUESTION  {number} / {total}",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        status.Children.Add(badge);

        var phaseText = revealAnswer ? "✓" : countdownValue?.ToString() ?? (narrating ? "LISTEN" : "");
        var phaseColor = revealAnswer ? NeonGreen : countdownValue.HasValue ? NeonGold : NeonPurple;
        var phase = BuildCountdownRing(phaseText, phaseColor, countdownValue.HasValue, narrating);
        Grid.SetColumn(phase, 2);
        status.Children.Add(phase);

        Grid.SetRow(status, 2);
        page.Children.Add(status);

        var questionPanel = new Border
        {
            Background = Brush(Color.FromArgb(248, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonBlue),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(30),
            Padding = new Thickness(38, 26, 38, 26),
            Margin = new Thickness(0, 10, 0, 24),
            Effect = Glow(NeonBlue, 24, 0.55),
            Child = BuildFittedQuestionText(
                question.Question,
                48,
                Math.Max(1, options.Width - 204)),
        };
        Grid.SetRow(questionPanel, 3);
        page.Children.Add(questionPanel);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            var answer = BuildGameShowAnswer(
                question.Answers[i],
                i,
                revealAnswer && i == question.CorrectIndex,
                emphasizeReveal && i == question.CorrectIndex);
            answer.MinHeight = 190;
            answer.Margin = new Thickness(0, 0, 0, 20);
            stack.Children.Add(answer);
        }

        Grid.SetRow(stack, 4);
        page.Children.Add(stack);

        root.Child = page;
        return root;
    }

    private static FrameworkElement BuildHeroLogo(QuizVideoBuildOptions options, QuizVisualTheme theme)
    {
        if (!string.IsNullOrWhiteSpace(options.QuizLogoPath))
        {
            var bitmap = LoadBitmap(QuizBranding.ValidateLogoPath(options.QuizLogoPath));
            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Height = options.Vertical ? 112 : 146,
                MaxWidth = options.Vertical ? 390 : 520,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                Effect = Glow(NeonBlue, 22, 0.55),
            };
        }

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new TextBlock
        {
            Text = "💡",
            FontSize = options.Vertical ? 46 : 52,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "FACTBURST QUIZ",
            Foreground = Brush(NeonGold),
            FontSize = options.Vertical ? 40 : 48,
            FontWeight = FontWeights.Black,
            TextAlignment = TextAlignment.Center,
            Effect = Glow(NeonGold, 18, 0.55),
        });
        return stack;
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

        content.Children.Add(BuildHeroLogo(options, theme));
        content.Children.Add(new Border
        {
            Background = Brush(Color.FromArgb(238, DeepPanel.R, DeepPanel.G, DeepPanel.B)),
            BorderBrush = Brush(NeonGold),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(36, 12, 36, 12),
            Margin = new Thickness(0, 30, 0, 24),
            Effect = Glow(NeonGold, 20, 0.5),
            Child = new TextBlock
            {
                Text = "QUIZ TIME",
                Foreground = Brush(NeonGold),
                FontSize = options.Vertical ? 36 : 32,
                FontWeight = FontWeights.Bold,
            },
        });

        content.Children.Add(new TextBlock
        {
            Text = QuizPublishMetadataGenerator.DisplayName(title),
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 76 : 70,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.82,
        });

        content.Children.Add(new TextBlock
        {
            Text = "ARE YOU READY?",
            Foreground = Brush(NeonBlue),
            FontSize = options.Vertical ? 34 : 30,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 26, 0, 0),
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

        content.Children.Add(BuildHeroLogo(options, theme));
        content.Children.Add(new TextBlock
        {
            Text = "👍  Like this quiz? Subscribe for more!",
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 62 : 58,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.84,
            Margin = new Thickness(0, 34, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Like the video and share your score in the comments",
            Foreground = Brush(NeonBlue),
            FontSize = options.Vertical ? 34 : 30,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 28, 0, 0),
        });

        root.Child = content;
        return root;
    }

    private static Border CardRoot(QuizVideoBuildOptions options, QuizVisualTheme theme, bool transparent = false)
    {
        var gradient = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Blend(theme.Background, NeonBlue, 0.18), 0),
                new(theme.Background, 0.42),
                new(Blend(theme.Background, NeonPurple, 0.16), 1),
            },
            new Point(0, 0),
            new Point(1, 1));

        return new Border
        {
            Width = options.Width,
            Height = options.Height,
            Background = transparent ? Brushes.Transparent : gradient,
            BorderBrush = Brush(Color.FromArgb(110, NeonBlue.R, NeonBlue.G, NeonBlue.B)),
            BorderThickness = new Thickness(options.Vertical ? 8 : 6),
        };
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
            root["quiz_type"] = visual.QuizType;

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
