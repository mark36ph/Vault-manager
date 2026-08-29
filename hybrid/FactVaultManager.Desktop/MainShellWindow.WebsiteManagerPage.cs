using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record WebsiteManagerQuizRow(
    string Title,
    string Slug,
    string Status,
    string PublishAt,
    string UpdatedAt,
    int QuestionCount,
    int HistoryId,
    string RawPublishAt);

public sealed record WebsiteManagerSummary(
    int Total,
    int Live,
    int Upcoming,
    int Questions,
    int Scheduled,
    int MissingScheduled);

public static class FactburstWebsiteManagerPlanner
{
    public static WebsiteManagerSummary Build(
        IEnumerable<FactburstWebsiteQuizSummary> websiteQuizzes,
        IEnumerable<string> scheduledSlugs,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(websiteQuizzes);
        ArgumentNullException.ThrowIfNull(scheduledSlugs);

        var site = websiteQuizzes.ToList();
        var live = 0;
        var upcoming = 0;
        foreach (var quiz in site)
        {
            if (!string.Equals(quiz.Status, "published", StringComparison.OrdinalIgnoreCase))
                continue;
            if (DateTimeOffset.TryParse(quiz.PublishAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishAt) && publishAt > now)
                upcoming++;
            else
                live++;
        }

        var websiteSlugs = site
            .Select(quiz => (quiz.Slug ?? "").Trim())
            .Where(slug => slug.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scheduled = scheduledSlugs
            .Select(slug => (slug ?? "").Trim())
            .Where(slug => slug.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WebsiteManagerSummary(
            site.Count,
            live,
            upcoming,
            site.Sum(quiz => Math.Max(0, quiz.QuestionCount)),
            scheduled.Count,
            scheduled.Count(slug => !websiteSlugs.Contains(slug)));
    }

    public static string DisplayDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        return parsed.LocalDateTime.ToString("ddd dd MMM yyyy • HH:mm", CultureInfo.InvariantCulture);
    }
}

public partial class MainShellWindow
{
    private bool _websiteManagerInitialized;
    private int _websiteManagerTabIndex = -1;
    private DispatcherTimer? _websiteManagerGuardTimer;
    private DataGrid? _websiteManagerGrid;
    private TextBlock? _websiteConnectionText;
    private TextBlock? _websiteConnectionNoteText;
    private TextBlock? _websiteQuizCountText;
    private TextBlock? _websiteQuizCountNoteText;
    private TextBlock? _websiteScheduleText;
    private TextBlock? _websiteScheduleNoteText;
    private TextBlock? _websiteQuestionsText;
    private TextBlock? _websiteQuestionsNoteText;
    private TextBlock? _websiteStatusText;
    private Button? _websiteSyncAllButton;
    private Button? _websiteResyncButton;
    private Button? _websiteOpenProjectButton;

