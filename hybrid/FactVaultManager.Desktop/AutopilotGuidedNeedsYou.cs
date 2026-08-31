using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _autopilotGuidedNeedsYouInitialized;
    private static bool _autopilotGuidedWindowHandlerRegistered;

    public void InitializeAutopilotGuidedNeedsYou()
    {
        if (_autopilotGuidedNeedsYouInitialized)
            return;

        _autopilotGuidedNeedsYouInitialized = true;

        // The aligned queue remains the source of truth for the Needs You count, but the normal
        // user path now goes directly to one guided task instead of opening an intermediate list.
        RemoveHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(AlignedTaskQueuePreviewMouseDown));
        PreviewKeyDown -= AlignedTaskQueuePreviewKeyDown;

        AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(GuidedNeedsYouPreviewMouseDown),
            handledEventsToo: true);
        PreviewKeyDown += GuidedNeedsYouPreviewKeyDown;

        if (!_autopilotGuidedWindowHandlerRegistered)
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(GuidedNeedsYouWindow_Loaded),
                handledEventsToo: true);
            _autopilotGuidedWindowHandlerRegistered = true;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureGuidedNeedsYouButton));
    }

    private void EnsureGuidedNeedsYouButton()
    {
        if (_autopilotNeedsPanel is null)
            return;

        var existing = _autopilotNeedsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                AlignedTaskQueueButtonTag,
                StringComparison.Ordinal));

        if (existing is not null && string.Equals(existing.Content?.ToString(), "Start next task", StringComparison.Ordinal))
            return;

        var insertIndex = existing is null
            ? Math.Min(1, _autopilotNeedsPanel.Children.Count)
            : _autopilotNeedsPanel.Children.IndexOf(existing);
        if (existing is not null)
            _autopilotNeedsPanel.Children.Remove(existing);

        var button = new Button
        {
            Content = "Start next task",
            Tag = AlignedTaskQueueButtonTag,
            MinWidth = 142,
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 2),
            ToolTip = "Work through Autopilot jobs one at a time. Factburst advances to the next task after each completed action.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(0, 204, 255));
        button.Click += async (_, _) => await StartNextGuidedAutopilotTaskAsync();
        _autopilotNeedsPanel.Children.Insert(Math.Max(0, insertIndex), button);
    }

    private void GuidedNeedsYouPreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
            return;
        var button = FindAutopilotTaskButton(eventArgs.OriginalSource as DependencyObject);
        if (button is null || !TryGuidedTaskFilter(button, out var filter))
            return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAutopilotNeedsYouQueueAsync(filter)));
    }

    private void GuidedNeedsYouPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space))
            return;
        if (Keyboard.FocusedElement is not Button button || !TryGuidedTaskFilter(button, out var filter))
            return;

        eventArgs.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(async () => await ShowAutopilotNeedsYouQueueAsync(filter)));
    }

    private static bool TryGuidedTaskFilter(Button button, out AutopilotNeedsYouTaskKind? filter)
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

    private async Task StartNextGuidedAutopilotTaskAsync()
    {
        const string title = "Autopilot • Next action";
        EnsureScheduledReleaseReadinessPage();
        if (_scheduledReadinessGrid is not null)
            await RefreshScheduledReleaseReadinessAsync(false);

        var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
        await PruneResolvedReplyDraftsFromYouTubeAsync(state);
        FactburstFullAutopilotStateStore.Save(_data.SettingsPath, state);

        var tasks = AutopilotNeedsYouAlignedPlanner.Build(
                _scheduledReadinessRows,
                state,
                LoadAlignedGrowthSnapshots())
            .Where(task => task.ActionReady)
            .ToList();

        var next = tasks.FirstOrDefault();
        if (next is null)
        {
            MessageBox.Show(
                this,
                "Nothing needs your action right now. Autopilot will keep working in the background.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await RefreshAutopilotHomeAsync();
            return;
        }

        switch (next.Kind)
        {
            case AutopilotAlignedTaskKind.RelatedVideo:
            case AutopilotAlignedTaskKind.InstagramPromo:
            case AutopilotAlignedTaskKind.ViewerReply:
                // Show the combined human-action queue as a one-task-at-a-time wizard. This lets a
                // user finish Related video, Instagram and reply work without returning to Autopilot.
                await ShowAutopilotNeedsYouQueueAsync(null);
                break;

            case AutopilotAlignedTaskKind.PackagingRescue:
            case AutopilotAlignedTaskKind.ReleaseWarning:
                MessageBox.Show(
                    this,
                    "The next task is a YouTube packaging review. Factburst will open the exact Performance area for it now.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                NavigateLegacy("YouTube Manager", "Performance");
                break;
        }
    }

    private static void GuidedNeedsYouWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window { Title: "Autopilot • Needs You", Owner: MainShellWindow owner } window)
            return;

        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => owner.ApplyGuidedNeedsYouWindow(window)));
    }

    private void ApplyGuidedNeedsYouWindow(Window window)
    {
        if (!window.IsVisible || window.Content is not Border { Child: Grid root } || root.RowDefinitions.Count < 5)
            return;

        window.Width = 780;
        window.Height = 525;
        window.MinWidth = 700;
        window.MinHeight = 470;

        var headingGrid = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 0);
        var heading = headingGrid?.Children.OfType<StackPanel>().FirstOrDefault();
        var headingText = heading?.Children.OfType<TextBlock>().ToList() ?? [];
        if (headingText.Count > 0)
            headingText[0].Text = "Autopilot • Next action";
        if (headingText.Count > 1)
            headingText[1].Text = "One task at a time. Complete the step below and Factburst automatically moves you to the next job.";

        var showAll = headingGrid?.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Show all tasks", StringComparison.Ordinal));
        if (showAll is not null)
            showAll.Visibility = Visibility.Collapsed;

        var taskCard = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 2);
        var taskGrid = FindGuidedDescendant<DataGrid>(taskCard);
        if (taskCard is null || taskGrid is null)
            return;

        taskCard.Visibility = Visibility.Collapsed;
        root.RowDefinitions[2].Height = new GridLength(0);

        var summary = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => Grid.GetRow(text) == 1);
        var detailCard = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 3);
        var detailStack = detailCard?.Child as StackPanel;
        var detailTitle = detailStack?.Children.OfType<TextBlock>().FirstOrDefault();
        var footer = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 4);
        var status = footer?.Children.OfType<TextBlock>().FirstOrDefault();
        var buttons = footer?.Children.OfType<Button>().ToList() ?? [];

        void UpdateGuidedStep()
        {
            var count = taskGrid.Items.Count;
            var index = taskGrid.SelectedIndex;
            if (count <= 0 || index < 0)
            {
                if (summary is not null)
                    summary.Text = "All caught up";
                if (status is not null)
                    status.Text = "Nothing else needs you right now. You can close this window.";
                return;
            }

            if (summary is not null)
                summary.Text = $"Step {index + 1} of {count}";

            var selected = taskGrid.SelectedItem as AutopilotNeedsYouTaskItem;
            if (selected is not null && detailTitle is not null)
                detailTitle.Text = $"Step {index + 1}: {selected.Title}";

            var primary = buttons.FirstOrDefault(button =>
                button.Content?.ToString() is "Copy title + Studio" or "Preview promo" or "Open on YouTube" or "Open");
            var complete = buttons.FirstOrDefault(button =>
                button.Content?.ToString() is "Mark saved" or "Publish now" or "Send reply" or "Complete");
            var secondary = buttons.FirstOrDefault(button =>
                button.Content?.ToString() is "Open full quiz" or "Open project folder" or "Open item");
            var skip = buttons.FirstOrDefault(button => button.Content?.ToString() is "Next" or "Skip for now");
            var close = buttons.FirstOrDefault(button => button.Content?.ToString() is "Close" or "Done");

            if (secondary is not null)
                secondary.Visibility = Visibility.Collapsed;
            if (skip is not null)
                skip.Content = "Skip for now";
            if (close is not null)
                close.Content = "Done";

            if (selected is null)
                return;

            switch (selected.Kind)
            {
                case AutopilotNeedsYouTaskKind.RelatedVideo:
                    if (primary is not null) primary.Content = "1. Open Studio";
                    if (complete is not null) complete.Content = "2. I saved it";
                    if (status is not null)
                        status.Text = "1. Open Studio. 2. Choose the matching full quiz as Related video and SAVE. 3. Return here and click “I saved it”.";
                    break;
                case AutopilotNeedsYouTaskKind.InstagramPromo:
                    if (primary is not null) primary.Content = "1. Preview";
                    if (complete is not null) complete.Content = "2. Publish";
                    if (status is not null)
                        status.Text = "1. Preview the prepared promo. 2. If it looks right, click Publish. Factburst records it and advances automatically.";
                    break;
                case AutopilotNeedsYouTaskKind.ViewerReply:
                    if (primary is not null) primary.Content = "View comment";
                    if (complete is not null) complete.Content = "Send reply";
                    if (status is not null)
                        status.Text = "Edit the suggested reply if needed, then click Send reply. Factburst removes the task after YouTube confirms it.";
                    break;
            }
        }

        taskGrid.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(UpdateGuidedStep));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(UpdateGuidedStep));
    }

    private static T? FindGuidedDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
            return null;
        if (root is T typed)
            return typed;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var found = FindGuidedDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (found is not null)
                return found;
        }
        return null;
    }
}
