using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizAutopilotHookKey = "quiz-autopilot-hooked";
    private static readonly TimeSpan QuizAutopilotMediaPersistencePollInterval = TimeSpan.FromSeconds(1);
    private static readonly bool QuizAutopilotUiRegistered = RegisterQuizAutopilotUi();
    private bool _quizAutopilotFinishing;

    private static bool RegisterQuizAutopilotUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizAutopilotButton_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizAutopilotButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Tag?.ToString(), QuizBatchAutomationButtonTag, StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window ||
            button.Resources.Contains(QuizAutopilotHookKey))
        {
            return;
        }

        button.Resources[QuizAutopilotHookKey] = true;
        button.Content = "Generate + Autopilot...";
        button.ToolTip =
            "Generate fresh quizzes, schedule the full videos on the next free YouTube days, preserve visual-question media for promos, create promo Shorts, create tracking links, prepare the website, then open one batch promo-scheduling step for YouTube/Facebook.";
        button.Click += window.QuizAutopilotBatchButton_Click;
    }

    private async void QuizAutopilotBatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quizAutopilotFinishing)
            return;

        var existingIds = await Task.Run(() => _data.GetQuizHistory(2_000)
            .Select(history => history.Id)
            .ToHashSet());
        var persistedIds = new HashSet<int>();
        var questionBank = await Task.Run(LoadQuizAutopilotQuestionBank);

        // The original Generate + Schedule click handler runs first. It yields before
        // rendering the first quiz, so this companion handler can preserve image media
        // while the batch is being built and before its promo renderer needs it. Keep
        // database scans and media copies off the WPF dispatcher so Autopilot does not
        // make the rest of the app unresponsive while a batch is rendering.
        await Dispatcher.Yield(DispatcherPriority.Background);
        while (_quizBatchAutomationRunning || _quizBatchRenderRunning)
        {
            await Task.Run(() => TryPersistNewQuizProjectQuestionMedia(existingIds, persistedIds, questionBank));
            await Task.Delay(QuizAutopilotMediaPersistencePollInterval);
        }
        await Task.Run(() => TryPersistNewQuizProjectQuestionMedia(existingIds, persistedIds, questionBank));

        var created = await Task.Run(() => _data.GetQuizHistory(2_000)
            .Where(history => !existingIds.Contains(history.Id))
            .Where(history =>
                history.PublishedOnYouTube &&
                string.Equals(history.VideoType, "Video", StringComparison.Ordinal) &&
                ScheduledReleaseReadinessPlanner.TryFutureSchedule(
                    history.YouTubeScheduledFor,
                    DateTimeOffset.Now,
                    out _))
            .OrderBy(history => history.YouTubeScheduledFor, StringComparer.Ordinal)
            .ThenBy(history => history.Id)
            .ToList());
        if (created.Count == 0)
            return;

        var button = sender as Button;
        var originalContent = button?.Content;
        _quizAutopilotFinishing = true;
        if (button is not null)
        {
            button.IsEnabled = false;
            button.Content = "Autopilot finishing...";
        }

        try
        {
            await FinishNewScheduledQuizBatchAsync(created);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                "The quizzes were generated, but Autopilot could not finish every readiness step.\n\n" + error.Message,
                "Quiz Autopilot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _quizAutopilotFinishing = false;
            if (button is not null)
            {
                button.Content = originalContent ?? "Generate + Autopilot...";
                button.IsEnabled = true;
            }
        }
    }

    private IReadOnlyDictionary<int, QuizQuestion> LoadQuizAutopilotQuestionBank()
    {
        try
        {
            return _data.GetQuizQuestions(limit: 10_000)
                .Where(question => question.Id > 0)
                .GroupBy(question => question.Id)
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch
        {
            return new Dictionary<int, QuizQuestion>();
        }
    }

    private void TryPersistNewQuizProjectQuestionMedia(
        ISet<int> existingIds,
        ISet<int> persistedIds,
        IReadOnlyDictionary<int, QuizQuestion> bank)
    {
        foreach (var history in _data.GetQuizHistory(2_000).Where(history =>
                     !existingIds.Contains(history.Id) &&
                     !persistedIds.Contains(history.Id)))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(history.ProjectFolder) ||
                    !Directory.Exists(history.ProjectFolder) ||
                    !File.Exists(Path.Combine(history.ProjectFolder, "quiz.json")))
                {
                    continue;
                }

                PersistQuizProjectQuestionMedia(history.ProjectFolder, bank);
                persistedIds.Add(history.Id);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                // A project can still be in the middle of being written/moved. The polling
                // loop retries it on the next pass instead of interrupting the render batch.
            }
        }
    }

    internal static bool PersistQuizProjectQuestionMedia(
        string projectFolder,
        IReadOnlyDictionary<int, QuizQuestion> bank)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;

        var quizPath = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        if (!File.Exists(quizPath))
            return false;

        var root = JsonNode.Parse(File.ReadAllText(quizPath)) as JsonObject;
        if (root?["questions"] is not JsonArray questions)
            return false;

        var changed = false;
        var containsLogoQuestion = false;
        foreach (var node in questions.OfType<JsonObject>())
        {
            if (!int.TryParse(node["id"]?.ToString(), out var id) ||
                !bank.TryGetValue(id, out var question))
            {
                continue;
            }

            containsLogoQuestion |= QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo;
            var source = (question.ImagePath ?? "").Trim();
            if (source.Length == 0 || !File.Exists(source))
                continue;

            source = Path.GetFullPath(source);
            var mediaFolder = Path.Combine(projectFolder, "QuestionMedia");
            Directory.CreateDirectory(mediaFolder);
            var extension = Path.GetExtension(source);
            if (extension.Length == 0 || extension.Length > 8)
                extension = ".png";
            var destination = Path.Combine(mediaFolder, $"question-{id}{extension.ToLowerInvariant()}");
            if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                File.Copy(source, destination, overwrite: true);

            var relative = Path.GetRelativePath(projectFolder, destination).Replace('\\', '/');
            if (!string.Equals(node["image_path"]?.ToString(), relative, StringComparison.Ordinal))
            {
                node["image_path"] = relative;
                changed = true;
            }
        }

        if (containsLogoQuestion &&
            !string.Equals(root["quiz_type"]?.ToString(), QuizTypeCatalog.Logo, StringComparison.OrdinalIgnoreCase))
        {
            root["quiz_type"] = QuizTypeCatalog.Logo;
            changed = true;
        }

        if (!changed)
            return false;

        var temporary = quizPath + ".autopilot.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, quizPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return true;
    }

    private async Task FinishNewScheduledQuizBatchAsync(IReadOnlyList<QuizHistorySummary> created)
    {
        var ids = created.Select(history => history.Id).ToHashSet();
        var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        var trackingCreated = 0;
        var websiteSynced = 0;
        var problems = new List<string>();

        if (trackerSettings.IsConfigured)
        {
            foreach (var history in created)
            {
                try
                {
                    SetScheduledReadinessStatus("Autopilot: creating tracking for " + history.UploadTitleDisplay + "...");
                    await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                        trackerSettings.BaseUrl,
                        trackerSettings.ApiKey,
                        FactburstLinkTrackerClient.CampaignSlug(history),
                        history.Id,
                        history.UploadTitleDisplay,
                        history.YouTubeUrl);
                    trackingCreated++;
                }
                catch (Exception error)
                {
                    problems.Add($"{history.UploadTitleDisplay}: tracking — {error.Message}");
                }
            }

            var questionImagePaths = _data.GetQuizQuestions(limit: 10_000)
                .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
                .GroupBy(question => question.Id)
                .ToDictionary(group => group.Key, group => group.First().ImagePath);
            using var website = new FactburstWebsitePublishingClient();
            foreach (var history in created)
            {
                if (!ScheduledReleaseReadinessPlanner.TryFutureSchedule(
                        history.YouTubeScheduledFor,
                        DateTimeOffset.Now,
                        out var publishAt))
                {
                    continue;
                }

                try
                {
                    SetScheduledReadinessStatus("Autopilot: preparing website for " + history.UploadTitleDisplay + "...");
                    var payload = FactburstWebsiteQuizBuilder.Build(history, publishAt, questionImagePaths);
                    await website.PublishQuizAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey, payload);
                    websiteSynced++;
                }
                catch (Exception error)
                {
                    problems.Add($"{history.UploadTitleDisplay}: website — {error.Message}");
                }
            }
        }
        else
        {
            problems.Add("Link Tracker is not configured, so tracking and website preparation were skipped.");
        }

        await RefreshScheduledReleaseReadinessAsync(false);

        var newRows = _scheduledReadinessRows
            .Where(row => ids.Contains(row.HistoryId))
            .OrderBy(row => row.PublishAt)
            .ToList();
        var schedulablePromos = newRows.Any(row =>
            string.Equals(row.Promo, "Ready", StringComparison.Ordinal) &&
            string.Equals(row.Tracking, "Ready", StringComparison.Ordinal) &&
            (!string.Equals(row.YouTubePromo, "Uploaded", StringComparison.Ordinal) ||
             !string.Equals(row.FacebookPromo, "Uploaded", StringComparison.Ordinal)));

        if (schedulablePromos && _scheduledReadinessGrid is not null)
        {
            var previousItems = _scheduledReadinessGrid.ItemsSource;
            try
            {
                _scheduledReadinessGrid.ItemsSource = newRows;
                SetScheduledReadinessStatus(
                    $"Autopilot prepared {created.Count:N0} new release(s). Confirm the promo time once to schedule their YouTube/Facebook promos.");
                await ScheduleMissingPromosAsync(new Button { Content = "Autopilot promo scheduling" });
            }
            finally
            {
                _scheduledReadinessGrid.ItemsSource = previousItems;
                ApplyScheduledReadinessView();
            }
        }

        await RefreshScheduledReleaseReadinessAsync(false);
        var remaining = _scheduledReadinessRows
            .Where(row => ids.Contains(row.HistoryId) && row.ReadyCount < row.TotalChecks)
            .ToList();
        var related = remaining.Count(row => string.Equals(row.NextAction, "Set Related video", StringComparison.Ordinal));
        var instagram = remaining.Count(row => string.Equals(row.NextAction, "Publish Instagram promo", StringComparison.Ordinal));
        var automatic = remaining.Count - related - instagram;

        var status =
            $"Autopilot: {created.Count:N0} scheduled • {trackingCreated:N0} tracking • {websiteSynced:N0} website" +
            (automatic > 0 ? $" • {automatic:N0} automatic issue(s) still need attention" : "") +
            (related > 0 ? $" • {related:N0} Related video manual" : "") +
            (instagram > 0 ? $" • {instagram:N0} Instagram manual" : "");
        SetScheduledReadinessStatus(status);

        if (problems.Count > 0)
        {
            MessageBox.Show(
                this,
                status + "\n\nAutopilot could not complete:\n" +
                string.Join("\n", problems.Take(8).Select(problem => "• " + problem)),
                "Quiz Autopilot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
