using System.Diagnostics;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

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
}

public partial class MainShellWindow
{
    private DispatcherTimer? _autopilotNeedsYouCountSyncTimer;

    public void InitializeAutopilotNeedsYouCountSync()
    {
        if (_autopilotNeedsYouCountSyncTimer is not null) return;

        _autopilotNeedsYouCountSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _autopilotNeedsYouCountSyncTimer.Tick += (_, _) => SyncAutopilotNeedsYouCount();
        _autopilotNeedsYouCountSyncTimer.Start();

        Closed += (_, _) => _autopilotNeedsYouCountSyncTimer?.Stop();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(SyncAutopilotNeedsYouCount));
    }

    private void SyncAutopilotNeedsYouCount()
    {
        if (_autopilotHomeRefreshing || _autopilotHomeTabIndex < 0) return;

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
            var total = tasks.Count;
            var health = AutopilotHomePlanner.Health(_fullAutopilotRunning, total);

            if (_autopilotNeedsText is not null)
                _autopilotNeedsText.Text = total == 0 ? "Nothing" : total.ToString("N0");
            if (_autopilotNeedsNoteText is not null)
                _autopilotNeedsNoteText.Text = total == 0
                    ? "Autopilot is handling the queue"
                    : "Actual pending tasks requiring your input";
            if (_autopilotHealthText is not null)
                _autopilotHealthText.Text = health;

            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                HeaderStatusText.Text = $"Autopilot: {health} • {rows.Count:N0} scheduled • {total:N0} need you";
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot Needs You count sync failed: " + error);
        }
    }
}
