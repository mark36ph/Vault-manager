using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record YouTubeGrowthUiSummary(
    string RecommendedCategory,
    string RecommendationReason,
    string TopCategory,
    int Scored,
    int Winners,
    int Rescues,
    int WeakTopics,
    int Learning);

public static class YouTubeGrowthUiSummaryBuilder
{
    public static YouTubeGrowthUiSummary Build(
        IReadOnlyList<string> categoryPlan,
        IReadOnlyList<YouTubeGrowthSnapshot> snapshots)
    {
        var latest = snapshots
            .GroupBy(snapshot => snapshot.HistoryId)
            .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
            .ToList();
        var recent = latest.Where(snapshot => snapshot.CheckedAtUtc >= DateTime.UtcNow.AddDays(-60)).ToList();
        if (recent.Count > 0)
            latest = recent;

        var recommended = categoryPlan.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(recommended))
            recommended = "General Knowledge";

        var mature = latest
            .Where(snapshot => !string.Equals(snapshot.Label, "Learning", StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => !string.Equals(snapshot.Label, "Historical", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var topCategory = mature
            .GroupBy(snapshot => snapshot.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Category = group.Key,
                Score = group.Average(snapshot => snapshot.Score),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Category)
            .FirstOrDefault() ?? "Learning";

        var recommendedRows = mature
            .Where(snapshot => string.Equals(snapshot.Category, recommended, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string reason;
        if (recommendedRows.Count == 0)
        {
            reason = mature.Count == 0
                ? "Analytics Autopilot is still learning from the first full-video performance window."
                : $"{recommended} is in the rotation/experiment mix while Autopilot gathers more full-video evidence.";
        }
        else
        {
            var averageScore = recommendedRows.Average(snapshot => snapshot.Score);
            var winners = recommendedRows.Count(snapshot => string.Equals(snapshot.Label, "Winner", StringComparison.OrdinalIgnoreCase));
            reason = winners > 0
                ? $"{recommended} has produced {winners:N0} Winner result{(winners == 1 ? "" : "s")} and averages {averageScore:0.0}/100, so Autopilot is giving it another full-video slot."
                : $"{recommended} averages {averageScore:0.0}/100 in recent full-video performance and is the next Growth Autopilot slot.";
        }

        return new YouTubeGrowthUiSummary(
            recommended,
            reason,
            topCategory,
            latest.Count,
            latest.Count(snapshot => string.Equals(snapshot.Label, "Winner", StringComparison.OrdinalIgnoreCase)),
            latest.Count(snapshot => string.Equals(snapshot.Label, "Packaging rescue", StringComparison.OrdinalIgnoreCase)),
            latest.Count(snapshot => string.Equals(snapshot.Label, "Weak topic", StringComparison.OrdinalIgnoreCase)),
            latest.Count(snapshot => string.Equals(snapshot.Label, "Learning", StringComparison.OrdinalIgnoreCase)));
    }
}

public partial class MainShellWindow
{
    private const string YouTubeGrowthUiRefreshHook = "youtube-growth-ui-refresh-hook";
    private static readonly bool YouTubeGrowthAnalyticsUiRegistered = RegisterYouTubeGrowthAnalyticsUi();

    private static bool RegisterYouTubeGrowthAnalyticsUi()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(YouTubeGrowthAnalyticsGrid_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(YouTubeGrowthAnalyticsRefreshButton_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void YouTubeGrowthAnalyticsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid ||
            Window.GetWindow(grid) is not MainShellWindow window ||
            !grid.Columns.Any(column => string.Equals(column.Header?.ToString(), "Engagement", StringComparison.Ordinal)))
        {
            return;
        }

        window._youtubeAnalyticsGrid = grid;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(window.ApplyYouTubeGrowthAnalyticsUi));
    }

    private static void YouTubeGrowthAnalyticsRefreshButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Refresh from YouTube", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window ||
            button.Resources.Contains(YouTubeGrowthUiRefreshHook))
        {
            return;
        }

        button.Resources[YouTubeGrowthUiRefreshHook] = true;
        button.Click += window.YouTubeGrowthAnalyticsUiRefresh_Click;
    }

    private async void YouTubeGrowthAnalyticsUiRefresh_Click(object sender, RoutedEventArgs e)
    {
        // The public-stat refresh and richer Analytics Autopilot refresh are separate
        // non-blocking handlers. Wait for both so the growth UI is always the final view.
        await Task.Delay(150);
        for (var attempt = 0; attempt < 600 && (_youtubeAnalyticsPageRefreshing || _youtubeGrowthRefreshRunning); attempt++)
            await Task.Delay(100);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        ApplyYouTubeGrowthAnalyticsUi();
    }

    private void ApplyYouTubeGrowthAnalyticsUi()
    {
        if (_youtubeAnalyticsGrid is null)
            return;

        var history = _data.GetQuizHistory(2_000);
        var historyById = history.ToDictionary(item => item.Id);
        var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
            .GroupBy(snapshot => snapshot.HistoryId)
            .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
            .ToList();
        var snapshotsByHistoryId = snapshots.ToDictionary(snapshot => snapshot.HistoryId);

        var plan = BuildYouTubeGrowthCategoryPlan(1);
        var summary = YouTubeGrowthUiSummaryBuilder.Build(plan, snapshots);

        if (_youtubeNextQuizText is not null)
            _youtubeNextQuizText.Text = $"{summary.RecommendedCategory} Quiz — Full video";
        if (_youtubeNextQuizReasonText is not null)
            _youtubeNextQuizReasonText.Text = summary.RecommendationReason;

        RenameYouTubeAnalyticsStatCard(_youtubeTrackedVideosText, "Full videos scored");
        RenameYouTubeAnalyticsStatCard(_youtubeTrackedViewsText, "28-day full views");
        RenameYouTubeAnalyticsStatCard(_youtubeTrackedLikesText, "28-day full likes");
        RenameYouTubeAnalyticsStatCard(_youtubeTrackedCommentsText, "28-day full comments");
        RenameYouTubeAnalyticsStatCard(_youtubeTopCategoryText, "Top growth category");

        if (_youtubeTrackedVideosText is not null)
            _youtubeTrackedVideosText.Text = summary.Scored.ToString("N0");
        if (_youtubeTrackedViewsText is not null)
            _youtubeTrackedViewsText.Text = snapshots.Sum(snapshot => snapshot.Views).ToString("N0");
        if (_youtubeTrackedLikesText is not null)
            _youtubeTrackedLikesText.Text = snapshots.Sum(snapshot => snapshot.Likes).ToString("N0");
        if (_youtubeTrackedCommentsText is not null)
            _youtubeTrackedCommentsText.Text = snapshots.Sum(snapshot => snapshot.Comments).ToString("N0");
        if (_youtubeTopCategoryText is not null)
            _youtubeTopCategoryText.Text = summary.TopCategory;

        if (_youtubeAnalyticsGrid.ItemsSource is IEnumerable<YouTubeAnalyticsRow> sourceRows)
        {
            var rows = sourceRows
                .Where(row => historyById.TryGetValue(row.HistoryId, out var item) &&
                              string.Equals(item.VideoType, "Video", StringComparison.Ordinal))
                .OrderByDescending(row => snapshotsByHistoryId.GetValueOrDefault(row.HistoryId)?.Score ?? -1)
                .ThenByDescending(row => row.Views)
                .ToList();
            ConfigureYouTubeGrowthAnalyticsColumns(_youtubeAnalyticsGrid, snapshotsByHistoryId);
            _youtubeAnalyticsGrid.ItemsSource = rows;
        }

        if (_youtubeAnalyticsPageStatus is not null)
        {
            _youtubeAnalyticsPageStatus.Text = snapshots.Count == 0
                ? "Growth Autopilot connected • waiting for published full-video analytics."
                : $"Growth Autopilot: {summary.Scored:N0} full videos scored • {summary.Winners:N0} winners • {summary.Rescues:N0} packaging rescue • {summary.WeakTopics:N0} weak topic • {summary.Learning:N0} learning";
        }
    }

    private static void RenameYouTubeAnalyticsStatCard(TextBlock? value, string label)
    {
        if (value?.Parent is not StackPanel panel)
            return;
        var labelText = panel.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => !ReferenceEquals(text, value));
        if (labelText is not null)
            labelText.Text = label;
    }

