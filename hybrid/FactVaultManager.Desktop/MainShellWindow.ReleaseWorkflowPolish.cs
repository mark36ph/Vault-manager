using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ReleaseFixSelectedButtonTag = "release-readiness-fix-selected";
    private const string ReleaseAutoFixButtonTag = "release-readiness-auto-fix";
    private bool _releaseWorkflowPolishInitialized;
    private int _releaseWorkflowPolishAttempts;
    private bool _releaseReadinessActionsPolished;
    private bool _uploadManagerPolished;
    private Button? _scheduledReadinessFixButton;

    public void InitializeReleaseWorkflowPolishForApp()
    {
        if (_releaseWorkflowPolishInitialized) return;
        _releaseWorkflowPolishInitialized = true;

        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureReleaseWorkflowPolish));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureReleaseWorkflowPolish));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureReleaseWorkflowPolish));
    }

    private void EnsureReleaseWorkflowPolish()
    {
        var readinessReady = EnsureReleaseReadinessActions();
        var uploadManagerReady = EnsureSimplifiedUploadManager();
        if (readinessReady && uploadManagerReady) return;
        if (++_releaseWorkflowPolishAttempts >= 50) return;

        var retry = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            EnsureReleaseWorkflowPolish();
        };
        retry.Start();
    }

    private bool EnsureReleaseReadinessActions()
    {
        if (_releaseReadinessActionsPolished) return true;
        if (_scheduledReadinessGrid is null ||
            _scheduledReadinessOpenButton?.Parent is not StackPanel actions)
        {
            return false;
        }

        var autoFix = actions.Children.OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Create missing tracking links",
                StringComparison.Ordinal));
        if (autoFix is not null)
        {
            autoFix.Content = "Fix automatic issues";
            autoFix.Tag = ReleaseAutoFixButtonTag;
            autoFix.MinWidth = 158;
            autoFix.ToolTip =
                "Safely fix release tasks the app can complete without review. Currently this creates missing tracking links; uploads, comments and Related video still require your confirmation.";
        }

        _scheduledReadinessFixButton = actions.Children.OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                ReleaseFixSelectedButtonTag,
                StringComparison.Ordinal));
        if (_scheduledReadinessFixButton is null)
        {
            _scheduledReadinessFixButton = new Button
            {
                Content = "Fix selected",
                Tag = ReleaseFixSelectedButtonTag,
                MinWidth = 118,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = _scheduledReadinessGrid.SelectedItem is ScheduledReleaseReadinessRow,
                ToolTip = "Open the exact workflow needed for the selected quiz's next release task.",
            };
            StyleQuizHistoryButton(_scheduledReadinessFixButton, Color.FromRgb(70, 235, 115));
            _scheduledReadinessFixButton.Click += async (_, _) => await FixSelectedScheduledReadinessAsync();
            var openIndex = actions.Children.IndexOf(_scheduledReadinessOpenButton);
            actions.Children.Insert(openIndex < 0 ? actions.Children.Count : openIndex, _scheduledReadinessFixButton);
        }

        _scheduledReadinessGrid.SelectionChanged += (_, _) =>
        {
            if (_scheduledReadinessFixButton is not null)
                _scheduledReadinessFixButton.IsEnabled = _scheduledReadinessGrid.SelectedItem is ScheduledReleaseReadinessRow;
        };

        var actionColumn = _scheduledReadinessGrid.Columns
            .FirstOrDefault(column => string.Equals(
                Convert.ToString(column.Header),
                "Next thing to fix",
                StringComparison.Ordinal));
        if (actionColumn is DataGridTextColumn)
        {
            var index = _scheduledReadinessGrid.Columns.IndexOf(actionColumn);
            _scheduledReadinessGrid.Columns.RemoveAt(index);
            _scheduledReadinessGrid.Columns.Insert(index, BuildScheduledReadinessFixColumn());
        }

        _releaseReadinessActionsPolished = true;
        return true;
    }

    private DataGridTemplateColumn BuildScheduledReadinessFixColumn()
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ScheduledReleaseReadinessRow.NextAction)));
        button.SetBinding(FrameworkElement.TagProperty, new Binding());
        button.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
        button.SetValue(FrameworkElement.ToolTipProperty, "Click to work on this release task");
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(ScheduledReadinessFixRow_Click));

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 213, 82))));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        var ready = new Trigger { Property = ContentControl.ContentProperty, Value = "Ready for release" };
        ready.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(70, 235, 115))));
        style.Triggers.Add(ready);
        button.SetValue(FrameworkElement.StyleProperty, style);

        return new DataGridTemplateColumn
        {
            Header = "Next thing to fix",
            CellTemplate = new DataTemplate { VisualTree = button },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 210,
        };
    }

    private async void ScheduledReadinessFixRow_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: ScheduledReleaseReadinessRow row })
        {
            if (_scheduledReadinessGrid is not null)
                _scheduledReadinessGrid.SelectedItem = row;
            await FixScheduledReadinessRowAsync(row);
        }
        eventArgs.Handled = true;
    }

    private async Task FixSelectedScheduledReadinessAsync()
    {
        if (_scheduledReadinessGrid?.SelectedItem is not ScheduledReleaseReadinessRow row)
        {
            MessageBox.Show(this, "Select a scheduled quiz first.", "Release Readiness",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await FixScheduledReadinessRowAsync(row);
    }

    private async Task FixScheduledReadinessRowAsync(ScheduledReleaseReadinessRow row)
    {
        var history = _data.GetQuizHistory(2_000)
            .FirstOrDefault(item => item.Id == row.HistoryId);
        if (history is null)
        {
            MessageBox.Show(this, "The selected quiz could not be found in Quiz History.", "Release Readiness",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            switch (row.NextAction)
            {
                case "Create YouTube package":
                    GenerateSelectedQuizYouTubePackage(history);
                    await RefreshScheduledReleaseReadinessAsync(false);
                    break;

                case "Create promo Short":
                    ShowQuizPromoShortDialog(history);
                    await RefreshScheduledReleaseReadinessAsync(false);
                    break;

                case "Create tracking link":
                    if (await CreateSelectedScheduledTrackingLinkAsync(history))
                        await RefreshScheduledReleaseReadinessAsync(false);
                    break;

                case "Check tracker connection":
                case "Configure Link Tracker":
                    OpenLinkTrackerSettings();
                    break;

                case "Schedule promo":
                    await RunSelectedReadinessBatchActionAsync(
                        row,
                        "Schedule promo",
                        ScheduleMissingPromosAsync);
                    break;

                case "Set Related video":
                    await RunSelectedReadinessBatchActionAsync(
                        row,
                        "Related video setup",
                        ShowScheduledRelatedVideoGuideAsync);
                    break;

                case "Publish Instagram promo":
                    ShowQuizPromoShortUploadDialog(history);
                    await RefreshScheduledReleaseReadinessAsync(false);
                    break;

                case "Prepare first comment":
                    ShowQuizPublishingMetadata(history, manageComments: true);
                    await RefreshScheduledReleaseReadinessAsync(false);
                    break;

                case "Ready for release":
                    MessageBox.Show(this,
                        $"{history.UploadTitleDisplay}\n\nAll release-readiness checks are complete.",
                        "Ready for Release",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;

                default:
                    OpenScheduledReadinessInUploadManager();
                    break;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Release Readiness",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> CreateSelectedScheduledTrackingLinkAsync(QuizHistorySummary history)
    {
        var settings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!settings.IsConfigured)
        {
            OpenLinkTrackerSettings();
            return false;
        }
        if (string.IsNullOrWhiteSpace(history.YouTubeUrl))
        {
            MessageBox.Show(this,
                "This quiz needs a saved YouTube URL before its tracking link can be created. Open it in Upload Manager and complete the YouTube upload first.",
                "Create Tracking Link",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenScheduledReadinessInUploadManager();
            return false;
        }

        SetScheduledReadinessStatus("Creating tracking link for " + history.UploadTitleDisplay + "...");
        await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
            settings.BaseUrl,
            settings.ApiKey,
            FactburstLinkTrackerClient.CampaignSlug(history),
            history.Id,
            history.UploadTitleDisplay,
            history.YouTubeUrl);
        return true;
    }

    private void OpenLinkTrackerSettings()
    {
        if (MainTabs is null) return;
        MainTabs.SelectedIndex = 5;
        ApplyNavigationSelection(5);
        SelectSettingsPage("tracker");
    }

    private async Task RunSelectedReadinessBatchActionAsync(
        ScheduledReleaseReadinessRow row,
        string buttonText,
        Func<Button, Task> action)
    {
        if (_scheduledReadinessGrid is null) return;

        var sourceButton = new Button { Content = buttonText };
        _scheduledReadinessGrid.ItemsSource = new[] { row };
        try
        {
            await action(sourceButton);
        }
        finally
        {
            ApplyScheduledReadinessView();
        }
    }

    private bool EnsureSimplifiedUploadManager()
    {
        if (_uploadManagerPolished) return true;
        if (_uploadManagerGrid is null) return false;

        ScrollViewer.SetHorizontalScrollBarVisibility(_uploadManagerGrid, ScrollBarVisibility.Disabled);

        foreach (var column in _uploadManagerGrid.Columns
                     .Where(column => string.Equals(Convert.ToString(column.Header), "Type", StringComparison.Ordinal) ||
                                      string.Equals(Convert.ToString(column.Header), "First comment", StringComparison.Ordinal))
                     .ToList())
        {
            _uploadManagerGrid.Columns.Remove(column);
        }

        foreach (var column in _uploadManagerGrid.Columns)
        {
            var header = Convert.ToString(column.Header);
            if (string.Equals(header, "Current step", StringComparison.Ordinal))
            {
                column.Header = "Next step";
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                column.MinWidth = 180;
            }
            else if (string.Equals(header, "Promo Short", StringComparison.Ordinal))
            {
                column.Header = "Promo";
                column.Width = new DataGridLength(115);
            }
            else if (string.Equals(header, "Quiz", StringComparison.Ordinal))
            {
                column.Width = new DataGridLength(2, DataGridLengthUnitType.Star);
                column.MinWidth = 260;
            }
        }

        SimplifyUploadManagerStats();
        SimplifyUploadManagerActions();

        if (Content is DependencyObject root)
        {
            var subtitle = FindVisualChildren<TextBlock>(root)
                .FirstOrDefault(text => string.Equals(
                    text.Text,
                    "Upload completed quizzes, track schedules, and post first comments when publication is ready.",
                    StringComparison.Ordinal));
            if (subtitle is not null)
            {
                subtitle.Text = "Select a quiz, then choose the next action. Platform status stays visible without the extra detail columns.";
                subtitle.TextWrapping = TextWrapping.Wrap;
            }
        }

        _uploadManagerPolished = true;
        return true;
    }

    private void SimplifyUploadManagerStats()
    {
        if (Content is not DependencyObject root) return;

        var commentsLabel = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(text => string.Equals(text.Text, "Comments ready", StringComparison.Ordinal));
        var completeLabel = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(text => string.Equals(text.Text, "Upload complete", StringComparison.Ordinal));
        if (commentsLabel?.Parent is not StackPanel commentsStack ||
            commentsStack.Parent is not Border commentsCard ||
            commentsCard.Parent is not Grid stats ||
            completeLabel?.Parent is not StackPanel completeStack ||
            completeStack.Parent is not Border completeCard ||
            !ReferenceEquals(completeCard.Parent, stats))
        {
            return;
        }

        stats.Children.Remove(commentsCard);
        if (stats.ColumnDefinitions.Count == 4)
            stats.ColumnDefinitions.RemoveAt(2);
        Grid.SetColumn(completeCard, 2);
        completeCard.Margin = new Thickness(5, 0, 0, 0);
    }

    private void SimplifyUploadManagerActions()
    {
        if (Content is not DependencyObject root) return;

        var upload = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "Upload Selected", StringComparison.Ordinal));
        if (upload?.Parent is not WrapPanel actions) return;

        var comments = actions.Children.OfType<Button>()
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "First Comments", StringComparison.Ordinal));
        var queue = actions.Children.OfType<Button>()
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "Upload Queue", StringComparison.Ordinal));
        if (comments is null || queue is null) return;

        actions.Children.Clear();
        upload.Content = "Upload selected";
        upload.Margin = new Thickness(0);
        actions.Children.Add(upload);

        var promo = new Button
        {
            Content = "Promo next step",
            MinWidth = 126,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Create the promo if it is missing; otherwise open the promo upload workflow.",
        };
        StyleQuizHistoryButton(promo, Color.FromRgb(204, 70, 255));
        promo.Click += (_, _) => OpenSelectedUploadManagerPromoStep();
        actions.Children.Add(promo);

        comments.Content = "First comments";
        comments.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(comments);

        queue.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(queue);

        var more = new Button
        {
            Content = "More actions ▾",
            MinWidth = 116,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(more, Color.FromRgb(170, 185, 220));
        var menu = new ContextMenu();
        var retry = new MenuItem { Header = "Retry failed step" };
        retry.Click += async (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                await RetryFailedUploadStepsAsync(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Retry Failed Step",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };
        var reset = new MenuItem { Header = "Reset upload state" };
        reset.Click += (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowResetUploadStateDialog(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Reset Upload State",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };
        menu.Items.Add(retry);
        menu.Items.Add(reset);
        more.ContextMenu = menu;
        more.Click += (_, _) =>
        {
            if (more.ContextMenu is null) return;
            more.ContextMenu.PlacementTarget = more;
            more.ContextMenu.IsOpen = true;
        };
        actions.Children.Add(more);
    }

    private void OpenSelectedUploadManagerPromoStep()
    {
        if (_uploadManagerGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(this, "Select a long-form quiz first.", "Promo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Promotional Shorts belong to long-form quizzes. Select a long-form quiz first.", "Promo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (QuizPromoShortPaths.FindExisting(history.ProjectFolder) is null)
            ShowQuizPromoShortDialog(history);
        else
            ShowQuizPromoShortUploadDialog(history);

        RefreshUploadManager();
    }
}
