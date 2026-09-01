using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteYouTubeScheduleSyncInitialized;
    private bool _websiteYouTubeScheduleSyncRunning;
    private DispatcherTimer? _websiteYouTubeScheduleSyncTimer;

    public void InitializeWebsiteYouTubeScheduleSync()
    {
        if (_websiteYouTubeScheduleSyncInitialized) return;
        _websiteYouTubeScheduleSyncInitialized = true;

        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(18));
                await RunWebsiteYouTubeScheduleSyncAsync();
            }));

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) ||
                MainTabs.SelectedIndex != _websiteManagerTabIndex)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(async () => await ReconcileWebsiteYouTubeStateAsync(
                    alignSchedules: AutopilotSchedulePreferencesStore.Load(_data.SettingsPath).AutoFillEnabled)));
        };

        _websiteYouTubeScheduleSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _websiteYouTubeScheduleSyncTimer.Tick += async (_, _) => await RunWebsiteYouTubeScheduleSyncAsync();
        _websiteYouTubeScheduleSyncTimer.Start();
        Closed += (_, _) => _websiteYouTubeScheduleSyncTimer?.Stop();
    }

    private async Task RunWebsiteYouTubeScheduleSyncAsync()
    {
        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        if (!preferences.AutoFillEnabled) return;
        await ReconcileWebsiteYouTubeStateAsync(alignSchedules: true);
    }

    private async Task ReconcileWebsiteYouTubeStateAsync(bool alignSchedules)
    {
        if (_websiteYouTubeScheduleSyncRunning) return;
        _websiteYouTubeScheduleSyncRunning = true;
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured) return;

            var histories = WebsiteSyncHistories(_data.GetQuizHistory(2_000));
            if (histories.Count == 0) return;

            using var website = new FactburstWebsitePublishingClient();
            var site = await website.FetchQuizzesAsync(tracker.BaseUrl, tracker.ApiKey);
            ReconcileWebsitePublicationStateFromLiveSite(site, histories, DateTimeOffset.Now);
            if (!alignSchedules) return;

            var siteBySlug = site
                .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
                .GroupBy(item => item.Slug.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            Dictionary<int, string>? questionImagePaths = null;
            using var visibility = new FactburstWebsiteVisibilityClient();
            var now = DateTimeOffset.Now;
            var uploaded = 0;
            var realigned = 0;
            var protectedFromEarlyRelease = 0;

            foreach (var history in histories)
            {
                var slug = FactburstLinkTrackerClient.CampaignSlug(history);
                siteBySlug.TryGetValue(slug, out var remote);
                var plan = WebsiteYouTubeSchedulePlanner.Plan(history, now);

                if (plan is null)
                {
                    if (remote is not null &&
                        string.Equals(remote.Status, "published", StringComparison.OrdinalIgnoreCase) &&
                        WebsiteYouTubeSchedulePlanner.IsKnownNotPublic(history))
                    {
                        await visibility.SetOfflineAsync(
                            tracker.BaseUrl,
                            tracker.ApiKey,
                            slug,
                            remote.PublishAt);
                        _data.PublicationState.Reset(history.Id, PublicationPlatform.Website, PublicationContentKind.Quiz);
                        _data.PublicationState.RecordUploaded(
                            history.Id,
                            PublicationPlatform.Website,
                            PublicationContentKind.Quiz,
                            remoteId: slug,
                            remoteUrl: null,
                            visibility: "draft",
                            source: "website-youtube-sync");
                        protectedFromEarlyRelease++;
                    }
                    continue;
                }

                if (remote is null)
                {
                    try
                    {
                        questionImagePaths ??= _data.GetQuizQuestions(limit: 10_000)
                            .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
                            .GroupBy(question => question.Id)
                            .ToDictionary(group => group.Key, group => group.First().ImagePath);

                        var payload = FactburstWebsiteQuizBuilder.Build(history, plan.PublishAt, questionImagePaths);
                        await website.PublishQuizAsync(tracker.BaseUrl, tracker.ApiKey, payload);
                        RecordWebsiteYouTubePlan(history.Id, payload.Slug, plan, "website-youtube-sync");
                        uploaded++;
                    }
                    catch (Exception error) when (IsUnavailableProjectError(error))
                    {
                        _data.PublicationState.RecordFailure(
                            history.Id,
                            PublicationPlatform.Website,
                            PublicationContentKind.Quiz,
                            "prepare",
                            error.Message,
                            source: "website-youtube-sync");
                    }
                    catch (Exception error)
                    {
                        _data.PublicationState.RecordFailure(
                            history.Id,
                            PublicationPlatform.Website,
                            PublicationContentKind.Quiz,
                            "sync",
                            error.Message,
                            source: "website-youtube-sync");
                        Debug.WriteLine($"Website YouTube sync failed for {slug}: {error}");
                    }
                    continue;
                }

                // A draft is an explicit Website-page offline state. Keep it offline.
                // Published copies, however, always follow the long-form YouTube release time.
                if (string.Equals(remote.Status, "published", StringComparison.OrdinalIgnoreCase) &&
                    !WebsiteYouTubeSchedulePlanner.PublishTimesMatch(remote.PublishAt, plan.PublishAt))
                {
                    await visibility.FollowScheduleAsync(
                        tracker.BaseUrl,
                        tracker.ApiKey,
                        slug,
                        plan.PublishAt);
                    RecordWebsiteYouTubePlan(history.Id, slug, plan, "website-youtube-sync");
                    realigned++;
                }
            }

            if (uploaded > 0 || realigned > 0 || protectedFromEarlyRelease > 0)
            {
                Debug.WriteLine(
                    $"Website YouTube sync: {uploaded} uploaded, {realigned} realigned, " +
                    $"{protectedFromEarlyRelease} protected from early release.");
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine("Website YouTube schedule sync failed: " + error);
        }
        finally
        {
            _websiteYouTubeScheduleSyncRunning = false;
        }
    }

    private void ReconcileWebsitePublicationStateFromLiveSite(
        IReadOnlyList<FactburstWebsiteQuizSummary> site,
        IReadOnlyList<QuizHistorySummary> histories,
        DateTimeOffset now)
    {
        var state = _data.PublicationState;
        var siteBySlug = site
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
            .GroupBy(item => item.Slug.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var history in histories)
        {
            var slug = FactburstLinkTrackerClient.CampaignSlug(history);
            if (!siteBySlug.TryGetValue(slug, out var remote))
            {
                var existing = state.Get(history.Id, PublicationPlatform.Website, PublicationContentKind.Quiz);
                if (existing?.HasRemotePublication == true)
                    state.Reset(history.Id, PublicationPlatform.Website, PublicationContentKind.Quiz);
                continue;
            }

            state.Reset(history.Id, PublicationPlatform.Website, PublicationContentKind.Quiz);
            if (string.Equals(remote.Status, "draft", StringComparison.OrdinalIgnoreCase))
            {
                state.RecordUploaded(
                    history.Id,
                    PublicationPlatform.Website,
                    PublicationContentKind.Quiz,
                    remoteId: slug,
                    remoteUrl: null,
                    visibility: "draft",
                    source: "website-live-reconcile");
                continue;
            }

            if (ParseWebsiteSyncDate(remote.PublishAt) is { } publishAt && publishAt > now)
            {
                state.RecordScheduled(
                    history.Id,
                    PublicationPlatform.Website,
                    PublicationContentKind.Quiz,
                    publishAt,
                    remoteId: slug,
                    visibility: "published",
                    source: "website-live-reconcile");
            }
            else
            {
                state.RecordPublished(
                    history.Id,
                    PublicationPlatform.Website,
                    PublicationContentKind.Quiz,
                    remoteId: slug,
                    publishedAt: ParseWebsiteSyncDate(remote.PublishAt) ?? now,
                    visibility: "published",
                    source: "website-live-reconcile");
            }
        }
    }

    private void RecordWebsiteYouTubePlan(
        int historyId,
        string slug,
        WebsiteYouTubeReleasePlan plan,
        string source)
    {
        if (plan.IsScheduled)
        {
            _data.PublicationState.RecordScheduled(
                historyId,
                PublicationPlatform.Website,
                PublicationContentKind.Quiz,
                plan.PublishAt,
                remoteId: slug,
                visibility: "published",
                source: source);
        }
        else
        {
            _data.PublicationState.RecordPublished(
                historyId,
                PublicationPlatform.Website,
                PublicationContentKind.Quiz,
                remoteId: slug,
                publishedAt: plan.PublishAt,
                visibility: "published",
                source: source);
        }
    }

    private static List<QuizHistorySummary> WebsiteSyncHistories(IEnumerable<QuizHistorySummary> histories) =>
        histories
            .Where(history => !string.Equals(history.VideoType, "Short", StringComparison.OrdinalIgnoreCase))
            .GroupBy(FactburstLinkTrackerClient.CampaignSlug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(history => history.Id).First())
            .ToList();

    private static DateTimeOffset? ParseWebsiteSyncDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