    private static void ConfigureYouTubeGrowthAnalyticsColumns(
        DataGrid grid,
        IReadOnlyDictionary<int, YouTubeGrowthSnapshot> snapshots)
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(YouTubeAnalyticsRow.Quiz)),
            SortMemberPath = nameof(YouTubeAnalyticsRow.Quiz),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(GrowthColumn("Category", snapshots, YouTubeGrowthColumnValue.Category, 126));
        grid.Columns.Add(NumberColumn("Views", nameof(YouTubeAnalyticsRow.Views), 80));
        grid.Columns.Add(GrowthColumn("Views/day", snapshots, YouTubeGrowthColumnValue.ViewsPerDay, 88));
        grid.Columns.Add(GrowthColumn("Avg viewed", snapshots, YouTubeGrowthColumnValue.AverageViewed, 92));
        grid.Columns.Add(GrowthColumn("Watch mins", snapshots, YouTubeGrowthColumnValue.WatchMinutes, 92));
        grid.Columns.Add(GrowthColumn("Subs", snapshots, YouTubeGrowthColumnValue.Subscribers, 64));
        grid.Columns.Add(GrowthColumn("Score", snapshots, YouTubeGrowthColumnValue.Score, 68));
        grid.Columns.Add(GrowthColumn("Status", snapshots, YouTubeGrowthColumnValue.Status, 158));
    }

    private static DataGridTextColumn GrowthColumn(
        string header,
        IReadOnlyDictionary<int, YouTubeGrowthSnapshot> snapshots,
        YouTubeGrowthColumnValue value,
        double width) => new()
    {
        Header = header,
        Binding = new Binding(nameof(YouTubeAnalyticsRow.HistoryId))
        {
            Converter = new YouTubeGrowthHistoryValueConverter(snapshots, value),
        },
        Width = new DataGridLength(width),
    };
}

