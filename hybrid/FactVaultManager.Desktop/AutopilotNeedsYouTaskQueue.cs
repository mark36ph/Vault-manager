using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public enum AutopilotNeedsYouTaskKind
{
    RelatedVideo,
    InstagramPromo,
    ViewerReply,
}

public sealed record AutopilotNeedsYouTaskItem(
    AutopilotNeedsYouTaskKind Kind,
    string Key,
    int HistoryId,
    string Title,
    string Detail,
    DateTimeOffset? DueAt,
    string ProjectFolder,
    string CommentId,
    string VideoId,
    string Draft)
{
    public string TypeDisplay => Kind switch
    {
        AutopilotNeedsYouTaskKind.RelatedVideo => "Related video",
        AutopilotNeedsYouTaskKind.InstagramPromo => "Instagram promo",
        AutopilotNeedsYouTaskKind.ViewerReply => "Viewer reply",
        _ => Kind.ToString(),
    };

    public string DueDisplay => DueAt is null
        ? "Now"
        : DueAt.Value.LocalDateTime.ToString("ddd dd MMM • HH:mm", CultureInfo.InvariantCulture);
}

public static class AutopilotNeedsYouTaskPlanner
{
    public static IReadOnlyList<AutopilotNeedsYouTaskItem> Build(
        IEnumerable<ScheduledReleaseReadinessRow> readinessRows,
        FactburstFullAutopilotState state)
    {
        ArgumentNullException.ThrowIfNull(readinessRows);
        ArgumentNullException.ThrowIfNull(state);

        var rows = readinessRows.ToList();
        var tasks = new List<AutopilotNeedsYouTaskItem>();

        foreach (var row in ScheduledPromoBatchPlanner.SelectMissingRelatedVideos(rows))
        {
            tasks.Add(new AutopilotNeedsYouTaskItem(
                AutopilotNeedsYouTaskKind.RelatedVideo,
                $"related:{row.HistoryId}",
                row.HistoryId,
                row.Quiz,
                "Open the matching promo Short in YouTube Studio, select the full quiz as its Related video, save it, then confirm the save in Factburst.",
                row.PublishAt,
                row.ProjectFolder,
                "",
                "",
                ""));
        }

        foreach (var row in rows
                     .Where(row => string.Equals(row.InstagramPromo, "Next day", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(row => row.PublishAt)
                     .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(row => row.HistoryId))
        {
            tasks.Add(new AutopilotNeedsYouTaskItem(
                AutopilotNeedsYouTaskKind.InstagramPromo,
                $"instagram:{row.HistoryId}",
                row.HistoryId,
                row.Quiz,
                "The promo is prepared. Instagram does not support this app's future Reel scheduling flow, so approve and publish it when you are ready.",
                PromoDueAt(row.PublishAt),
                row.ProjectFolder,
                "",
                "",
                ""));
        }

        foreach (var draft in state.ReplyDrafts
                     .Where(draft => !string.IsNullOrWhiteSpace(draft.CommentId))
                     .GroupBy(draft => draft.CommentId, StringComparer.Ordinal)
                     .Select(group => group.OrderByDescending(value => value.CreatedAtUtc).First())
                     .OrderBy(value => value.CreatedAtUtc))
        {
            var author = string.IsNullOrWhiteSpace(draft.Author) ? "Viewer" : draft.Author.Trim();
            tasks.Add(new AutopilotNeedsYouTaskItem(
                AutopilotNeedsYouTaskKind.ViewerReply,
                $"reply:{draft.CommentId}",
                0,
                $"Reply to {author}",
                draft.CommentText.Trim(),
                null,
                "",
                draft.CommentId.Trim(),
                draft.VideoId.Trim(),
                draft.Draft.Trim()));
        }

        return tasks
            .OrderBy(task => task.Kind)
            .ThenBy(task => task.DueAt ?? DateTimeOffset.MinValue)
            .ThenBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int Count(
        IEnumerable<AutopilotNeedsYouTaskItem> tasks,
        AutopilotNeedsYouTaskKind kind) =>
        tasks.Count(task => task.Kind == kind);

    public static bool RemoveReplyDraft(FactburstFullAutopilotState state, string? commentId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var id = (commentId ?? "").Trim();
        if (id.Length == 0) return false;
        var removed = state.ReplyDrafts.RemoveAll(draft =>
            string.Equals(draft.CommentId, id, StringComparison.Ordinal));
        return removed > 0;
    }

    public static DateTimeOffset PromoDueAt(DateTimeOffset longFormPublishAt)
    {
        var local = DateTime.SpecifyKind(
            longFormPublishAt.LocalDateTime.Date.AddDays(1).AddHours(18),
            DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
            local = local.AddHours(1);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}

public partial class MainShellWindow
{
    private const string AutopilotTaskQueueButtonTag = "autopilot-needs-you-task-queue";
    private bool _autopilotTaskQueueInitialized;
    private DispatcherTimer? _autopilotTaskQueueGuardTimer;
    private Window? _autopilotTaskQueueWindow;

    public void InitializeAutopilotNeedsYouTaskQueue()
    {
        if (_autopilotTaskQueueInitialized) return;
        _autopilotTaskQueueInitialized = true;

        AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(AutopilotTaskQueuePreviewMouseDown),
            handledEventsToo: true);
        PreviewKeyDown += AutopilotTaskQueuePreviewKeyDown;

        _autopilotTaskQueueGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _autopilotTaskQueueGuardTimer.Tick += (_, _) =>
        {
            PruneLocallyHandledReplyDrafts();
            EnsureAutopilotTaskQueueEntryButton();
        };
        _autopilotTaskQueueGuardTimer.Start();

        Closed += (_, _) => _autopilotTaskQueueGuardTimer?.Stop();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureAutopilotTaskQueueEntryButton));
    }

    private void EnsureAutopilotTaskQueueEntryButton()
    {
        if (_autopilotNeedsPanel is null) return;
        var existing = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                AutopilotTaskQueueButtonTag,
                StringComparison.Ordinal));
        if (existing is not null) return;

        var button = new Button
        {
            Content = "Manage task queue",
            Tag = AutopilotTaskQueueButtonTag,
            MinWidth = 142,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 2),
            ToolTip = "Work through Related videos, Instagram promos and viewer replies in one queue.",
        };
        button.Click += async (_, _) => await ShowAutopilotNeedsYouQueueAsync(null);
        _autopilotNeedsPanel.Children.Insert(Math.Min(1, _autopilotNeedsPanel.Children.Count), button);
    }

    private void AutopilotTaskQueuePreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        var button = FindAutopilotTaskButton(eventArgs.OriginalSource as DependencyObject);
        if (button is null || !TryTaskFilter(button, out var filter)) return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAutopilotNeedsYouQueueAsync(filter)));
    }

    private void AutopilotTaskQueuePreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space)) return;
        if (Keyboard.FocusedElement is not Button button || !TryTaskFilter(button, out var filter)) return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAutopilotNeedsYouQueueAsync(filter)));
    }

    private static Button? FindAutopilotTaskButton(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button button) return button;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool TryTaskFilter(Button button, out AutopilotNeedsYouTaskKind? filter)
    {
        filter = null;
        var content = Convert.ToString(button.Content) ?? "";
        if (string.Equals(content, "Open tasks", StringComparison.Ordinal))
        {
            filter = AutopilotNeedsYouTaskKind.RelatedVideo;
            return true;
        }
        if (string.Equals(content, "Open Instagram", StringComparison.Ordinal))
        {
            filter = AutopilotNeedsYouTaskKind.InstagramPromo;
            return true;
        }
        if (string.Equals(content, "Review replies", StringComparison.Ordinal))
        {
            filter = AutopilotNeedsYouTaskKind.ViewerReply;
            return true;
        }
        return false;
    }

    private async Task ShowAutopilotNeedsYouQueueAsync(AutopilotNeedsYouTaskKind? filter)
    {
        if (_autopilotTaskQueueWindow is not null)
        {
            _autopilotTaskQueueWindow.Activate();
            return;
        }

        const string dialogTitle = "Autopilot • Needs You";
        EnsureScheduledReleaseReadinessPage();
        if (_scheduledReadinessGrid is not null)
            await RefreshScheduledReleaseReadinessAsync(false);

        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        await PruneResolvedReplyDraftsFromYouTubeAsync(state);
        FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);

        var allTasks = AutopilotNeedsYouTaskPlanner.Build(_scheduledReadinessRows, state).ToList();
        var visibleTasks = ApplyTaskFilter(allTasks, filter);
        if (visibleTasks.Count == 0)
        {
            MessageBox.Show(
                this,
                filter is null
                    ? "Nothing needs your attention right now."
                    : $"There are no pending {TaskFilterName(filter.Value).ToLowerInvariant()} tasks.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await RefreshAutopilotHomeAsync();
            return;
        }

        var histories = _data.GetQuizHistory(2_000)
            .GroupBy(history => history.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var window = new Window
        {
            Title = dialogTitle,
            Owner = this,
            Width = 1040,
            Height = 700,
            MinWidth = 900,
            MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
        };
        _autopilotTaskQueueWindow = window;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headingGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = filter is null ? "Needs You task queue" : TaskFilterName(filter.Value),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Finish the few jobs that require a human decision. Completed items disappear from Autopilot automatically.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 20, 0),
        });
        headingGrid.Children.Add(heading);
        var showAll = new Button
        {
            Content = "Show all tasks",
            MinWidth = 112,
            MinHeight = 34,
            Visibility = filter is null ? Visibility.Collapsed : Visibility.Visible,
        };
        StyleQuizHistoryButton(showAll, Color.FromRgb(0, 204, 255));
        Grid.SetColumn(showAll, 1);
        headingGrid.Children.Add(showAll);
        root.Children.Add(headingGrid);

        var summary = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 213, 82)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var taskGrid = BuildManagerGrid();
        taskGrid.SelectionMode = DataGridSelectionMode.Single;
        taskGrid.Columns.Add(TextColumn("Type", nameof(AutopilotNeedsYouTaskItem.TypeDisplay), 132));
        taskGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Task",
            Binding = new Binding(nameof(AutopilotNeedsYouTaskItem.Title)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        taskGrid.Columns.Add(TextColumn("Due", nameof(AutopilotNeedsYouTaskItem.DueDisplay), 150));
        var taskCard = ManagerCard(taskGrid);
        Grid.SetRow(taskCard, 2);
        root.Children.Add(taskCard);

        var detailCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 78)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 111, 214)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 12, 0, 0),
        };
        var details = new StackPanel();
        var detailTitle = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var detailText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        };
        var replyBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 105,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(9),
            Visibility = Visibility.Collapsed,
        };
        details.Children.Add(detailTitle);
        details.Children.Add(detailText);
        details.Children.Add(replyBox);
        detailCard.Child = details;
        Grid.SetRow(detailCard, 3);
        root.Children.Add(detailCard);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 5; index++)
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        footer.Children.Add(status);
        var primary = ChecklistButton("Open", Color.FromRgb(0, 204, 255));
        var secondary = ChecklistButton("Open item", Color.FromRgb(88, 188, 255));
        var complete = ChecklistButton("Complete", Color.FromRgb(70, 235, 115));
        var skip = ChecklistButton("Next", Color.FromRgb(255, 202, 45));
        var close = ChecklistButton("Close", Color.FromRgb(180, 190, 210));
        primary.MinWidth = 132;
        secondary.MinWidth = 116;
        complete.MinWidth = 116;
        skip.MinWidth = 78;
        close.MinWidth = 78;
        Grid.SetColumn(primary, 1);
        Grid.SetColumn(secondary, 2);
        Grid.SetColumn(complete, 3);
        Grid.SetColumn(skip, 4);
        Grid.SetColumn(close, 5);
        footer.Children.Add(primary);
        footer.Children.Add(secondary);
        footer.Children.Add(complete);
        footer.Children.Add(skip);
        footer.Children.Add(close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        AutopilotNeedsYouTaskItem? Current() => taskGrid.SelectedItem as AutopilotNeedsYouTaskItem;

        void UpdateSummary()
        {
            summary.Text =
                $"{visibleTasks.Count:N0} shown • " +
                $"{AutopilotNeedsYouTaskPlanner.Count(allTasks, AutopilotNeedsYouTaskKind.RelatedVideo):N0} Related video • " +
                $"{AutopilotNeedsYouTaskPlanner.Count(allTasks, AutopilotNeedsYouTaskKind.InstagramPromo):N0} Instagram • " +
                $"{AutopilotNeedsYouTaskPlanner.Count(allTasks, AutopilotNeedsYouTaskKind.ViewerReply):N0} Replies";
        }

        void UpdateSelected()
        {
            var item = Current();
            if (item is null)
            {
                detailTitle.Text = "Select a task";
                detailText.Text = "Choose an item above to see the action required.";
                replyBox.Visibility = Visibility.Collapsed;
                primary.IsEnabled = secondary.IsEnabled = complete.IsEnabled = false;
                return;
            }

            detailTitle.Text = item.Title;
            detailText.Text = item.Detail;
            replyBox.Visibility = item.Kind == AutopilotNeedsYouTaskKind.ViewerReply
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (item.Kind == AutopilotNeedsYouTaskKind.ViewerReply)
                replyBox.Text = item.Draft;

            primary.IsEnabled = true;
            secondary.IsEnabled = true;
            complete.IsEnabled = true;
            primary.Visibility = Visibility.Visible;
            secondary.Visibility = Visibility.Visible;
            complete.Visibility = Visibility.Visible;

            switch (item.Kind)
            {
                case AutopilotNeedsYouTaskKind.RelatedVideo:
                    primary.Content = "Copy title + Studio";
                    secondary.Content = "Open full quiz";
                    complete.Content = "Mark saved";
                    break;
                case AutopilotNeedsYouTaskKind.InstagramPromo:
                    primary.Content = "Preview promo";
                    secondary.Content = "Open project folder";
                    complete.Content = "Publish now";
                    break;
                case AutopilotNeedsYouTaskKind.ViewerReply:
                    primary.Content = "Open on YouTube";
                    secondary.Visibility = Visibility.Collapsed;
                    complete.Content = "Send reply";
                    break;
            }
        }

        async Task ReloadAsync(string message)
        {
            if (_scheduledReadinessGrid is not null)
                await RefreshScheduledReleaseReadinessAsync(false);
            state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            PruneLocallyHandledReplyDrafts(state);
            allTasks = AutopilotNeedsYouTaskPlanner.Build(_scheduledReadinessRows, state).ToList();
            visibleTasks = ApplyTaskFilter(allTasks, filter);
            taskGrid.ItemsSource = null;
            taskGrid.ItemsSource = visibleTasks;
            UpdateSummary();
            status.Text = message;
            if (visibleTasks.Count > 0)
                taskGrid.SelectedIndex = 0;
            else
            {
                detailTitle.Text = "All done";
                detailText.Text = filter is null
                    ? "Nothing needs your attention right now."
                    : $"No {TaskFilterName(filter.Value).ToLowerInvariant()} tasks remain.";
                replyBox.Visibility = Visibility.Collapsed;
                primary.IsEnabled = secondary.IsEnabled = complete.IsEnabled = false;
            }
            await RefreshAutopilotHomeAsync();
        }

        async Task PublishInstagramAsync(AutopilotNeedsYouTaskItem item)
        {
            if (!histories.TryGetValue(item.HistoryId, out var history))
                throw new InvalidOperationException("The quiz history record for this promo is missing.");
            if (QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null)
            {
                await ReloadAsync("Instagram promo was already published.");
                return;
            }

            var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder)
                        ?? throw new FileNotFoundException("The prepared promo video could not be found.");
            video = SocialVideoUploadRules.ValidateVideoFile(video);
            var duration = await new NativeFfmpegTimelineService().MediaDurationAsync(video);
            SocialVideoUploadRules.ValidateInstagramDuration(duration);

            var title = QuizPromoShortUploadMetadata.Title(history.UploadTitleDisplay);
            var description = QuizPromoShortUploadMetadata.Description(
                history.UploadTitleDisplay,
                history.YouTubeUrl,
                history.Hashtags);
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (tracker.IsConfigured)
            {
                var links = FactburstLinkTrackerClient.BuildLinks(
                    tracker.BaseUrl,
                    FactburstLinkTrackerClient.CampaignSlug(history));
                description = FactburstLinkTrackerClient.ReplaceFullQuizLink(description, links.InstagramUrl);
            }
            var caption = SocialVideoUploadRules.InstagramCaption(description);
            var preflight = await ConfirmSocialPublishingPreflightAsync(
                window,
                SocialUploadDestination.Instagram,
                video,
                title,
                "private",
                null);
            if (preflight is null) return;

            status.Text = $"Publishing Instagram promo: {history.UploadTitleDisplay}...";
            complete.IsEnabled = false;
            try
            {
                var result = await _instagramReelUpload.UploadReelAsync(
                    preflight.FacebookPageToken,
                    video,
                    caption);
                QuizPromoShortSocialPublicationStore.RecordInstagram(
                    history.ProjectFolder,
                    result,
                    DateTimeOffset.Now);
                await ReloadAsync("Instagram promo published and recorded. Autopilot has removed it from the queue.");
                if (_instagramAnalyticsGrid is not null)
                    await RefreshInstagramManagerAsync(false);
            }
            finally
            {
                complete.IsEnabled = true;
            }
        }

        primary.Click += async (_, _) =>
        {
            var item = Current();
            if (item is null) return;
            try
            {
                if (item.Kind == AutopilotNeedsYouTaskKind.RelatedVideo)
                {
                    if (!histories.TryGetValue(item.HistoryId, out var history))
                        throw new InvalidOperationException("The quiz history record is missing.");
                    var promo = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder)
                                ?? throw new InvalidOperationException("The uploaded YouTube promo record is missing.");
                    var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl)
                                      ?? throw new InvalidOperationException("The full quiz YouTube link is invalid.");
                    Clipboard.SetText(history.UploadTitleDisplay);
                    OpenChecklistUrl(QuizPromoRelatedVideoLinks.StudioEditUrl(promo.VideoId), dialogTitle);
                    status.Text = "Full quiz title copied. In Studio choose Related video, select the matching quiz and click SAVE.";
                }
                else if (item.Kind == AutopilotNeedsYouTaskKind.InstagramPromo)
                {
                    if (!histories.TryGetValue(item.HistoryId, out var history))
                        throw new InvalidOperationException("The quiz history record is missing.");
                    var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder)
                                ?? throw new FileNotFoundException("The prepared promo video could not be found.");
                    Process.Start(new ProcessStartInfo(video) { UseShellExecute = true });
                    status.Text = "Opened the prepared Instagram promo for review.";
                }
                else
                {
                    var url = YouTubeManagementService.BuildCommentUrl(item.VideoId, item.CommentId);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    status.Text = "Opened the viewer comment on YouTube.";
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(window, error.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        secondary.Click += (_, _) =>
        {
            var item = Current();
            if (item is null) return;
            try
            {
                if (!histories.TryGetValue(item.HistoryId, out var history))
                    throw new InvalidOperationException("The quiz history record is missing.");
                if (item.Kind == AutopilotNeedsYouTaskKind.RelatedVideo)
                {
                    var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl)
                                      ?? throw new InvalidOperationException("The full quiz YouTube link is invalid.");
                    OpenChecklistUrl(QuizPromoRelatedVideoLinks.WatchUrl(longVideoId), dialogTitle);
                }
                else if (item.Kind == AutopilotNeedsYouTaskKind.InstagramPromo)
                {
                    if (!Directory.Exists(history.ProjectFolder))
                        throw new DirectoryNotFoundException("The quiz project folder is missing.");
                    Process.Start(new ProcessStartInfo(history.ProjectFolder) { UseShellExecute = true });
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(window, error.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        complete.Click += async (_, _) =>
        {
            var item = Current();
            if (item is null) return;
            try
            {
                if (item.Kind == AutopilotNeedsYouTaskKind.RelatedVideo)
                {
                    if (!histories.TryGetValue(item.HistoryId, out var history))
                        throw new InvalidOperationException("The quiz history record is missing.");
                    var promo = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder)
                                ?? throw new InvalidOperationException("The uploaded YouTube promo record is missing.");
                    var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl)
                                      ?? throw new InvalidOperationException("The full quiz YouTube link is invalid.");
                    if (MessageBox.Show(
                            window,
                            "Confirm that you selected the matching full quiz as the Short's Related video and clicked SAVE in YouTube Studio.",
                            "Confirm Related Video",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    QuizPromoRelatedVideoStore.MarkSet(
                        history.ProjectFolder,
                        promo.VideoId,
                        longVideoId,
                        DateTimeOffset.UtcNow);
                    await ReloadAsync("Related video confirmed. The task has been removed from the queue.");
                    UpdatePromoRelatedVideoChecklistButtonLabel();
                }
                else if (item.Kind == AutopilotNeedsYouTaskKind.InstagramPromo)
                {
                    await PublishInstagramAsync(item);
                }
                else
                {
                    var reply = replyBox.Text.Trim();
                    if (reply.Length == 0)
                        throw new InvalidOperationException("Enter a reply first.");
                    if (MessageBox.Show(
                            window,
                            $"Send this reply publicly to {item.Title.Replace("Reply to ", "", StringComparison.Ordinal)}?\n\n{reply}",
                            "Send YouTube Reply",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;

                    var token = await GetYouTubeManagementAccessTokenAsync();
                    var settings = _data.LoadSettings();
                    var channel = await _youtubeManagement.GetMyChannelAsync(token);
                    SocialPublishingAccountGuard.EnsureMatches(
                        "YouTube channel",
                        settings.ApprovedYouTubeChannelId,
                        channel.Id);
                    await _youtubeManagement.ReplyAsync(token, item.CommentId, reply);
                    _youtubeHandledCommentIds.Add(item.CommentId);
                    state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
                    AutopilotNeedsYouTaskPlanner.RemoveReplyDraft(state, item.CommentId);
                    FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
                    if (_youtubeCommentsGrid is not null)
                        await RefreshYouTubeCommentsAsync(false);
                    await ReloadAsync("Reply sent and removed from the Autopilot queue.");
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(window, error.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        skip.Click += (_, _) =>
        {
            if (visibleTasks.Count == 0) return;
            var next = taskGrid.SelectedIndex + 1;
            taskGrid.SelectedIndex = next >= visibleTasks.Count ? 0 : next;
            taskGrid.ScrollIntoView(taskGrid.SelectedItem);
            status.Text = "Skipped for now. The task stays pending.";
        };
        close.Click += (_, _) => window.Close();
        showAll.Click += async (_, _) =>
        {
            filter = null;
            showAll.Visibility = Visibility.Collapsed;
            heading.Children.OfType<TextBlock>().First().Text = "Needs You task queue";
            await ReloadAsync("Showing all tasks that require your input.");
        };
        taskGrid.SelectionChanged += (_, _) => UpdateSelected();

        window.Content = new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.65),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };

        taskGrid.ItemsSource = visibleTasks;
        UpdateSummary();
        taskGrid.SelectedIndex = 0;
        status.Text = "Select a task and use the actions on the right. Nothing is marked complete until its real platform action succeeds or you explicitly confirm it.";

        try
        {
            window.ShowDialog();
        }
        finally
        {
            _autopilotTaskQueueWindow = null;
            await RefreshAutopilotHomeAsync();
        }
    }

    private static List<AutopilotNeedsYouTaskItem> ApplyTaskFilter(
        IEnumerable<AutopilotNeedsYouTaskItem> tasks,
        AutopilotNeedsYouTaskKind? filter) =>
        tasks
            .Where(task => filter is null || task.Kind == filter.Value)
            .ToList();

    private static string TaskFilterName(AutopilotNeedsYouTaskKind kind) => kind switch
    {
        AutopilotNeedsYouTaskKind.RelatedVideo => "Related video tasks",
        AutopilotNeedsYouTaskKind.InstagramPromo => "Instagram promo tasks",
        AutopilotNeedsYouTaskKind.ViewerReply => "Viewer reply tasks",
        _ => "Needs You tasks",
    };

    private void PruneLocallyHandledReplyDrafts()
    {
        if (_youtubeHandledCommentIds.Count == 0) return;
        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        if (!PruneLocallyHandledReplyDrafts(state)) return;
        FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
    }

    private bool PruneLocallyHandledReplyDrafts(FactburstFullAutopilotState state)
    {
        if (_youtubeHandledCommentIds.Count == 0) return false;
        var removed = state.ReplyDrafts.RemoveAll(draft =>
            _youtubeHandledCommentIds.Contains(draft.CommentId));
        return removed > 0;
    }

    private async Task PruneResolvedReplyDraftsFromYouTubeAsync(FactburstFullAutopilotState state)
    {
        PruneLocallyHandledReplyDrafts(state);
        if (state.ReplyDrafts.Count == 0) return;

        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthClientId.Length == 0 || settings.YouTubeOAuthRefreshToken.Length == 0)
            return;

        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(token);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel",
                settings.ApprovedYouTubeChannelId,
                channel.Id);
            var published = await _youtubeManagement.ListCommentsAsync(token, channel.Id, "published");
            var byId = published
                .Where(comment => !string.IsNullOrWhiteSpace(comment.Id))
                .GroupBy(comment => comment.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            state.ReplyDrafts.RemoveAll(draft =>
                byId.TryGetValue(draft.CommentId, out var comment) &&
                (comment.ReplyCount > 0 || comment.IsOwnComment));
        }
        catch (Exception error)
        {
            Debug.WriteLine("Autopilot reply queue cleanup skipped: " + error.Message);
        }
    }
}
