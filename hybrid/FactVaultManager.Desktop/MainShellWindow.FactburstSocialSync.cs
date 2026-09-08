using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

/// <summary>
/// Publishes the desktop app's local social publication state to the Factburst website.
/// The desktop remains the source of truth for actual media uploads; this is a status/stats
/// bridge only and never uploads media to the website.
/// </summary>
public partial class MainShellWindow
{
    private readonly DispatcherTimer _factburstSocialSyncTimer = new();
    private bool _factburstSocialSyncRunning;

    static MainShellWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindow_LoadedForFactburstSocialSync));
    }

    private static void MainShellWindow_LoadedForFactburstSocialSync(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
            window.InitializeFactburstSocialSync();
    }

    private void InitializeFactburstSocialSync()
    {
        if (_factburstSocialSyncTimer.Interval != TimeSpan.Zero)
            return;

        _factburstSocialSyncTimer.Interval = TimeSpan.FromMinutes(15);
        _factburstSocialSyncTimer.Tick += async (_, _) => await SyncFactburstSocialStateAsync();
        Closed += (_, _) => _factburstSocialSyncTimer.Stop();
        _factburstSocialSyncTimer.Start();

        _ = SyncFactburstSocialStateAsync();
    }

    private async Task SyncFactburstSocialStateAsync()
    {
        if (_factburstSocialSyncRunning)
            return;

        _factburstSocialSyncRunning = true;
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured)
                return;

            var histories = _data.GetQuizHistory();
            if (histories.Count == 0)
                return;

            using var client = new FactburstWebsiteSocialStatsClient();
            var capturedAt = DateTimeOffset.UtcNow;
            var records = new List<FactburstSocialStatsRecord>(histories.Count * 3);

            foreach (var history in histories)
            {
                foreach (var platform in new[] { "youtube", "facebook", "instagram" })
                {
                    try
                    {
                        records.Add(FactburstWebsiteSocialStatsClient.FromHistory(history, platform, capturedAt));
                    }
                    catch (Exception error)
                    {
                        Debug.WriteLine($"Factburst social sync skipped quiz {history.Id}/{platform}: {error.Message}");
                    }
                }
            }

            foreach (var batch in records.Chunk(100))
                await client.PushAsync(tracker.ApiKey, batch);
        }
        catch (Exception error)
        {
            // Social sync must never interrupt quiz generation, rendering, or publishing.
            Debug.WriteLine("Factburst social sync failed: " + error.Message);
        }
        finally
        {
            _factburstSocialSyncRunning = false;
        }
    }
}
