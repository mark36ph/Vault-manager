using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record YouTubeFirstCommentAutopilotState(
    DateTimeOffset ActivatedAtUtc,
    IReadOnlyList<int> WatchedHistoryIds);

public static class YouTubeFirstCommentAutopilotPlanner
{
    public static readonly TimeSpan InitialGraceWindow = TimeSpan.FromHours(2);

    public static bool ShouldWatch(
        QuizHistorySummary history,
        DateTimeOffset activatedAtUtc,
        ISet<int> alreadyWatched)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(alreadyWatched);

        if (alreadyWatched.Contains(history.Id))
            return IsBaseEligible(history);
        if (!IsBaseEligible(history))
            return false;
        if (!DateTimeOffset.TryParse(
                history.YouTubeScheduledFor,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var scheduledFor))
        {
            return false;
        }

        return scheduledFor >= activatedAtUtc.Subtract(InitialGraceWindow);
    }

    public static bool IsBaseEligible(QuizHistorySummary history) =>
        history.PublishedOnYouTube &&
        string.Equals(history.VideoType, "Video", StringComparison.Ordinal) &&
        history.PinnedComment.Trim().Length > 0 &&
        history.YouTubeFirstCommentId.Trim().Length == 0 &&
        YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl) is not null;
}

public static class YouTubeFirstCommentAutopilotStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static YouTubeFirstCommentAutopilotState LoadOrCreate(string path, DateTimeOffset nowUtc)
    {
        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<YouTubeFirstCommentAutopilotState>(File.ReadAllText(path));
                if (saved is not null && saved.ActivatedAtUtc != default)
                    return saved with { WatchedHistoryIds = saved.WatchedHistoryIds ?? [] };
            }
        }
        catch
        {
            // A corrupt watchdog state must never cause a retroactive comment backfill.
        }

        var created = new YouTubeFirstCommentAutopilotState(nowUtc, []);
        Save(path, created);
        return created;
    }

    public static void Save(string path, YouTubeFirstCommentAutopilotState state)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var normalized = state with
        {
            WatchedHistoryIds = state.WatchedHistoryIds.Distinct().OrderBy(id => id).ToArray(),
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }
}

public partial class MainShellWindow
{
    private bool _youtubeFirstCommentAutopilotInitialized;
    private bool _youtubeFirstCommentAutopilotRunning;
    private DispatcherTimer? _youtubeFirstCommentAutopilotTimer;

    private void InitializeYouTubeFirstCommentAutopilot()
    {
        if (_youtubeFirstCommentAutopilotInitialized)
            return;

        _youtubeFirstCommentAutopilotInitialized = true;
        _youtubeFirstCommentAutopilotTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _youtubeFirstCommentAutopilotTimer.Tick += async (_, _) =>
            await RunYouTubeFirstCommentAutopilotAsync();
        _youtubeFirstCommentAutopilotTimer.Start();
        Closed += (_, _) => _youtubeFirstCommentAutopilotTimer?.Stop();

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await RunYouTubeFirstCommentAutopilotAsync();
            }));
    }

    private async Task RunYouTubeFirstCommentAutopilotAsync()
    {
        if (_youtubeFirstCommentAutopilotRunning)
            return;

        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthRefreshToken.Length == 0 || settings.YouTubeOAuthClientId.Length == 0)
            return;

        _youtubeFirstCommentAutopilotRunning = true;
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var statePath = YouTubeFirstCommentAutopilotStatePath();
            var state = YouTubeFirstCommentAutopilotStateStore.LoadOrCreate(statePath, nowUtc);
            var watched = state.WatchedHistoryIds.ToHashSet();
            var histories = _data.GetQuizHistory(2_000);

            foreach (var history in histories)
            {
                if (YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, state.ActivatedAtUtc, watched))
                    watched.Add(history.Id);
            }

            watched.RemoveWhere(historyId =>
            {
                var history = histories.FirstOrDefault(item => item.Id == historyId);
                return history is null || !YouTubeFirstCommentAutopilotPlanner.IsBaseEligible(history);
            });

            state = state with { WatchedHistoryIds = watched.OrderBy(id => id).ToArray() };
            YouTubeFirstCommentAutopilotStateStore.Save(statePath, state);

            var candidates = histories
                .Where(history => watched.Contains(history.Id))
                .Select(history => new
                {
                    History = history,
                    VideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl),
                })
                .Where(item => item.VideoId is not null)
                .ToList();
            if (candidates.Count == 0)
                return;

            var accessToken = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel",
                settings.ApprovedYouTubeChannelId,
                channel.Id);

            var publicationStates = await _youtubePublicationStatus.FetchAsync(
                accessToken,
                candidates.Select(item => item.VideoId!));

            var completed = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                if (!publicationStates.TryGetValue(candidate.VideoId!, out var publication) ||
                    !string.Equals(publication.PrivacyStatus, "public", StringComparison.OrdinalIgnoreCase) ||
                    publication.PublishAt is { } publishAt && publishAt > nowUtc)
                {
                    continue;
                }

                try
                {
                    var commentId = await _youtubeManagement.PostTopLevelCommentAsync(
                        accessToken,
                        candidate.VideoId!,
                        candidate.History.PinnedComment);
                    if (commentId.Length == 0)
                        continue;

                    _data.UpdateQuizHistoryYouTubeFirstComment(candidate.History.Id, commentId);
                    completed.Add(candidate.History.Id);
                    Debug.WriteLine(
                        $"First Comment Autopilot: recorded YouTube comment {commentId} for quiz history {candidate.History.Id}.");
                }
                catch (Exception error)
                {
                    // One video with comments disabled or a transient YouTube error must not
                    // stop comments from being posted on the rest of the release queue.
                    Debug.WriteLine(
                        $"First Comment Autopilot: quiz history {candidate.History.Id} was not commented: {error}");
                }
            }

            if (completed.Count > 0)
            {
                watched.ExceptWith(completed);
                YouTubeFirstCommentAutopilotStateStore.Save(
                    statePath,
                    state with { WatchedHistoryIds = watched.OrderBy(id => id).ToArray() });
                RefreshQuizHistory();
                RefreshUploadManager();
                await RefreshScheduledReleaseReadinessAsync(false);
            }
        }
        catch (Exception error)
        {
            // This is intentionally silent background automation. A temporary YouTube,
            // OAuth or network problem will be retried on the next watchdog pass.
            Debug.WriteLine("First Comment Autopilot failed: " + error);
        }
        finally
        {
            _youtubeFirstCommentAutopilotRunning = false;
        }
    }

    private string YouTubeFirstCommentAutopilotStatePath() =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(_data.SettingsPath))!,
            "youtube-first-comment-autopilot.json");
}
