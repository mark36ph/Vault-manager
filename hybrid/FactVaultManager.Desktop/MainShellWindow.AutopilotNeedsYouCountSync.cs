using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record AutopilotNeedsYouGroupedSummary(
    int Total,
    int RelatedVideos,
    int InstagramPromos,
    int PackagingRescues,
    int ViewerReplies,
    int ReleaseWarnings);

public static class AutopilotNeedsYouCountSummary
{
    public static int Total(
        int relatedVideos,
        int instagramPromos,
        int packagingRescues,
        int viewerReplies,
        int releaseWarnings)
    {
        return Math.Max(0, relatedVideos) +
               Math.Max(0, instagramPromos) +
               Math.Max(0, packagingRescues) +
               Math.Max(0, viewerReplies) +
               Math.Max(0, releaseWarnings);
    }

    public static string Health(bool running, int total) =>
        total > 0 ? "Needs you" : running ? "Working" : "Healthy";

    public static AutopilotNeedsYouGroupedSummary FromAlignedTasks(
        IEnumerable<AutopilotAlignedTaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        // "Needs you" means something the user can act on now. Future Related Video,
        // Instagram and other queued work remains in the aligned planner, but it must not
        // inflate the home counter or make Start next task open an empty wizard.
        var list = tasks
            .Where(task => task.ActionReady)
            .ToList();
        var relatedVideos = AutopilotNeedsYouAlignedPlanner.Count(list, AutopilotAlignedTaskKind.RelatedVideo);
        var instagramPromos = AutopilotNeedsYouAlignedPlanner.Count(list, AutopilotAlignedTaskKind.InstagramPromo);
        var packagingRescues = AutopilotNeedsYouAlignedPlanner.Count(list, AutopilotAlignedTaskKind.PackagingRescue);
        var viewerReplies = AutopilotNeedsYouAlignedPlanner.Count(list, AutopilotAlignedTaskKind.ViewerReply);
        var releaseWarnings = AutopilotNeedsYouAlignedPlanner.Count(list, AutopilotAlignedTaskKind.ReleaseWarning);

        return new AutopilotNeedsYouGroupedSummary(
            Total(relatedVideos, instagramPromos, packagingRescues, viewerReplies, releaseWarnings),
            relatedVideos,
            instagramPromos,
            packagingRescues,
            viewerReplies,
            releaseWarnings);
    }
}

public partial class MainShellWindow
{
    private DispatcherTimer? _autopilotNeedsYouCountSyncTimer;
    private int _autopilotNeedsYouCountSyncBusy;

    public void InitializeAutopilotNeedsYouCountSync()
    {
        if (_autopilotNeedsYouCountSyncTimer is not null) return;

        // Needs You reads Autopilot state and YouTube snapshot files. Keep those reads off the
        // WPF dispatcher and never allow the five-second timer to stack overlapping refreshes.
        _autopilotNeedsYouCountSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _autopilotNeedsYouCountSyncTimer.Tick += async (_, _) =>
        {
            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                await SyncAutopilotNeedsYouCountAsync();
        };
        _autopilotNeedsYouCountSyncTimer.Start();

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) ||
                MainTabs.SelectedIndex != _autopilotHomeTabIndex)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => _ = SyncAutopilotNeedsYouCountAsync()));
        };

        Closed += (_, _) => _autopilotNeedsYouCountSyncTimer?.Stop();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                    _ = SyncAutopilotNeedsYouCountAsync();
            }));
    }

    private async Task SyncAutopilotNeedsYouCountAsync()
    {
        if (_autopilotHomeRefreshing ||
            _autopilotHomeTabIndex < 0 ||
            MainTabs.SelectedIndex != _autopilotHomeTabIndex ||
            Interlocked.CompareExchange(ref _autopilotNeedsYouCountSyncBusy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var rows = _scheduledReadinessRows
                .Where(row => row.PublishAt >= DateTimeOffset.Now.AddHours(-2))
                .ToList();
            var settingsPath = _data.SettingsPath;
            var growthStorePath = YouTubeGrowthStorePath();

            var grouped = await Task.Run(() =>
            {
                var state = FactburstFullAutopilotStateStore.Load(settingsPath);
                var snapshots = YouTubeGrowthSnapshotStore.Load(growthStorePath)
                    .GroupBy(snapshot => snapshot.HistoryId)
                    .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                    .ToList();
                var tasks = AutopilotNeedsYouAlignedPlanner.Build(rows, state, snapshots);
                return AutopilotNeedsYouCountSummary.FromAlignedTasks(tasks);
            });

            if (!IsLoaded ||
                _autopilotHomeTabIndex < 0 ||
                MainTabs.SelectedIndex != _autopilotHomeTabIndex)
            {
                return;
            }

            var total = grouped.Total;
            var health = AutopilotNeedsYouCountSummary.Health(_fullAutopilotRunning, total);
            var needsText = total == 0 ? "Nothing" : total.ToString("N0");
            var needsChanged = !string.Equals(_autopilotNeedsText?.Text, needsText, StringComparison.Ordinal);

            SetAutopilotTextIfChanged(_autopilotNeedsText, needsText);
            SetAutopilotTextIfChanged(
                _autopilotNeedsNoteText,
                total == 0
                    ? "Autopilot is handling the queue"
                    : "Ready now — Factburst will guide you one task at a time");
            SetAutopilotTextIfChanged(_autopilotHealthText, health);

            // The cleanup walks the Needs You visual tree, so only run it when the visible count
            // actually changes instead of doing the same work on every five-second timer tick.
            if (needsChanged)
                ApplyAutopilotHomeCleanup();

            SetAutopilotTextIfChanged(
                HeaderStatusText,
                $"Autopilot: {health} • {rows.Count:N0} scheduled • {total:N0} need you");
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot Needs You count sync failed: " + error);
        }
        finally
        {
            Interlocked.Exchange(ref _autopilotNeedsYouCountSyncBusy, 0);
        }
    }

    private static void SetAutopilotTextIfChanged(TextBlock? target, string value)
    {
        if (target is null || string.Equals(target.Text, value, StringComparison.Ordinal))
            return;
        target.Text = value;
    }
}
