using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryPageInitialized;
    private int _quizHistoryTabIndex = -1;
    private DataGrid? _quizHistoryGrid;
    private TextBlock? _quizHistoryVideoCountText;
    private TextBlock? _quizHistoryShortCountText;
    private TextBlock? _quizHistoryPublishedCountText;
    private TextBlock? _quizHistoryQuestionUseCountText;

    private void InitializeQuizHistoryPage()
    {
        if (_quizHistoryPageInitialized || MainTabs is null)
            return;

        _quizHistoryPageInitialized = true;
        var tab = new TabItem { Content = BuildQuizHistoryPage() };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
            tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _quizHistoryTabIndex = MainTabs.Items.Count - 1;
        AddQuizHistoryNavigationButton(_quizHistoryTabIndex);
        RefreshQuizHistory();
    }

    private FrameworkElement BuildQuizHistoryPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Quiz History",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55,
            },
        });

        var refresh = new Button
        {
            Content = "Refresh",
            MinWidth = 88,
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += (_, _) => RefreshQuizHistory();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var videos = BuildQuizHistoryStatCard("Videos", Color.FromRgb(0, 204, 255));
        _quizHistoryVideoCountText = videos.Value;
        videos.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(videos.Card);

        var shorts = BuildQuizHistoryStatCard("Shorts", Color.FromRgb(204, 70, 255));
        _quizHistoryShortCountText = shorts.Value;
        shorts.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(shorts.Card, 1);
        stats.Children.Add(shorts.Card);

        var published = BuildQuizHistoryStatCard("Published", Color.FromRgb(255, 202, 45));
        _quizHistoryPublishedCountText = published.Value;
        published.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(published.Card, 2);
        stats.Children.Add(published.Card);

        var questionUses = BuildQuizHistoryStatCard("Questions used", Color.FromRgb(70, 235, 115));
        _quizHistoryQuestionUseCountText = questionUses.Value;
        questionUses.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(questionUses.Card, 3);
        stats.Children.Add(questionUses.Card);

        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _quizHistoryGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            MinRowHeight = 42,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(62, 20, 37)),
        };

        var historyCellStyle = new Style(typeof(DataGridCell));
        historyCellStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(62, 20, 37))));
        historyCellStyle.Setters.Add(new Setter(
            Control.ForegroundProperty,
            Brushes.White));
        historyCellStyle.Setters.Add(new Setter(
            Control.PaddingProperty,
            new Thickness(9, 6, 9, 6)));
        historyCellStyle.Setters.Add(new Setter(
            Control.VerticalContentAlignmentProperty,
            VerticalAlignment.Center));
        historyCellStyle.Setters.Add(new Setter(
            DataGridCell.BorderThicknessProperty,
            new Thickness(0)));
        var publishedCellTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(QuizHistorySummary.PublishedOnYouTube)),
            Value = true,
        };
        publishedCellTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(8, 68, 52))));
        historyCellStyle.Triggers.Add(publishedCellTrigger);
        var selectedCellTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true,
        };
        selectedCellTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selectedCellTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        historyCellStyle.Triggers.Add(selectedCellTrigger);
        _quizHistoryGrid.CellStyle = historyCellStyle;

        var historyHeaderStyle = new Style(typeof(DataGridColumnHeader));
        historyHeaderStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        historyHeaderStyle.Setters.Add(new Setter(
            Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        historyHeaderStyle.Setters.Add(new Setter(
            Control.FontWeightProperty,
            FontWeights.SemiBold));
        historyHeaderStyle.Setters.Add(new Setter(
            Control.PaddingProperty,
            new Thickness(9, 9, 9, 9)));
        historyHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 204, 255))));
        historyHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderThicknessProperty,
            new Thickness(0, 0, 0, 1)));
        _quizHistoryGrid.ColumnHeaderStyle = historyHeaderStyle;

        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "No.",
            Binding = new Binding(nameof(QuizHistorySummary.Id)),
            SortMemberPath = nameof(QuizHistorySummary.Id),
            Width = new DataGridLength(55),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Date",
            Binding = new Binding(nameof(QuizHistorySummary.CreatedDisplay)),
            SortMemberPath = nameof(QuizHistorySummary.Created),
            Width = new DataGridLength(104),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Type",
            Binding = new Binding(nameof(QuizHistorySummary.VideoType)),
            SortMemberPath = nameof(QuizHistorySummary.Format),
            Width = new DataGridLength(72),
        });
        _quizHistoryGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Published",
            Binding = new Binding(nameof(QuizHistorySummary.PublishedOnYouTube)),
            SortMemberPath = nameof(QuizHistorySummary.PublishedOnYouTube),
            Width = new DataGridLength(82),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Series",
            Binding = new Binding(nameof(QuizHistorySummary.SeriesName)),
            SortMemberPath = nameof(QuizHistorySummary.SeriesName),
            Width = new DataGridLength(190),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ep.",
            Binding = new Binding(nameof(QuizHistorySummary.EpisodeLabel)),
            SortMemberPath = nameof(QuizHistorySummary.EpisodeNumber),
            Width = new DataGridLength(60),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "YouTube title",
            Binding = new Binding(nameof(QuizHistorySummary.YouTubeTitle)),
            SortMemberPath = nameof(QuizHistorySummary.YouTubeTitle),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Questions",
            Binding = new Binding(nameof(QuizHistorySummary.QuestionCount)),
            SortMemberPath = nameof(QuizHistorySummary.QuestionCount),
            Width = new DataGridLength(84),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Categories",
            Binding = new Binding(nameof(QuizHistorySummary.Categories)),
            SortMemberPath = nameof(QuizHistorySummary.Categories),
            Width = new DataGridLength(175),
        });
        _quizHistoryGrid.MouseDoubleClick += QuizHistoryGrid_MouseDoubleClick;

        var tableCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255),
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.4,
            },
            Child = _quizHistoryGrid,
        };
        Grid.SetRow(tableCard, 2);
        root.Children.Add(tableCard);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "Double-click a quiz to update its YouTube link and published status.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var view = new Button { Content = "Questions", MinWidth = 92, ToolTip = "View the questions used in the selected quiz" };
        StyleQuizHistoryButton(view, Color.FromRgb(0, 204, 255));
        view.Click += (_, _) => ShowSelectedQuizHistoryQuestions();
        actions.Children.Add(view);
        var publishing = new Button { Content = "Publishing", MinWidth = 92, Margin = new Thickness(8, 0, 0, 0), ToolTip = "View saved YouTube publishing metadata" };
        StyleQuizHistoryButton(publishing, Color.FromRgb(204, 70, 255));
        publishing.Click += (_, _) => ShowSelectedQuizPublishingMetadata();
        actions.Children.Add(publishing);
        var youtube = new Button { Content = "YouTube", MinWidth = 86, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Update the YouTube link and published status" };
        StyleQuizHistoryButton(youtube, Color.FromRgb(255, 202, 45));
        youtube.Click += (_, _) => ShowSelectedQuizYouTubePublication();
        actions.Children.Add(youtube);
        var openFolder = new Button { Content = "Folder", MinWidth = 78, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Open the selected quiz export folder" };
        StyleQuizHistoryButton(openFolder, Color.FromRgb(70, 235, 115));
        openFolder.Click += (_, _) => OpenSelectedQuizHistoryFolder();
        actions.Children.Add(openFolder);
        var delete = new Button
        {
            Content = "Delete",
            MinWidth = 78,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
            ToolTip = "Delete the selected quiz-history entry",
        };
        StyleQuizHistoryButton(delete, Color.FromRgb(248, 90, 105));
        delete.Click += (_, _) => DeleteSelectedQuizHistory();
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.58),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private static void StyleQuizHistoryButton(Button button, Color accent)
    {
        button.Background = new SolidColorBrush(Color.FromRgb(13, 18, 78));
        button.BorderBrush = new SolidColorBrush(accent);
        button.BorderThickness = new Thickness(2);
        button.Foreground = Brushes.White;
        button.FontWeight = FontWeights.SemiBold;
        button.Padding = new Thickness(12, 6, 12, 6);
        button.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = accent,
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = 0.4,
        };
    }

    private static (Border Card, TextBlock Value) BuildQuizHistoryStatCard(string label, Color accent)
    {
        var value = new TextBlock
        {
            Text = "0",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        var content = new StackPanel();
        content.Children.Add(value);
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 2, 0, 0),
        });
        return (
            new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 18, 78)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 12, 16, 12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accent,
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.58,
                },
                Child = content,
            },
            value);
    }

    private void AddQuizHistoryNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;
        var questionsButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizQuestionBankTabIndex.ToString(), StringComparison.Ordinal));
        if (questionsButton?.Parent is not StackPanel navigation)
            return;

        var historyButton = new Button
        {
            Content = "↻   Quiz History",
            Tag = tabIndex.ToString(),
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            historyButton.Style = navStyle;
        historyButton.Click += Navigate_Click;
        var questionsIndex = navigation.Children.IndexOf(questionsButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, questionsIndex + 1), historyButton);
    }

    private void RefreshQuizHistory()
    {
        if (!_quizHistoryPageInitialized || _quizHistoryGrid is null)
            return;

        try
        {
            var history = _data.GetQuizHistory();
            var statistics = QuizHistoryStatistics.Calculate(history);
            _quizHistoryGrid.ItemsSource = history;
            if (_quizHistoryVideoCountText is not null)
                _quizHistoryVideoCountText.Text = statistics.Videos.ToString("N0");
            if (_quizHistoryShortCountText is not null)
                _quizHistoryShortCountText.Text = statistics.Shorts.ToString("N0");
            if (_quizHistoryPublishedCountText is not null)
                _quizHistoryPublishedCountText.Text = statistics.Published.ToString("N0");
            if (_quizHistoryQuestionUseCountText is not null)
                _quizHistoryQuestionUseCountText.Text = statistics.QuestionsUsed.ToString("N0");
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Quiz history: {error.Message}");
        }
    }

    private void QuizHistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            ShowSelectedQuizYouTubePublication();
    }

    private void ShowSelectedQuizHistoryQuestions()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
            return;

        var questions = _data.GetQuizHistoryQuestions(history.Id);
        var dialog = new Window
        {
            Title = $"Questions — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 1080,
            Height = 700,
            MinWidth = 780,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        var series = string.IsNullOrWhiteSpace(history.SeriesName)
            ? "Unnumbered legacy export"
            : $"{history.SeriesName} {history.EpisodeLabel}";
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock
        {
            Text = series,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"{history.CreatedDisplay}  •  {history.QuestionCount} questions  •  {history.Format}  •  {history.QuestionSeconds} seconds per question",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"Categories: {history.Categories}",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 13, 16, 13),
            Child = summary,
        });

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            AlternationCount = 2,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            MinRowHeight = 42,
            Margin = new Thickness(0, 14, 0, 12),
            ItemsSource = questions,
        };
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        grid.CellStyle = cellStyle;
        var questionTextStyle = new Style(typeof(TextBlock));
        questionTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        questionTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(QuizHistoryQuestion.Position)),
            Width = new DataGridLength(52),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Bank No.",
            Binding = new Binding(nameof(QuizHistoryQuestion.QuestionId)),
            Width = new DataGridLength(82),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Category",
            Binding = new Binding(nameof(QuizHistoryQuestion.Category)),
            Width = new DataGridLength(150),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Level",
            Binding = new Binding(nameof(QuizHistoryQuestion.Difficulty)),
            Width = new DataGridLength(90),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(QuizHistoryQuestion.Question)),
            ElementStyle = questionTextStyle,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var close = new Button
        {
            Content = "Close",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true,
        };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);
        dialog.ShowDialog();
    }

    private void ShowSelectedQuizPublishingMetadata()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
            return;
        if (string.IsNullOrWhiteSpace(history.YouTubeTitle))
        {
            MessageBox.Show(
                this,
                "This is a legacy quiz-history entry and does not have publishing metadata recorded.",
                "Quiz Publishing",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = $"Publishing — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 760,
            Height = 680,
            MinWidth = 620,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = stack;
        dialog.Content = scroll;

        stack.Children.Add(new TextBlock
        {
            Text = $"{history.SeriesName} {history.EpisodeLabel}",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Publishing metadata saved with this successful quiz export.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 12),
        });
        AddQuizHistoryPublishingField(stack, "YouTube title", history.YouTubeTitle, 58);
        AddQuizHistoryPublishingField(stack, "Description", history.YouTubeDescription, 180);
        AddQuizHistoryPublishingField(stack, "Hashtags", history.Hashtags, 58);
        AddQuizHistoryPublishingField(stack, "Pinned comment", history.PinnedComment, 95);

        var copy = new Button
        {
            Content = "Copy all metadata",
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 12, 0, 0),
        };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(
                $"TITLE\n{history.YouTubeTitle}\n\nDESCRIPTION\n{history.YouTubeDescription}\n\nHASHTAGS\n{history.Hashtags}\n\nPINNED COMMENT\n{history.PinnedComment}");
        };
        stack.Children.Add(copy);
        dialog.ShowDialog();
    }

    private static void AddQuizHistoryPublishingField(Panel parent, string label, string value, double height)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
        });
        parent.Children.Add(new TextBox
        {
            Text = value,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = height,
        });
    }

    private void ShowSelectedQuizYouTubePublication()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
            return;

        var dialog = new Window
        {
            Title = $"YouTube — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 580,
            Height = 290,
            MinWidth = 500,
            MinHeight = 260,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        root.Children.Add(new TextBlock
        {
            Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var published = new CheckBox
        {
            Content = "Published on YouTube",
            IsChecked = history.PublishedOnYouTube,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(published, 1);
        root.Children.Add(published);

        var label = new TextBlock
        {
            Text = "YouTube video link",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(label, 2);
        root.Children.Add(label);

        var url = new TextBox
        {
            Text = history.YouTubeUrl,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(url, 3);
        root.Children.Add(url);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var open = new Button { Content = "Open video", MinWidth = 95 };
        open.Click += (_, _) =>
        {
            try
            {
                var videoUrl = QuizYouTubePublication.NormalizeUrl(url.Text);
                if (videoUrl.Length == 0)
                    throw new InvalidOperationException("Enter the YouTube video link first.");
                Process.Start(new ProcessStartInfo(videoUrl) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Open YouTube Video", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        actions.Children.Add(open);
        var cancel = new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        actions.Children.Add(cancel);
        var save = new Button { Content = "Save", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                if (!_data.UpdateQuizHistoryYouTubePublication(history.Id, published.IsChecked == true, url.Text))
                    throw new InvalidOperationException("The selected quiz-history entry no longer exists.");
                dialog.DialogResult = true;
                RefreshQuizHistory();
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Save YouTube Status", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        actions.Children.Add(save);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        dialog.ShowDialog();
    }

    private void OpenSelectedQuizHistoryFolder()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history || string.IsNullOrWhiteSpace(history.ProjectFolder))
            return;
        try
        {
            if (!Directory.Exists(history.ProjectFolder))
                throw new DirectoryNotFoundException("The recorded quiz export folder no longer exists.");
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{history.ProjectFolder}\"") { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Open Quiz Export Folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
