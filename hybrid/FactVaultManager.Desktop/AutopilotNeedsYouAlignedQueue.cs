using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public enum AutopilotAlignedTaskKind
{
    RelatedVideo,
    InstagramPromo,
    ViewerReply,
    PackagingRescue,
    ReleaseWarning,
}

public sealed record AutopilotAlignedTaskItem(
    AutopilotAlignedTaskKind Kind,
    string Key,
    int HistoryId,
    string Title,
    string Detail,
    string State,
    DateTimeOffset? DueAt,
    string ProjectFolder,
    string CommentId,
    string VideoId,
    string Draft,
    bool ActionReady)
{
    public string TypeDisplay => Kind switch
    {
        AutopilotAlignedTaskKind.RelatedVideo => "Related video",
        AutopilotAlignedTaskKind.InstagramPromo => "Instagram promo",
        AutopilotAlignedTaskKind.ViewerReply => "Viewer reply",
        AutopilotAlignedTaskKind.PackagingRescue => "Packaging rescue",
        AutopilotAlignedTaskKind.ReleaseWarning => "Release warning",
        _ => Kind.ToString(),
    };

    public string DueDisplay => DueAt is null
        ? "Now"
        : DueAt.Value.LocalDateTime.ToString("ddd dd MMM • HH:mm", CultureInfo.InvariantCulture);
}

public static class AutopilotNeedsYouAlignedPlanner
{
    public static IReadOnlyList<AutopilotAlignedTaskItem> Build(
        IEnumerable<ScheduledReleaseReadinessRow> readinessRows,
        FactburstFullAutopilotState state,
        IEnumerable<YouTubeGrowthSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(readinessRows);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshots);

        var rows = readinessRows.ToList();
        var tasks = new List<AutopilotAlignedTaskItem>();

