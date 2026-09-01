using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed class QuizHistoryStatusDisplayConverter : IValueConverter
{
    public static readonly QuizHistoryStatusDisplayConverter Instance = new();

    private static readonly string[] ScheduleFormats =
    [
        "dd-MM-yyyy HH:mm",
        "dd-MM-yyyy H:mm",
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value?.ToString() ?? "").Trim();
        const string prefix = "Scheduled ";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return text;

        var scheduleText = text[prefix.Length..].Trim();
        if (!DateTime.TryParseExact(
                scheduleText,
                ScheduleFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var scheduled))
        {
            return text;
        }

        return scheduled.TimeOfDay == TimeSpan.Zero
            ? $"Scheduled {scheduled:dd MMM}"
            : $"Sched. {scheduled:dd MMM HH:mm}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public partial class MainShellWindow
{
    private const string SimpleNeedsNoteTag = "autopilot-simple-needs-note";
    private bool _dailyUiCleanupInitialized;
    private DispatcherTimer? _dailyUiCleanupTimer;
    private Button? _dailyUpdatesButton;
    private bool _dailyUpdateCheckStarted;

    public void InitializeDailyUiCleanup()
    {
        if (_dailyUiCleanupInitialized)
            return;

        _dailyUiCleanupInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyDailyUiCleanup));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyDailyUiCleanup));

        _dailyUiCleanupTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _dailyUiCleanupTimer.Tick += (_, _) => ApplyDailyUiCleanup();
        _dailyUiCleanupTimer.Start();
        Closed += (_, _) => _dailyUiCleanupTimer?.Stop();
    }

    private void ApplyDailyUiCleanup()
    {
        ApplyGlobalHeaderCleanup();
        ApplyQuizHistoryTableCleanup();
        ApplyAutopilotHomeCleanup();
    }

    private void ApplyGlobalHeaderCleanup()
    {
        if (Content is not DependencyObject root)
            return;

        var refresh = FindVisualChildren<Button>(root)
            .FirstOrDefault(button =>
            {
                var text = (button.Content?.ToString() ?? "").Trim();
                return string.Equals(text, "↻  Refresh", StringComparison.Ordinal) ||
                       string.Equals(text, "↻ Refresh", StringComparison.Ordinal) ||
                       string.Equals(text, "Refresh", StringComparison.Ordinal);
            });
        if (refresh is not null && Window.GetWindow(refresh) == this)
        {
            refresh.Content = "Refresh";
            refresh.MinWidth = 88;
            refresh.ToolTip = "Refresh the current Factburst data and status";
        }

        var updates = FindVisualChildren<Button>(root)
            .FirstOrDefault(button =>
            {
                var text = (button.Content?.ToString() ?? "").Trim();
                return string.Equals(text, "Updates", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "Update available", StringComparison.OrdinalIgnoreCase);
            });
        if (updates is null || Window.GetWindow(updates) != this)
            return;

        _dailyUpdatesButton = updates;
        updates.MinWidth = 82;
        updates.Padding = new Thickness(12, 0, 12, 0);
        if (!_updates.IsInstalled)
        {
            // Keep this exact content so the existing one-time installer bootstrap handler remains active.
            updates.Content = "Updates";
            updates.Opacity = 0.82;
            updates.ToolTip = "Install the current Factburst Quiz Manager release";
            return;
        }

        if (!_dailyUpdateCheckStarted)
        {
            _dailyUpdateCheckStarted = true;
            _ = RefreshDailyUpdateIndicatorAsync();
        }
    }

    private async Task RefreshDailyUpdateIndicatorAsync()
    {
        var button = _dailyUpdatesButton;
        if (button is null || !_updates.IsInstalled)
            return;

        try
        {
            var update = await _updates.CheckAsync();
            if (!IsLoaded || button != _dailyUpdatesButton)
                return;

            if (update is null)
            {
                button.Content = "Updates";
                button.Opacity = 0.68;
                button.ToolTip = $"Factburst Quiz Manager {_updates.CurrentVersion} is up to date. Click to check again.";
            }
            else
            {
                button.Content = "Update available";
                button.Opacity = 1;
                button.Background = new SolidColorBrush(Color.FromRgb(15, 108, 189));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189));
                button.Foreground = Brushes.White;
                button.FontWeight = FontWeights.SemiBold;
                button.ToolTip = "A newer Factburst Quiz Manager build is available. Click to install it.";
            }
        }
        catch
        {
            if (IsLoaded && button == _dailyUpdatesButton)
            {
                button.Content = "Updates";
                button.Opacity = 0.68;
                button.ToolTip = "Check for Factburst Quiz Manager updates";
            }
        }
    }

    private void ApplyQuizHistoryTableCleanup()
    {
        if (_quizHistoryGrid is null)
            return;
        if (_libraryStableLayoutLocked)
            return;

        _quizHistoryGrid.MinColumnWidth = 46;
        _quizHistoryGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

        foreach (var column in _quizHistoryGrid.Columns)
        {
            var header = column.Header?.ToString() ?? "";
            switch (header)
            {
                case "No.":
                    column.Width = new DataGridLength(50);
                    break;
                case "Date":
                    column.Width = new DataGridLength(92);
                    break;
                case "Type":
                    column.Width = new DataGridLength(64);
                    break;
                case "YouTube":
                case "Status":
                    column.Header = "Status";
                    column.Width = new DataGridLength(128);
                    if (column is DataGridTextColumn statusColumn)
                    {
                        statusColumn.Binding = new Binding(nameof(QuizHistorySummary.YouTubePublicationDisplay))
                        {
                            Converter = QuizHistoryStatusDisplayConverter.Instance,
                        };
                    }
                    break;
                case "Views":
                    column.Width = new DataGridLength(64);
                    break;
                case "Likes":
                    column.Width = new DataGridLength(58);
                    break;
                case "Uploaded":
                case "Upload date":
                    column.Header = "Upload date";
                    column.Width = new DataGridLength(94);
                    break;
                case "Series":
                    column.Width = new DataGridLength(170);
                    break;
                case "Ep.":
                    column.Width = new DataGridLength(52);
                    break;
                case "YouTube title":
                    column.Width = new DataGridLength(1.45, DataGridLengthUnitType.Star);
                    if (column is DataGridTextColumn titleColumn)
                        titleColumn.ElementStyle = BuildHistoryTextStyle(nameof(QuizHistorySummary.YouTubeTitle));
                    break;
                case "Questions":
                    column.Width = new DataGridLength(76);
                    break;
                case "Categories":
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    if (column is DataGridTextColumn categoryColumn)
                        categoryColumn.ElementStyle = BuildHistoryTextStyle(nameof(QuizHistorySummary.Categories));
                    break;
            }
        }
    }

    private static Style BuildHistoryTextStyle(string path)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path)));
        return style;
    }

    private void ApplyAutopilotHomeCleanup()
    {
        if (_autopilotNeedsPanel is null)
            return;

        // The older aligned-queue guard can recreate a manager button after a home refresh.
        // Guided mode owns the normal Needs You path now.
        _alignedTaskQueueGuardTimer?.Stop();
        EnsureGuidedNeedsYouButton();

        var guided = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Content?.ToString(),
                "Start next task",
                StringComparison.Ordinal));

        var oldRows = _autopilotNeedsPanel.Children
            .OfType<Grid>()
            .ToList();
        foreach (var row in oldRows)
            _autopilotNeedsPanel.Children.Remove(row);

        var obsoleteButtons = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .Where(button => !ReferenceEquals(button, guided))
            .ToList();
        foreach (var button in obsoleteButtons)
            _autopilotNeedsPanel.Children.Remove(button);

        var existingNote = _autopilotNeedsPanel.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Tag?.ToString(), SimpleNeedsNoteTag, StringComparison.Ordinal));
        if (existingNote is null)
        {
            existingNote = new TextBlock
            {
                Tag = SimpleNeedsNoteTag,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 13,
            };
            var insertIndex = guided is null
                ? _autopilotNeedsPanel.Children.Count
                : Math.Min(_autopilotNeedsPanel.Children.IndexOf(guided) + 1, _autopilotNeedsPanel.Children.Count);
            _autopilotNeedsPanel.Children.Insert(Math.Max(0, insertIndex), existingNote);
        }

        var needsValue = (_autopilotNeedsText?.Text ?? "").Trim();
        var nothingPending = needsValue.Length == 0 ||
                             string.Equals(needsValue, "Nothing", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(needsValue, "0", StringComparison.OrdinalIgnoreCase);
        if (guided is not null)
            guided.Visibility = nothingPending ? Visibility.Collapsed : Visibility.Visible;

        existingNote.Foreground = new SolidColorBrush(nothingPending
            ? Color.FromRgb(2, 122, 72)
            : Color.FromRgb(52, 64, 84));
        existingNote.Text = nothingPending
            ? "✓ Nothing needs your attention. Autopilot will keep working in the background."
            : $"{needsValue} task{(needsValue == "1" ? "" : "s")} need your input. Use Start next task and Factburst will guide you through them one at a time.";

        if (_autopilotHomeTabIndex < 0 || _autopilotHomeTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_autopilotHomeTabIndex] is not TabItem homeTab)
        {
            return;
        }

        foreach (var button in FindVisualChildren<Button>(homeTab))
        {
            if (string.Equals(button.Content?.ToString(), "Generate + Fill Schedule", StringComparison.Ordinal))
            {
                button.Content = "Fill schedule";
                button.MinWidth = 130;
                button.ToolTip = "Create enough quizzes to keep the release schedule filled";
            }
        }

        foreach (var text in FindVisualChildren<TextBlock>(homeTab))
        {
            if (string.Equals(text.Text, "Generate + Fill Schedule", StringComparison.Ordinal))
                text.Text = "Keep the schedule filled";
            else if (text.Text.StartsWith("Autopilot chooses the category mix, renders the quizzes", StringComparison.Ordinal))
                text.Text = "Create enough quizzes to keep releases covered. Autopilot handles scheduling, publishing, promos and follow-up checks.";
        }
    }
}
