using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizBatchAutomationButtonTag = "QuizBatchAutomationButton";
    private const string QuizBatchAutomationScheduleTime = "09:00";
    private static readonly bool QuizBatchAutomationUiRegistered = RegisterQuizBatchAutomationUi();
    private bool _quizBatchAutomationRunning;

    private sealed record QuizBatchAutomationWorkItem(
        QuizBatchRenderItemResult Render,
        QuizHistorySummary History,
        string ThumbnailPath,
        DateTimeOffset PublishAt);

    private sealed record QuizBatchAutomationScheduledItem(
        QuizHistorySummary History,
        DateTimeOffset PublishAt);

    private static bool RegisterQuizBatchAutomationUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizBatchAutomationTarget_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizBatchAutomationTarget_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button batchRenderButton ||
            !string.Equals(batchRenderButton.Content?.ToString(), "Batch Render...", StringComparison.Ordinal) ||
            Window.GetWindow(batchRenderButton) is not MainShellWindow window ||
            batchRenderButton.Parent is not StackPanel actions ||
            actions.Children.OfType<FrameworkElement>().Any(child =>
                string.Equals(child.Tag?.ToString(), QuizBatchAutomationButtonTag, StringComparison.Ordinal)))
        {
            return;
        }

        var button = new Button
        {
            Content = "Generate + Schedule...",
            Tag = QuizBatchAutomationButtonTag,
            Height = batchRenderButton.Height,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(0),
            ToolTip = "Generate fresh quizzes, schedule each full quiz to YouTube on the next free 09:00 day, then create each promo Short ready for the following day.",
        };
        button.Click += window.GenerateAndScheduleQuizBatch_Click;

        var index = actions.Children.IndexOf(batchRenderButton);
        actions.Children.Insert(index < 0 ? 0 : index, button);
        batchRenderButton.Margin = new Thickness(8, 0, 0, 0);
    }

    private async void GenerateAndScheduleQuizBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBatchAutomationRunning || _quizBatchRenderRunning)
            return;

        var requested = AskQuizBatchAutomationCount();
        if (requested is null)
            return;

        // The same performance plan shown on YouTube Performance now drives the actual
        // generated categories. The Builder selection is only a fallback if no plan can
        // be produced (for example, when the question bank has no enabled categories).
        var performancePlan = BuildYouTubeGrowthCategoryPlan(requested.Value);
        var originalCategory = _quizCategoryComboBox?.SelectedItem;
        var originalTitle = _quizTitleTextBox?.Text ?? "";

        var button = sender as Button;
        var progressWindow = CreateQuizBatchAutomationProgressWindow(
            requested.Value,
            out var progressText,
            out var progressBar,
            out var detailText);
        var rendered = new List<QuizBatchRenderItemResult>();
        var renderFailures = new List<string>();
        var scheduleFailures = new List<string>();
        var promoFailures = new List<string>();
        var warnings = new List<string>();
        var scheduled = new List<QuizBatchAutomationScheduledItem>();
        var promosCreated = 0;

        _quizBatchAutomationRunning = true;
        _quizBatchRenderRunning = true;
        if (button is not null)
            button.IsEnabled = false;
        progressWindow.Show();

        try
        {
            for (var index = 0; index < requested.Value; index++)
            {
                var number = index + 1;
                var fallbackCategory = SelectedQuizCategory();
                var recommendedCategory = QuizAutopilotPerformancePlan.CategoryForSlot(
                    performancePlan,
                    index,
                    fallbackCategory);

                if (!string.IsNullOrWhiteSpace(recommendedCategory))
                {
                    ApplyYouTubeGrowthCategory(recommendedCategory);
                    progressText.Text = $"Rendering quiz {number:N0} of {requested.Value:N0} • {recommendedCategory}";
                    detailText.Text = $"Performance recommends {recommendedCategory} • selecting fresh questions with rotation rules...";
                    if (_quizPageStatusText is not null)
                        _quizPageStatusText.Text = $"Growth Autopilot: quiz {number}/{requested.Value} • {recommendedCategory}";
                }
                else
                {
                    progressText.Text = $"Rendering quiz {number:N0} of {requested.Value:N0}";
                    detailText.Text = "Selecting fresh questions with rotation rules...";
                }

                progressBar.Value = index * 55.0 / requested.Value;

                // Keep this yield: routed Full Autopilot can still reserve the first slot
                // for a queued Winner follow-up. Subsequent slots are explicitly reset to
                // their performance-plan category before their questions are selected.
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    rendered.Add(await RenderOneBatchQuizAsync(
                        number,
                        requested.Value,
                        stage => detailText.Text = stage));
                }
                catch (Exception error)
                {
                    renderFailures.Add($"Quiz {number}: {error.Message}");
                }
            }

            if (rendered.Count == 0)
                return;

            progressText.Text = "Preparing YouTube packages";
            detailText.Text = "Creating A/B title and thumbnail packages before upload...";
            progressBar.Value = 57;
            await Dispatcher.Yield(DispatcherPriority.Background);

            var packaged = new List<(QuizBatchRenderItemResult Render, QuizHistorySummary History, string ThumbnailPath)>();
            foreach (var item in rendered)
            {
                try
                {
                    var history = FindQuizHistoryByProjectFolder(item.ProjectFolder)
                        ?? throw new InvalidOperationException("The new quiz history record could not be found.");
                    var package = GenerateHistoricalYouTubePackage(history);
                    history = _data.GetQuizHistory(2_000).First(entry => entry.Id == history.Id);
                    var variantA = package.Variants.First(variant =>
                        string.Equals(variant.Key, "A", StringComparison.OrdinalIgnoreCase));
                    var thumbnail = SocialVideoUploadRules.ValidateThumbnailFile(
                        Path.Combine(package.ProjectFolder, variantA.ThumbnailFileName))
                        ?? throw new InvalidOperationException("Package A thumbnail is missing.");
                    packaged.Add((item, history, thumbnail));
                }
                catch (Exception error)
                {
                    scheduleFailures.Add($"{item.YouTubeTitle}: package preparation failed: {error.Message}");
                }
            }

            if (packaged.Count == 0)
                return;

            var now = DateTimeOffset.Now;
            var openDates = QuizScheduleDatePlanner.FindNextOpenDates(
                _data.GetQuizHistory(2_000),
                DateTime.Today.AddDays(1),
                packaged.Count,
                now);
            var workItems = new List<QuizBatchAutomationWorkItem>(packaged.Count);
            for (var index = 0; index < packaged.Count; index++)
            {
                var publishAt = SocialVideoUploadRules.ResolveScheduledPublishAt(
                    schedule: true,
                    openDates[index],
                    QuizBatchAutomationScheduleTime,
                    now,
                    includesFacebook: false)
                    ?? throw new InvalidOperationException("A YouTube publication date could not be resolved.");
                workItems.Add(new QuizBatchAutomationWorkItem(
                    packaged[index].Render,
                    packaged[index].History,
                    packaged[index].ThumbnailPath,
                    publishAt));
            }

            progressText.Text = "Checking YouTube destination";
            detailText.Text = "Confirm the connected channel once for the whole batch...";
            progressBar.Value = 60;
            var first = workItems[0];
            var preflight = await ConfirmSocialPublishingPreflightAsync(
                this,
                SocialUploadDestination.YouTube,
                first.Render.VideoPath,
                first.History.UploadTitleDisplay,
                "private",
                first.PublishAt,
                workItems.Count);
            if (preflight is null)
            {
                scheduleFailures.Add("YouTube scheduling was cancelled at the account preflight.");
                return;
            }

            for (var index = 0; index < workItems.Count; index++)
            {
                var item = workItems[index];
                progressText.Text = $"Scheduling YouTube {index + 1:N0} of {workItems.Count:N0}";
                progressBar.Value = 60 + ((index + 1) * 22.0 / workItems.Count);
                detailText.Text = $"{item.PublishAt:ddd dd MMM HH:mm} • {item.History.UploadTitleDisplay}";
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var videoPath = SocialVideoUploadRules.ValidateVideoFile(item.Render.VideoPath);
                    var title = item.History.UploadTitleDisplay.Trim();
                    var description = SocialVideoUploadRules.UploadDescription(item.History);
                    SocialVideoUploadRules.ValidateUploadMetadata(
                        item.History.VideoType,
                        title,
                        description,
                        requireFullYouTubeVideoLink: false);

                    var result = await _youtubeVideoUpload.UploadAsync(
                        preflight.YouTubeAccessToken,
                        videoPath,
                        new YouTubeVideoUpload(
                            title,
                            description,
                            "private",
                            NotifySubscribers: true,
                            PublishAt: item.PublishAt));

                    try
                    {
                        await _youtubeManagement.VerifyUploadedVideoAsync(
                            preflight.YouTubeAccessToken,
                            result.VideoId,
                            preflight.YouTubeChannel!.Id,
                            title,
                            "private");
                    }
                    catch (Exception error)
                    {
                        warnings.Add($"{title}: YouTube verification: {error.Message}");
                    }

                    _data.UpdateQuizHistoryYouTubeAnalytics(
                        item.History.Id,
                        true,
                        result.Url,
                        0,
                        0,
                        item.PublishAt.LocalDateTime.Date);
                    _data.UpdateQuizHistoryYouTubeUploadState(item.History.Id, "private", item.PublishAt);

                    try
                    {
                        await _youtubeVideoUpload.SetThumbnailAsync(
                            preflight.YouTubeAccessToken,
                            result.VideoId,
                            item.ThumbnailPath);
                    }
                    catch (Exception error)
                    {
                        warnings.Add($"{title}: thumbnail: {error.Message}");
                    }

                    var refreshed = _data.GetQuizHistory(2_000).First(entry => entry.Id == item.History.Id);
                    scheduled.Add(new QuizBatchAutomationScheduledItem(refreshed, item.PublishAt));
                }
                catch (Exception error)
                {
                    scheduleFailures.Add($"{item.History.UploadTitleDisplay}: {error.Message}");
                }
            }

            if (scheduled.Count == 0)
                return;

            progressText.Text = "Creating promo Shorts";
            detailText.Text = "Preparing one shared Fable CTA narration for the batch...";
            progressBar.Value = 84;
            await Dispatcher.Yield(DispatcherPriority.Background);

            var settings = _data.LoadSettings();
            var apiKey = NativeProviderCredentials.FromSettings(settings).Get("openai");
            var quizLogoPath = _data.LoadQuizLogoPath();
            var sharedCtaAudio = await PrepareSharedScheduledPromoCtaAsync(apiKey);
            var sharedCtaScript = Path.ChangeExtension(sharedCtaAudio, ".txt");
            var renderer = new QuizPromoNativeShortRenderer();

            for (var index = 0; index < scheduled.Count; index++)
            {
                var item = scheduled[index];
                progressText.Text = $"Creating promo {index + 1:N0} of {scheduled.Count:N0}";
                progressBar.Value = 84 + ((index + 1) * 16.0 / scheduled.Count);
                detailText.Text = $"Ready for {item.PublishAt.AddDays(1):ddd dd MMM} • {item.History.UploadTitleDisplay}";
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var sourceVideo = SocialVideoUploadRules.FindLikelyRenderedVideo(item.History.ProjectFolder)
                        ?? throw new FileNotFoundException("The final long-form video was not found.");
                    var uploadSnapshot = QuizPromoShortUploadState.Capture(item.History.ProjectFolder);
                    try
                    {
                        CopySharedScheduledPromoCta(
                            sharedCtaAudio,
                            File.Exists(sharedCtaScript) ? sharedCtaScript : null,
                            item.History.ProjectFolder);
                        await renderer.CreateAsync(
                            sourceVideo,
                            item.History.ProjectFolder,
                            item.History.UploadTitleDisplay,
                            item.History.YouTubeUrl,
                            QuizPromoShortScript.DefaultCallToAction,
                            apiKey,
                            quizLogoPath,
                            message => detailText.Text = message);
                        promosCreated++;
                    }
                    finally
                    {
                        QuizPromoShortUploadState.Restore(item.History.ProjectFolder, uploadSnapshot);
                    }
                }
                catch (Exception error)
                {
                    promoFailures.Add($"{item.History.UploadTitleDisplay}: {error.Message}");
                }
            }
        }
        catch (Exception error)
        {
            scheduleFailures.Add(error.Message);
        }
        finally
        {
            _quizBatchAutomationRunning = false;
            _quizBatchRenderRunning = false;
            if (button is not null)
                button.IsEnabled = true;
            progressWindow.Close();

            if (_quizCategoryComboBox is not null && originalCategory is not null)
                _quizCategoryComboBox.SelectedItem = originalCategory;
            if (_quizTitleTextBox is not null)
                _quizTitleTextBox.Text = originalTitle;

            RefreshQuizBank();
            RefreshQuizDraftUsageCounts();
            RefreshQuizHistory();
            RefreshQuizPreview();
            RefreshQuizPublishingSeries();
            RefreshUploadManager();
            try { await RefreshScheduledReleaseReadinessAsync(false); } catch { }

            ShowQuizBatchAutomationSummary(
                requested.Value,
                rendered.Count,
                scheduled,
                promosCreated,
                renderFailures,
                scheduleFailures,
                promoFailures,
                warnings);
        }
    }

    private int? AskQuizBatchAutomationCount()
    {
        var countBox = new TextBox
        {
            Text = "5",
            MinWidth = 90,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Generate, schedule and prepare a quiz batch",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Number of fresh quizzes",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
        });
        panel.Children.Add(countBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Autopilot uses the current Performance recommendation to choose the category for each quiz. It then renders, creates the YouTube A/B package, uploads and schedules the full video for 09:00 on the next free day, and prepares its promo Short for the following day.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var start = new Button { Content = "Generate + schedule", MinWidth = 150, Height = 34, FontWeight = FontWeights.SemiBold };
        actions.Children.Add(cancel);
        actions.Children.Add(start);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Generate + Schedule Quiz Batch",
            Width = 570,
            Height = 335,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) =>
        {
            if (!int.TryParse(countBox.Text.Trim(), out var count) || count is < 2 or > 20)
            {
                MessageBox.Show(dialog, "Enter a batch size from 2 to 20.", "Generate + Schedule", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.Tag = count;
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true || dialog.Tag is not int result)
            return null;
        return result;
    }

    private Window CreateQuizBatchAutomationProgressWindow(
        int count,
        out TextBlock progressText,
        out ProgressBar progressBar,
        out TextBlock detailText)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Automating {count:N0} quiz releases",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });
        progressText = new TextBlock { Text = "Starting batch...", FontWeight = FontWeights.SemiBold };
        panel.Children.Add(progressText);
        progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(0, 8, 0, 10) };
        panel.Children.Add(progressBar);
        detailText = new TextBlock { Text = "Preparing...", TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(detailText);
        panel.Children.Add(new TextBlock
        {
            Text = "Keep the app running until the summary appears. A failed item is reported while the rest of the batch continues.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        });

        return new Window
        {
            Owner = this,
            Title = "Quiz Release Automation",
            Width = 560,
            Height = 245,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };
    }

    private QuizHistorySummary? FindQuizHistoryByProjectFolder(string projectFolder)
    {
        string expected;
        try
        {
            expected = Path.GetFullPath(projectFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }

        foreach (var history in _data.GetQuizHistory(2_000).OrderByDescending(item => item.Id))
        {
            try
            {
                var actual = Path.GetFullPath(history.ProjectFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    return history;
            }
            catch
            {
            }
        }
        return null;
    }

    private void ShowQuizBatchAutomationSummary(
        int requested,
        int rendered,
        IReadOnlyList<QuizBatchAutomationScheduledItem> scheduled,
        int promosCreated,
        IReadOnlyList<string> renderFailures,
        IReadOnlyList<string> scheduleFailures,
        IReadOnlyList<string> promoFailures,
        IReadOnlyList<string> warnings)
    {
        var lines = new List<string>
        {
            $"Requested: {requested:N0}",
            $"Rendered: {rendered:N0}",
            $"YouTube scheduled: {scheduled.Count:N0}",
            $"Promo Shorts created: {promosCreated:N0}",
        };

        if (scheduled.Count > 0)
        {
            lines.Add("");
            lines.Add("YouTube schedule:");
            foreach (var item in scheduled.Take(12))
                lines.Add($"• {item.PublishAt:ddd dd MMM • HH:mm} — {item.History.UploadTitleDisplay}");
            if (scheduled.Count > 12)
                lines.Add($"• …and {scheduled.Count - 12:N0} more");
            lines.Add("");
            lines.Add("Each promo is prepared locally for the following day. Release Readiness can finish tracking links and schedule the YouTube/Facebook promo uploads; Instagram remains a next-day manual publication task.");
        }

        var problems = renderFailures.Concat(scheduleFailures).Concat(promoFailures).ToList();
        if (problems.Count > 0)
        {
            lines.Add("");
            lines.Add("Needs attention:");
            foreach (var problem in problems.Take(10))
                lines.Add("• " + problem);
            if (problems.Count > 10)
                lines.Add($"• …and {problems.Count - 10:N0} more");
        }

        if (warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Warnings:");
            foreach (var warning in warnings.Take(6))
                lines.Add("• " + warning);
            if (warnings.Count > 6)
                lines.Add($"• …and {warnings.Count - 6:N0} more");
        }

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, lines),
            "Quiz Release Automation",
            MessageBoxButton.OK,
            problems.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
