using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizGrowthAutopilotHookKey = "quiz-growth-autopilot-hooked";
    private static readonly bool QuizGrowthAutopilotUiRegistered = RegisterQuizGrowthAutopilotUi();
    private bool _quizGrowthAutopilotRunning;

    private static bool RegisterQuizGrowthAutopilotUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizGrowthAutopilotButton_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizGrowthAutopilotButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Generate + Autopilot", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window ||
            button.Resources.Contains(QuizGrowthAutopilotHookKey))
        {
            return;
        }

        button.Resources[QuizGrowthAutopilotHookKey] = true;
        button.Click += window.QuizGrowthAutopilotBatchButton_Click;
    }

    private async void QuizGrowthAutopilotBatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quizGrowthAutopilotRunning)
            return;

        var existingIds = _data.GetQuizHistory(2_000)
            .Select(history => history.Id)
            .ToHashSet();

        await Dispatcher.Yield(DispatcherPriority.Background);
        while (_quizBatchAutomationRunning || _quizBatchRenderRunning)
            await Task.Delay(250);

        // Give the existing Autopilot finisher a chance to enter its tracking/site phase,
        // then wait for it so playlist work is the final unattended YouTube step.
        await Task.Delay(350);
        while (_quizAutopilotFinishing)
            await Task.Delay(250);

        var created = _data.GetQuizHistory(2_000)
            .Where(history => !existingIds.Contains(history.Id))
            .Where(history =>
                history.PublishedOnYouTube &&
                string.Equals(history.VideoType, "Video", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(history.YouTubeUrl))
            .OrderBy(history => history.YouTubeScheduledFor, StringComparer.Ordinal)
            .ThenBy(history => history.Id)
            .ToList();
        if (created.Count == 0)
            return;

        _quizGrowthAutopilotRunning = true;
        var problems = new List<string>();
        var assigned = 0;
        try
        {
            SetScheduledReadinessStatus($"Growth Autopilot: organising {created.Count:N0} new quiz(es) into YouTube playlists...");
            var accessToken = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
            var settings = _data.LoadSettings();
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel",
                settings.ApprovedYouTubeChannelId,
                channel.Id);

            var playlists = (await _youtubeManagement.ListPlaylistsAsync(accessToken)).ToList();
            foreach (var history in created)
            {
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
                    }

                    var videoId = QuizGrowthPlaylistPlanner.VideoId(history.YouTubeUrl);
                    if (videoId.Length == 0)
                        throw new InvalidOperationException("The uploaded YouTube video ID could not be read from its URL.");

                    await _youtubeManagement.AddVideoToPlaylistAsync(
                        accessToken,
                        playlist.Id,
                        videoId);
                    assigned++;
                }
                catch (Exception error)
                {
                    problems.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }
            }

            var status = $"Growth Autopilot: {assigned:N0}/{created.Count:N0} new full quiz(es) added to category playlists";
            SetScheduledReadinessStatus(status);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = status;
        }
        catch (Exception error)
        {
            problems.Add(error.Message);
        }
        finally
        {
            _quizGrowthAutopilotRunning = false;
        }

        if (problems.Count > 0)
        {
            MessageBox.Show(
                this,
                "The release batch is still scheduled, but Growth Autopilot could not finish every playlist step.\n\n" +
                string.Join("\n", problems.Take(8).Select(problem => "• " + problem)),
                "Growth Autopilot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
