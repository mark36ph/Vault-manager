using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private DataGrid? _quizDuplicatesGrid;
    private TextBlock? _quizDuplicatesStatusText;
    private TabItem? _quizDuplicatesTab;

    private void EnsureQuizDuplicatesTab(TabControl tabs)
    {
        if (_quizDuplicatesTab is not null || tabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "Duplicates", StringComparison.OrdinalIgnoreCase)))
            return;

        _quizDuplicatesTab = new TabItem
        {
            Header = "Duplicates",
            Content = BuildQuizDuplicatesPanel(),
        };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            _quizDuplicatesTab.Style = sectionStyle;

        tabs.Items.Insert(Math.Min(2, tabs.Items.Count), _quizDuplicatesTab);
        tabs.SelectionChanged += (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.Source, tabs) && ReferenceEquals(tabs.SelectedItem, _quizDuplicatesTab))
                RefreshQuizDuplicateSection();
        };
    }

    private FrameworkElement BuildQuizDuplicatesPanel()
    {
        var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Duplicate review",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        _quizDuplicatesStatusText = new TextBlock
        {
            Text = "Open this tab to scan the question bank for likely duplicates.",
            Foreground = QuizMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        };
        heading.Children.Add(_quizDuplicatesStatusText);
        header.Children.Add(heading);

        var refresh = new Button
        {
            Content = "Scan duplicates",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        refresh.Click += (_, _) => RefreshQuizDuplicateSection();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        _quizDuplicatesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 34,
            RowBackground = Brushes.White,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 248, 252)),
            AlternationCount = 2,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 234, 240)),
        };
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Match",
            Binding = new Binding(nameof(QuizDuplicateCandidate.MatchType)),
            Width = new DataGridLength(90),
        });
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Keep ID",
            Binding = new Binding(nameof(QuizDuplicateCandidate.KeepId)),
            Width = new DataGridLength(65),
        });
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Keep question",
            Binding = new Binding(nameof(QuizDuplicateCandidate.KeepQuestion)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Duplicate ID",
            Binding = new Binding(nameof(QuizDuplicateCandidate.DuplicateId)),
            Width = new DataGridLength(85),
        });
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Duplicate question",
            Binding = new Binding(nameof(QuizDuplicateCandidate.DuplicateQuestion)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizDuplicatesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Answer",
            Binding = new Binding(nameof(QuizDuplicateCandidate.CorrectAnswer)),
            Width = new DataGridLength(125),
        });
        Grid.SetRow(_quizDuplicatesGrid, 1);
        root.Children.Add(_quizDuplicatesGrid);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var delete = new Button
        {
            Content = "Delete selected duplicate",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(180, 35, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 35, 35)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Deletes the newer duplicate while keeping the earlier question shown in the Keep column.",
        };
        delete.Click += DeleteSelectedQuizDuplicate_Click;
        actions.Children.Add(delete);
        actions.Children.Add(new TextBlock
        {
            Text = "Nothing is deleted automatically. Review each pair before deleting.",
            Foreground = QuizMutedBrush(),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        });
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return root;
    }

    private void RefreshQuizDuplicateSection()
    {
        if (_quizDuplicatesGrid is null)
            return;

        try
        {
            var questions = _data.GetQuizQuestions(limit: 10_000);
            var candidates = QuizDuplicateReview.FindCandidates(questions);
            _quizDuplicatesGrid.ItemsSource = candidates;
            if (_quizDuplicatesStatusText is not null)
            {
                var exact = candidates.Count(item => string.Equals(item.MatchType, "Same wording", StringComparison.Ordinal));
                var reworded = candidates.Count - exact;
                _quizDuplicatesStatusText.Text = candidates.Count == 0
                    ? $"Scanned {questions.Count:N0} questions • no likely duplicates found"
                    : $"Scanned {questions.Count:N0} questions • {candidates.Count:N0} likely duplicates ({exact:N0} same wording, {reworded:N0} reworded)";
            }
        }
        catch (Exception error)
        {
            if (_quizDuplicatesStatusText is not null)
                _quizDuplicatesStatusText.Text = $"Could not scan duplicates: {error.Message}";
        }
    }

    private void DeleteSelectedQuizDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_quizDuplicatesGrid?.SelectedItem is not QuizDuplicateCandidate candidate)
        {
            MessageBox.Show(
                this,
                "Select a duplicate pair first.",
                "Duplicate Review",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Keep question #{candidate.KeepId}:\n{candidate.KeepQuestion}\n\nDelete duplicate #{candidate.DuplicateId}:\n{candidate.DuplicateQuestion}?",
            "Delete Duplicate Question",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _data.DeleteQuizQuestion(candidate.DuplicateId);
            _quizDraftQuestions = _quizDraftQuestions
                .Where(question => question.Id != candidate.DuplicateId)
                .ToList();
            if (_quizDraftGrid is not null)
                _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);

            RefreshQuizBank();
            RefreshQuizDuplicateSection();
            RefreshQuizCategorySection();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Deleted duplicate question #{candidate.DuplicateId}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Duplicate Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
