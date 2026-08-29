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

    public static AutopilotNeedsYouGroupedSummary FromAlignedTasks(
        IEnumerable<AutopilotAlignedTaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var list = tasks.ToList();
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

    public static bool NeedsCardRefresh(
        AutopilotNeedsYouGroupedSummary? previous,
        AutopilotNeedsYouGroupedSummary current) =>
        previous is null || previous != current;
}

public partial class MainShellWindow
{
    private DispatcherTimer? _autopilotNeedsYouCountSyncTimer;
    private AutopilotNeedsYouGroupedSummary? _autopilotNeedsYouRenderedSummary;

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
            var grouped = AutopilotNeedsYouCountSummary.FromAlignedTasks(tasks);
            var total = grouped.Total;
            var health = AutopilotHomePlanner.Health(_fullAutopilotRunning, total);

            if (_autopilotNeedsText is not null)
                _autopilotNeedsText.Text = total == 0 ? "Nothing" : total.ToString("N0");
            if (_autopilotNeedsNoteText is not null)
                _autopilotNeedsNoteText.Text = total == 0
                    ? "Autopilot is handling the queue"
                    : "Actual pending tasks requiring your input";
            if (_autopilotHealthText is not null)
                _autopilotHealthText.Text = health;

            var cards = BuildAutopilotHomeTaskCards(grouped);
            var summaryChanged = AutopilotNeedsYouCountSummary.NeedsCardRefresh(_autopilotNeedsYouRenderedSummary, grouped);
            var visibleCardsMatch = RenderedAutopilotHomeTaskCardsMatch(cards);
            if (summaryChanged || !visibleCardsMatch)
            {
                RenderManualAutopilotTasks(cards);
                _autopilotNeedsYouRenderedSummary = grouped;
            }

            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                HeaderStatusText.Text = $"Autopilot: {health} • {rows.Count:N0} scheduled • {total:N0} need you";
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot Needs You count sync failed: " + error);
        }
    }

    private List<AutopilotManualTask> BuildAutopilotHomeTaskCards(AutopilotNeedsYouGroupedSummary grouped)
    {
        var cards = new List<AutopilotManualTask>();

        if (grouped.RelatedVideos > 0)
        {
            cards.Add(new AutopilotManualTask(
                $"Set Related Video on {grouped.RelatedVideos:N0} Short{(grouped.RelatedVideos == 1 ? "" : "s")}",
                "YouTube keeps this Studio-only, so Autopilot cannot set it through the API.",
                "Release Readiness",
                "Open tasks"));
        }

        if (grouped.InstagramPromos > 0)
        {
            cards.Add(new AutopilotManualTask(
                $"Instagram: {grouped.InstagramPromos:N0} promo{(grouped.InstagramPromos == 1 ? "" : "s")}",
                "Only Instagram promos whose posting time has arrived are shown here.",
                "Instagram Manager",
                "Open Instagram"));
        }

        if (grouped.PackagingRescues > 0)
        {
            cards.Add(new AutopilotManualTask(
                $"Review {grouped.PackagingRescues:N0} packaging rescue{(grouped.PackagingRescues == 1 ? "" : "s")}",
                "Replacement A/B/C title and thumbnail packages are prepared; applying them remains your decision.",
                "YouTube Manager",
                "Review rescue"));
        }

        if (grouped.ViewerReplies > 0)
        {
            cards.Add(new AutopilotManualTask(
                $"Review {grouped.ViewerReplies:N0} viewer repl{(grouped.ViewerReplies == 1 ? "y" : "ies")}",
                "Autopilot drafted replies but will not speak publicly to viewers without approval.",
                "YouTube Manager",
                "Review replies"));
        }

        if (grouped.ReleaseWarnings > 0)
        {
            cards.Add(new AutopilotManualTask(
                $"Check {grouped.ReleaseWarnings:N0} release packaging warning{(grouped.ReleaseWarnings == 1 ? "" : "s")}",
                "Autopilot will repair safe release state, but it will not blindly overwrite a live title or thumbnail.",
                "Release Readiness",
                "Review warning"));
        }

        return cards;
    }

    private bool RenderedAutopilotHomeTaskCardsMatch(IReadOnlyList<AutopilotManualTask> expectedCards)
    {
        if (_autopilotNeedsPanel is null) return false;

        var expectedTitles = expectedCards
            .Select(card => card.Title.Trim())
            .ToList();
        var renderedTitles = _autopilotNeedsPanel.Children
            .OfType<Grid>()
            .Select(row => row.Children.OfType<StackPanel>().FirstOrDefault())
            .Where(stack => stack is not null)
            .Select(stack => stack!.Children.OfType<TextBlock>().FirstOrDefault()?.Text?.Trim() ?? "")
            .Where(IsAutopilotHomeTaskCardTitle)
            .ToList();

        return renderedTitles.SequenceEqual(expectedTitles, StringComparer.Ordinal);
    }

    private static bool IsAutopilotHomeTaskCardTitle(string title) =>
        title.StartsWith("Set Related Video on ", StringComparison.Ordinal) ||
        title.StartsWith("Instagram: ", StringComparison.Ordinal) ||
        title.StartsWith("Review ", StringComparison.Ordinal) ||
        title.StartsWith("Check ", StringComparison.Ordinal);
}
