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
        IEnumerable<YouTubeGrowthSnapshot> snapshots,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(readinessRows);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshots);

        var current = now ?? DateTimeOffset.Now;
        var rows = readinessRows.ToList();
        var tasks = new List<AutopilotAlignedTaskItem>();

        foreach (var row in rows
                     .Where(row => IsPending(row.RelatedVideo, "Set"))
                     .OrderBy(row => row.PublishAt)
                     .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(row => row.HistoryId))
        {
            var ready = string.Equals(row.YouTubePromo, "Uploaded", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(row.RelatedVideo, "Needs setting", StringComparison.OrdinalIgnoreCase);
            var detail = row.RelatedVideo.Trim() switch
            {
                "Waiting" => "The promo Short is not uploaded yet. This stays visible so the Studio-only Related video step is not forgotten once the Short exists.",
                "Invalid full link" => "Repair the full quiz YouTube link before linking the promo Short to it.",
                _ => "Open the matching promo Short in YouTube Studio, select the full quiz as its Related video and save it.",
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

        foreach (var item in rows
                     .Where(row => string.Equals(row.InstagramPromo, "Next day", StringComparison.OrdinalIgnoreCase))
                     .Select(row => new
                     {
                         Row = row,
                         DueAt = AutopilotNeedsYouTaskPlanner.PromoDueAt(row.PublishAt),
                     })
                     .Where(item => item.DueAt <= current)
                     .OrderBy(item => item.DueAt)
                     .ThenBy(item => item.Row.Quiz, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Row.HistoryId))
        {
            var row = item.Row;
            tasks.Add(new AutopilotAlignedTaskItem(
                AutopilotAlignedTaskKind.InstagramPromo,
                $"instagram:{row.HistoryId}",
                row.HistoryId,
                row.Quiz,
                "The promo is prepared and its Instagram posting time has arrived. Approve and publish it now.",
                row.InstagramPromo,
                item.DueAt,
                row.ProjectFolder,
                "",
                "",
                "",
                true));
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
                    ? "A replacement title/thumbnail package is prepared for review."
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

    private static bool IsPending(string? value, params string[] additionalCompletedStates)
    {
        var text = (value ?? "").Trim();
        if (!AutopilotHomePlanner.NeedsManualValue(text)) return false;
        return !additionalCompletedStates.Any(completed =>
            string.Equals(text, completed, StringComparison.OrdinalIgnoreCase));
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
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureAlignedTaskQueueButton));
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
            ToolTip = "Show every item included in the Autopilot Needs You count.",
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
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(async () => await ShowAlignedTaskQueueAsync(filter)));
    }

    private void AlignedTaskQueuePreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space)) return;
        if (Keyboard.FocusedElement is not Button button || !TryAlignedFilter(button, out var filter)) return;
        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(async () => await ShowAlignedTaskQueueAsync(filter)));
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
        var allTasks = AutopilotNeedsYouAlignedPlanner.Build(
            _scheduledReadinessRows,
            state,
            LoadAlignedGrowthSnapshots()).ToList();
        var visibleTasks = FilterAlignedTasks(allTasks, filter);

        if (visibleTasks.Count == 0)
        {
            MessageBox.Show(this, "Nothing is pending in this section.", dialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAutopilotHomeAsync();
            return;
        }

        var window = new Window
        {
            Title = dialogTitle,
            Owner = this,
            Width = 1080,
            Height = 700,
            MinWidth = 900,
            MinHeight = 600,
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
            Text = "This is the same task list used by the Autopilot counter. Only work that is ready for you now appears here.",
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
            Text = AutopilotNeedsYouAlignedPlanner.Summary(allTasks, visibleTasks.Count),
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
        taskGrid.Columns.Add(TextColumn("State", nameof(AutopilotAlignedTaskItem.State), 150));
        taskGrid.Columns.Add(TextColumn("Due", nameof(AutopilotAlignedTaskItem.DueDisplay), 160));
        var taskCard = ManagerCard(taskGrid);
        Grid.SetRow(taskCard, 2);
        root.Children.Add(taskCard);

        var details = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 78)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 111, 214)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 12, 0, 0),
        };
        var detailStack = new StackPanel();
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
        detailStack.Children.Add(detailTitle);
        detailStack.Children.Add(detailText);
        details.Child = detailStack;
        Grid.SetRow(details, 3);
        root.Children.Add(details);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 4; index++)
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new TextBlock
        {
            Text = "Select a task. Work task opens the existing action flow when the item is ready.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        footer.Children.Add(status);
        var work = ChecklistButton("Work task", Color.FromRgb(0, 204, 255));
        var source = ChecklistButton("Open source", Color.FromRgb(88, 188, 255));
        var next = ChecklistButton("Next", Color.FromRgb(255, 202, 45));
        var close = ChecklistButton("Close", Color.FromRgb(180, 190, 210));
        work.MinWidth = 108;
        source.MinWidth = 108;
        next.MinWidth = 78;
        close.MinWidth = 78;
        Grid.SetColumn(work, 1);
        Grid.SetColumn(source, 2);
        Grid.SetColumn(next, 3);
        Grid.SetColumn(close, 4);
        footer.Children.Add(work);
        footer.Children.Add(source);
        footer.Children.Add(next);
        footer.Children.Add(close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        AutopilotAlignedTaskItem? Current() => taskGrid.SelectedItem as AutopilotAlignedTaskItem;

        void UpdateSelected()
        {
            var item = Current();
            if (item is null)
            {
                detailTitle.Text = "Select a task";
                detailText.Text = "Choose an item above to see what is required.";
                work.IsEnabled = source.IsEnabled = false;
                return;
            }
            detailTitle.Text = item.Title;
            detailText.Text = item.Detail;
            work.IsEnabled = item.ActionReady;
            source.IsEnabled = true;
            work.Content = item.Kind switch
            {
                AutopilotAlignedTaskKind.PackagingRescue => "Review rescue",
                AutopilotAlignedTaskKind.ReleaseWarning => "Review warning",
                _ => item.ActionReady ? "Work task" : "Waiting",
            };
        }

        async Task OpenExistingWorkflowAsync(AutopilotAlignedTaskItem item)
        {
            if (!item.ActionReady) return;
            window.Close();
            switch (item.Kind)
            {
                case AutopilotAlignedTaskKind.RelatedVideo:
                    await ShowAutopilotNeedsYouQueueAsync(AutopilotNeedsYouTaskKind.RelatedVideo);
                    break;
                case AutopilotAlignedTaskKind.InstagramPromo:
                    await ShowAutopilotNeedsYouQueueAsync(AutopilotNeedsYouTaskKind.InstagramPromo);
                    break;
                case AutopilotAlignedTaskKind.ViewerReply:
                    await ShowAutopilotNeedsYouQueueAsync(AutopilotNeedsYouTaskKind.ViewerReply);
                    break;
                case AutopilotAlignedTaskKind.PackagingRescue:
                case AutopilotAlignedTaskKind.ReleaseWarning:
                    NavigateLegacy("YouTube Manager", "Performance");
                    break;
            }
        }

        work.Click += async (_, _) =>
        {
            var item = Current();
            if (item is not null)
                await OpenExistingWorkflowAsync(item);
        };

        source.Click += (_, _) =>
        {
            var item = Current();
            if (item is null) return;
            try
            {
                switch (item.Kind)
                {
                    case AutopilotAlignedTaskKind.RelatedVideo:
                        window.Close();
                        NavigateLegacy(
                            string.Equals(item.State, "Invalid full link", StringComparison.OrdinalIgnoreCase) ? "Quiz History" : "Upload Manager",
                            string.Equals(item.State, "Invalid full link", StringComparison.OrdinalIgnoreCase) ? "Library" : "Advanced");
                        break;
                    case AutopilotAlignedTaskKind.InstagramPromo:
                        if (Directory.Exists(item.ProjectFolder))
                            Process.Start(new ProcessStartInfo(item.ProjectFolder) { UseShellExecute = true });
                        else
                        {
                            window.Close();
                            NavigateLegacy("Instagram Manager", "Advanced");
                        }
                        break;
                    case AutopilotAlignedTaskKind.ViewerReply:
                        if (!string.IsNullOrWhiteSpace(item.VideoId) && !string.IsNullOrWhiteSpace(item.CommentId))
                            Process.Start(new ProcessStartInfo(YouTubeManagementService.BuildCommentUrl(item.VideoId, item.CommentId)) { UseShellExecute = true });
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

        next.Click += (_, _) =>
        {
            if (visibleTasks.Count == 0) return;
            var index = taskGrid.SelectedIndex + 1;
            taskGrid.SelectedIndex = index >= visibleTasks.Count ? 0 : index;
            taskGrid.ScrollIntoView(taskGrid.SelectedItem);
            status.Text = "Skipped for now. The task stays pending.";
        };
        close.Click += (_, _) => window.Close();
        showAll.Click += (_, _) =>
        {
            filter = null;
            visibleTasks = FilterAlignedTasks(allTasks, null);
            taskGrid.ItemsSource = null;
            taskGrid.ItemsSource = visibleTasks;
            headingTitle.Text = "Needs You task queue";
            showAll.Visibility = Visibility.Collapsed;
            summary.Text = AutopilotNeedsYouAlignedPlanner.Summary(allTasks, visibleTasks.Count);
            taskGrid.SelectedIndex = visibleTasks.Count > 0 ? 0 : -1;
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
        taskGrid.SelectedIndex = 0;

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

    private List<YouTubeGrowthSnapshot> LoadAlignedGrowthSnapshots() =>
        YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
            .GroupBy(snapshot => snapshot.HistoryId)
            .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
            .ToList();

    private static List<AutopilotAlignedTaskItem> FilterAlignedTasks(
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
