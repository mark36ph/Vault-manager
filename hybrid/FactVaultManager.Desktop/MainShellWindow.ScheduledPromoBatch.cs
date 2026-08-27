using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduledPromoBatchButtonTag = "scheduled-promo-batch";
    private Button? _scheduledPromoBatchButton;
    private int _scheduledPromoBatchUiAttempts;

    public void InitializeScheduledPromoBatchForApp()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledPromoBatchButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledPromoBatchButton));
    }

    private void EnsureScheduledPromoBatchButton()
    {
        if (_scheduledPromoBatchButton?.Parent is not null) return;
        if (Content is not DependencyObject root)
        {
            RetryScheduledPromoBatchButton();
            return;
        }

        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                ScheduledPromoBatchButtonTag,
                StringComparison.Ordinal));
        if (existing is not null)
        {
            _scheduledPromoBatchButton = existing;
            return;
        }

        var createLinks = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Create missing links",
                StringComparison.Ordinal));
        if (createLinks?.Parent is not StackPanel actions)
        {
            RetryScheduledPromoBatchButton();
            return;
        }

        var button = new Button
        {
            Content = "Prepare missing promos",
            Tag = ScheduledPromoBatchButtonTag,
            MinWidth = 162,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Render every missing promo Short in the scheduled range currently shown. This does not upload or publish anything.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(204, 70, 255));
        button.Click += async (_, _) => await PrepareMissingScheduledPromosAsync(button);

        var createLinksIndex = actions.Children.IndexOf(createLinks);
        actions.Children.Insert(createLinksIndex < 0 ? 0 : createLinksIndex, button);
        _scheduledPromoBatchButton = button;
    }

    private void RetryScheduledPromoBatchButton()
    {
        if (++_scheduledPromoBatchUiAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureScheduledPromoBatchButton();
        };
        timer.Start();
    }

    private async Task PrepareMissingScheduledPromosAsync(Button sourceButton)
    {
        const string title = "Prepare Scheduled Promo Shorts";
        if (_scheduledReadinessGrid?.ItemsSource is not IEnumerable<ScheduledReleaseReadinessRow> visibleRows)
        {
            MessageBox.Show(this, "Open Release Readiness and refresh it first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = ScheduledPromoBatchPlanner.SelectMissingPromos(visibleRows);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "All scheduled quizzes in the current range already have promo Shorts.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Prepare {targets.Count:N0} missing promo Short(s) for the scheduled quizzes currently shown?\n\n" +
            "This only renders local promo MP4 files. It will not upload, schedule or publish anything. " +
            "The shared Fable end-card narration is generated once and reused across the batch.",
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        var created = 0;
        var skipped = 0;
        var failed = 0;
        var problems = new List<string>();

        try
        {
            var settings = _data.LoadSettings();
            var apiKey = NativeProviderCredentials.FromSettings(settings).Get("openai");
            var quizLogoPath = _data.LoadQuizLogoPath();
            var histories = _data.GetQuizHistory(2_000)
                .GroupBy(history => history.Id)
                .ToDictionary(group => group.Key, group => group.First());

            SetScheduledReadinessStatus("Preparing the shared Fable promo narration...");
            var sharedCtaAudio = await PrepareSharedScheduledPromoCtaAsync(apiKey);
            var sharedCtaScript = Path.ChangeExtension(sharedCtaAudio, ".txt");
            var renderer = new QuizPromoNativeShortRenderer();

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                sourceButton.Content = $"Preparing {index + 1}/{targets.Count}";
                SetScheduledReadinessStatus(
                    $"Preparing {index + 1:N0}/{targets.Count:N0}: {target.Quiz}");

                if (!histories.TryGetValue(target.HistoryId, out var history))
                {
                    skipped++;
                    problems.Add($"{target.Quiz}: quiz history record is missing.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(history.ProjectFolder) || !Directory.Exists(history.ProjectFolder))
                {
                    skipped++;
                    problems.Add($"{target.Quiz}: project folder is missing.");
                    continue;
                }

                var sourceVideo = SocialVideoUploadRules.FindLikelyRenderedVideo(history.ProjectFolder);
                if (sourceVideo is null)
                {
                    skipped++;
                    problems.Add($"{target.Quiz}: final long-form video was not found.");
                    continue;
                }

                var uploadSnapshot = QuizPromoShortUploadState.Capture(history.ProjectFolder);
                try
                {
                    CopySharedScheduledPromoCta(
                        sharedCtaAudio,
                        File.Exists(sharedCtaScript) ? sharedCtaScript : null,
                        history.ProjectFolder);
                    await renderer.CreateAsync(
                        sourceVideo,
                        history.ProjectFolder,
                        history.UploadTitleDisplay,
                        history.YouTubeUrl,
                        QuizPromoShortScript.DefaultCallToAction,
                        apiKey,
                        quizLogoPath,
                        message => SetScheduledReadinessStatus(
                            $"{index + 1:N0}/{targets.Count:N0} • {target.Quiz}\n{message}"));
                    created++;
                }
                catch (Exception error)
                {
                    failed++;
                    problems.Add($"{target.Quiz}: {error.Message}");
                }
                finally
                {
                    QuizPromoShortUploadState.Restore(history.ProjectFolder, uploadSnapshot);
                }
            }

            RefreshUploadManager();
            await RefreshScheduledReleaseReadinessAsync(false);

            var summary = ScheduledPromoBatchPlanner.Summary(created, skipped, failed);
            var detail = problems.Count == 0
                ? ""
                : "\n\nNeeds attention:\n" + string.Join("\n", problems.Take(8).Select(problem => "• " + problem)) +
                  (problems.Count > 8 ? $"\n• ...and {problems.Count - 8:N0} more." : "");
            MessageBox.Show(
                this,
                summary + "\n\nNo promos were uploaded or published." + detail,
                title,
                MessageBoxButton.OK,
                failed > 0 || skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus("Promo batch stopped: " + error.Message);
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private static async Task<string> PrepareSharedScheduledPromoCtaAsync(string apiKey)
    {
        var cacheFolder = Path.Combine(
            Path.GetTempPath(),
            "FactVaultManager",
            "ScheduledPromoBatch",
            "FableCta");
        Directory.CreateDirectory(cacheFolder);
        using var speech = new NativeQuizSpeechProvider(apiKey, voice: "fable");
        return await speech.GeneratePromoCallToActionAsync(
            QuizPromoShortScript.DefaultCallToAction,
            cacheFolder);
    }

    private static void CopySharedScheduledPromoCta(
        string audioPath,
        string? scriptPath,
        string projectFolder)
    {
        var outputFolder = QuizPromoShortPaths.Folder(projectFolder);
        Directory.CreateDirectory(outputFolder);
        var destinationAudio = Path.Combine(outputFolder, Path.GetFileName(audioPath));
        File.Copy(audioPath, destinationAudio, overwrite: true);
        if (scriptPath is not null && File.Exists(scriptPath))
        {
            var destinationScript = Path.Combine(outputFolder, Path.GetFileName(scriptPath));
            File.Copy(scriptPath, destinationScript, overwrite: true);
        }
    }
}