    public void InitializeWebsiteManagerPage()
    {
        if (_websiteManagerInitialized) return;
        _websiteManagerInitialized = true;

        _websiteManagerGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _websiteManagerGuardTimer.Tick += (_, _) => EnsureWebsiteManagerPage();
        _websiteManagerGuardTimer.Start();
        Closed += (_, _) => _websiteManagerGuardTimer?.Stop();

        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            if (MainTabs.SelectedIndex == _websiteManagerTabIndex)
                await RefreshWebsiteManagerAsync(false);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteManagerPage));
    }

    private void EnsureWebsiteManagerPage()
    {
        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null) return;

        if (_websiteManagerTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildWebsiteManagerPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _websiteManagerTabIndex = MainTabs.Items.Count - 1;
        }

        if (_autopilotNavButtons.ContainsKey("Website"))
        {
            _websiteManagerGuardTimer?.Stop();
            return;
        }

        var button = new Button
        {
            Content = "◎   Website",
            Tag = AutopilotFirstNavTag + ":Website",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        button.Click += (_, _) => NavigateWebsiteManager();

        var advanced = _autopilotNavButtons.TryGetValue("Advanced", out var advancedButton) ? advancedButton : null;
        var index = advanced is null ? _autopilotNavContainer.Children.Count : _autopilotNavContainer.Children.IndexOf(advanced);
        _autopilotNavContainer.Children.Insert(index < 0 ? _autopilotNavContainer.Children.Count : index, button);
        _autopilotNavButtons["Website"] = button;
        _websiteManagerGuardTimer?.Stop();
    }

    private void NavigateWebsiteManager()
    {
        EnsureWebsiteManagerPage();
        if (_websiteManagerTabIndex < 0) return;
        MainTabs.SelectedIndex = _websiteManagerTabIndex;
        SelectAutopilotNav("Website");
        _ = RefreshWebsiteManagerAsync(false);
    }

    private FrameworkElement BuildWebsiteManagerPage()
    {
        var root = new Grid { Margin = new Thickness(26, 22, 26, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Website",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Manage the Factburst Cloudflare quiz catalogue, release timing and scheduled quiz sync from one place.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 20, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var refresh = new Button { Content = "Refresh", MinWidth = 92, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += async (_, _) => await RefreshWebsiteManagerAsync(true);
        _websiteSyncAllButton = new Button { Content = "Sync scheduled", MinWidth = 130, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        _websiteSyncAllButton.Click += async (_, _) => await SyncWebsiteScheduledAsync();
        var settings = new Button { Content = "Website settings", MinWidth = 124, MinHeight = 36 };
        settings.Click += (_, _) => NavigateLegacy("Settings", "Settings");
        actions.Children.Add(refresh);
        actions.Children.Add(_websiteSyncAllButton);
        actions.Children.Add(settings);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddWebsiteStat(stats, 0, "Connection", out _websiteConnectionText, out _websiteConnectionNoteText);
        AddWebsiteStat(stats, 1, "Website quizzes", out _websiteQuizCountText, out _websiteQuizCountNoteText);
        AddWebsiteStat(stats, 2, "Schedule sync", out _websiteScheduleText, out _websiteScheduleNoteText);
        AddWebsiteStat(stats, 3, "Questions", out _websiteQuestionsText, out _websiteQuestionsNoteText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _websiteManagerGrid = BuildManagerGrid();
        _websiteManagerGrid.SelectionMode = DataGridSelectionMode.Single;
        _websiteManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(WebsiteManagerQuizRow.Title)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteManagerGrid.Columns.Add(TextColumn("Status", nameof(WebsiteManagerQuizRow.Status), 95));
        _websiteManagerGrid.Columns.Add(TextColumn("Publish", nameof(WebsiteManagerQuizRow.PublishAt), 190));
        _websiteManagerGrid.Columns.Add(TextColumn("Questions", nameof(WebsiteManagerQuizRow.QuestionCount), 88));
        _websiteManagerGrid.Columns.Add(TextColumn("Updated", nameof(WebsiteManagerQuizRow.UpdatedAt), 190));
        _websiteManagerGrid.Columns.Add(TextColumn("Slug", nameof(WebsiteManagerQuizRow.Slug), 185));
        _websiteManagerGrid.SelectionChanged += (_, _) => UpdateWebsiteSelectionButtons();
        var card = ManagerCard(_websiteManagerGrid);
        Grid.SetRow(card, 2);
        root.Children.Add(card);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _websiteStatusText = new TextBlock
        {
            Text = "Website status will appear here.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        footer.Children.Add(_websiteStatusText);
        _websiteOpenProjectButton = new Button { Content = "Open project", MinWidth = 108, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _websiteOpenProjectButton.Click += (_, _) => OpenWebsiteSelectedProject();
        _websiteResyncButton = new Button { Content = "Resync selected", MinWidth = 120, MinHeight = 36, IsEnabled = false };
        _websiteResyncButton.Click += async (_, _) => await ResyncSelectedWebsiteQuizAsync();
        Grid.SetColumn(_websiteOpenProjectButton, 1);
        Grid.SetColumn(_websiteResyncButton, 2);
        footer.Children.Add(_websiteOpenProjectButton);
        footer.Children.Add(_websiteResyncButton);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private static void AddWebsiteStat(Grid parent, int column, string label, out TextBlock value, out TextBlock note)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        });
        value = new TextBlock
        {
            Text = "—",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 5, 0, 0),
        };
        note = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0),
        };
        stack.Children.Add(value);
        stack.Children.Add(note);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private async Task RefreshWebsiteManagerAsync(bool showErrors)
    {
        if (_websiteManagerGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            SetWebsiteDisconnected("Link Tracker is not configured. Open Website settings to add the Cloudflare tracker key.");
            return;
        }

        try
        {
            if (_websiteConnectionText is not null) _websiteConnectionText.Text = "Checking…";
            if (_websiteStatusText is not null) _websiteStatusText.Text = "Refreshing Cloudflare website inventory…";

            EnsureScheduledReleaseReadinessPage();
            if (_scheduledReadinessGrid is not null)
                await RefreshScheduledReleaseReadinessAsync(false);

            using var website = new FactburstWebsitePublishingClient();
            var site = await website.FetchQuizzesAsync(tracker.BaseUrl, tracker.ApiKey);
            var histories = _data.GetQuizHistory(2_000)
                .GroupBy(history => history.Id)
                .Select(group => group.First())
                .ToList();
            var historyBySlug = histories
                .GroupBy(FactburstLinkTrackerClient.CampaignSlug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var scheduledRows = _scheduledReadinessRows
                .Where(row => row.PublishAt >= DateTimeOffset.Now.AddHours(-2))
                .ToList();
            var scheduledSlugs = scheduledRows
                .Where(row => historyBySlug.Values.Any(history => history.Id == row.HistoryId))
                .Select(row => histories.First(history => history.Id == row.HistoryId))
                .Select(FactburstLinkTrackerClient.CampaignSlug)
                .ToList();
            var summary = FactburstWebsiteManagerPlanner.Build(site, scheduledSlugs, DateTimeOffset.Now);

            var rows = site
                .Select(quiz =>
                {
                    historyBySlug.TryGetValue(quiz.Slug, out var history);
                    return new WebsiteManagerQuizRow(
                        history?.UploadTitleDisplay ?? quiz.Slug,
                        quiz.Slug,
                        quiz.Status,
                        FactburstWebsiteManagerPlanner.DisplayDate(quiz.PublishAt),
                        FactburstWebsiteManagerPlanner.DisplayDate(quiz.UpdatedAt),
                        quiz.QuestionCount,
                        history?.Id ?? 0,
                        quiz.PublishAt);
                })
                .OrderByDescending(row => ParseWebsiteDate(row.RawPublishAt))
                .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _websiteManagerGrid.ItemsSource = rows;
            if (_websiteConnectionText is not null) _websiteConnectionText.Text = "Connected";
            if (_websiteConnectionNoteText is not null) _websiteConnectionNoteText.Text = tracker.BaseUrl;
            if (_websiteQuizCountText is not null) _websiteQuizCountText.Text = summary.Total.ToString("N0");
            if (_websiteQuizCountNoteText is not null) _websiteQuizCountNoteText.Text = $"{summary.Live:N0} live • {summary.Upcoming:N0} upcoming";
            if (_websiteScheduleText is not null) _websiteScheduleText.Text = summary.Scheduled.ToString("N0");
            if (_websiteScheduleNoteText is not null)
                _websiteScheduleNoteText.Text = summary.MissingScheduled == 0
                    ? "all scheduled quizzes are on the website"
                    : $"{summary.MissingScheduled:N0} scheduled missing from website";
            if (_websiteQuestionsText is not null) _websiteQuestionsText.Text = summary.Questions.ToString("N0");
            if (_websiteQuestionsNoteText is not null) _websiteQuestionsNoteText.Text = "questions stored in Cloudflare";
            if (_websiteStatusText is not null)
                _websiteStatusText.Text = $"Website connected • {summary.Total:N0} quizzes • {summary.Live:N0} live • {summary.Upcoming:N0} upcoming" +
                    (summary.MissingScheduled > 0 ? $" • {summary.MissingScheduled:N0} scheduled need syncing" : "");
            UpdateWebsiteSelectionButtons();
        }
        catch (Exception error)
        {
            SetWebsiteDisconnected(error.Message);
            Debug.WriteLine("Website manager refresh failed: " + error);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Factburst Website", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetWebsiteDisconnected(string note)
    {
        if (_websiteConnectionText is not null) _websiteConnectionText.Text = "Needs setup";
        if (_websiteConnectionNoteText is not null) _websiteConnectionNoteText.Text = note;
        if (_websiteQuizCountText is not null) _websiteQuizCountText.Text = "—";
        if (_websiteQuizCountNoteText is not null) _websiteQuizCountNoteText.Text = "Website inventory unavailable";
        if (_websiteScheduleText is not null) _websiteScheduleText.Text = "—";
        if (_websiteScheduleNoteText is not null) _websiteScheduleNoteText.Text = "Configure Website settings";
        if (_websiteQuestionsText is not null) _websiteQuestionsText.Text = "—";
        if (_websiteQuestionsNoteText is not null) _websiteQuestionsNoteText.Text = "Cloudflare not connected";
        if (_websiteStatusText is not null) _websiteStatusText.Text = note;
        if (_websiteManagerGrid is not null) _websiteManagerGrid.ItemsSource = null;
        UpdateWebsiteSelectionButtons();
    }

    private async Task SyncWebsiteScheduledAsync()
    {
        if (_websiteSyncAllButton is null) return;
        EnsureScheduledReleaseReadinessPage();
        if (_scheduledReadinessGrid is not null)
            await RefreshScheduledReleaseReadinessAsync(false);
        await PrepareScheduledWebsiteQuizzesAsync(_websiteSyncAllButton);
        await RefreshWebsiteManagerAsync(false);
    }

    private async Task ResyncSelectedWebsiteQuizAsync()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row || row.HistoryId <= 0) return;
        const string title = "Resync Website Quiz";
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured)
                throw new InvalidOperationException("Configure Settings → Link Tracker first.");

            _data.RecoverQuizHistoryProjectFolders();
            var history = _data.GetQuizHistory(2_000).FirstOrDefault(item => item.Id == row.HistoryId)
                          ?? throw new InvalidOperationException("The local quiz history record is missing.");
            var scheduled = _scheduledReadinessRows.FirstOrDefault(item => item.HistoryId == history.Id);
            var publishAt = scheduled?.PublishAt ?? ParseWebsiteDate(row.RawPublishAt) ?? DateTimeOffset.Now;
            var questionImagePaths = _data.GetQuizQuestions(limit: 10_000)
                .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
                .ToDictionary(question => question.Id, question => question.ImagePath);
            var payload = FactburstWebsiteQuizBuilder.Build(history, publishAt, questionImagePaths);

            if (MessageBox.Show(
                    this,
                    $"Resync {history.UploadTitleDisplay} to the website?\n\nThis refreshes the existing Cloudflare copy and preserves its release time.",
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (_websiteResyncButton is not null) _websiteResyncButton.IsEnabled = false;
            if (_websiteStatusText is not null) _websiteStatusText.Text = $"Resyncing {history.UploadTitleDisplay}…";
            using var website = new FactburstWebsitePublishingClient();
            await website.PublishQuizAsync(tracker.BaseUrl, tracker.ApiKey, payload);
            await RefreshWebsiteManagerAsync(false);
            if (_websiteStatusText is not null) _websiteStatusText.Text = $"Resynced {history.UploadTitleDisplay}.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateWebsiteSelectionButtons();
        }
    }

    private void OpenWebsiteSelectedProject()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row || row.HistoryId <= 0) return;
        try
        {
            var history = _data.GetQuizHistory(2_000).FirstOrDefault(item => item.Id == row.HistoryId)
                          ?? throw new InvalidOperationException("The local quiz history record is missing.");
            if (!Directory.Exists(history.ProjectFolder))
                throw new DirectoryNotFoundException("The local project folder is unavailable.");
            Process.Start(new ProcessStartInfo(history.ProjectFolder) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Factburst Website", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateWebsiteSelectionButtons()
    {
        var canUseLocal = _websiteManagerGrid?.SelectedItem is WebsiteManagerQuizRow row && row.HistoryId > 0;
        if (_websiteResyncButton is not null) _websiteResyncButton.IsEnabled = canUseLocal;
        if (_websiteOpenProjectButton is not null) _websiteOpenProjectButton.IsEnabled = canUseLocal;
    }

    private static DateTimeOffset? ParseWebsiteDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
