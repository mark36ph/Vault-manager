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

    public void InitializeAutopilotNeedsYouCountSync()
    {
        if (_autopilotNeedsYouCountSyncTimer is not null) return;

        // Needs You reads Autopilot state and YouTube snapshot files. It only needs a frequent
        // refresh while the Autopilot home page is visible; publishing supervisors have their
        // own cadence and are not driven by this UI counter.
        _autopilotNeedsYouCountSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _autopilotNeedsYouCountSyncTimer.Tick += (_, _) =>
        {
            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                SyncAutopilotNeedsYouCount();
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
                new Action(SyncAutopilotNeedsYouCount));
        };

        Closed += (_, _) => _autopilotNeedsYouCountSyncTimer?.Stop();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                    SyncAutopilotNeedsYouCount();
            }));
    }

    private void SyncAutopilotNeedsYouCount()
    {
        if (_autopilotHomeRefreshing ||
            _autopilotHomeTabIndex < 0 ||
            MainTabs.SelectedIndex != _autopilotHomeTabIndex)
        {
            return;
        }

        try
        {
            var rows = _scheduledReadinessRows
                .Where(row => row.PublishAt >= DateTimeOffset.Now.AddHours(-2))
                .ToList();
            var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
                .GroupBy(snapshot => snapshot.HistoryId)
                .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                .ToList();

            var tasks = AutopilotNeedsYouAlignedPlanner.Build(rows, state, snapshots);
            var grouped = AutopilotNeedsYouCountSummary.FromAlignedTasks(tasks);
            var total = grouped.Total;
            var health = AutopilotNeedsYouCountSummary.Health(_fullAutopilotRunning, total);

            SetAutopilotTextIfChanged(_autopilotNeedsText, total == 0 ? "Nothing" : total.ToString("N0"));
            SetAutopilotTextIfChanged(
                _autopilotNeedsNoteText,
                total == 0
                    ? "Autopilot is handling the queue"
                    : "Ready now — Factburst will guide you one task at a time");
            SetAutopilotTextIfChanged(_autopilotHealthText, health);

            // Daily UI cleanup owns the guided Needs You panel. Update the existing controls in
            // place instead of rebuilding task rows.
            ApplyAutopilotHomeCleanup();

            SetAutopilotTextIfChanged(
                HeaderStatusText,
                $"Autopilot: {health} • {rows.Count:N0} scheduled • {total:N0} need you");
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot Needs You count sync failed: " + error);
        }
    }

    private static void SetAutopilotTextIfChanged(TextBlock? target, string value)
    {
        if (target is null || string.Equals(target.Text, value, StringComparison.Ordinal))
            return;
        target.Text = value;
    }
}