        foreach (var row in rows
                     .Where(row => AutopilotHomePlanner.NeedsManualValue(row.RelatedVideo))
                     .OrderBy(row => row.PublishAt)
                     .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(row => row.HistoryId))
        {
            var ready = string.Equals(row.YouTubePromo, "Uploaded", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(row.RelatedVideo, "Needs setting", StringComparison.OrdinalIgnoreCase);
            var detail = row.RelatedVideo.Trim() switch
            {
                "Waiting" => "The promo Short is not uploaded yet. Keep this task visible so the Studio-only Related video step is not forgotten once the Short exists.",
                "Invalid full link" => "The full quiz YouTube link must be repaired before its promo Short can be linked as a Related video.",
                _ => "Open the matching promo Short in YouTube Studio, select the full quiz as its Related video, save it, then confirm the save in Factburst.",
            };
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.RelatedVideo,
                $"related:{row.HistoryId}",
                row.HistoryId,
                row.Quiz,
                detail,
                row.RelatedVideo,
                row.PublishAt,
                row.ProjectFolder,
                "",
                "",
                "",
                ready));
        }

        foreach (var row in rows
                     .Where(row => AutopilotHomePlanner.NeedsManualValue(row.InstagramPromo))
                     .OrderBy(row => row.PublishAt)
                     .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(row => row.HistoryId))
        {
            var ready = string.Equals(row.InstagramPromo, "Next day", StringComparison.OrdinalIgnoreCase);
            var detail = ready
                ? "The promo is prepared. Instagram does not support this app's future Reel scheduling flow, so approve and publish it when you are ready."
                : "The promo is not ready to publish yet. It remains visible so the manual Instagram step is not lost; finish the promo prerequisites first.";
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.InstagramPromo,
                $"instagram:{row.HistoryId}",
                row.HistoryId,
                row.Quiz,
                detail,
                row.InstagramPromo,
                AutopilotNeedsYouTaskPlanner.PromoDueAt(row.PublishAt),
                row.ProjectFolder,
                "",
                "",
                "",
                ready));
        }

        foreach (var draft in state.ReplyDrafts
                     .Where(draft => !string.IsNullOrWhiteSpace(draft.CommentId))
                     .GroupBy(draft => draft.CommentId, StringComparer.Ordinal)
                     .Select(group => group.OrderByDescending(value => value.CreatedAtUtc).First())
                     .OrderBy(value => value.CreatedAtUtc))
        {
            var author = string.IsNullOrWhiteSpace(draft.Author) ? "Viewer" : draft.Author.Trim();
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.ViewerReply,
                $"reply:{draft.CommentId}",
                0,
                $"Reply to {author}",
                draft.CommentText.Trim(),
                "Approval needed",
                null,
                "",
                draft.CommentId.Trim(),
                draft.VideoId.Trim(),
                draft.Draft.Trim(),
                true));
        }

        foreach (var snapshot in snapshots
                     .Where(snapshot => string.Equals(snapshot.Label, "Packaging rescue", StringComparison.OrdinalIgnoreCase) && snapshot.RescuePackagePrepared)
                     .GroupBy(snapshot => snapshot.HistoryId)
                     .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                     .OrderBy(snapshot => snapshot.CheckedAtUtc))
        {
            var category = string.IsNullOrWhiteSpace(snapshot.Category) ? "Quiz" : snapshot.Category.Trim();
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.PackagingRescue,
                $"rescue:{snapshot.HistoryId}",
                snapshot.HistoryId,
                $"Review packaging rescue • {category}",
                string.IsNullOrWhiteSpace(snapshot.Reason)
                    ? "A replacement title/thumbnail package is prepared. Review it before applying any live packaging change."
                    : snapshot.Reason.Trim(),
                "Review prepared package",
                null,
                "",
                "",
                snapshot.VideoId.Trim(),
                "",
                true));
        }

        foreach (var audit in state.PostReleaseAudits
                     .Where(record => NeedsReleaseWarning(record.Attention))
                     .GroupBy(record => record.HistoryId)
                     .Select(group => group.OrderByDescending(record => record.CheckedAtUtc).First())
                     .OrderBy(record => record.CheckedAtUtc))
        {
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.ReleaseWarning,
                $"warning:{audit.HistoryId}",
                audit.HistoryId,
                "Review release packaging warning",
                audit.Attention.Trim(),
                "Manual review",
                null,
                "",
                "",
                audit.VideoId.Trim(),
                "",
                true));
        }

        return tasks
            .OrderBy(task => task.Kind)
            .ThenBy(task => task.DueAt ?? DateTimeOffset.MinValue)
            .ThenBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int Count(IEnumerable<AutopilotAlignedTaskItem> tasks, AutopilotAlignedTaskKind kind) =>
        tasks.Count(task => task.Kind == kind);

    public static string Summary(IEnumerable<AutopilotAlignedTaskItem> tasks, int shown)
    {
        var list = tasks.ToList();
        var parts = new List<string>
        {
            $"{Math.Max(0, shown):N0} shown",
            $"{Count(list, AutopilotAlignedTaskKind.RelatedVideo):N0} Related video",
            $"{Count(list, AutopilotAlignedTaskKind.InstagramPromo):N0} Instagram",
            $"{Count(list, AutopilotAlignedTaskKind.ViewerReply):N0} Replies",
        };
        var rescues = Count(list, AutopilotAlignedTaskKind.PackagingRescue);
        var warnings = Count(list, AutopilotAlignedTaskKind.ReleaseWarning);
        if (rescues > 0) parts.Add($"{rescues:N0} Rescue");
        if (warnings > 0) parts.Add($"{warnings:N0} Warning");
        return string.Join(" • ", parts);
    }

    private static bool NeedsReleaseWarning(string? attention)
    {
        var text = (attention ?? "").Trim();
        return text.Contains("title differs", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("thumbnail", StringComparison.OrdinalIgnoreCase);
    }
}

public partial class MainShellWindow
{
    private const string AlignedTaskQueueButtonTag = "autopilot-needs-you-aligned-task-queue";
    private bool _alignedTaskQueueInitialized;
    private DispatcherTimer? _alignedTaskQueueGuardTimer;
    private Window? _alignedTaskQueueWindow;

    public void InitializeAutopilotNeedsYouAlignedQueue()
    {
        if (_alignedTaskQueueInitialized) return;
        _alignedTaskQueueInitialized = true;

        _autopilotTaskQueueGuardTimer?.Stop();
        RemoveHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(AutopilotTaskQueuePreviewMouseDown));
        PreviewKeyDown -= AutopilotTaskQueuePreviewKeyDown;

        AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(AlignedTaskQueuePreviewMouseDown),
            handledEventsToo: true);
        PreviewKeyDown += AlignedTaskQueuePreviewKeyDown;

        _alignedTaskQueueGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _alignedTaskQueueGuardTimer.Tick += (_, _) => EnsureAlignedTaskQueueButton();
        _alignedTaskQueueGuardTimer.Start();
        Closed += (_, _) => _alignedTaskQueueGuardTimer?.Stop();

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureAlignedTaskQueueButton));
    }

    private void EnsureAlignedTaskQueueButton()
    {
        if (_autopilotNeedsPanel is null) return;

        var legacy = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), AutopilotTaskQueueButtonTag, StringComparison.Ordinal));
        if (legacy is not null)
            _autopilotNeedsPanel.Children.Remove(legacy);

        var existing = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), AlignedTaskQueueButtonTag, StringComparison.Ordinal));
        if (existing is not null) return;

        var button = new Button
        {
            Content = "Manage task queue",
            Tag = AlignedTaskQueueButtonTag,
            MinWidth = 142,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 2),
            ToolTip = "Work through every item included in the Autopilot Needs You count.",
        };
        button.Click += async (_, _) => await ShowAlignedTaskQueueAsync(null);
        _autopilotNeedsPanel.Children.Insert(Math.Min(1, _autopilotNeedsPanel.Children.Count), button);
    }

    private void AlignedTaskQueuePreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        var button = FindAutopilotTaskButton(eventArgs.OriginalSource as DependencyObject);
        if (button is null || !TryAlignedFilter(button, out var filter)) return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAlignedTaskQueueAsync(filter)));
    }

    private void AlignedTaskQueuePreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space)) return;
        if (Keyboard.FocusedElement is not Button button || !TryAlignedFilter(button, out var filter)) return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAlignedTaskQueueAsync(filter)));
    }

    private static bool TryAlignedFilter(Button button, out AutopilotAlignedTaskKind? filter)
    {
        filter = null;
        var content = Convert.ToString(button.Content) ?? "";
        if (string.Equals(content, "Open tasks", StringComparison.Ordinal))
        {
            filter = AutopilotAlignedTaskKind.RelatedVideo;
            return true;
        }
        if (string.Equals(content, "Open Instagram", StringComparison.Ordinal))
        {
            filter = AutopilotAlignedTaskKind.InstagramPromo;
            return true;
        }
        if (string.Equals(content, "Review replies", StringComparison.Ordinal))
        {
            filter = AutopilotAlignedTaskKind.ViewerReply;
            return true;
        }
        return false;
    }

    private async Task ShowAlignedTaskQueueAsync(AutopilotAlignedTaskKind? filter)
    {
        if (_alignedTaskQueueWindow is not null)
        {
            _alignedTaskQueueWindow.Activate();
            return;
        }

        const string dialogTitle = "Autopilot • Needs You";
        EnsureScheduledReleaseReadinessPage();
        if (_scheduledReadinessGrid is not null)
            await RefreshScheduledReleaseReadinessAsync(false);

        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        await PruneResolvedReplyDraftsFromYouTubeAsync(state);
        FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
        var snapshots = LatestGrowthSnapshots();
        var allTasks = AutopilotNeedsYouAlignedPlanner.Build(_scheduledReadinessRows, state, snapshots).ToList();
        var visibleTasks = ApplyAlignedFilter(allTasks, filter);

        if (visibleTasks.Count == 0)
        {
            MessageBox.Show(
                this,
                filter is null ? "Nothing needs your attention right now." : "There are no pending tasks in this section.",
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
            Width = 1080,
            Height = 720,
            MinWidth = 920,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
        };
        _alignedTaskQueueWindow = window;

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
        var headingTitle = new TextBlock
        {
            Text = filter is null ? "Needs You task queue" : AlignedFilterName(filter.Value),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        };
        heading.Children.Add(headingTitle);
        heading.Children.Add(new TextBlock
        {
            Text = "This list uses the same task planner as the Autopilot counter. Waiting prerequisites stay visible, but publish/complete actions remain disabled until they are ready.",
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
        taskGrid.Columns.Add(TextColumn("Type", nameof(AutopilotAlignedTaskItem.TypeDisplay), 140));
        taskGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Task",
            Binding = new Binding(nameof(AutopilotAlignedTaskItem.Title)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        taskGrid.Columns.Add(TextColumn("State", nameof(AutopilotAlignedTaskItem.State), 145));
        taskGrid.Columns.Add(TextColumn("Due", nameof(AutopilotAlignedTaskItem.DueDisplay), 150));
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
        var next = ChecklistButton("Next", Color.FromRgb(255, 202, 45));
        var close = ChecklistButton("Close", Color.FromRgb(180, 190, 210));
        primary.MinWidth = 132;
        secondary.MinWidth = 116;
        complete.MinWidth = 116;
        next.MinWidth = 78;
        close.MinWidth = 78;
        Grid.SetColumn(primary, 1);
        Grid.SetColumn(secondary, 2);
        Grid.SetColumn(complete, 3);
        Grid.SetColumn(next, 4);
        Grid.SetColumn(close, 5);
        footer.Children.Add(primary);
        footer.Children.Add(secondary);
        footer.Children.Add(complete);
        footer.Children.Add(next);
        footer.Children.Add(close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        AutopilotAlignedTaskItem? Current() => taskGrid.SelectedItem as AutopilotAlignedTaskItem;

        void UpdateSummary() => summary.Text = AutopilotNeedsYouAlignedPlanner.Summary(allTasks, visibleTasks.Count);

        void UpdateSelected()
        {
            var item = Current();
            if (item is null)
            {
                detailTitle.Text = "Select a task";
                detailText.Text = "Choose an item above to see what is required.";
                replyBox.Visibility = Visibility.Collapsed;
                primary.IsEnabled = secondary.IsEnabled = complete.IsEnabled = false;
                return;
            }

            detailTitle.Text = item.Title;
            detailText.Text = item.Detail;
            replyBox.Visibility = item.Kind == AutopilotAlignedTaskKind.ViewerReply ? Visibility.Visible : Visibility.Collapsed;
            if (item.Kind == AutopilotAlignedTaskKind.ViewerReply)
                replyBox.Text = item.Draft;

            primary.Visibility = Visibility.Visible;
            secondary.Visibility = Visibility.Visible;
            complete.Visibility = Visibility.Visible;
            primary.IsEnabled = true;
            secondary.IsEnabled = true;
            complete.IsEnabled = item.ActionReady;

            switch (item.Kind)
            {
                case AutopilotAlignedTaskKind.RelatedVideo:
                    if (item.ActionReady)
                    {
                        primary.Content = "Copy title + Studio";
                        secondary.Content = "Open full quiz";
                        complete.Content = "Mark saved";
                    }
                    else
                    {
                        primary.Content = string.Equals(item.State, "Invalid full link", StringComparison.OrdinalIgnoreCase)
                            ? "Open Library"
                            : "Open Upload Manager";
                        secondary.Visibility = Visibility.Collapsed;
                        complete.Visibility = Visibility.Collapsed;
                    }
                    break;
                case AutopilotAlignedTaskKind.InstagramPromo:
                    if (item.ActionReady)
                    {
                        primary.Content = "Preview promo";
                        secondary.Content = "Open project folder";
                        complete.Content = "Publish now";
                    }
                    else
                    {
                        primary.Content = "Open project folder";
                        secondary.Visibility = Visibility.Collapsed;
                        complete.Visibility = Visibility.Collapsed;
                    }
                    break;
                case AutopilotAlignedTaskKind.ViewerReply:
                    primary.Content = "Open on YouTube";
                    secondary.Visibility = Visibility.Collapsed;
                    complete.Content = "Send reply";
                    break;
                case AutopilotAlignedTaskKind.PackagingRescue:
                    primary.Content = "Open Performance";
                    secondary.Visibility = Visibility.Collapsed;
                    complete.Visibility = Visibility.Collapsed;
                    break;
                case AutopilotAlignedTaskKind.ReleaseWarning:
                    primary.Content = "Open Performance";
                    secondary.Content = "Open Library";
                    complete.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        async Task ReloadAsync(string message)
        {
            if (_scheduledReadinessGrid is not null)
                await RefreshScheduledReleaseReadinessAsync(false);
            state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            PruneLocallyHandledReplyDrafts(state);
            snapshots = LatestGrowthSnapshots();
            allTasks = AutopilotNeedsYouAlignedPlanner.Build(_scheduledReadinessRows, state, snapshots).ToList();
            visibleTasks = ApplyAlignedFilter(allTasks, filter);
            taskGrid.ItemsSource = null;
            taskGrid.ItemsSource = visibleTasks;
            UpdateSummary();
            status.Text = message;
            if (visibleTasks.Count > 0)
                taskGrid.SelectedIndex = 0;
            else
            {
                detailTitle.Text = "All done";
                detailText.Text = "No tasks remain in this view.";
                replyBox.Visibility = Visibility.Collapsed;
                primary.IsEnabled = secondary.IsEnabled = complete.IsEnabled = false;
            }
            await RefreshAutopilotHomeAsync();
            SyncAutopilotNeedsYouCount();
        }

        async Task PublishInstagramAsync(AutopilotAlignedTaskItem item)
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
                var result = await _instagramReelUpload.UploadReelAsync(preflight.FacebookPageToken, video, caption);
                QuizPromoShortSocialPublicationStore.RecordInstagram(history.ProjectFolder, result, DateTimeOffset.Now);
                await ReloadAsync("Instagram promo published and removed from the queue.");
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
                switch (item.Kind)
                {
                    case AutopilotAlignedTaskKind.RelatedVideo:
                        if (!item.ActionReady)
                        {
                            window.Close();
                            NavigateLegacy(
                                string.Equals(item.State, "Invalid full link", StringComparison.OrdinalIgnoreCase) ? "Quiz History" : "Upload Manager",
                                string.Equals(item.State, "Invalid full link", StringComparison.OrdinalIgnoreCase) ? "Library" : "Advanced");
                            return;
                        }
                        if (!histories.TryGetValue(item.HistoryId, out var relatedHistory))
                            throw new InvalidOperationException("The quiz history record is missing.");
                        var promo = QuizPromoShortPublicationStore.LoadYouTube(relatedHistory.ProjectFolder)
                                    ?? throw new InvalidOperationException("The uploaded YouTube promo record is missing.");
                        _ = YouTubeVideoAnalyticsService.TryGetVideoId(relatedHistory.YouTubeUrl)
                            ?? throw new InvalidOperationException("The full quiz YouTube link is invalid.");
                        Clipboard.SetText(relatedHistory.UploadTitleDisplay);
                        OpenChecklistUrl(QuizPromoRelatedVideoLinks.StudioEditUrl(promo.VideoId), dialogTitle);
                        status.Text = "Full quiz title copied. In Studio choose Related video, select the matching quiz and click SAVE.";
                        break;

                    case AutopilotAlignedTaskKind.InstagramPromo:
                        if (!histories.TryGetValue(item.HistoryId, out var instagramHistory))
                            throw new InvalidOperationException("The quiz history record is missing.");
                        if (!item.ActionReady)
                        {
                            if (!Directory.Exists(instagramHistory.ProjectFolder))
                                throw new DirectoryNotFoundException("The quiz project folder is missing.");
                            Process.Start(new ProcessStartInfo(instagramHistory.ProjectFolder) { UseShellExecute = true });
                            status.Text = "Opened the project folder. Finish the promo prerequisites, then refresh the queue.";
                            break;
                        }
                        var video = QuizPromoShortPaths.FindExisting(instagramHistory.ProjectFolder)
                                    ?? throw new FileNotFoundException("The prepared promo video could not be found.");
                        Process.Start(new ProcessStartInfo(video) { UseShellExecute = true });
                        status.Text = "Opened the prepared Instagram promo for review.";
                        break;

                    case AutopilotAlignedTaskKind.ViewerReply:
                        Process.Start(new ProcessStartInfo(YouTubeManagementService.BuildCommentUrl(item.VideoId, item.CommentId)) { UseShellExecute = true });
                        status.Text = "Opened the viewer comment on YouTube.";
                        break;

                    case AutopilotAlignedTaskKind.PackagingRescue:
                    case AutopilotAlignedTaskKind.ReleaseWarning:
                        window.Close();
                        NavigateLegacy("YouTube Manager", "Performance");
                        break;
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
                if (item.Kind == AutopilotAlignedTaskKind.ReleaseWarning)
                {
                    window.Close();
                    NavigateLegacy("Quiz History", "Library");
                    return;
                }
                if (!histories.TryGetValue(item.HistoryId, out var history))
                    throw new InvalidOperationException("The quiz history record is missing.");
                if (item.Kind == AutopilotAlignedTaskKind.RelatedVideo)
                {
                    var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl)
                                      ?? throw new InvalidOperationException("The full quiz YouTube link is invalid.");
                    OpenChecklistUrl(QuizPromoRelatedVideoLinks.WatchUrl(longVideoId), dialogTitle);
                }
                else if (item.Kind == AutopilotAlignedTaskKind.InstagramPromo)
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
            if (item is null || !item.ActionReady) return;
            try
            {
                if (item.Kind == AutopilotAlignedTaskKind.RelatedVideo)
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
                    QuizPromoRelatedVideoStore.MarkSet(history.ProjectFolder, promo.VideoId, longVideoId, DateTimeOffset.UtcNow);
                    await ReloadAsync("Related video confirmed and removed from the queue.");
                    UpdatePromoRelatedVideoChecklistButtonLabel();
                }
                else if (item.Kind == AutopilotAlignedTaskKind.InstagramPromo)
                {
                    await PublishInstagramAsync(item);
                }
                else if (item.Kind == AutopilotAlignedTaskKind.ViewerReply)
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
                    SocialPublishingAccountGuard.EnsureMatches("YouTube channel", settings.ApprovedYouTubeChannelId, channel.Id);
                    await _youtubeManagement.ReplyAsync(token, item.CommentId, reply);
                    _youtubeHandledCommentIds.Add(item.CommentId);
                    state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
                    AutopilotNeedsYouTaskPlanner.RemoveReplyDraft(state, item.CommentId);
                    FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);
                    if (_youtubeCommentsGrid is not null)
                        await RefreshYouTubeCommentsAsync(false);
                    await ReloadAsync("Reply sent and removed from the queue.");
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(window, error.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        next.Click += (_, _) =>
        {
            if (visibleTasks.Count == 0) return;
            var index = taskGrid.SelectedIndex + 1;
            taskGrid.SelectedIndex = index >= visibleTasks.Count ? 0 : index;
            taskGrid.ScrollIntoView(taskGrid.SelectedItem);
            status.Text = "Skipped for now. The task stays pending.";
        };
        close.Click += (_, _) => window.Close();
        showAll.Click += async (_, _) =>
        {
            filter = null;
            showAll.Visibility = Visibility.Collapsed;
            headingTitle.Text = "Needs You task queue";
            await ReloadAsync("Showing every task included in the Autopilot Needs You count.");
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
        status.Text = "Select a task. Waiting items remain visible, while publish/complete actions only enable when the prerequisite state is ready.";

        try
        {
            window.ShowDialog();
        }
        finally
        {
            _alignedTaskQueueWindow = null;
            await RefreshAutopilotHomeAsync();
            SyncAutopilotNeedsYouCount();
        }
    }

    private List<YouTubeGrowthSnapshot> LatestGrowthSnapshots() =>
        YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
            .GroupBy(snapshot => snapshot.HistoryId)
            .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
            .ToList();

    private static List<AutopilotAlignedTaskItem> ApplyAlignedFilter(
        IEnumerable<AutopilotAlignedTaskItem> tasks,
        AutopilotAlignedTaskKind? filter) =>
        tasks.Where(task => filter is null || task.Kind == filter.Value).ToList();

    private static string AlignedFilterName(AutopilotAlignedTaskKind kind) => kind switch
    {
        AutopilotAlignedTaskKind.RelatedVideo => "Related video tasks",
        AutopilotAlignedTaskKind.InstagramPromo => "Instagram promo tasks",
        AutopilotAlignedTaskKind.ViewerReply => "Viewer reply tasks",
        AutopilotAlignedTaskKind.PackagingRescue => "Packaging rescue tasks",
        AutopilotAlignedTaskKind.ReleaseWarning => "Release warning tasks",
        _ => "Needs You tasks",
    };
}
