using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private DispatcherTimer? _instagramPromoAutopilotTimer;
    private DispatcherTimer? _instagramPromoNeedsSyncTimer;
    private bool _instagramPromoFollowupInitialized;
    private bool _instagramPromoAutopilotRunning;
    private static bool _instagramPromoGuidedHandlerRegistered;

    public void InitializeInstagramPromoFollowup()
    {
        if (_instagramPromoFollowupInitialized)
            return;

        _instagramPromoFollowupInitialized = true;

        // Build 126 extends the same home counter with post-release Instagram work. Stop the
        // older counter timer so two one-second writers cannot fight over the Needs You value.
        _autopilotNeedsYouCountSyncTimer?.Stop();

        if (!_instagramPromoGuidedHandlerRegistered)
        {
            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.ClickEvent,
                new RoutedEventHandler(InstagramPromoGuidedButton_Click),
                handledEventsToo: true);
            _instagramPromoGuidedHandlerRegistered = true;
        }

        _instagramPromoNeedsSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _instagramPromoNeedsSyncTimer.Tick += (_, _) => SyncAutopilotNeedsYouWithInstagram();
        _instagramPromoNeedsSyncTimer.Start();

        _instagramPromoAutopilotTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _instagramPromoAutopilotTimer.Tick += async (_, _) => await RunInstagramPromoFollowupAsync();
        _instagramPromoAutopilotTimer.Start();

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                await RunInstagramPromoFollowupAsync();
                SyncAutopilotNeedsYouWithInstagram();
            }));

        Closed += (_, _) =>
        {
            _instagramPromoNeedsSyncTimer?.Stop();
            _instagramPromoAutopilotTimer?.Stop();
        };
    }

    private static void InstagramPromoGuidedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Start next task", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        var need = window.BuildInstagramPromoFollowupNeeds().FirstOrDefault();
        if (need is null)
            return;

        // A class handler runs before the button's normal Click handler. Marking the event
        // handled keeps the legacy guided queue from opening an empty task list for this
        // post-release Instagram item.
        e.Handled = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await window.PublishNextInstagramPromoManuallyAsync()));
    }

    private IReadOnlyList<InstagramPromoFollowupNeed> BuildInstagramPromoFollowupNeeds(
        FactburstFullAutopilotState? suppliedState = null)
    {
        var now = DateTimeOffset.Now;
        var state = suppliedState ?? FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        IReadOnlyList<PublicationStateEntry> publications;
        try
        {
            publications = _data.PublicationState.List();
        }
        catch (Exception error)
        {
            Debug.WriteLine("Instagram follow-up publication state: " + error.Message);
            publications = [];
        }

        var publicationByHistory = publications
            .GroupBy(entry => entry.HistoryId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        var needs = new List<InstagramPromoFollowupNeed>();

        foreach (var history in _data.GetQuizHistory(2_000))
        {
            if (!InstagramPromoFollowupPlanner.IsWithinWindow(
                    history,
                    now,
                    InstagramPromoFollowupPlanner.NeedsYouWindow))
            {
                continue;
            }

            var historyPublications = publicationByHistory.TryGetValue(history.Id, out var entries)
                ? entries
                : [];
            if (!InstagramPromoFollowupPlanner.IsVerifiedYouTubePublic(history.Id, state, historyPublications))
                continue;
            if (QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null)
                continue;

            var instagramPublication = historyPublications.FirstOrDefault(entry =>
                string.Equals(entry.ContentKind, PublicationContentKind.Promo, StringComparison.Ordinal) &&
                string.Equals(entry.Platform, PublicationPlatform.Instagram, StringComparison.OrdinalIgnoreCase));
            var projectReady = history.ProjectFolder.Trim().Length > 0 && Directory.Exists(history.ProjectFolder);
            string? promoPath = null;
            if (projectReady)
            {
                try { promoPath = QuizPromoShortPaths.FindExisting(history.ProjectFolder); }
                catch (Exception error) { Debug.WriteLine("Instagram promo lookup: " + error.Message); }
            }

            var detail = instagramPublication?.HasIssue == true
                ? "Instagram auto-post needs attention: " + instagramPublication.LastError
                : !projectReady
                    ? "The full quiz is public on YouTube, but its project folder is missing."
                    : promoPath is null
                        ? "The full quiz is public on YouTube, but the prepared Instagram promo file is missing."
                        : preferences.AutoFillEnabled
                            ? "The full quiz is public on YouTube and its Instagram promo is still missing. Autopilot will retry automatically, or you can publish it now."
                            : "The full quiz is public on YouTube and its Instagram promo is ready. Autopilot is off, so publish it now.";

            var publishedAt = InstagramPromoFollowupPlanner.ReleaseAt(history) ?? now;
            needs.Add(new InstagramPromoFollowupNeed(
                history.Id,
                history.UploadTitleDisplay,
                history.ProjectFolder,
                publishedAt,
                detail,
                promoPath is not null,
                instagramPublication?.HasIssue == true));
        }

        return needs
            .OrderBy(need => need.YouTubePublishedAt)
            .ThenBy(need => need.Quiz, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SyncAutopilotNeedsYouWithInstagram()
    {
        if (_autopilotHomeRefreshing || _autopilotHomeTabIndex < 0)
            return;

        try
        {
            var now = DateTimeOffset.Now;
            var scheduleRows = _scheduledReadinessRows
                .Where(row => row.PublishAt >= now.AddHours(-2))
                .ToList();
            var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
                .GroupBy(snapshot => snapshot.HistoryId)
                .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                .ToList();

            var alignedTasks = AutopilotNeedsYouAlignedPlanner.Build(scheduleRows, state, snapshots);
            var grouped = AutopilotNeedsYouCountSummary.FromAlignedTasks(alignedTasks);
            var alreadyCountedInstagram = alignedTasks
                .Where(task => task.ActionReady && task.Kind == AutopilotAlignedTaskKind.InstagramPromo)
                .Select(task => task.HistoryId)
                .ToHashSet();
            var instagramNeeds = BuildInstagramPromoFollowupNeeds(state)
                .Where(need => !alreadyCountedInstagram.Contains(need.HistoryId))
                .ToList();
            var total = grouped.Total + instagramNeeds.Count;
            var health = AutopilotNeedsYouCountSummary.Health(_fullAutopilotRunning, total);

            SetAutopilotTextIfChanged(_autopilotNeedsText, total == 0 ? "Nothing" : total.ToString("N0"));
            SetAutopilotTextIfChanged(
                _autopilotNeedsNoteText,
                total == 0
                    ? "Autopilot is handling the queue"
                    : instagramNeeds.Count > 0
                        ? $"{instagramNeeds.Count:N0} Instagram promo{(instagramNeeds.Count == 1 ? "" : "s")} still need posting"
                        : "Ready now — Factburst will guide you one task at a time");
            SetAutopilotTextIfChanged(_autopilotHealthText, health);

            ApplyAutopilotHomeCleanup();

            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
            {
                SetAutopilotTextIfChanged(
                    HeaderStatusText,
                    $"Autopilot: {health} • {scheduleRows.Count:N0} scheduled • {total:N0} need you");
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine("Instagram Needs You sync failed: " + error);
        }
    }

    private async Task RunInstagramPromoFollowupAsync()
    {
        if (_instagramPromoAutopilotRunning)
            return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        if (!preferences.AutoFillEnabled)
            return;

        _instagramPromoAutopilotRunning = true;
        try
        {
            var now = DateTimeOffset.Now;
            var publicationState = _data.PublicationState;
            IReadOnlyList<PublicationStateEntry> publications;
            try { publications = publicationState.List(); }
            catch { publications = []; }

            var recentCandidates = _data.GetQuizHistory(2_000)
                .Where(history => InstagramPromoFollowupPlanner.IsWithinWindow(
                    history,
                    now,
                    InstagramPromoFollowupPlanner.AutomaticWindow))
                .Where(history => QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is null)
                .Select(history => new
                {
                    History = history,
                    VideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl),
                    Publication = publications.FirstOrDefault(entry =>
                        entry.HistoryId == history.Id &&
                        string.Equals(entry.ContentKind, PublicationContentKind.Promo, StringComparison.Ordinal) &&
                        string.Equals(entry.Platform, PublicationPlatform.Instagram, StringComparison.OrdinalIgnoreCase)),
                })
                .Where(item => item.VideoId is not null)
                .Where(item => InstagramPromoFollowupPlanner.RetryAllowed(item.Publication, now))
                .OrderBy(item => InstagramPromoFollowupPlanner.ReleaseAt(item.History))
                .ToList();
            if (recentCandidates.Count == 0)
                return;

            var settings = _data.LoadSettings();
            if (settings.ApprovedYouTubeChannelId.Trim().Length == 0)
                return;

            var youtubeToken = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(youtubeToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel",
                settings.ApprovedYouTubeChannelId,
                channel.Id);
            var remoteStates = await _fullAutopilotYouTubeAudit.FetchAsync(
                youtubeToken,
                recentCandidates.Select(item => item.VideoId!));

            var publicCandidate = recentCandidates.FirstOrDefault(item =>
                remoteStates.TryGetValue(item.VideoId!, out var remote) &&
                string.Equals(remote.PrivacyStatus, "public", StringComparison.OrdinalIgnoreCase));
            if (publicCandidate is null)
                return;

            publicationState.RecordPublished(
                publicCandidate.History.Id,
                PublicationPlatform.YouTube,
                PublicationContentKind.Quiz,
                publicCandidate.VideoId,
                publicCandidate.History.YouTubeUrl,
                publishedAt: InstagramPromoFollowupPlanner.ReleaseAt(publicCandidate.History) ?? now,
                visibility: "public",
                source: "autopilot-youtube-public");

            await PublishInstagramPromoAutomaticallyAsync(publicCandidate.History);
        }
        catch (Exception error)
        {
            Debug.WriteLine("Instagram promo Autopilot failed: " + error);
        }
        finally
        {
            _instagramPromoAutopilotRunning = false;
            SyncAutopilotNeedsYouWithInstagram();
            RefreshLibraryPublicationStatusSnapshot();
        }
    }

    private async Task PublishInstagramPromoAutomaticallyAsync(QuizHistorySummary history)
    {
        var publicationState = _data.PublicationState;
        if (QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null)
            return;

        publicationState.BeginAttempt(
            history.Id,
            PublicationPlatform.Instagram,
            PublicationContentKind.Promo);

        try
        {
            var settings = _data.LoadSettings();
            if (settings.ApprovedFacebookPageId.Trim().Length == 0)
                throw new InvalidOperationException(
                    "Approve the Facebook Page / linked Instagram destination once before Autopilot can publish Instagram promos automatically.");

            var pageToken = FacebookPageToken();
            var identity = await _facebookAnalytics.GetPageIdentityAsync(pageToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "Facebook Page",
                settings.ApprovedFacebookPageId,
                identity.PageId);

            await PublishInstagramPromoCoreAsync(history, pageToken, "autopilot-instagram-promo");
            SetScheduledReadinessStatus("Autopilot published the Instagram promo for " + history.UploadTitleDisplay + ".");
        }
        catch (Exception error)
        {
            publicationState.RecordFailure(
                history.Id,
                PublicationPlatform.Instagram,
                PublicationContentKind.Promo,
                SocialUploadJournalStep.Upload,
                error.Message,
                source: "autopilot-instagram-promo");
            Debug.WriteLine($"Instagram promo #{history.Id} needs attention: {error}");
        }
    }

    private async Task PublishNextInstagramPromoManuallyAsync()
    {
        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        var need = BuildInstagramPromoFollowupNeeds(state).FirstOrDefault();
        if (need is null)
        {
            SyncAutopilotNeedsYouWithInstagram();
            return;
        }

        var history = _data.GetQuizHistory(2_000).FirstOrDefault(item => item.Id == need.HistoryId);
        if (history is null)
        {
            MessageBox.Show(this, "The quiz history record is missing.", "Instagram promo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!need.PromoFileReady)
        {
            MessageBox.Show(this, need.Detail, "Instagram promo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder)
                        ?? throw new FileNotFoundException("The prepared Instagram promo video could not be found.");
            video = SocialVideoUploadRules.ValidateVideoFile(video);
            var title = QuizPromoShortUploadMetadata.Title(history.UploadTitleDisplay);
            var preflight = await ConfirmSocialPublishingPreflightAsync(
                this,
                SocialUploadDestination.Instagram,
                video,
                title,
                "private",
                scheduledFor: null);
            if (preflight is null)
                return;

            await PublishInstagramPromoCoreAsync(history, preflight.FacebookPageToken, "needs-you-instagram-promo");
            MessageBox.Show(
                this,
                "Instagram promo published successfully. It has been removed from Needs You.",
                "Instagram promo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            if (_instagramAnalyticsGrid is not null)
                await RefreshInstagramManagerAsync(false);
        }
        catch (Exception error)
        {
            _data.PublicationState.RecordFailure(
                history.Id,
                PublicationPlatform.Instagram,
                PublicationContentKind.Promo,
                SocialUploadJournalStep.Upload,
                error.Message,
                source: "needs-you-instagram-promo");
            MessageBox.Show(this, error.Message, "Instagram promo",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SyncAutopilotNeedsYouWithInstagram();
            RefreshLibraryPublicationStatusSnapshot();
        }
    }

    private async Task PublishInstagramPromoCoreAsync(
        QuizHistorySummary history,
        string pageToken,
        string source)
    {
        if (QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null)
            return;

        var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder)
                    ?? throw new FileNotFoundException("The prepared Instagram promo video could not be found.");
        video = SocialVideoUploadRules.ValidateVideoFile(video);
        var duration = await new NativeFfmpegTimelineService().MediaDurationAsync(video);
        SocialVideoUploadRules.ValidateInstagramDuration(duration);

        var description = QuizPromoShortUploadMetadata.Description(
            history.UploadTitleDisplay,
            history.YouTubeUrl,
            history.Hashtags);
        var caption = SocialVideoUploadRules.InstagramCaption(description);
        var result = await _instagramReelUpload.UploadReelAsync(pageToken, video, caption);

        QuizPromoShortSocialPublicationStore.RecordInstagram(
            history.ProjectFolder,
            result,
            DateTimeOffset.Now);
        _data.PublicationState.RecordPublished(
            history.Id,
            PublicationPlatform.Instagram,
            PublicationContentKind.Promo,
            result.MediaId,
            result.Url,
            publishedAt: DateTimeOffset.Now,
            source: source);
    }
}
