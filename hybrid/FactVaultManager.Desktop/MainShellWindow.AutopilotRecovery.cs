using System.Diagnostics;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private DispatcherTimer? _autopilotRecoveryTimer;
    private bool _autopilotRecoveryInitialized;
    private bool _autopilotRecoveryRunning;

    public void InitializeAutopilotRecoverySupervisor()
    {
        if (_autopilotRecoveryInitialized) return;
        _autopilotRecoveryInitialized = true;

        Loaded += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            await RunAutopilotRecoveryPassAsync();
        };

        _autopilotRecoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _autopilotRecoveryTimer.Tick += async (_, _) => await RunAutopilotRecoveryPassAsync();
        _autopilotRecoveryTimer.Start();
    }

    private async Task RunAutopilotRecoveryPassAsync()
    {
        if (_autopilotRecoveryRunning) return;
        _autopilotRecoveryRunning = true;
        try
        {
            var state = AutopilotRecoveryStateStore.Load(_data.SettingsPath);
            var appSettings = _data.LoadSettings();
            var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);

            await RunRecoveryStepAsync(
                state,
                "YouTube",
                !string.IsNullOrWhiteSpace(appSettings.YouTubeOAuthClientId) &&
                !string.IsNullOrWhiteSpace(appSettings.YouTubeOAuthRefreshToken),
                async () =>
                {
                    var token = await GetYouTubeManagementAccessTokenAsync();
                    var channel = await _youtubeManagement.GetMyChannelAsync(token);
                    SocialPublishingAccountGuard.EnsureMatches(
                        "YouTube channel",
                        appSettings.ApprovedYouTubeChannelId,
                        channel.Id);
                });

            await RunRecoveryStepAsync(
                state,
                "Facebook",
                !string.IsNullOrWhiteSpace(appSettings.FacebookPageAccessToken),
                async () =>
                {
                    var page = await _facebookAnalytics.GetPageIdentityAsync(appSettings.FacebookPageAccessToken);
                    SocialPublishingAccountGuard.EnsureMatches(
                        "Facebook Page",
                        appSettings.ApprovedFacebookPageId,
                        page.PageId);
                });

            await RunRecoveryStepAsync(
                state,
                "Tracker",
                trackerSettings.IsConfigured,
                async () =>
                {
                    var tracker = new FactburstLinkTrackerClient();
                    if (!await tracker.HealthAsync(trackerSettings.BaseUrl))
                        throw new InvalidOperationException("The branded Factburst tracker health check did not return OK.");
                });

            await RunRecoveryStepAsync(
                state,
                "Website",
                trackerSettings.IsConfigured,
                async () =>
                {
                    using var website = new FactburstWebsitePublishingClient();
                    _ = await website.FetchQuizzesAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey);
                });

            AutopilotRecoveryStateStore.Save(_data.SettingsPath, state);
            await UpdateAutopilotRecoveryUiAsync(state);
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot recovery supervisor failed: " + error);
        }
        finally
        {
            _autopilotRecoveryRunning = false;
        }
    }

    private static async Task RunRecoveryStepAsync(
        AutopilotRecoveryState state,
        string name,
        bool configured,
        Func<Task> action)
    {
        var subsystem = AutopilotRecoveryPolicy.GetOrCreate(state, name);
        if (!configured)
        {
            AutopilotRecoveryPolicy.RecordNotConfigured(subsystem);
            return;
        }

        var utcNow = DateTime.UtcNow;
        if (!AutopilotRecoveryPolicy.ShouldAttempt(subsystem, utcNow))
            return;

        try
        {
            await action();
            AutopilotRecoveryPolicy.RecordSuccess(subsystem, DateTime.UtcNow);
        }
        catch (Exception error)
        {
            AutopilotRecoveryPolicy.RecordFailure(subsystem, DateTime.UtcNow, error.Message);
        }
    }

    private async Task UpdateAutopilotRecoveryUiAsync(AutopilotRecoveryState state)
    {
        if (_autopilotHomeTabIndex >= 0)
            await RefreshAutopilotHomeAsync();

        var overall = AutopilotRecoveryPolicy.OverallStatus(state);
        if (overall == "Healthy") return;

        var affected = state.Subsystems
            .Where(item => item.State is "Recovering" or "Needs setup")
            .Select(item => item.Name)
            .ToList();
        var detail = affected.Count == 0 ? "connections" : string.Join(", ", affected);

        if (_autopilotHealthText is not null)
            _autopilotHealthText.Text = overall;
        if (_autopilotStatusText is not null)
        {
            _autopilotStatusText.Text = overall == "Recovering"
                ? $"Autopilot is retrying {detail} automatically. Other automation continues normally."
                : $"Autopilot needs setup attention for {detail}. Open Settings to reconnect or correct the account configuration.";
        }
        HeaderStatusText.Text = overall == "Recovering"
            ? $"Autopilot: recovering {detail} automatically"
            : $"Autopilot: setup needed for {detail}";
    }
}
