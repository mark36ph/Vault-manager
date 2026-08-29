using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteVisibilityControlsInitialized;
    private DispatcherTimer? _websiteVisibilityControlsTimer;
    private Button? _websiteGoLiveButton;
    private Button? _websiteFollowScheduleButton;
    private Button? _websiteTakeOfflineButton;

    public void InitializeWebsiteVisibilityControls()
    {
        if (_websiteVisibilityControlsInitialized) return;
        _websiteVisibilityControlsInitialized = true;

        _websiteVisibilityControlsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _websiteVisibilityControlsTimer.Tick += (_, _) => EnsureWebsiteVisibilityControls();
        _websiteVisibilityControlsTimer.Start();
        Closed += (_, _) => _websiteVisibilityControlsTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteVisibilityControls));
    }

    private void EnsureWebsiteVisibilityControls()
    {
        if (_websiteGoLiveButton?.Parent is not null)
        {
            _websiteVisibilityControlsTimer?.Stop();
            return;
        }

        if (_websiteManagerGrid is null || _websiteOpenProjectButton?.Parent is not Grid footer || _websiteResyncButton is null)
            return;

        _websiteManagerGrid.SelectionMode = DataGridSelectionMode.Extended;
        _websiteManagerGrid.SelectionUnit = DataGridSelectionUnit.FullRow;

        var statusColumn = _websiteManagerGrid.Columns
            .OfType<DataGridTextColumn>()
            .FirstOrDefault(column => string.Equals(Convert.ToString(column.Header), "Status", StringComparison.Ordinal));
        if (statusColumn is not null)
        {
            statusColumn.Header = "Website state";
            statusColumn.Width = 110;
            statusColumn.Binding = new Binding(".")
            {
                Converter = WebsiteVisibilityStateConverter.Instance,
            };
        }

        if (footer.ColumnDefinitions.Count < 4)
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stateActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        _websiteGoLiveButton = new Button
        {
            Content = "Go live now",
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
            ToolTip = "Make every selected quiz publicly playable immediately.",
        };
        _websiteFollowScheduleButton = new Button
        {
            Content = "Follow schedule",
            MinWidth = 112,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
            ToolTip = "Return every selected quiz to its linked local scheduled release time.",
        };
        _websiteTakeOfflineButton = new Button
        {
            Content = "Take offline",
            MinWidth = 104,
            MinHeight = 36,
            IsEnabled = false,
            ToolTip = "Hide every selected quiz without deleting its questions or metadata.",
        };

        _websiteGoLiveButton.Click += async (_, _) => await SetSelectedWebsiteLiveNowAsync();
        _websiteFollowScheduleButton.Click += async (_, _) => await SetSelectedWebsiteFollowScheduleAsync();
        _websiteTakeOfflineButton.Click += async (_, _) => await SetSelectedWebsiteOfflineAsync();

        stateActions.Children.Add(_websiteGoLiveButton);
        stateActions.Children.Add(_websiteFollowScheduleButton);
        stateActions.Children.Add(_websiteTakeOfflineButton);

        Grid.SetColumn(stateActions, 1);
        Grid.SetColumn(_websiteOpenProjectButton, 2);
        Grid.SetColumn(_websiteResyncButton, 3);
        footer.Children.Add(stateActions);

        _websiteManagerGrid.SelectionChanged += (_, _) => UpdateWebsiteVisibilityButtons();
        UpdateWebsiteVisibilityButtons();
        _websiteVisibilityControlsTimer?.Stop();
    }

    private IReadOnlyList<WebsiteManagerQuizRow> SelectedWebsiteRows() =>
        _websiteManagerGrid?.SelectedItems
            .OfType<WebsiteManagerQuizRow>()
            .GroupBy(row => row.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList()
        ?? [];

    private void UpdateWebsiteVisibilityButtons()
    {
        var rows = SelectedWebsiteRows();
        if (rows.Count == 0)
        {
            SetWebsiteVisibilityButtonsEnabled(false);
            return;
        }

        var states = rows
            .Select(row => FactburstWebsiteVisibility.DisplayState(row.Status, row.RawPublishAt, DateTimeOffset.Now))
            .ToList();
        var allScheduled = rows.All(row => row.HistoryId > 0 && _scheduledReadinessRows.Any(item => item.HistoryId == row.HistoryId));

        if (_websiteGoLiveButton is not null)
            _websiteGoLiveButton.IsEnabled = states.Any(state => !string.Equals(state, "Live", StringComparison.Ordinal));
        if (_websiteFollowScheduleButton is not null)
            _websiteFollowScheduleButton.IsEnabled = allScheduled;
        if (_websiteTakeOfflineButton is not null)
            _websiteTakeOfflineButton.IsEnabled = states.Any(state => !string.Equals(state, "Offline", StringComparison.Ordinal));

        if (_websiteStatusText is not null)
        {
            if (rows.Count == 1)
            {
                var row = rows[0];
                var schedule = row.HistoryId > 0
                    ? _scheduledReadinessRows.FirstOrDefault(item => item.HistoryId == row.HistoryId)
                    : null;
                var scheduleNote = schedule is null
                    ? "No local scheduled release is linked."
                    : $"Local schedule: {schedule.PublishAt.LocalDateTime:g}.";
                _websiteStatusText.Text = $"Selected: {row.Title} • Website state: {states[0]}. {scheduleNote}";
            }
            else
            {
                var live = states.Count(state => string.Equals(state, "Live", StringComparison.Ordinal));
                var upcoming = states.Count(state => string.Equals(state, "Upcoming", StringComparison.Ordinal));
                var offline = states.Count(state => string.Equals(state, "Offline", StringComparison.Ordinal));
                _websiteStatusText.Text = $"Selected {rows.Count:N0} quizzes • {live:N0} live • {upcoming:N0} upcoming • {offline:N0} offline" +
                    (allScheduled ? " • all linked to local schedules" : " • some are not linked to a local schedule");
            }
        }
    }

    private async Task SetSelectedWebsiteLiveNowAsync()
    {
        var rows = SelectedWebsiteRows();
        if (rows.Count == 0) return;
        const string title = "Go Live on Website";
        var prompt = rows.Count == 1
            ? $"Make {rows[0].Title} live on the Factburst website now?\n\nThe quiz will become publicly playable immediately. Its questions and project data are not changed."
            : $"Make all {rows.Count:N0} selected quizzes live on the Factburst website now?\n\nThey will become publicly playable immediately. Their questions and project data are not changed.";
        if (MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            rows,
            title,
            $"Making {rows.Count:N0} selected quiz{(rows.Count == 1 ? "" : "zes")} live…",
            async (client, tracker, row) => await client.SetLiveNowAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                row.Slug,
                DateTimeOffset.UtcNow),
            $"{rows.Count:N0} quiz{(rows.Count == 1 ? "" : "zes")} set live.");
    }

    private async Task SetSelectedWebsiteOfflineAsync()
    {
        var rows = SelectedWebsiteRows();
        if (rows.Count == 0) return;
        const string title = "Take Quiz Offline";
        var prompt = rows.Count == 1
            ? $"Take {rows[0].Title} offline?\n\nIt will disappear from the public quiz website, but its Cloudflare quiz record, questions, images and release time are kept. You can put it live again at any time."
            : $"Take all {rows.Count:N0} selected quizzes offline?\n\nThey will disappear from the public website, but their Cloudflare records, questions, images and release times are kept. You can put them live again at any time.";
        if (MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            rows,
            title,
            $"Taking {rows.Count:N0} selected quiz{(rows.Count == 1 ? "" : "zes")} offline…",
            async (client, tracker, row) => await client.SetOfflineAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                row.Slug,
                row.RawPublishAt),
            $"{rows.Count:N0} quiz{(rows.Count == 1 ? "" : "zes")} taken offline.");
    }

    private async Task SetSelectedWebsiteFollowScheduleAsync()
    {
        var rows = SelectedWebsiteRows();
        if (rows.Count == 0) return;
        var scheduledRows = rows.Select(row => new
        {
            Row = row,
            Schedule = row.HistoryId > 0
                ? _scheduledReadinessRows.FirstOrDefault(item => item.HistoryId == row.HistoryId)
                : null,
        }).ToList();
        var missing = scheduledRows.Where(item => item.Schedule is null).Select(item => item.Row.Title).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(
                this,
                $"Follow schedule needs every selected quiz to be linked to a local scheduled release.\n\nMissing schedule: {string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? "…" : "")}",
                "Follow Website Schedule",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var upcoming = scheduledRows.Count(item => item.Schedule!.PublishAt > DateTimeOffset.Now);
        var live = scheduledRows.Count - upcoming;
        const string title = "Follow Website Schedule";
        var prompt = rows.Count == 1
            ? $"Return {rows[0].Title} to its local release schedule?\n\nWebsite state: {(upcoming == 1 ? "Upcoming" : "Live")}\nRelease time: {scheduledRows[0].Schedule!.PublishAt.LocalDateTime:f}\n\nFuture content resyncs will preserve this website timing until you change it again here."
            : $"Return all {rows.Count:N0} selected quizzes to their local release schedules?\n\nResult: {live:N0} live • {upcoming:N0} upcoming\n\nFuture content resyncs will preserve these website timings until you change them again here.";
        if (MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            rows,
            title,
            $"Applying schedules to {rows.Count:N0} selected quiz{(rows.Count == 1 ? "" : "zes")}…",
            async (client, tracker, row) =>
            {
                var scheduled = scheduledRows.First(item => string.Equals(item.Row.Slug, row.Slug, StringComparison.OrdinalIgnoreCase)).Schedule!;
                await client.FollowScheduleAsync(tracker.BaseUrl, tracker.ApiKey, row.Slug, scheduled.PublishAt);
            },
            $"{rows.Count:N0} quiz{(rows.Count == 1 ? "" : "zes")} returned to schedule.");
    }

    private async Task ChangeSelectedWebsiteVisibilityAsync(
        IReadOnlyList<WebsiteManagerQuizRow> rows,
        string title,
        string progress,
        Func<FactburstWebsiteVisibilityClient, FactburstTrackerSettings, WebsiteManagerQuizRow, Task> change,
        string success)
    {
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured)
                throw new InvalidOperationException("Configure Settings → Link Tracker first.");

            SetWebsiteVisibilityButtonsEnabled(false);
            if (_websiteStatusText is not null) _websiteStatusText.Text = progress;

            var failures = new List<string>();
            using var client = new FactburstWebsiteVisibilityClient();
            foreach (var row in rows)
            {
                try
                {
                    await change(client, tracker, row);
                }
                catch (Exception error)
                {
                    failures.Add($"{row.Title}: {error.Message}");
                }
            }

            var slugs = rows.Select(row => row.Slug).ToList();
            await RefreshWebsiteManagerAsync(false);
            ReselectWebsiteQuizzes(slugs);
            UpdateWebsiteVisibilityButtons();

            if (failures.Count > 0)
            {
                if (_websiteStatusText is not null)
                    _websiteStatusText.Text = $"Updated {rows.Count - failures.Count:N0} of {rows.Count:N0} selected quizzes.";
                MessageBox.Show(
                    this,
                    $"{failures.Count:N0} selected quiz{(failures.Count == 1 ? "" : "zes")} could not be updated:\n\n{string.Join("\n", failures.Take(8))}{(failures.Count > 8 ? "\n…" : "")}",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_websiteStatusText is not null) _websiteStatusText.Text = success;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateWebsiteVisibilityButtons();
        }
    }

    private void SetWebsiteVisibilityButtonsEnabled(bool enabled)
    {
        if (_websiteGoLiveButton is not null) _websiteGoLiveButton.IsEnabled = enabled;
        if (_websiteFollowScheduleButton is not null) _websiteFollowScheduleButton.IsEnabled = enabled;
        if (_websiteTakeOfflineButton is not null) _websiteTakeOfflineButton.IsEnabled = enabled;
    }

    private void ReselectWebsiteQuizzes(IReadOnlyCollection<string> slugs)
    {
        if (_websiteManagerGrid?.ItemsSource is not IEnumerable<WebsiteManagerQuizRow> rows || slugs.Count == 0) return;
        var selected = rows.Where(item => slugs.Contains(item.Slug, StringComparer.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0) return;
        _websiteManagerGrid.SelectedItems.Clear();
        foreach (var row in selected)
            _websiteManagerGrid.SelectedItems.Add(row);
        _websiteManagerGrid.ScrollIntoView(selected[0]);
    }
}

internal sealed class WebsiteVisibilityStateConverter : IValueConverter
{
    public static WebsiteVisibilityStateConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is WebsiteManagerQuizRow row
            ? FactburstWebsiteVisibility.DisplayState(row.Status, row.RawPublishAt, DateTimeOffset.Now)
            : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
