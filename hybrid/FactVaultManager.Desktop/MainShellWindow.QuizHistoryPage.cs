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
    private TextBlock? _quizHistoryStatusText;

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
        var root = new Grid { Margin = new Thickness(18, 16, 18, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Quiz History",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        _quizHistoryStatusText = new TextBlock
        {
            Text = "No quiz exports recorded yet.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
        };
        heading.Children.Add(_quizHistoryStatusText);
        header.Children.Add(heading);

        var refresh = new Button
        {
            Content = "Refresh",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        refresh.Click += (_, _) => RefreshQuizHistory();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        _quizHistoryGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            AlternationCount = 2,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "No.",
            Binding = new Binding(nameof(QuizHistorySummary.Id)),
            SortMemberPath = nameof(QuizHistorySummary.Id),
            Width = new DataGridLength(58),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Created",
            Binding = new Binding(nameof(QuizHistorySummary.Created)),
            SortMemberPath = nameof(QuizHistorySummary.Created),
            Width = new DataGridLength(140),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Series",
            Binding = new Binding(nameof(QuizHistorySummary.SeriesName)),
            SortMemberPath = nameof(QuizHistorySummary.SeriesName),
            Width = new DataGridLength(175),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ep.",
            Binding = new Binding(nameof(QuizHistorySummary.EpisodeLabel)),
            SortMemberPath = nameof(QuizHistorySummary.EpisodeNumber),
            Width = new DataGridLength(58),
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
            Width = new DataGridLength(78),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Categories",
            Binding = new Binding(nameof(QuizHistorySummary.Categories)),
            SortMemberPath = nameof(QuizHistorySummary.Categories),
            Width = new DataGridLength(180),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Format",
            Binding = new Binding(nameof(QuizHistorySummary.Format)),
            SortMemberPath = nameof(QuizHistorySummary.Format),
            Width = new DataGridLength(65),
        });
        _quizHistoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Seconds",
            Binding = new Binding(nameof(QuizHistorySummary.QuestionSeconds)),
            SortMemberPath = nameof(QuizHistorySummary.QuestionSeconds),
            Width = new DataGridLength(68),
        });
        _quizHistoryGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Shuffled",
            Binding = new Binding(nameof(QuizHistorySummary.ShuffleAnswers)),
            SortMemberPath = nameof(QuizHistorySummary.ShuffleAnswers),
            Width = new DataGridLength(72),
        });
        _quizHistoryGrid.MouseDoubleClick += QuizHistoryGrid_MouseDoubleClick;
        Grid.SetRow(_quizHistoryGrid, 1);
        root.Children.Add(_quizHistoryGrid);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var view = new Button { Content = "View questions", MinWidth = 110 };
        view.Click += (_, _) => ShowSelectedQuizHistoryQuestions();
        actions.Children.Add(view);
        var publishing = new Button { Content = "View publishing", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        publishing.Click += (_, _) => ShowSelectedQuizPublishingMetadata();
        actions.Children.Add(publishing);
        var openFolder = new Button { Content = "Open export folder", MinWidth = 125, Margin = new Thickness(8, 0, 0, 0) };
        openFolder.Click += (_, _) => OpenSelectedQuizHistoryFolder();
        actions.Children.Add(openFolder);
        var delete = new Button { Content = "Delete quiz", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        delete.Click += (_, _) => DeleteSelectedQuizHistory();
        actions.Children.Add(delete);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return root;
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
            _quizHistoryGrid.ItemsSource = history;
            if (_quizHistoryStatusText is not null)
            {
                var questions = history.Sum(item => item.QuestionCount);
                var seriesCount = history
                    .Select(item => item.SeriesName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                _quizHistoryStatusText.Text = history.Count == 0
                    ? "No quiz exports recorded yet. New successful Resolve exports will appear here."
                    : $"{history.Count:N0} exported quiz{(history.Count == 1 ? "" : "zes")} • {seriesCount:N0} series • {questions:N0} recorded question uses • newest first";
            }
        }
        catch (Exception error)
        {
            if (_quizHistoryStatusText is not null)
                _quizHistoryStatusText.Text = $"Quiz history: {error.Message}";
        }
    }

    private void QuizHistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            ShowSelectedQuizHistoryQuestions();
    }

    private void ShowSelectedQuizHistoryQuestions()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
            return;

        var questions = _data.GetQuizHistoryQuestions(history.Id);
        var dialog = new Window
        {
            Title = $"Quiz History #{history.Id} — {history.Title}",
            Owner = this,
            Width = 980,
            Height = 650,
            MinWidth = 720,
            MinHeight = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dialog.Content = root;

        var series = string.IsNullOrWhiteSpace(history.SeriesName)
            ? "Unnumbered legacy export"
            : $"{history.SeriesName} {history.EpisodeLabel}";
        root.Children.Add(new TextBlock
        {
            Text = $"{series} • {history.Created} • {history.QuestionCount} questions • {history.Categories} • {history.Format} • {history.QuestionSeconds} sec/question",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            AlternationCount = 2,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            ItemsSource = questions,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(QuizHistoryQuestion.Position)), Width = new DataGridLength(48) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Bank No.", Binding = new Binding(nameof(QuizHistoryQuestion.QuestionId)), Width = new DataGridLength(72) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding(nameof(QuizHistoryQuestion.Category)), Width = new DataGridLength(130) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new Binding(nameof(QuizHistoryQuestion.Difficulty)), Width = new DataGridLength(85) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Question", Binding = new Binding(nameof(QuizHistoryQuestion.Question)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
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
