using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string GrowthAnalyticsRefreshHook = "youtube-growth-refresh-hook";
    private const string GrowthAutopilotBatchHook = "youtube-growth-batch-hook";
    private static readonly bool YouTubeAnalyticsAutopilotRegistered = RegisterYouTubeAnalyticsAutopilot();
    private readonly YouTubeAnalyticsAutopilotService _youtubeAnalyticsAutopilot = new();
    private bool _youtubeGrowthRefreshRunning;
    private bool _youtubeGrowthStartupRefreshQueued;

    private static bool RegisterYouTubeAnalyticsAutopilot()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(YouTubeAnalyticsAutopilotButton_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(YouTubeAnalyticsAutopilotWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void YouTubeAnalyticsAutopilotWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window || window._youtubeGrowthStartupRefreshQueued)
            return;
        window._youtubeGrowthStartupRefreshQueued = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await window.RefreshYouTubeGrowthAnalyticsAsync(showErrors: false);
            }));
    }

    private static void YouTubeAnalyticsAutopilotButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Window.GetWindow(button) is not MainShellWindow window)
            return;

        var content = button.Content?.ToString() ?? "";
        if (string.Equals(content, "Refresh from YouTube", StringComparison.Ordinal) &&
            !button.Resources.Contains(GrowthAnalyticsRefreshHook))
        {
            button.Resources[GrowthAnalyticsRefreshHook] = true;
            button.Click += window.YouTubeGrowthAnalyticsRefresh_Click;
        }

        if ((string.Equals(content, "Generate + Autopilot", StringComparison.Ordinal) ||
             string.Equals(content, "Generate + Autopilot...", StringComparison.Ordinal)) &&
            !button.Resources.Contains(GrowthAutopilotBatchHook))
        {
            button.Resources[GrowthAutopilotBatchHook] = true;
            button.Click += window.YouTubeGrowthAutopilotBatch_Click;
        }
    }

    private async void YouTubeGrowthAnalyticsRefresh_Click(object sender, RoutedEventArgs e)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RefreshYouTubeGrowthAnalyticsAsync(showErrors: true);
    }

    private async void YouTubeGrowthAutopilotBatch_Click(object sender, RoutedEventArgs e)
    {
        // GenerateAndScheduleQuizBatch_Click runs first and yields before the first render.
        // This companion handler uses that yield to choose the first performance-driven
        // category, then advances the category whenever a new history row appears.
        if (!_quizBatchAutomationRunning && !_quizBatchRenderRunning)
            return;

        var originalCategory = _quizCategoryComboBox?.SelectedItem;
        var originalTitle = _quizTitleTextBox?.Text ?? "";
        var existingIds = _data.GetQuizHistory(2_000).Select(history => history.Id).ToHashSet();
        var plan = BuildYouTubeGrowthCategoryPlan(20);
        if (plan.Count == 0)
            return;

        var renderedCount = 0;
        ApplyYouTubeGrowthCategory(plan[0]);
        if (_quizPageStatusText is not null)
            _quizPageStatusText.Text = $"Growth Autopilot: starting with {plan[0]} based on channel performance";

        try
        {
            while (_quizBatchAutomationRunning || _quizBatchRenderRunning)
            {
                var createdCount = _data.GetQuizHistory(2_000).Count(history => !existingIds.Contains(history.Id));
                if (createdCount > renderedCount)
                {
                    renderedCount = createdCount;
                    if (renderedCount < plan.Count && (_quizBatchAutomationRunning || _quizBatchRenderRunning))
                    {
                        var next = plan[renderedCount];
                        ApplyYouTubeGrowthCategory(next);
                        if (_quizPageStatusText is not null)
                            _quizPageStatusText.Text = $"Growth Autopilot: next category {next}";
                    }
                }
                await Task.Delay(25);
            }
        }
        finally
        {
            if (_quizCategoryComboBox is not null && originalCategory is not null)
                _quizCategoryComboBox.SelectedItem = originalCategory;
            if (_quizTitleTextBox is not null)
                _quizTitleTextBox.Text = originalTitle;
        }
    }

    private IReadOnlyList<string> BuildYouTubeGrowthCategoryPlan(int count)
    {
        var categories = _data.GetQuizCategorySummaries()
            .Where(summary => summary.EnabledCount > 0)
            .Select(summary => summary.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categories.Length == 0)
            return [];

        var history = _data.GetQuizHistory(2_000)
            .Where(item => string.Equals(item.VideoType, "Video", StringComparison.Ordinal))
            .ToList();
        var counts = categories.ToDictionary(
            category => category,
            category => history.Count(item => string.Equals(item.AnalyticsCategory, category, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath()).ToList();
        var categoriesWithAnalytics = snapshots.Select(snapshot => snapshot.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maxViews = Math.Max(1L, history.Where(item => item.PublishedOnYouTube).Select(item => item.YouTubeViews).DefaultIfEmpty(0).Max());
        foreach (var item in history.Where(item => item.PublishedOnYouTube && item.YouTubeViews > 0))
        {
            if (categoriesWithAnalytics.Contains(item.AnalyticsCategory))
                continue;
            var score = 35 + (65 * Math.Sqrt(item.YouTubeViews / (double)maxViews));
            snapshots.Add(new YouTubeGrowthSnapshot(
                item.Id,
                YouTubeVideoAnalyticsService.TryGetVideoId(item.YouTubeUrl) ?? "history-" + item.Id,
                item.AnalyticsCategory,
                DateTime.UtcNow,
                28,
                item.YouTubeViews,
                item.YouTubeViews / 28.0,
                0,
                0,
                0,
                0,
                0,
                item.YouTubeLikes,
                0,
                Math.Round(score, 1),
                "Historical",
                "Public YouTube views used until richer Analytics Autopilot data is available."));
        }

        return YouTubeGrowthCategoryPlanner.BuildPlan(categories, snapshots, counts, count);
    }

    private void ApplyYouTubeGrowthCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || _quizCategoryComboBox is null)
            return;
        try
        {
            SelectQuizBatchCategory(category);
            if (_quizTitleTextBox is not null)
                _quizTitleTextBox.Text = category;
        }
        catch
        {
            // A category can be disabled while the app is open. The current selection
            // remains usable and the rest of the batch can continue.
        }
    }

    private async Task RefreshYouTubeGrowthAnalyticsAsync(bool showErrors)
    {
        if (_youtubeGrowthRefreshRunning)
            return;

        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthRefreshToken.Length == 0 || settings.YouTubeOAuthClientId.Length == 0)
            return;

        var history = _data.GetQuizHistory(2_000)
            .Where(item => item.PublishedOnYouTube)
            .Where(item => !item.YouTubeIsScheduled)
            .Where(item => string.Equals(item.VideoType, "Video", StringComparison.Ordinal))
            .Select(item => (History: item, VideoId: YouTubeVideoAnalyticsService.TryGetVideoId(item.YouTubeUrl)))
            .Where(item => item.VideoId is not null)
            .Select(item => (item.History, VideoId: item.VideoId!))
            .ToList();
        if (history.Count == 0)
            return;

        _youtubeGrowthRefreshRunning = true;
        try
        {
            var accessToken = await GetYouTubeManagementAccessTokenAsync();
            var end = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
            var start = end.AddDays(-27);
            var metrics = await _youtubeAnalyticsAutopilot.FetchAsync(
                accessToken,
                history.Select(item => item.VideoId),
                start,
                end);

            var raw = history
                .Where(item => metrics.ContainsKey(item.VideoId))
                .Select(item => new
                {
                    item.History,
                    item.VideoId,
                    Metric = metrics[item.VideoId],
                    AgeDays = YouTubeGrowthAgeDays(item.History),
                })
                .ToList();
            var velocities = raw
                .Select(item => item.Metric.Views / Math.Max(1.0, Math.Min(28, item.AgeDays)))
                .OrderBy(value => value)
                .ToArray();
            var median = velocities.Length == 0
                ? 1
                : velocities.Length % 2 == 1
                    ? velocities[velocities.Length / 2]
                    : (velocities[(velocities.Length / 2) - 1] + velocities[velocities.Length / 2]) / 2.0;

            var previous = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
                .GroupBy(snapshot => snapshot.VideoId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First(), StringComparer.Ordinal);
            var current = new List<YouTubeGrowthSnapshot>();
            var rescuePrepared = 0;
            foreach (var item in raw)
            {
                var assessment = YouTubeGrowthClassifier.Assess(item.Metric, item.AgeDays, median);
                var viewsPerDay = item.Metric.Views / Math.Max(1.0, Math.Min(28, item.AgeDays));
                var alreadyPrepared = previous.TryGetValue(item.VideoId, out var old) && old.RescuePackagePrepared;
                var prepared = alreadyPrepared;
                if (!prepared && string.Equals(assessment.Label, "Packaging rescue", StringComparison.Ordinal))
                {
                    try
                    {
                        GenerateHistoricalYouTubePackage(item.History);
                        prepared = true;
                        rescuePrepared++;
                    }
                    catch
                    {
                        // A rescue package is a growth enhancement, never a reason to
                        // stop analytics refresh or the publishing pipeline.
                    }
                }

                current.Add(new YouTubeGrowthSnapshot(
                    item.History.Id,
                    item.VideoId,
                    item.History.AnalyticsCategory,
                    DateTime.UtcNow,
                    item.AgeDays,
                    item.Metric.Views,
                    viewsPerDay,
                    item.Metric.EstimatedMinutesWatched,
                    item.Metric.AverageViewDurationSeconds,
                    item.Metric.AverageViewPercentage,
                    item.Metric.SubscribersGained,
                    item.Metric.SubscribersLost,
                    item.Metric.Likes,
                    item.Metric.Comments,
                    assessment.Score,
                    assessment.Label,
                    assessment.Reason,
                    prepared));
            }

            var untouched = previous.Values.Where(snapshot => current.All(item => !string.Equals(item.VideoId, snapshot.VideoId, StringComparison.Ordinal)));
            YouTubeGrowthSnapshotStore.Save(YouTubeGrowthStorePath(), current.Concat(untouched));

            var winners = current.Count(snapshot => string.Equals(snapshot.Label, "Winner", StringComparison.Ordinal));
            var rescues = current.Count(snapshot => string.Equals(snapshot.Label, "Packaging rescue", StringComparison.Ordinal));
            if (_youtubeAnalyticsPageStatus is not null)
            {
                _youtubeAnalyticsPageStatus.Text =
                    $"Analytics Autopilot: {current.Count:N0} full quizzes scored • {winners:N0} winners • {rescues:N0} packaging rescue" +
                    (rescuePrepared > 0 ? $" • {rescuePrepared:N0} fresh A/B package(s) prepared" : "");
            }
            RefreshYouTubeRecommendation();
        }
        catch (Exception error)
        {
            if (_youtubeAnalyticsPageStatus is not null)
                _youtubeAnalyticsPageStatus.Text = "Analytics Autopilot: " + error.Message;
            if (showErrors && error.Message.Contains("Reconnect YouTube", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    error.Message,
                    "Analytics Autopilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            _youtubeGrowthRefreshRunning = false;
        }
    }

    private string YouTubeGrowthStorePath() =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_data.SettingsPath))!, "youtube-growth-analytics.json");

    private static double YouTubeGrowthAgeDays(QuizHistorySummary history)
    {
        if (DateTimeOffset.TryParse(history.YouTubeScheduledFor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var scheduled) &&
            scheduled <= DateTimeOffset.Now)
            return Math.Max(0.25, (DateTimeOffset.Now - scheduled).TotalDays);
        if (DateTime.TryParse(history.YouTubeUploadDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var uploaded))
            return Math.Max(0.25, (DateTime.Now - uploaded).TotalDays);
        if (DateTime.TryParse(history.Created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var created))
            return Math.Max(0.25, (DateTime.Now - created).TotalDays);
        return 28;
    }
}
