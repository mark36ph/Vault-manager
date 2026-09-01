using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string FullAutopilotRescueButtonTag = "full-autopilot-rescue-package";
    private readonly YouTubePostReleaseAuditService _fullAutopilotYouTubeAudit = new();
    private readonly YouTubeVideoUploadService _fullAutopilotYouTubeUpload = new();
    private readonly QuizWinnerPromoRenderer _fullAutopilotWinnerPromoRenderer = new();
    private DispatcherTimer? _fullAutopilotTimer;
    private bool _fullAutopilotInitialized;
    private bool _fullAutopilotRunning;
    private DataGrid? _fullAutopilotReplyDraftGrid;
    private string _fullAutopilotLastDraftText = "";
    private Button? _fullAutopilotRescueButton;

    public void InitializeFullAutopilot()
    {
        if (_fullAutopilotInitialized) return;
        _fullAutopilotInitialized = true;

        AddHandler(Button.ClickEvent, new RoutedEventHandler(FullAutopilotButton_Click), handledEventsToo: true);
        Loaded += (_, _) =>
        {
            EnsureFullAutopilotUiHooks();
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(12));
                    await RunFullAutopilotAsync();
                }));
        };
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureFullAutopilotUiHooks));

        _fullAutopilotTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _fullAutopilotTimer.Tick += async (_, _) => await RunFullAutopilotAsync();
        _fullAutopilotTimer.Start();
    }

    private void FullAutopilotButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (eventArgs.OriginalSource is not Button button) return;
        var content = Convert.ToString(button.Content) ?? "";

        if (string.Equals(content, "Comments", StringComparison.Ordinal))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(EnsureFullAutopilotUiHooks));
            return;
        }

        if (!string.Equals(content, "Generate + Autopilot", StringComparison.Ordinal) &&
            !string.Equals(content, "Generate + Autopilot...", StringComparison.Ordinal))
            return;

        // The original batch click handler yields before its first render, and the Growth
        // handler chooses its normal performance plan synchronously. This routed handler
        // runs after those button handlers and can therefore reserve the first slot for
        // a queued Winner follow-up without disturbing the rest of the Growth plan.
        if (!_quizBatchAutomationRunning && !_quizBatchRenderRunning)
            return;

        try
        {
            var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            var followUp = YouTubeWinnerFollowUpPlanner.ConsumeNext(state, DateTime.UtcNow);
            if (followUp is null) return;
            SelectQuizBatchCategory(followUp.Category);
            if (_quizTitleTextBox is not null)
                _quizTitleTextBox.Text = followUp.Category;
            FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Winner follow-up: reserved the next full-video slot for {followUp.Category}.";
        }
        catch (Exception error)
        {
            Debug.WriteLine("Winner follow-up slot could not be applied: " + error);
        }
    }

    private async Task RunFullAutopilotAsync()
    {
        if (_fullAutopilotRunning) return;
        _fullAutopilotRunning = true;
        var notes = new List<string>();
        var settingsPath = _data.SettingsPath;
        FactburstFullAutopilotState? state = null;
        try
        {
            // Routine Autopilot refreshes must never perform the legacy recursive project-path
            // recovery. Current startup consolidation owns migration, while the explicit Quiz
            // History "Update paths" action remains available for manual repair when needed.
            var growthStorePath = YouTubeGrowthStorePath();
            var local = await Task.Run(() =>
            {
                var loadedState = FactburstFullAutopilotStateStore.Load(settingsPath);
                var histories = _data.GetQuizHistory(2_000);
                var latestSnapshots = YouTubeGrowthSnapshotStore.Load(growthStorePath)
                    .GroupBy(snapshot => snapshot.VideoId, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                    .ToList();
                return (State: loadedState, Histories: histories, LatestSnapshots: latestSnapshots);
            });

            var activeState = local.State;
            state = activeState;
            var histories = local.Histories;
            RegisterNewFullAutopilotReleaseWatches(activeState, histories);

            var newWinners = YouTubeWinnerFollowUpPlanner.EnqueueNewWinners(
                activeState,
                local.LatestSnapshots,
                DateTime.UtcNow);
            RegisterWinnerPromoBundles(activeState);
            if (newWinners > 0)
                notes.Add($"{newWinners:N0} winner follow-up queued");

            await RunIsolatedAsync("Facebook first comment", notes,
                () => RunFacebookFirstCommentAutopilotAsync(activeState, histories));
            await RunIsolatedAsync("post-release audit", notes,
                () => RunYouTubePostReleaseAuditAsync(activeState, histories));
            await RunIsolatedAsync("comment triage", notes,
                () => RunYouTubeCommentTriageAsync(activeState));
            await RunIsolatedAsync("winner promos", notes,
                () => RunWinnerPromoAutopilotAsync(activeState, histories));

            await Task.Run(() => FactburstFullAutopilotStateStore.Save(settingsPath, activeState));
            EnsureFullAutopilotUiHooks();
            var summary = notes.Count == 0
                ? "Full Autopilot: monitoring releases, winners and comments"
                : "Full Autopilot: " + string.Join(" • ", notes.Take(5));
            SetScheduledReadinessStatus(summary);
        }
        catch (Exception error)
        {
            Debug.WriteLine("Full Autopilot supervisor failed: " + error);
        }
        finally
        {
            if (state is not null)
            {
                try { await Task.Run(() => FactburstFullAutopilotStateStore.Save(settingsPath, state)); }
                catch (Exception error) { Debug.WriteLine("Full Autopilot state save failed: " + error.Message); }
            }
            _fullAutopilotRunning = false;
        }
    }

    private static async Task RunIsolatedAsync(string name, List<string> notes, Func<Task<string?>> action)
    {
        try
        {
            var note = await action();
            if (!string.IsNullOrWhiteSpace(note)) notes.Add(note);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Full Autopilot {name} failed: {error}");
            notes.Add(name + " retry pending");
        }
    }

    private void RegisterNewFullAutopilotReleaseWatches(
        FactburstFullAutopilotState state,
        IReadOnlyList<QuizHistorySummary> histories)
    {
        var now = DateTimeOffset.Now;
        var facebook = state.FacebookFirstCommentWatchIds.ToHashSet();
        var youtube = state.YouTubePostReleaseWatchIds.ToHashSet();
        foreach (var history in histories)
        {
            if (FullAutopilotReleasePlanner.ShouldWatchFacebookFirstComment(history, state.ActivatedAtUtc, now))
                facebook.Add(history.Id);
            if (FullAutopilotReleasePlanner.ShouldWatchYouTubePostRelease(history, state.ActivatedAtUtc, now))
                youtube.Add(history.Id);
        }
        state.FacebookFirstCommentWatchIds = facebook.OrderBy(id => id).ToList();
        state.YouTubePostReleaseWatchIds = youtube.OrderBy(id => id).ToList();
    }

    private static void RegisterWinnerPromoBundles(FactburstFullAutopilotState state)
    {
        var existing = state.WinnerPromoBundles.Select(bundle => bundle.SourceVideoId).ToHashSet(StringComparer.Ordinal);
        foreach (var winner in state.WinnerFollowUps)
        {
            if (string.IsNullOrWhiteSpace(winner.VideoId) || existing.Contains(winner.VideoId)) continue;
            state.WinnerPromoBundles.Add(new YouTubeWinnerPromoBundle
            {
                HistoryId = winner.HistoryId,
                SourceVideoId = winner.VideoId,
                DetectedAtUtc = winner.DetectedAtUtc,
            });
            existing.Add(winner.VideoId);
        }
    }

    private async Task<string?> RunFacebookFirstCommentAutopilotAsync(
        FactburstFullAutopilotState state,
        IReadOnlyList<QuizHistorySummary> histories)
    {
        if (state.FacebookFirstCommentWatchIds.Count == 0) return null;
        var settings = _data.LoadSettings();
        var token = settings.FacebookPageAccessToken.Trim();
        if (token.Length == 0) return null;

        var identity = await _facebookAnalytics.GetPageIdentityAsync(token);
        SocialPublishingAccountGuard.EnsureMatches(
            "Facebook Page",
            settings.ApprovedFacebookPageId,
            identity.PageId);
        var pageVideos = await _facebookAnalytics.ListPageVideosAsync(token);
        var byId = pageVideos.Videos.ToDictionary(video => video.VideoId, StringComparer.Ordinal);
        var historyById = histories.ToDictionary(history => history.Id);
        var posted = 0;

        foreach (var historyId in state.FacebookFirstCommentWatchIds.ToArray())
        {
            if (!historyById.TryGetValue(historyId, out var history) ||
                !history.PublishedOnFacebook ||
                !string.IsNullOrWhiteSpace(history.FacebookFirstCommentId))
            {
                state.FacebookFirstCommentWatchIds.Remove(historyId);
                continue;
            }

            var reelId = FacebookReelAnalyticsService.TryGetReelId(history.FacebookUrl);
            if (string.IsNullOrWhiteSpace(reelId) || !byId.TryGetValue(reelId, out var liveVideo))
                continue;
            if (liveVideo.PublishedAt is { } publishedAt && publishedAt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(2))
                continue;

            var commentId = await _facebookComments.PostTopLevelCommentAsync(token, reelId, history.PinnedComment);
            _data.UpdateQuizHistoryFacebookFirstComment(history.Id, commentId);
            state.FacebookFirstCommentWatchIds.Remove(historyId);
            FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            posted++;
        }
        return posted == 0 ? null : $"{posted:N0} Facebook first comment{(posted == 1 ? "" : "s")} posted";
    }

    private async Task<string?> RunYouTubePostReleaseAuditAsync(
        FactburstFullAutopilotState state,
        IReadOnlyList<QuizHistorySummary> histories)
    {
        if (state.YouTubePostReleaseWatchIds.Count == 0) return null;
        var historyById = histories.ToDictionary(history => history.Id);
        var targets = state.YouTubePostReleaseWatchIds
            .Select(id => historyById.GetValueOrDefault(id))
            .Where(history => history is not null)
            .Select(history => history!)
            .Select(history => new
            {
                History = history,
                VideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl),
            })
            .Where(item => item.VideoId is not null)
            .ToList();
        if (targets.Count == 0) return null;

        var accessToken = await GetYouTubeManagementAccessTokenAsync();
        var settings = _data.LoadSettings();
        var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
        SocialPublishingAccountGuard.EnsureMatches(
            "YouTube channel",
            settings.ApprovedYouTubeChannelId,
            channel.Id);
        var states = await _fullAutopilotYouTubeAudit.FetchAsync(accessToken, targets.Select(item => item.VideoId!));

        var playlists = (await _youtubeManagement.ListPlaylistsAsync(accessToken)).ToList();
        var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        var trackerCampaigns = trackerSettings.IsConfigured
            ? (await _factburstLinkTracker.FetchStatsAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey))
                .Select(item => item.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var website = new FactburstWebsitePublishingClient();
        var websiteQuizzes = trackerSettings.IsConfigured
            ? (await website.FetchQuizzesAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey))
                .Select(item => item.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var questionImagePaths = _data.GetQuizQuestions(limit: 10_000)
            .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
            .GroupBy(question => question.Id)
            .ToDictionary(group => group.Key, group => group.First().ImagePath);

        var repaired = 0;
        var audited = 0;
        foreach (var item in targets)
        {
            if (!states.TryGetValue(item.VideoId!, out var remote) ||
                !string.Equals(remote.PrivacyStatus, "public", StringComparison.OrdinalIgnoreCase))
                continue;

            var history = item.History;
            var record = new YouTubePostReleaseAuditRecord
            {
                HistoryId = history.Id,
                VideoId = item.VideoId!,
                CheckedAtUtc = DateTime.UtcNow,
                IsPublic = true,
                TitleMatches = string.Equals(remote.Title, history.UploadTitleDisplay, StringComparison.Ordinal),
                ThumbnailPresent = remote.ThumbnailPresent,
                FirstCommentReady = !string.IsNullOrWhiteSpace(history.YouTubeFirstCommentId),
            };
            var attention = new List<string>();
            if (!record.TitleMatches) attention.Add("YouTube title differs from the saved title");
            if (!record.ThumbnailPresent) attention.Add("YouTube thumbnail could not be verified");
            if (!record.FirstCommentReady)
                attention.Add(string.IsNullOrWhiteSpace(history.PinnedComment)
                    ? "first comment text is missing"
                    : "first comment is waiting for First Comment Autopilot");

            try
            {
                var category = QuizGrowthPlaylistPlanner.Category(history);
                var playlist = QuizGrowthPlaylistPlanner.FindExisting(category, playlists);
                if (playlist is null)
                {
                    playlist = await _youtubeManagement.CreatePlaylistAsync(
                        accessToken,
                        YouTubeCategoryPlaylistPlanner.PlaylistTitle(category),
                        QuizGrowthPlaylistPlanner.Description(category),
                        "public");
                    playlists.Add(playlist);
                    record.Repairs++;
                }
                var playlistVideos = await _youtubeManagement.ListPlaylistVideosAsync(accessToken, playlist.Id);
                if (!playlistVideos.Any(video => string.Equals(video.VideoId, item.VideoId, StringComparison.Ordinal)))
                {
                    await _youtubeManagement.AddVideoToPlaylistAsync(accessToken, playlist.Id, item.VideoId!);
                    record.Repairs++;
                }
                record.PlaylistReady = true;
            }
            catch (Exception error)
            {
                attention.Add("playlist: " + error.Message);
            }

            var slug = FactburstLinkTrackerClient.CampaignSlug(history);
            if (trackerSettings.IsConfigured)
            {
                try
                {
                    if (!trackerCampaigns.Contains(slug))
                    {
                        await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                            trackerSettings.BaseUrl,
                            trackerSettings.ApiKey,
                            slug,
                            history.Id,
                            history.UploadTitleDisplay,
                            history.YouTubeUrl);
                        trackerCampaigns.Add(slug);
                        record.Repairs++;
                    }
                    record.TrackerReady = true;
                }
                catch (Exception error)
                {
                    attention.Add("tracking: " + error.Message);
                }

                try
                {
                    if (!websiteQuizzes.Contains(slug))
                    {
                        var publishAt = ParseReleaseTime(history.YouTubeScheduledFor) ?? DateTimeOffset.Now.AddMinutes(-1);
                        var payload = FactburstWebsiteQuizBuilder.Build(history, publishAt, questionImagePaths);
                        await website.PublishQuizAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey, payload);
                        websiteQuizzes.Add(slug);
                        record.Repairs++;
                    }
                    record.WebsiteReady = true;
                }
                catch (Exception error)
                {
                    attention.Add("website: " + error.Message);
                }
            }
            else
            {
                attention.Add("Link Tracker is not configured");
            }

            record.Attention = string.Join("; ", attention);
            state.PostReleaseAudits.RemoveAll(value => value.HistoryId == history.Id);
            state.PostReleaseAudits.Add(record);
            state.PostReleaseAudits = state.PostReleaseAudits
                .OrderByDescending(value => value.CheckedAtUtc)
                .Take(100)
                .ToList();
            repaired += record.Repairs;
            audited++;
            if (record.AutomationComplete)
                state.YouTubePostReleaseWatchIds.Remove(history.Id);
            FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
        }

        return audited == 0 ? null : $"{audited:N0} release audit{(audited == 1 ? "" : "s")} • {repaired:N0} repaired";
    }

    private async Task<string?> RunYouTubeCommentTriageAsync(FactburstFullAutopilotState state)
    {
        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthRefreshToken.Length == 0 || settings.YouTubeOAuthClientId.Length == 0)
            return null;
        var accessToken = await GetYouTubeManagementAccessTokenAsync();
        var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
        SocialPublishingAccountGuard.EnsureMatches(
            "YouTube channel",
            settings.ApprovedYouTubeChannelId,
            channel.Id);
        var comments = await _youtubeManagement.ListCommentsAsync(accessToken, channel.Id, "published");
        var needsReply = YouTubeCommentInbox.Filter(comments, needsReply: true, _youtubeHandledCommentIds);
        var existing = state.ReplyDrafts.Select(draft => draft.CommentId).ToHashSet(StringComparer.Ordinal);
        var drafted = 0;
        foreach (var comment in needsReply)
        {
            if (existing.Contains(comment.Id)) continue;
            state.ReplyDrafts.Add(new YouTubeReplyDraft
            {
                CommentId = comment.Id,
                VideoId = comment.VideoId,
                Author = comment.Author,
                CommentText = comment.Text,
                Draft = YouTubeReplyDraftPlanner.Draft(comment.Text),
                CreatedAtUtc = DateTime.UtcNow,
            });
            existing.Add(comment.Id);
            drafted++;
        }
        state.ReplyDrafts = state.ReplyDrafts
            .OrderByDescending(draft => draft.CreatedAtUtc)
            .Take(500)
            .ToList();
        if (_youtubeNeedsReplyCountText is not null)
            _youtubeNeedsReplyCountText.Text = needsReply.Count.ToString("N0");
        if (_youtubeCommentsStatus is not null && needsReply.Count > 0)
            _youtubeCommentsStatus.Text = $"Autopilot found {needsReply.Count:N0} comment(s) needing a reply. Select one to review its draft.";
        EnsureReplyDraftUiHook();
        return drafted == 0 ? null : $"{drafted:N0} reply draft{(drafted == 1 ? "" : "s")} prepared";
    }

    private async Task<string?> RunWinnerPromoAutopilotAsync(
        FactburstFullAutopilotState state,
        IReadOnlyList<QuizHistorySummary> histories)
    {
        var bundle = state.WinnerPromoBundles
            .Where(value => !value.Completed)
            .Where(value => value.NextAttemptAtUtc is null || value.NextAttemptAtUtc <= DateTime.UtcNow)
            .OrderBy(value => value.DetectedAtUtc)
            .FirstOrDefault();
        if (bundle is null) return null;
        var history = histories.FirstOrDefault(value => value.Id == bundle.HistoryId);
        if (history is null || !Directory.Exists(history.ProjectFolder)) return null;

        try
        {
            if (bundle.Variants.Count == 0)
            {
                var sourceVideo = SocialVideoUploadRules.FindLikelyRenderedVideo(history.ProjectFolder)
                    ?? throw new FileNotFoundException("The Winner quiz rendered video could not be found.");
                var settings = _data.LoadSettings();
                var apiKey = NativeProviderCredentials.FromSettings(settings).Get("openai");
                var logoPath = _data.LoadQuizLogoPath();
                var rendered = await _fullAutopilotWinnerPromoRenderer.CreateAsync(
                    sourceVideo,
                    history.ProjectFolder,
                    history.UploadTitleDisplay,
                    history.YouTubeUrl,
                    apiKey,
                    logoPath,
                    message => SetScheduledReadinessStatus(message));
                var schedules = YouTubeWinnerPromoSchedulePlanner.Create(DateTimeOffset.Now, rendered.Count);
                for (var index = 0; index < rendered.Count; index++)
                {
                    bundle.Variants.Add(new YouTubeWinnerPromoVariant
                    {
                        Number = rendered[index].Number,
                        QuestionId = rendered[index].QuestionId,
                        SceneTitle = rendered[index].SceneTitle,
                        VideoPath = rendered[index].VideoPath,
                        PublishAt = schedules[index],
                    });
                }
                FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            }

            if (bundle.Variants.Count == 0)
            {
                bundle.Completed = true;
                return null;
            }

            var settingsForUpload = _data.LoadSettings();
            var accessToken = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel",
                settingsForUpload.ApprovedYouTubeChannelId,
                channel.Id);

            var description = QuizPromoShortUploadMetadata.Description(
                history.UploadTitleDisplay,
                history.YouTubeUrl,
                history.Hashtags);
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (tracker.IsConfigured)
            {
                var links = await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                    tracker.BaseUrl,
                    tracker.ApiKey,
                    FactburstLinkTrackerClient.CampaignSlug(history),
                    history.Id,
                    history.UploadTitleDisplay,
                    history.YouTubeUrl);
                description = FactburstLinkTrackerClient.ReplaceFullQuizLink(description, links.YouTubePromoUrl);
            }

            var uploaded = 0;
            foreach (var variant in bundle.Variants.Where(value => string.IsNullOrWhiteSpace(value.YouTubeVideoId)))
            {
                if (variant.PublishAt < DateTimeOffset.Now.AddMinutes(10))
                {
                    var replacement = YouTubeWinnerPromoSchedulePlanner.Create(DateTimeOffset.Now, 1)[0];
                    variant.PublishAt = replacement;
                }
                var result = await _fullAutopilotYouTubeUpload.UploadAsync(
                    accessToken,
                    variant.VideoPath,
                    new YouTubeVideoUpload(
                        WinnerPromoTitle(history, variant.Number),
                        description,
                        "private",
                        NotifySubscribers: false,
                        PublishAt: variant.PublishAt));
                variant.YouTubeVideoId = result.VideoId;
                variant.YouTubeUrl = result.Url;
                uploaded++;
                FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            }

            bundle.Completed = bundle.Variants.Count > 0 && bundle.Variants.All(value => !string.IsNullOrWhiteSpace(value.YouTubeVideoId));
            bundle.LastError = "";
            bundle.NextAttemptAtUtc = null;
            FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            return uploaded == 0 ? null : $"{uploaded:N0} Winner promo{(uploaded == 1 ? "" : "s")} scheduled";
        }
        catch (Exception error)
        {
            bundle.Attempts++;
            bundle.LastError = error.Message;
            bundle.NextAttemptAtUtc = DateTime.UtcNow.AddHours(Math.Min(6, Math.Max(1, bundle.Attempts)));
            FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
            throw;
        }
    }

    private static string WinnerPromoTitle(QuizHistorySummary history, int number)
    {
        var category = history.AnalyticsCategory.Trim();
        if (category.Length == 0) category = "Factburst";
        var suffix = $" Quiz Challenge {number} #Shorts";
        var maximum = Math.Max(1, 100 - suffix.Length);
        if (category.Length > maximum) category = category[..maximum].TrimEnd();
        return category + suffix;
    }

    private static DateTimeOffset? ParseReleaseTime(string? value) =>
        DateTimeOffset.TryParse(
            (value ?? "").Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    private void EnsureFullAutopilotUiHooks()
    {
        EnsureReplyDraftUiHook();
        EnsureRescuePackageButton();
    }

    private void EnsureReplyDraftUiHook()
    {
        if (_youtubeCommentsGrid is null || ReferenceEquals(_fullAutopilotReplyDraftGrid, _youtubeCommentsGrid))
            return;
        _fullAutopilotReplyDraftGrid = _youtubeCommentsGrid;
        _youtubeCommentsGrid.SelectionChanged += (_, _) => FillSelectedYouTubeReplyDraft();
        FillSelectedYouTubeReplyDraft();
    }

    private void FillSelectedYouTubeReplyDraft()
    {
        if (_youtubeReplyText is null || _youtubeCommentsGrid?.SelectedItem is not YouTubeCommentItem comment)
            return;
        var current = _youtubeReplyText.Text.Trim();
        if (current.Length > 0 && !string.Equals(current, _fullAutopilotLastDraftText, StringComparison.Ordinal))
            return;
        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        var draft = state.ReplyDrafts.FirstOrDefault(value => string.Equals(value.CommentId, comment.Id, StringComparison.Ordinal));
        if (draft is null) return;
        _youtubeReplyText.Text = draft.Draft;
        _fullAutopilotLastDraftText = draft.Draft;
        _youtubeReplyText.CaretIndex = _youtubeReplyText.Text.Length;
    }

    private void EnsureRescuePackageButton()
    {
        if (Content is not DependencyObject root || _youtubeAnalyticsGrid is null) return;
        var open = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "Open selected video", StringComparison.Ordinal));
        if (open?.Parent is not Grid footer) return;
        var existing = footer.Children.OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), FullAutopilotRescueButtonTag, StringComparison.Ordinal));
        if (existing is not null)
        {
            _fullAutopilotRescueButton = existing;
            UpdateRescuePackageButton();
            return;
        }

        if (footer.ColumnDefinitions.Count < 3)
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(open, 2);
        var rescue = new Button
        {
            Content = "Open rescue package",
            Tag = FullAutopilotRescueButtonTag,
            MinWidth = 146,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Open the automatically prepared A/B/C title and thumbnail package for the selected Packaging rescue video.",
        };
        StyleQuizHistoryButton(rescue, Color.FromRgb(70, 235, 115));
        rescue.Click += (_, _) => OpenSelectedRescuePackage();
        Grid.SetColumn(rescue, 1);
        footer.Children.Add(rescue);
        _fullAutopilotRescueButton = rescue;
        _youtubeAnalyticsGrid.SelectionChanged += (_, _) => UpdateRescuePackageButton();
        UpdateRescuePackageButton();
    }

    private void UpdateRescuePackageButton()
    {
        if (_fullAutopilotRescueButton is null) return;
        _fullAutopilotRescueButton.IsEnabled = SelectedRescueHistory() is not null;
    }

    private QuizHistorySummary? SelectedRescueHistory()
    {
        if (_youtubeAnalyticsGrid?.SelectedItem is not YouTubeAnalyticsRow row) return null;
        var snapshot = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
            .Where(value => value.HistoryId == row.HistoryId)
            .OrderByDescending(value => value.CheckedAtUtc)
            .FirstOrDefault();
        if (snapshot is null ||
            !string.Equals(snapshot.Label, "Packaging rescue", StringComparison.OrdinalIgnoreCase) ||
            !snapshot.RescuePackagePrepared)
            return null;
        var history = _data.GetQuizHistory(2_000).FirstOrDefault(value => value.Id == row.HistoryId);
        return history is not null && QuizYouTubePackaging.Exists(history.ProjectFolder) ? history : null;
    }

    private void OpenSelectedRescuePackage()
    {
        var history = SelectedRescueHistory();
        if (history is null) return;
        var folder = Path.GetFullPath(history.ProjectFolder);
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
