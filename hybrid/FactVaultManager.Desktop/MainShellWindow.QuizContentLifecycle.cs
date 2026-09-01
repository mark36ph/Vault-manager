using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizContentLifecycleUiInitialized;
    private string _quizContentLifecycleFilter = "All";
    private TextBlock? _quizContentLifecycleSummaryText;
    private readonly Dictionary<string, Button> _quizContentLifecycleFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, QuizContentLifecycleResult> _quizContentLifecycleByHistoryId = [];

    public void InitializeQuizContentLifecycleUi()
    {
        if (_quizContentLifecycleUiInitialized)
            return;

        _quizContentLifecycleUiInitialized = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(BuildQuizContentLifecycleUi));
    }

    private void BuildQuizContentLifecycleUi()
    {
        if (_quizHistoryGrid is null ||
            _quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Border { Child: Grid root })
        {
            return;
        }

        var title = root.Children
            .OfType<Grid>()
            .Where(grid => Grid.GetRow(grid) == 0)
            .SelectMany(grid => grid.Children.OfType<TextBlock>())
            .FirstOrDefault(text => string.Equals(text.Text, "Quiz History", StringComparison.Ordinal));
        if (title is not null)
            title.Text = "Library";

        if (!_quizHistoryGrid.Columns.Any(column => string.Equals(column.Header?.ToString(), "Stage", StringComparison.Ordinal)))
        {
            var stageColumn = new DataGridTextColumn
            {
                Header = "Stage",
                Binding = new Binding
                {
                    Converter = new QuizContentLifecycleValueConverter(this, showNextAction: false),
                },
                CanUserSort = false,
                Width = new DataGridLength(128),
            };
            var nextActionColumn = new DataGridTextColumn
            {
                Header = "Next action",
                Binding = new Binding
                {
                    Converter = new QuizContentLifecycleValueConverter(this, showNextAction: true),
                },
                CanUserSort = false,
                Width = new DataGridLength(205),
            };

            var insertAt = Math.Min(3, _quizHistoryGrid.Columns.Count);
            _quizHistoryGrid.Columns.Insert(insertAt, stageColumn);
            _quizHistoryGrid.Columns.Insert(insertAt + 1, nextActionColumn);
        }

        var tableCard = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 2 && ReferenceEquals(border.Child, _quizHistoryGrid));
        if (tableCard is not null)
        {
            tableCard.Child = null;
            var tableLayout = new Grid();
            tableLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tableLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var workflowBar = BuildQuizContentLifecycleFilterBar();
            tableLayout.Children.Add(workflowBar);
            Grid.SetRow(_quizHistoryGrid, 1);
            tableLayout.Children.Add(_quizHistoryGrid);
            tableCard.Child = tableLayout;
        }

        var itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(DataGrid));
        itemsSourceDescriptor?.AddValueChanged(
            _quizHistoryGrid,
            (_, _) => Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(RefreshQuizContentLifecycleSnapshot)));

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) || MainTabs.SelectedIndex != _quizHistoryTabIndex)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(RefreshQuizContentLifecycleSnapshot));
        };

        RefreshQuizContentLifecycleSnapshot();
    }

    private FrameworkElement BuildQuizContentLifecycleFilterBar()
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 18, 72)),
            Margin = new Thickness(0),
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 8, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        filters.Children.Add(new TextBlock
        {
            Text = "Workflow",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 94)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });

        foreach (var filter in QuizContentLifecycle.Filters)
        {
            var button = new Button
            {
                Content = filter,
                MinHeight = 30,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = $"Show {filter.ToLowerInvariant()} quizzes",
            };
            button.Click += (_, _) =>
            {
                _quizContentLifecycleFilter = filter;
                ApplyQuizContentLifecycleFilter();
            };
            _quizContentLifecycleFilterButtons[filter] = button;
            filters.Children.Add(button);
        }
        root.Children.Add(filters);

        _quizContentLifecycleSummaryText = new TextBlock
        {
            Text = "Content workflow",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(_quizContentLifecycleSummaryText, 2);
        root.Children.Add(_quizContentLifecycleSummaryText);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = root,
        };
    }

    private void RefreshQuizContentLifecycleSnapshot()
    {
        if (_quizHistoryGrid?.ItemsSource is not IEnumerable<QuizHistorySummary> source)
            return;

        try
        {
            var history = source.ToList();
            IReadOnlyList<PublicationStateEntry> publications;
            try
            {
                publications = _data.PublicationState.List();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"Content lifecycle publication state: {error.Message}");
                publications = [];
            }

            var publicationByHistory = publications
                .GroupBy(item => item.HistoryId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<PublicationStateEntry>)group.ToList());
            var now = DateTimeOffset.Now;
            var next = new Dictionary<int, QuizContentLifecycleResult>();

            foreach (var item in history)
            {
                var states = publicationByHistory.TryGetValue(item.Id, out var stored)
                    ? stored
                    : [];
                var folderExists = item.ProjectFolder.Trim().Length > 0 && Directory.Exists(item.ProjectFolder);
                var lifecycle = QuizContentLifecycle.Assess(
                    item,
                    states,
                    now,
                    folderExists,
                    renderedVideoExists: false);

                if (folderExists && string.Equals(lifecycle.Stage, QuizContentLifecycleStage.Exported, StringComparison.Ordinal))
                {
                    var rendered = false;
                    try
                    {
                        rendered = SocialVideoUploadRules.FindLikelyRenderedVideo(item.ProjectFolder) is not null;
                    }
                    catch (Exception error)
                    {
                        Debug.WriteLine($"Content lifecycle rendered video check #{item.Id}: {error.Message}");
                    }

                    if (rendered)
                    {
                        lifecycle = QuizContentLifecycle.Assess(
                            item,
                            states,
                            now,
                            projectFolderExists: true,
                            renderedVideoExists: true);
                    }
                }

                next[item.Id] = lifecycle;
            }

            _quizContentLifecycleByHistoryId.Clear();
            foreach (var pair in next)
                _quizContentLifecycleByHistoryId[pair.Key] = pair.Value;

            ApplyQuizContentLifecycleFilter();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Content lifecycle refresh: {error.Message}");
        }
    }

    private void ApplyQuizContentLifecycleFilter()
    {
        if (_quizHistoryGrid?.ItemsSource is not IEnumerable<QuizHistorySummary> source)
            return;

        var history = source.ToList();
        var view = CollectionViewSource.GetDefaultView(_quizHistoryGrid.ItemsSource);
        if (view is not null)
        {
            view.Filter = item =>
            {
                if (item is not QuizHistorySummary quiz)
                    return false;
                return !_quizContentLifecycleByHistoryId.TryGetValue(quiz.Id, out var lifecycle) ||
                       QuizContentLifecycle.MatchesFilter(lifecycle, _quizContentLifecycleFilter);
            };
            view.Refresh();
        }

        var visible = history.Count(item =>
            !_quizContentLifecycleByHistoryId.TryGetValue(item.Id, out var lifecycle) ||
            QuizContentLifecycle.MatchesFilter(lifecycle, _quizContentLifecycleFilter));
        var attention = history.Count(item =>
            _quizContentLifecycleByHistoryId.TryGetValue(item.Id, out var lifecycle) && lifecycle.NeedsAttention);
        if (_quizContentLifecycleSummaryText is not null)
        {
            _quizContentLifecycleSummaryText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Showing {0:N0} of {1:N0} • {2:N0} need attention",
                visible,
                history.Count,
                attention);
        }

        foreach (var pair in _quizContentLifecycleFilterButtons)
            StyleQuizContentLifecycleFilterButton(pair.Value, string.Equals(pair.Key, _quizContentLifecycleFilter, StringComparison.OrdinalIgnoreCase));

        _quizHistoryGrid.Items.Refresh();
    }

    private static void StyleQuizContentLifecycleFilterButton(Button button, bool active)
    {
        button.Background = new SolidColorBrush(active
            ? Color.FromRgb(25, 86, 170)
            : Color.FromRgb(13, 18, 78));
        button.BorderBrush = new SolidColorBrush(active
            ? Color.FromRgb(0, 204, 255)
            : Color.FromRgb(58, 78, 145));
        button.BorderThickness = new Thickness(active ? 2 : 1);
        button.Foreground = active
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(190, 215, 255));
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private QuizContentLifecycleResult ResolveQuizContentLifecycle(QuizHistorySummary history)
    {
        if (_quizContentLifecycleByHistoryId.TryGetValue(history.Id, out var lifecycle))
            return lifecycle;

        return new QuizContentLifecycleResult(
            QuizContentLifecycleStage.Exported,
            "Refresh Library",
            "Lifecycle information has not been refreshed yet.",
            false);
    }

    private sealed class QuizContentLifecycleValueConverter(
        MainShellWindow owner,
        bool showNextAction) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QuizHistorySummary history)
                return "—";
            var lifecycle = owner.ResolveQuizContentLifecycle(history);
            return showNextAction ? lifecycle.NextAction : lifecycle.Stage;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
