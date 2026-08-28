using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ReliableGrowthRefreshHook = "youtube-growth-ui-reliable-refresh";
    private const string ReliableGrowthTabHook = "youtube-growth-ui-reliable-tab";
    private bool _youtubeGrowthAnalyticsUiReliableInitialized;
    private bool _youtubeGrowthAnalyticsUiApplyQueued;

    internal void InitializeYouTubeGrowthAnalyticsUiReliably()
    {
        if (_youtubeGrowthAnalyticsUiReliableInitialized)
            return;

        _youtubeGrowthAnalyticsUiReliableInitialized = true;

        if (_youtubeManagerContent is not null)
            _youtubeManagerContent.LayoutUpdated += YouTubeGrowthManagerContent_LayoutUpdated;

        if (_youtubeManagerButtons.TryGetValue("analytics", out var analyticsButton) &&
            !analyticsButton.Resources.Contains(ReliableGrowthTabHook))
        {
            analyticsButton.Resources[ReliableGrowthTabHook] = true;
            analyticsButton.Click += YouTubeGrowthAnalyticsTab_Click;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                EnsureYouTubeGrowthAnalyticsUiWiring();
                ApplyYouTubeGrowthAnalyticsUi();
            }));
    }

    private void YouTubeGrowthManagerContent_LayoutUpdated(object? sender, EventArgs e)
    {
        if (!string.Equals(_youtubeManagerSection, "analytics", StringComparison.OrdinalIgnoreCase) ||
            _youtubeGrowthAnalyticsUiApplyQueued)
        {
            return;
        }

        _youtubeGrowthAnalyticsUiApplyQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _youtubeGrowthAnalyticsUiApplyQueued = false;
                EnsureYouTubeGrowthAnalyticsUiWiring();

                // Re-apply only when the legacy columns are visible. Once Growth UI has
                // replaced them, LayoutUpdated can continue without repeatedly rebinding.
                if (_youtubeAnalyticsGrid?.Columns.Any(column =>
                        string.Equals(column.Header?.ToString(), "Engagement", StringComparison.Ordinal)) == true)
                {
                    ApplyYouTubeGrowthAnalyticsUi();
                }
            }));
    }

    private void EnsureYouTubeGrowthAnalyticsUiWiring()
    {
        if (Content is not DependencyObject root)
            return;

        var refreshButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Content?.ToString(),
                "Refresh from YouTube",
                StringComparison.Ordinal));
        if (refreshButton is not null && !refreshButton.Resources.Contains(ReliableGrowthRefreshHook))
        {
            refreshButton.Resources[ReliableGrowthRefreshHook] = true;
            refreshButton.Click += YouTubeGrowthAnalyticsReliableRefresh_Click;
        }

        if (_youtubeManagerButtons.TryGetValue("analytics", out var analyticsButton) &&
            !analyticsButton.Resources.Contains(ReliableGrowthTabHook))
        {
            analyticsButton.Resources[ReliableGrowthTabHook] = true;
            analyticsButton.Click += YouTubeGrowthAnalyticsTab_Click;
        }
    }

    private async void YouTubeGrowthAnalyticsReliableRefresh_Click(object sender, RoutedEventArgs e)
    {
        // The existing Refresh from YouTube handler starts first. Wait for it to finish,
        // then explicitly run the richer analytics refresh rather than relying on another
        // runtime-discovered button hook.
        await Task.Delay(75);
        for (var attempt = 0; attempt < 600 && _youtubeAnalyticsPageRefreshing; attempt++)
            await Task.Delay(100);

        await RefreshYouTubeGrowthAnalyticsAsync(showErrors: true);
        for (var attempt = 0; attempt < 600 && _youtubeGrowthRefreshRunning; attempt++)
            await Task.Delay(100);

        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        ApplyYouTubeGrowthAnalyticsUi();
    }

    private async void YouTubeGrowthAnalyticsTab_Click(object sender, RoutedEventArgs e)
    {
        // The manager navigation rebuilds the Analytics section. Wait for the existing
        // navigation refresh to settle, then wire the newly-created controls and show the
        // Growth view as the final state.
        await Task.Delay(75);
        for (var attempt = 0; attempt < 600 && _youtubeAnalyticsPageRefreshing; attempt++)
            await Task.Delay(100);

        EnsureYouTubeGrowthAnalyticsUiWiring();
        await RefreshYouTubeGrowthAnalyticsAsync(showErrors: false);
        for (var attempt = 0; attempt < 600 && _youtubeGrowthRefreshRunning; attempt++)
            await Task.Delay(100);

        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        ApplyYouTubeGrowthAnalyticsUi();
    }
}