internal enum YouTubeGrowthColumnValue
{
    Category,
    ViewsPerDay,
    AverageViewed,
    WatchMinutes,
    Subscribers,
    Score,
    Status,
}

internal sealed class YouTubeGrowthHistoryValueConverter : IValueConverter
{
    private readonly IReadOnlyDictionary<int, YouTubeGrowthSnapshot> _snapshots;
    private readonly YouTubeGrowthColumnValue _value;

    public YouTubeGrowthHistoryValueConverter(
        IReadOnlyDictionary<int, YouTubeGrowthSnapshot> snapshots,
        YouTubeGrowthColumnValue value)
    {
        _snapshots = snapshots;
        _value = value;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int historyId || !_snapshots.TryGetValue(historyId, out var snapshot))
            return "—";

        return _value switch
        {
            YouTubeGrowthColumnValue.Category => snapshot.Category,
            YouTubeGrowthColumnValue.ViewsPerDay => snapshot.ViewsPerDay.ToString("0.0", CultureInfo.InvariantCulture),
            YouTubeGrowthColumnValue.AverageViewed => snapshot.AverageViewPercentage.ToString("0.0'%'", CultureInfo.InvariantCulture),
            YouTubeGrowthColumnValue.WatchMinutes => snapshot.EstimatedMinutesWatched.ToString("N0", CultureInfo.InvariantCulture),
            YouTubeGrowthColumnValue.Subscribers => NetSubscribers(snapshot).ToString("+0;-0;0", CultureInfo.InvariantCulture),
            YouTubeGrowthColumnValue.Score => snapshot.Score.ToString("0.0", CultureInfo.InvariantCulture),
            YouTubeGrowthColumnValue.Status => snapshot.RescuePackagePrepared && string.Equals(snapshot.Label, "Packaging rescue", StringComparison.OrdinalIgnoreCase)
                ? "Packaging rescue • ready"
                : snapshot.Label,
            _ => "—",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static long NetSubscribers(YouTubeGrowthSnapshot snapshot) =>
        snapshot.SubscribersGained - snapshot.SubscribersLost;
}
