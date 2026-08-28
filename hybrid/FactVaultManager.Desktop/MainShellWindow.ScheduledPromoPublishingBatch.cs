using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduledPromoPublishingButtonTag = "scheduled-promo-publishing-batch";
    private Button? _scheduledPromoPublishingButton;
    private int _scheduledPromoPublishingUiAttempts;
    private bool _scheduledPromoPublishingInitialized;

    private sealed record ScheduledPromoPublishingOptions(string TimeText, bool NotifySubscribers);

    private sealed record ScheduledPromoPublishingWorkItem(
        QuizHistorySummary History,
        string VideoPath,
        string Title,
        string Description,
        DateTimeOffset PublishAt,
        bool YouTube,
        bool Facebook);

    public void InitializeScheduledPromoPublishingBatchForApp()
    {
        if (_scheduledPromoPublishingInitialized) return;
        _scheduledPromoPublishingInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledPromoPublishingButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledPromoPublishingButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledPromoPublishingButton));
    }

    private void EnsureScheduledPromoPublishingButton()
    {
        if (_scheduledPromoPublishingButton?.Parent is not null) return;
        if (Content is not DependencyObject root)
        {
            RetryScheduledPromoPublishingButton();
            return;
        }

        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                ScheduledPromoPublishingButtonTag,
                StringComparison.Ordinal));
        if (existing is not null)
        {
            _scheduledPromoPublishingButton = existing;
            return;
        }

        var uploadManager = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Open Upload Manager",
                StringComparison.Ordinal));
        if (uploadManager?.Parent is not StackPanel actions)
        {
            RetryScheduledPromoPublishingButton();
            return;
        }

        var button = new Button
        {
            Content = "Schedule promos",
            Tag = ScheduledPromoPublishingButtonTag,
            MinWidth = 132,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Upload missing promo Shorts to YouTube and Facebook now, then schedule them for the day after each full quiz goes live. Instagram remains pending because Instagram Reel scheduling is not available through the current API.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(105, 164, 255));
        button.Click += async (_, _) => await ScheduleMissingPromosAsync(button);

        var uploadManagerIndex = actions.Children.IndexOf(uploadManager);
        actions.Children.Insert(uploadManagerIndex < 0 ? actions.Children.Count : uploadManagerIndex, button);
        _scheduledPromoPublishingButton = button;
    }

    private void RetryScheduledPromoPublishingButton()
    {
        if (++_scheduledPromoPublishingUiAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureScheduledPromoPublishingButton();
        };
        timer.Start();
    }

    private async Task ScheduleMissingPromosAsync(Button sourceButton)
    {
        const string dialogTitle = "Schedule Promo Uploads";
        if (_scheduledReadinessGrid?.ItemsSource is not IEnumerable<ScheduledReleaseReadinessRow> visibleRows)
        {
            MessageBox.Show(this, "Open Release Readiness and refresh it first.", dialogTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rows = visibleRows.ToList();
        var targets = ScheduledPromoBatchPlanner.SelectMissingScheduledUploads(rows);
        if (targets.Count == 0)
        {
            var waitingForTracking = rows.Any(row =>
                string.Equals(row.Promo, "Ready", StringComparison.Ordinal) &&
                !string.Equals(row.Tracking, "Ready", StringComparison.Ordinal) &&
                (string.Equals(row.YouTubePromo, "Ready", StringComparison.Ordinal) ||
                 string.Equals(row.FacebookPromo, "Ready", StringComparison.Ordinal)));
            MessageBox.Show(
                this,
                waitingForTracking
                    ? "Create the missing tracking links first. Promo scheduling uses those source-specific funnel URLs."
                    : "Every promo shown is either already uploaded to YouTube/Facebook or still needs its local promo file.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var options = ShowScheduledPromoPublishingOptions(targets);
        if (options is null) return;

        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        var youtubeScheduled = 0;
        var facebookScheduled = 0;
        var failed = 0;
        var skipped = 0;
        var problems = new List<string>();
        var warnings = new List<string>();

        try
        {
            var now = DateTimeOffset.Now;
            var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!trackerSettings.IsConfigured)
                throw new InvalidOperationException("Configure Settings → Link Tracker before scheduling promo uploads.");

            var histories = _data.GetQuizHistory(2_000)
                .GroupBy(history => history.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var workItems = new List<ScheduledPromoPublishingWorkItem>();
            var media = new NativeFfmpegTimelineService();

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                sourceButton.Content = $"Checking {index + 1}/{targets.Count}";
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

                var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder);
                if (video is null)
                {
                    skipped++;
                    problems.Add($"{target.Quiz}: promotional Short file is missing.");
                    continue;
                }

                var uploadYouTube = target.YouTube && QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder) is null;
                var uploadFacebook = target.Facebook && QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder) is null;
                if (!uploadYouTube && !uploadFacebook)
                {
                    skipped++;
                    continue;
                }

                var publishAt = ScheduledPromoBatchPlanner.ResolvePromoPublishAt(
                    target.LongFormPublishAt,
                    options.TimeText,
                    now);
                if (uploadFacebook && publishAt > now.AddDays(30))
                {
                    skipped++;
                    problems.Add($"{target.Quiz}: Facebook can only be scheduled up to 30 days ahead.");
                    continue;
                }

                var title = QuizPromoShortUploadMetadata.Title(history.UploadTitleDisplay);
                var description = QuizPromoShortUploadMetadata.Description(
                    history.UploadTitleDisplay,
                    history.YouTubeUrl,
                    history.Hashtags);
                var validatedVideo = SocialVideoUploadRules.ValidateVideoFile(video);
                SocialVideoUploadRules.ValidateUploadMetadata(
                    "Short",
                    title,
                    description,
                    requireFullYouTubeVideoLink: uploadYouTube || uploadFacebook);
                if (uploadFacebook)
                {
                    var duration = await media.MediaDurationAsync(validatedVideo);
                    SocialVideoUploadRules.ValidateFacebookDuration(duration);
                }

                workItems.Add(new ScheduledPromoPublishingWorkItem(
                    history,
                    validatedVideo,
                    title,
                    description,
                    publishAt,
                    uploadYouTube,
                    uploadFacebook));
            }

            if (workItems.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No promo uploads are currently schedulable." +
                    (problems.Count == 0 ? "" : "\n\n" + string.Join("\n", problems.Take(8))),
                    dialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var destinations = SocialUploadDestination.None;
            if (workItems.Any(item => item.YouTube)) destinations |= SocialUploadDestination.YouTube;
            if (workItems.Any(item => item.Facebook)) destinations |= SocialUploadDestination.Facebook;
            var first = workItems[0];
            var preflight = await ConfirmSocialPublishingPreflightAsync(
                this,
                destinations,
                first.VideoPath,
                first.Title,
                "private",
                first.PublishAt,
                workItems.Count);
            if (preflight is null) return;

            for (var index = 0; index < workItems.Count; index++)
            {
                var item = workItems[index];
                sourceButton.Content = $"Scheduling {index + 1}/{workItems.Count}";
                SetScheduledReadinessStatus(
                    $"Scheduling {index + 1:N0}/{workItems.Count:N0}: {item.History.UploadTitleDisplay} for {item.PublishAt:ddd dd MMM HH:mm}...");

                var links = FactburstLinkTrackerClient.BuildLinks(
                    trackerSettings.BaseUrl,
                    FactburstLinkTrackerClient.CampaignSlug(item.History));
                var youtubeDescription = FactburstLinkTrackerClient.ReplaceFullQuizLink(
                    item.Description,
                    links.YouTubePromoUrl);
                var facebookDescription = FactburstLinkTrackerClient.ReplaceFullQuizLink(
                    item.Description,
                    links.FacebookUrl);

                if (item.YouTube)
                {
                    try
                    {
                        SetScheduledReadinessStatus(
                            $"{index + 1:N0}/{workItems.Count:N0} • Scheduling YouTube promo: {item.History.UploadTitleDisplay}");
                        var result = await _youtubeVideoUpload.UploadAsync(
                            preflight.YouTubeAccessToken,
                            item.VideoPath,
                            new YouTubeVideoUpload(
                                item.Title,
                                youtubeDescription,
                                "private",
                                options.NotifySubscribers,
                                item.PublishAt));
                        QuizPromoShortPublicationStore.RecordYouTube(
                            item.History.ProjectFolder,
                            result,
                            "private",
                            DateTimeOffset.Now);
                        youtubeScheduled++;
                        try
                        {
                            await _youtubeManagement.VerifyUploadedVideoAsync(
                                preflight.YouTubeAccessToken,
                                result.VideoId,
                                preflight.YouTubeChannel!.Id,
                                item.Title,
                                "private");
                        }
                        catch (Exception warning)
                        {
                            warnings.Add($"{item.History.UploadTitleDisplay} — YouTube verification: {warning.Message}");
                        }
                    }
                    catch (Exception error)
                    {
                        failed++;
                        problems.Add($"{item.History.UploadTitleDisplay} — YouTube: {error.Message}");
                    }
                }

                if (item.Facebook)
                {
                    try
                    {
                        SetScheduledReadinessStatus(
                            $"{index + 1:N0}/{workItems.Count:N0} • Scheduling Facebook promo: {item.History.UploadTitleDisplay}");
                        var result = await _facebookReelUpload.UploadAsync(
                            preflight.FacebookPageToken,
                            item.VideoPath,
                            item.Title,
                            facebookDescription,
                            item.PublishAt);
                        QuizPromoShortSocialPublicationStore.RecordFacebook(
                            item.History.ProjectFolder,
                            result,
                            DateTimeOffset.Now);
                        facebookScheduled++;
                        try
                        {
                            await _facebookReelUpload.VerifyUploadedReelAsync(
                                preflight.FacebookPageToken,
                                result.VideoId);
                        }
                        catch (Exception warning)
                        {
                            warnings.Add($"{item.History.UploadTitleDisplay} — Facebook verification: {warning.Message}");
                        }
                    }
                    catch (Exception error)
                    {
                        failed++;
                        problems.Add($"{item.History.UploadTitleDisplay} — Facebook: {error.Message}");
                    }
                }
            }

            RefreshUploadManager();
            await RefreshScheduledReleaseReadinessAsync(false);

            var detail = problems.Count == 0
                ? ""
                : "\n\nNeeds attention:\n" + string.Join("\n", problems.Take(8).Select(problem => "• " + problem)) +
                  (problems.Count > 8 ? $"\n• ...and {problems.Count - 8:N0} more." : "");
            var warningText = warnings.Count == 0
                ? ""
                : "\n\nVerification warnings:\n" + string.Join("\n", warnings.Take(5).Select(warning => "• " + warning)) +
                  (warnings.Count > 5 ? $"\n• ...and {warnings.Count - 5:N0} more." : "");
            var skippedText = skipped == 0 ? "" : $"\nSkipped {skipped:N0} item(s) that were no longer schedulable.";
            MessageBox.Show(
                this,
                ScheduledPromoBatchPlanner.PublishingSummary(youtubeScheduled, facebookScheduled, failed) +
                skippedText +
                "\n\nYouTube and Facebook promos are uploaded now but scheduled to go public at the chosen time on the day after each full quiz goes live." +
                "\n\nInstagram remains pending because scheduled Instagram Reel publishing is not available through the current API. It can be published the following day without affecting the YouTube/Facebook schedules." +
                detail + warningText,
                dialogTitle,
                MessageBoxButton.OK,
                failed > 0 || skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus("Promo scheduling stopped: " + error.Message);
            MessageBox.Show(this, error.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private ScheduledPromoPublishingOptions? ShowScheduledPromoPublishingOptions(
        IReadOnlyList<ScheduledPromoPublishingTarget> targets)
    {
        var dialog = new Window
        {
            Title = "Schedule Promo Uploads",
            Owner = this,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = $"Schedule {targets.Count:N0} promo Short(s)",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Each missing YouTube and Facebook promo will upload now and go public at this local time on the day after its full quiz is scheduled.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 7, 0, 16),
        });

        var timePanel = new StackPanel { Orientation = Orientation.Horizontal };
        timePanel.Children.Add(new TextBlock
        {
            Text = "Promo time",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 100,
        });
        var time = new TextBox
        {
            Text = "18:00",
            Width = 78,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Local time in 24-hour HH:mm format",
        };
        timePanel.Children.Add(time);
        timePanel.Children.Add(new TextBlock
        {
            Text = "local time",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(10, 0, 0, 0),
        });
        root.Children.Add(timePanel);

        var notify = new CheckBox
        {
            Content = "Notify YouTube subscribers when each promo publishes",
            IsChecked = true,
            Margin = new Thickness(0, 14, 0, 0),
        };
        root.Children.Add(notify);
        root.Children.Add(new TextBlock
        {
            Text = "Instagram will not be published by this batch because Instagram does not currently support scheduled Reel publishing through the API. Publish it manually on the following day.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(185, 95, 20)),
            Margin = new Thickness(0, 14, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 82, MinHeight = 36, IsCancel = true };
        var confirm = new Button
        {
            Content = "Continue",
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        StyleQuizHistoryButton(confirm, Color.FromRgb(70, 235, 115));
        confirm.Click += (_, _) =>
        {
            try
            {
                var now = DateTimeOffset.Now;
                foreach (var target in targets)
                {
                    var publishAt = ScheduledPromoBatchPlanner.ResolvePromoPublishAt(
                        target.LongFormPublishAt,
                        time.Text,
                        now);
                    if (target.Facebook && publishAt > now.AddDays(30))
                        throw new ArgumentException("One or more Facebook promos are more than 30 days away. Use a shorter Release Readiness range.");
                }
                dialog.DialogResult = true;
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Schedule Promo Uploads", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        root.Children.Add(actions);
        dialog.Content = root;

        return dialog.ShowDialog() == true
            ? new ScheduledPromoPublishingOptions(time.Text.Trim(), notify.IsChecked == true)
            : null;
    }
}