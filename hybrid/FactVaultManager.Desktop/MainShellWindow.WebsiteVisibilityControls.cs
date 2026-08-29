using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            ToolTip = "Make the selected quiz publicly playable immediately.",
        };
        _websiteFollowScheduleButton = new Button
        {
            Content = "Follow schedule",
            MinWidth = 112,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
            ToolTip = "Return website timing to this quiz's local scheduled release time.",
        };
        _websiteTakeOfflineButton = new Button
        {
            Content = "Take offline",
            MinWidth = 104,
            MinHeight = 36,
            IsEnabled = false,
            ToolTip = "Hide the selected quiz from the public website without deleting its questions or metadata.",
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

    private void UpdateWebsiteVisibilityButtons()
    {
        var row = _websiteManagerGrid?.SelectedItem as WebsiteManagerQuizRow;
        if (row is null)
        {
            if (_websiteGoLiveButton is not null) _websiteGoLiveButton.IsEnabled = false;
            if (_websiteFollowScheduleButton is not null) _websiteFollowScheduleButton.IsEnabled = false;
            if (_websiteTakeOfflineButton is not null) _websiteTakeOfflineButton.IsEnabled = false;
            return;
        }

        var state = FactburstWebsiteVisibility.DisplayState(row.Status, row.RawPublishAt, DateTimeOffset.Now);
        var schedule = row.HistoryId > 0
            ? _scheduledReadinessRows.FirstOrDefault(item => item.HistoryId == row.HistoryId)
            : null;

        if (_websiteGoLiveButton is not null)
            _websiteGoLiveButton.IsEnabled = !string.Equals(state, "Live", StringComparison.Ordinal);
        if (_websiteFollowScheduleButton is not null)
            _websiteFollowScheduleButton.IsEnabled = schedule is not null;
        if (_websiteTakeOfflineButton is not null)
            _websiteTakeOfflineButton.IsEnabled = !string.Equals(state, "Offline", StringComparison.Ordinal);

        if (_websiteStatusText is not null)
        {
            var scheduleNote = schedule is null
                ? "No local scheduled release is linked."
                : $"Local schedule: {schedule.PublishAt.LocalDateTime:g}.";
            _websiteStatusText.Text = $"Selected: {row.Title} • Website state: {state}. {scheduleNote}";
        }
    }

    private async Task SetSelectedWebsiteLiveNowAsync()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row) return;
        const string title = "Go Live on Website";
        if (MessageBox.Show(
                this,
                $"Make {row.Title} live on the Factburst website now?\n\nThe quiz will become publicly playable immediately. Its questions and project data are not changed.",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            row,
            title,
            "Making quiz live…",
            async (client, tracker) => await client.SetLiveNowAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                row.Slug,
                DateTimeOffset.UtcNow));
    }

    private async Task SetSelectedWebsiteOfflineAsync()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row) return;
        const string title = "Take Quiz Offline";
        if (MessageBox.Show(
                this,
                $"Take {row.Title} offline?\n\nIt will disappear from the public quiz website, but its Cloudflare quiz record, questions, images and release time are kept. You can put it live again at any time.",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            row,
            title,
            "Taking quiz offline…",
            async (client, tracker) => await client.SetOfflineAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                row.Slug,
                row.RawPublishAt));
    }

    private async Task SetSelectedWebsiteFollowScheduleAsync()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row) return;
        var scheduled = row.HistoryId > 0
            ? _scheduledReadinessRows.FirstOrDefault(item => item.HistoryId == row.HistoryId)
            : null;
        if (scheduled is null)
        {
            MessageBox.Show(
                this,
                "This website quiz is not linked to a local scheduled release.",
                "Follow Website Schedule",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var resultingState = scheduled.PublishAt > DateTimeOffset.Now ? "Upcoming" : "Live";
        const string title = "Follow Website Schedule";
        if (MessageBox.Show(
                this,
                $"Return {row.Title} to its local release schedule?\n\nWebsite state: {resultingState}\nRelease time: {scheduled.PublishAt.LocalDateTime:f}\n\nFuture content resyncs will preserve this website timing until you change it again here.",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await ChangeSelectedWebsiteVisibilityAsync(
            row,
            title,
            "Applying scheduled website time…",
            async (client, tracker) => await client.FollowScheduleAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                row.Slug,
                scheduled.PublishAt));
    }

    private async Task ChangeSelectedWebsiteVisibilityAsync(
        WebsiteManagerQuizRow row,
        string title,
        string progress,
        Func<FactburstWebsiteVisibilityClient, FactburstTrackerSettings, Task> change)
    {
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured)
                throw new InvalidOperationException("Configure Settings → Link Tracker first.");

            SetWebsiteVisibilityButtonsEnabled(false);
            if (_websiteStatusText is not null) _websiteStatusText.Text = progress;

            using var client = new FactburstWebsiteVisibilityClient();
            await change(client, tracker);
            await RefreshWebsiteManagerAsync(false);
            ReselectWebsiteQuiz(row.Slug);
            UpdateWebsiteVisibilityButtons();
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

    private void ReselectWebsiteQuiz(string slug)
    {
        if (_websiteManagerGrid?.ItemsSource is not IEnumerable<WebsiteManagerQuizRow> rows) return;
        var match = rows.FirstOrDefault(item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        _websiteManagerGrid.SelectedItem = match;
        _websiteManagerGrid.ScrollIntoView(match);
    }
}
