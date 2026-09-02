using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduleTargetControlTag = "autopilot-schedule-target-controls";
    private static readonly bool AutopilotScheduleTargetUiRegistered = RegisterAutopilotScheduleTargetUi();
    private DispatcherTimer? _autopilotScheduleTargetTimer;
    private bool _autopilotScheduleTargetInitialized;
    private bool _autopilotScheduleFillStarting;
    private bool _autopilotMasterHoldingFullAutopilot;
    private ComboBox? _autopilotScheduleTargetChoice;
    private CheckBox? _autopilotScheduleAutoFillChoice;
    private TextBlock? _autopilotScheduleTargetStatusText;

    private static bool RegisterAutopilotScheduleTargetUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(AutopilotScheduleTargetButton_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(AutopilotScheduleFillButton_Click),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(AutopilotBatchCountWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    public void InitializeAutopilotScheduleTarget()
    {
        if (_autopilotScheduleTargetInitialized) return;
        _autopilotScheduleTargetInitialized = true;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        ApplyAutopilotMasterState(preferences.AutoFillEnabled);

        // BuildInfo calls this from the window Loaded route, so attaching another Loaded
        // handler here is unreliable. Queue the delayed startup evaluation directly.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                await EvaluateAutomaticScheduleFillAsync();
            }));

        _autopilotScheduleTargetTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _autopilotScheduleTargetTimer.Tick += async (_, _) => await EvaluateAutomaticScheduleFillAsync();
        _autopilotScheduleTargetTimer.Start();
        UpdateScheduleTargetStatus();
    }

    private static void AutopilotScheduleTargetButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Generate + Fill Schedule", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window ||
            button.Resources.Contains(ScheduleTargetControlTag))
        {
            return;
        }

        button.Resources[ScheduleTargetControlTag] = true;
        window.EnsureScheduleTargetControls(button);
    }

    private void EnsureScheduleTargetControls(Button fillButton)
    {
        if (fillButton.Parent is not Grid parent) return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        parent.Children.Remove(fillButton);

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 280,
        };

        _autopilotScheduleAutoFillChoice = new CheckBox
        {
            Content = preferences.AutoFillEnabled ? "AUTOPILOT ON" : "AUTOPILOT OFF",
            IsChecked = preferences.AutoFillEnabled,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "When ON, Factburst keeps at least the selected number of future full quizzes scheduled and runs all available Autopilot supervision while the app is open.",
            Margin = new Thickness(0, 0, 0, 8),
        };
        stack.Children.Add(_autopilotScheduleAutoFillChoice);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        controls.Children.Add(new TextBlock
        {
            Text = "Keep at least ",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(207, 220, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _autopilotScheduleTargetChoice = new ComboBox
        {
            Width = 58,
            Height = 26,
            ItemsSource = AutopilotScheduleTargetPlanner.AllowedTargets,
            SelectedItem = preferences.TargetDays,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(_autopilotScheduleTargetChoice);
        controls.Children.Add(new TextBlock
        {
            Text = " quizzes scheduled",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(207, 220, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 9, 0),
        });
        stack.Children.Add(controls);

        fillButton.Content = "Fill schedule now";
        fillButton.ToolTip = "Manual override. Autopilot tops the future scheduled quiz count back up automatically while it is ON.";
        fillButton.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(fillButton);

        _autopilotScheduleTargetStatusText = new TextBlock
        {
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 210, 255)),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        stack.Children.Add(_autopilotScheduleTargetStatusText);

        _autopilotScheduleTargetChoice.SelectionChanged += async (_, _) =>
        {
            if (_autopilotScheduleTargetChoice.SelectedItem is not int days) return;
            var saved = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
            saved.TargetDays = days;
            AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, saved);
            UpdateScheduleTargetStatus();
            if (saved.AutoFillEnabled) await EvaluateAutomaticScheduleFillAsync();
        };
        _autopilotScheduleAutoFillChoice.Checked += async (_, _) =>
        {
            var saved = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
            saved.AutoFillEnabled = true;
            AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, saved);
            UpdateAutopilotMasterToggleLabel(true);
            ApplyAutopilotMasterState(true);
            UpdateScheduleTargetStatus();
            await EvaluateAutomaticScheduleFillAsync();
            await RunFullAutopilotAsync();
        };
        _autopilotScheduleAutoFillChoice.Unchecked += (_, _) =>
        {
            var saved = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
            saved.AutoFillEnabled = false;
            AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, saved);
            UpdateAutopilotMasterToggleLabel(false);
            ApplyAutopilotMasterState(false);
            UpdateScheduleTargetStatus();
        };

        Grid.SetColumn(stack, Grid.GetColumn(fillButton));
        parent.Children.Add(stack);
        UpdateScheduleTargetStatus();
    }

    private static void AutopilotScheduleFillButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Fill schedule now", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        var preferences = AutopilotSchedulePreferencesStore.Load(window._data.SettingsPath);
        var now = DateTimeOffset.Now;
        var history = window._data.GetFutureScheduledYouTubeQuizHistory(now);
        var scheduled = AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now);
        var missing = AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, preferences.TargetDays, now);
        var count = AutopilotScheduleTargetPlanner.BatchSizeForMissingDays(missing);
        if (count == 0)
        {
            if (window._autopilotScheduleTargetStatusText is not null)
                window._autopilotScheduleTargetStatusText.Text = $"Target met — {scheduled:N0} future quizzes are scheduled.";
            e.Handled = true;
            return;
        }

        AutopilotBatchCountRequest.Arm(count);
        window.ArmTrustedAutopilotPublishingIfApproved();
        if (window._autopilotScheduleTargetStatusText is not null)
            window._autopilotScheduleTargetStatusText.Text = $"{scheduled:N0} scheduled • creating {count:N0} quiz{(count == 1 ? "" : "zes")} to restore the {preferences.TargetDays:N0} target...";
    }

    private static void AutopilotBatchCountWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window dialog ||
            !string.Equals(dialog.Title, "Generate + Schedule Quiz Batch", StringComparison.Ordinal) ||
            !AutopilotBatchCountRequest.TryConsume(out var count))
        {
            return;
        }

        var countBox = LogicalDescendants<TextBox>(dialog).FirstOrDefault();
        var start = LogicalDescendants<Button>(dialog)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Generate + schedule", StringComparison.Ordinal));
        if (countBox is null || start is null) return;

        countBox.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        dialog.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
    }

    private async Task EvaluateAutomaticScheduleFillAsync()
    {
        if (_autopilotScheduleFillStarting) return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        var now = DateTimeOffset.Now;
        var history = _data.GetFutureScheduledYouTubeQuizHistory(now);
        var scheduled = AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now);
        var missing = AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, preferences.TargetDays, now);
        var productionBusy = _quizBatchAutomationRunning || _quizBatchRenderRunning ||
                             _quizAutopilotFinishing || _quizGrowthAutopilotRunning;
        if (!AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, missing, productionBusy, DateTime.UtcNow))
        {
            UpdateScheduleTargetStatus();
            return;
        }

        var appSettings = _data.LoadSettings();
        if (string.IsNullOrWhiteSpace(appSettings.ApprovedYouTubeChannelId))
        {
            if (_autopilotScheduleTargetStatusText is not null)
                _autopilotScheduleTargetStatusText.Text = "Autopilot is waiting for the YouTube destination to be approved once.";
            return;
        }

        var recovery = AutopilotRecoveryStateStore.Load(_data.SettingsPath);
        var youtubeHealth = recovery.Subsystems.FirstOrDefault(item =>
            string.Equals(item.Name, "YouTube", StringComparison.OrdinalIgnoreCase));
        if (youtubeHealth?.State is "Recovering" or "Needs setup")
        {
            if (_autopilotScheduleTargetStatusText is not null)
                _autopilotScheduleTargetStatusText.Text = "Autopilot is waiting for the YouTube connection to recover.";
            return;
        }

        var count = AutopilotScheduleTargetPlanner.BatchSizeForMissingDays(missing);
        if (count == 0) return;

        _autopilotScheduleFillStarting = true;
        var productionStarted = false;
        try
        {
            NavigateLegacy("Quizzes", "Create");
            SelectQuizWorkspacePage("export");
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            if (_quizAutopilotPrimaryButton is null || !_quizAutopilotPrimaryButton.IsEnabled)
            {
                if (_autopilotScheduleTargetStatusText is not null)
                    _autopilotScheduleTargetStatusText.Text = "Autopilot is waiting for production to become ready; it will retry in a few minutes.";
                return;
            }

            AutopilotBatchCountRequest.Arm(count);
            AutopilotTrustedPublishingPreflight.Arm();
            if (_autopilotScheduleTargetStatusText is not null)
                _autopilotScheduleTargetStatusText.Text = $"Autopilot: {scheduled:N0} scheduled • starting {count:N0} new quiz{(count == 1 ? "" : "zes")} to restore {preferences.TargetDays:N0}.";

            _quizAutopilotPrimaryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            productionStarted = _quizBatchAutomationRunning || _quizBatchRenderRunning;
            if (!productionStarted)
            {
                AutopilotBatchCountRequest.Cancel();
                AutopilotTrustedPublishingPreflight.Cancel();
                if (_autopilotScheduleTargetStatusText is not null)
                    _autopilotScheduleTargetStatusText.Text = "Autopilot could not start quiz production. No cooldown was applied; it will retry automatically.";
                return;
            }

            preferences.LastAutomaticFillUtc = DateTime.UtcNow;
            AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, preferences);
        }
        catch (Exception error)
        {
            if (!productionStarted)
            {
                AutopilotBatchCountRequest.Cancel();
                AutopilotTrustedPublishingPreflight.Cancel();
            }
            Debug.WriteLine("Automatic schedule fill could not start: " + error);
            if (_autopilotScheduleTargetStatusText is not null)
                _autopilotScheduleTargetStatusText.Text = "Autopilot will retry later: " + error.Message;
        }
        finally
        {
            _autopilotScheduleFillStarting = false;
        }
    }

    private void ApplyAutopilotMasterState(bool enabled)
    {
        if (enabled)
        {
            if (_autopilotMasterHoldingFullAutopilot)
            {
                _fullAutopilotRunning = false;
                _autopilotMasterHoldingFullAutopilot = false;
            }
            _fullAutopilotTimer?.Start();
            return;
        }

        _fullAutopilotTimer?.Stop();
        if (!_fullAutopilotRunning)
        {
            _fullAutopilotRunning = true;
            _autopilotMasterHoldingFullAutopilot = true;
        }
    }

    private void UpdateAutopilotMasterToggleLabel(bool enabled)
    {
        if (_autopilotScheduleAutoFillChoice is not null)
            _autopilotScheduleAutoFillChoice.Content = enabled ? "AUTOPILOT ON" : "AUTOPILOT OFF";
    }

    private void ArmTrustedAutopilotPublishingIfApproved()
    {
        var settings = _data.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.ApprovedYouTubeChannelId))
            AutopilotTrustedPublishingPreflight.Arm();
    }

    private void UpdateScheduleTargetStatus()
    {
        try
        {
            var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
            var now = DateTimeOffset.Now;
            var history = _data.GetFutureScheduledYouTubeQuizHistory(now);
            var scheduled = AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now);
            var missing = AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, preferences.TargetDays, now);

            if (_autopilotScheduledCountText is not null)
                _autopilotScheduledCountText.Text = scheduled.ToString("N0");

            if (_autopilotScheduleTargetStatusText is null) return;
            if (!preferences.AutoFillEnabled)
            {
                _autopilotScheduleTargetStatusText.Text = missing == 0
                    ? $"Autopilot is off • {scheduled:N0} future quizzes scheduled"
                    : $"Autopilot is off • {scheduled:N0}/{preferences.TargetDays:N0} scheduled • {missing:N0} need creating";
                return;
            }

            _autopilotScheduleTargetStatusText.Text = missing == 0
                ? $"Autopilot running • {scheduled:N0} future quizzes scheduled"
                : $"Autopilot running • {scheduled:N0}/{preferences.TargetDays:N0} scheduled • {missing:N0} queued to create";
        }
        catch (Exception error)
        {
            Debug.WriteLine("Could not update schedule target status: " + error.Message);
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T typed) yield return typed;
            if (child is DependencyObject dependency)
            {
                foreach (var nested in LogicalDescendants<T>(dependency))
                    yield return nested;
            }
        }
    }
}
