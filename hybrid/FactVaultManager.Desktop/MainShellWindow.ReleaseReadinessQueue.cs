using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ReleaseReadinessQueueButtonTag = "release-readiness-work-queue";
    private const string ReleaseReadinessMoreActionsButtonTag = "release-readiness-more-actions";
    private static readonly bool ReleaseReadinessQueueRegistered = RegisterReleaseReadinessQueue();

    private bool _releaseReadinessQueueInitialized;
    private bool _releaseReadinessQueueRunning;
    private bool _releaseReadinessQueueGridHooked;
    private int _releaseReadinessQueueUiAttempts;
    private Button? _releaseReadinessQueueButton;
    private Button? _releaseReadinessMoreActionsButton;
    private TextBlock? _releaseReadinessQueueSummary;

    private static bool RegisterReleaseReadinessQueue()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ReleaseReadinessQueueWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void ReleaseReadinessQueueWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MainShellWindow window)
            window.InitializeReleaseReadinessQueueForApp();
    }

    private void InitializeReleaseReadinessQueueForApp()
    {
        if (_releaseReadinessQueueInitialized) return;
        _releaseReadinessQueueInitialized = true;

        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureReleaseReadinessQueueUi));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureReleaseReadinessQueueUi));
    }

    private void EnsureReleaseReadinessQueueUi()
    {
        if (_scheduledReadinessGrid is null ||
            _scheduledReadinessOpenButton?.Parent is not StackPanel actions)
        {
            RetryReleaseReadinessQueueUi();
            return;
        }

        if (!_releaseReadinessQueueGridHooked)
        {
            _releaseReadinessQueueGridHooked = true;
            _scheduledReadinessGrid.SelectionChanged += (_, _) => UpdateReleaseReadinessQueueUi();
        }

        if (_releaseReadinessQueueButton?.Parent is null)
        {
            _releaseReadinessQueueButton = new Button
            {
                Content = "Get schedule ready",
                Tag = ReleaseReadinessQueueButtonTag,
                MinWidth = 166,
                MinHeight = 40,
                ToolTip = "Work through every future scheduled quiz in release order. Safe batch tasks are grouped automatically; YouTube Studio and social tasks still wait for your confirmation.",
            };
            StyleQuizHistoryButton(_releaseReadinessQueueButton, Color.FromRgb(70, 235, 115));
            _releaseReadinessQueueButton.Click += async (_, _) =>
                await WorkThroughReleaseReadinessQueueAsync(_releaseReadinessQueueButton);

            var refresh = actions.Children.OfType<Button>()
                .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "Refresh", StringComparison.Ordinal));
            var refreshIndex = refresh is null ? actions.Children.Count : actions.Children.IndexOf(refresh);
            actions.Children.Insert(Math.Max(0, refreshIndex), _releaseReadinessQueueButton);
        }

        if (_releaseReadinessMoreActionsButton?.Parent is null)
        {
            _releaseReadinessMoreActionsButton = BuildReleaseReadinessMoreActionsButton();
            var refresh = actions.Children.OfType<Button>()
                .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "Refresh", StringComparison.Ordinal));
            var refreshIndex = refresh is null ? actions.Children.Count : actions.Children.IndexOf(refresh);
            actions.Children.Insert(Math.Max(0, refreshIndex), _releaseReadinessMoreActionsButton);
        }

        if (_releaseReadinessQueueSummary?.Parent is null && actions.Parent is Grid toolbar)
        {
            _releaseReadinessQueueSummary = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 12, 0),
                MaxWidth = 430,
            };
            Grid.SetColumn(_releaseReadinessQueueSummary, 1);
            toolbar.Children.Add(_releaseReadinessQueueSummary);
        }

        HideSecondaryReleaseReadinessButtons(actions);
        UpdateReleaseReadinessQueueUi();
        RetryReleaseReadinessQueueUi(force: true);
    }

    private Button BuildReleaseReadinessMoreActionsButton()
    {
        var button = new Button
        {
            Content = "More actions ▾",
            Tag = ReleaseReadinessMoreActionsButtonTag,
            MinWidth = 118,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Less common Release Readiness actions.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(170, 185, 220));

        var menu = new ContextMenu();
        var fixSelected = new MenuItem { Header = "Fix selected quiz" };
        fixSelected.Click += async (_, _) => await FixSelectedScheduledReadinessAsync();
        menu.Items.Add(fixSelected);

        var openSelected = new MenuItem { Header = "Open selected quiz" };
        openSelected.Click += (_, _) => OpenScheduledReadinessInUploadManager();
        menu.Items.Add(openSelected);

        var automatic = new MenuItem { Header = "Fix automatic issues" };
        automatic.Click += async (_, _) =>
            await CreateMissingScheduledTrackingLinksAsync(new Button { Content = "Fix automatic issues" });
        menu.Items.Add(automatic);

        menu.Items.Add(new Separator());
        var website = new MenuItem { Header = "Prepare website" };
        website.Click += async (_, _) =>
            await PrepareScheduledWebsiteQuizzesAsync(new Button { Content = "Prepare website" });
        menu.Items.Add(website);

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            if (button.ContextMenu is null) return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private void HideSecondaryReleaseReadinessButtons(StackPanel actions)
    {
        foreach (var button in actions.Children.OfType<Button>())
        {
            var text = Convert.ToString(button.Content);
            if (string.Equals(text, "Fix automatic issues", StringComparison.Ordinal) ||
                string.Equals(text, "Create missing tracking links", StringComparison.Ordinal) ||
                string.Equals(text, "Fix selected", StringComparison.Ordinal) ||
                string.Equals(text, "Open selected quiz", StringComparison.Ordinal))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }

        if (_scheduledWebsitePublishingButton is not null)
            _scheduledWebsitePublishingButton.Visibility = Visibility.Collapsed;
        if (_scheduledRelatedVideoGuideButton is not null)
            _scheduledRelatedVideoGuideButton.Visibility = Visibility.Collapsed;
        if (_scheduledPromoBatchButton is not null)
            _scheduledPromoBatchButton.Visibility = Visibility.Collapsed;
        if (_scheduledPromoPublishingButton is not null)
            _scheduledPromoPublishingButton.Visibility = Visibility.Collapsed;
    }

    private void RetryReleaseReadinessQueueUi(bool force = false)
    {
        if (!force && ++_releaseReadinessQueueUiAttempts >= 40) return;
        if (force && _releaseReadinessQueueUiAttempts >= 46) return;
        if (force) _releaseReadinessQueueUiAttempts++;

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(force ? 250 : 125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_scheduledReadinessOpenButton?.Parent is StackPanel actions)
            {
                HideSecondaryReleaseReadinessButtons(actions);
                UpdateReleaseReadinessQueueUi();
            }
            else
            {
                EnsureReleaseReadinessQueueUi();
            }
        };
        timer.Start();
    }

    private void UpdateReleaseReadinessQueueUi()
    {
        if (_releaseReadinessQueueButton is null || _scheduledReadinessGrid is null) return;

        var rows = (_scheduledReadinessGrid.ItemsSource as IEnumerable<ScheduledReleaseReadinessRow>)?
            .Where(row => row.ReadyCount < row.TotalChecks)
            .OrderBy(row => row.PublishAt)
            .ThenBy(row => row.HistoryId)
            .ToList() ?? new List<ScheduledReleaseReadinessRow>();

        _releaseReadinessQueueButton.Content = _releaseReadinessQueueRunning
            ? "Working through schedule..."
            : rows.Count == 0
                ? "Schedule ready"
                : "Get schedule ready";
        _releaseReadinessQueueButton.IsEnabled = !_releaseReadinessQueueRunning && rows.Count > 0;

        if (_releaseReadinessQueueSummary is not null)
            _releaseReadinessQueueSummary.Text = rows.Count == 0
                ? "No outstanding release tasks in this view"
                : "Next up: " + BuildReleaseReadinessTaskSummary(rows);
    }

    private static string BuildReleaseReadinessTaskSummary(IReadOnlyList<ScheduledReleaseReadinessRow> rows)
    {
        var groups = rows
            .GroupBy(row => row.NextAction, StringComparer.Ordinal)
            .OrderBy(group => group.Min(row => row.PublishAt))
            .Select(group => $"{group.Count():N0} {ReleaseReadinessActionLabel(group.Key, group.Count())}")
            .Take(3)
            .ToList();
        return string.Join(" • ", groups);
    }

    private static string ReleaseReadinessActionLabel(string action, int count) => action switch
    {
        "Set Related video" => count == 1 ? "Related video" : "Related videos",
        "Create promo Short" => count == 1 ? "Promo Short" : "Promo Shorts",
        "Create tracking link" => count == 1 ? "tracking link" : "tracking links",
        "Schedule promo" => count == 1 ? "promo to schedule" : "promos to schedule",
        "Create YouTube package" => count == 1 ? "YouTube package" : "YouTube packages",
        "Publish Instagram promo" => count == 1 ? "Instagram promo" : "Instagram promos",
        "Prepare first comment" => count == 1 ? "first comment" : "first comments",
        _ => action,
    };

    private async Task WorkThroughReleaseReadinessQueueAsync(Button sourceButton)
    {
        if (_releaseReadinessQueueRunning) return;
        _releaseReadinessQueueRunning = true;
        UpdateReleaseReadinessQueueUi();
        sourceButton.IsEnabled = false;

        try
        {
            if (_scheduledReadinessHorizon is not null && _scheduledReadinessHorizon.SelectedIndex != 2)
                _scheduledReadinessHorizon.SelectedIndex = 2;
            if (_scheduledReadinessView is not null && _scheduledReadinessView.SelectedIndex != 0)
                _scheduledReadinessView.SelectedIndex = 0;

            await WaitForScheduledReadinessRefreshAsync();
            await RefreshScheduledReleaseReadinessAsync(false);
            await WaitForScheduledReadinessRefreshAsync();

            var pass = 0;
            while (pass++ < 80)
            {
                var pending = _scheduledReadinessRows
                    .Where(row => row.ReadyCount < row.TotalChecks)
                    .OrderBy(row => row.PublishAt)
                    .ThenBy(row => row.HistoryId)
                    .ToList();

                if (pending.Count == 0)
                {
                    SetScheduledReadinessStatus("All future scheduled quizzes are release-ready.");
                    MessageBox.Show(
                        this,
                        "All future scheduled quizzes are release-ready.",
                        "Release Readiness",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var first = pending[0];
                var action = first.NextAction;
                var sameAction = pending.Where(row => string.Equals(row.NextAction, action, StringComparison.Ordinal)).ToList();
                var beforeIds = sameAction.Select(row => row.HistoryId).ToHashSet();
                SetScheduledReadinessStatus(
                    $"Next: {action} • {sameAction.Count:N0} matching task(s) • earliest {first.PublishAtDisplay}");

                var stopAfterAction = false;
                switch (action)
                {
                    case "Create promo Short":
                        await RunReleaseReadinessBatchRowsAsync(
                            sameAction,
                            "Prepare missing promos",
                            PrepareMissingScheduledPromosAsync);
                        break;

                    case "Create tracking link":
                        await CreateMissingScheduledTrackingLinksAsync(
                            new Button { Content = "Create tracking links" });
                        break;

                    case "Schedule promo":
                        await RunReleaseReadinessBatchRowsAsync(
                            sameAction,
                            "Schedule promos",
                            ScheduleMissingPromosAsync);
                        break;

                    case "Set Related video":
                        await RunReleaseReadinessBatchRowsAsync(
                            sameAction,
                            "Related video setup",
                            ShowScheduledRelatedVideoGuideAsync);
                        break;

                    case "Check tracker connection":
                    case "Configure Link Tracker":
                        await FixScheduledReadinessRowAsync(first);
                        stopAfterAction = true;
                        break;

                    default:
                        await FixScheduledReadinessRowAsync(first);
                        break;
                }

                await WaitForScheduledReadinessRefreshAsync();
                await RefreshScheduledReleaseReadinessAsync(false);
                await WaitForScheduledReadinessRefreshAsync();
                UpdateReleaseReadinessQueueUi();

                if (stopAfterAction)
                {
                    SetScheduledReadinessStatus(
                        "Schedule workflow paused while the required settings are updated. Click Get schedule ready again when that is done.");
                    return;
                }

                var remainingSameAction = _scheduledReadinessRows.Count(row =>
                    beforeIds.Contains(row.HistoryId) &&
                    row.ReadyCount < row.TotalChecks &&
                    string.Equals(row.NextAction, action, StringComparison.Ordinal));
                if (remainingSameAction >= beforeIds.Count)
                {
                    SetScheduledReadinessStatus(
                        $"Schedule workflow paused at “{action}” because nothing changed. Complete or confirm that step, then click Get schedule ready again.");
                    return;
                }
            }

            SetScheduledReadinessStatus(
                "Schedule workflow stopped after too many passes. Refresh Release Readiness and continue from the next action shown.");
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus("Schedule workflow stopped: " + error.Message);
            MessageBox.Show(this, error.Message, "Get Schedule Ready",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _releaseReadinessQueueRunning = false;
            UpdateReleaseReadinessQueueUi();
        }
    }

    private async Task RunReleaseReadinessBatchRowsAsync(
        IReadOnlyList<ScheduledReleaseReadinessRow> rows,
        string buttonText,
        Func<Button, Task> action)
    {
        if (_scheduledReadinessGrid is null || rows.Count == 0) return;

        var sourceButton = new Button { Content = buttonText };
        _scheduledReadinessGrid.ItemsSource = rows;
        try
        {
            await action(sourceButton);
        }
        finally
        {
            ApplyScheduledReadinessView();
        }
    }

    private async Task WaitForScheduledReadinessRefreshAsync()
    {
        var attempts = 0;
        while (_scheduledReadinessRefreshing && attempts++ < 200)
            await Task.Delay(50);
    }
}
