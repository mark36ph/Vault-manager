using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string AutopilotMasterUiTag = "autopilot-master-ui";
    private bool _autopilotMasterUiInitialized;
    private bool _autopilotMasterUiSyncing;
    private int _autopilotMasterUiAttempts;
    private DispatcherTimer? _autopilotMasterUiTimer;
    private ToggleButton? _autopilotMasterToggle;
    private ComboBox? _autopilotMasterTargetChoice;
    private TextBlock? _autopilotMasterHintText;

    public void InitializeAutopilotMasterUi()
    {
        if (_autopilotMasterUiInitialized)
            return;

        _autopilotMasterUiInitialized = true;

        // BuildInfo initializes this method from the window's Loaded route. Registering
        // another Loaded handler here is too late and can leave the real Autopilot page
        // without its ON/OFF controls. Queue the work directly and retry until the home
        // header exists instead.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureAutopilotMasterUi));

        Closed += (_, _) => _autopilotMasterUiTimer?.Stop();
    }

    private void EnsureAutopilotMasterUi()
    {
        if (_autopilotHealthText?.Parent is not StackPanel healthStack ||
            healthStack.Parent is not Border healthCard)
        {
            RetryAutopilotMasterUi();
            return;
        }

        if (healthCard.Tag?.ToString() == AutopilotMasterUiTag)
        {
            RefreshAutopilotMasterUiState();
            return;
        }

        _autopilotMasterUiTimer?.Stop();
        healthCard.Tag = AutopilotMasterUiTag;
        healthCard.Padding = new Thickness(14, 9, 14, 9);

        healthCard.Child = null;
        var outer = new StackPanel();
        outer.Children.Add(healthStack);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 7, 0, 0),
        };

        _autopilotMasterToggle = new ToggleButton
        {
            MinWidth = 58,
            Height = 28,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Master Autopilot switch. When ON, Factburst automatically tops the future quiz schedule back up to the selected target.",
        };
        _autopilotMasterToggle.Checked += async (_, _) => await SetAutopilotMasterEnabledAsync(true);
        _autopilotMasterToggle.Unchecked += async (_, _) => await SetAutopilotMasterEnabledAsync(false);
        controls.Children.Add(_autopilotMasterToggle);

        controls.Children.Add(new TextBlock
        {
            Text = "Keep",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
        });

        _autopilotMasterTargetChoice = new ComboBox
        {
            Width = 58,
            Height = 27,
            ItemsSource = AutopilotScheduleTargetPlanner.AllowedTargets,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            ToolTip = "Number of future full quizzes Autopilot should keep scheduled.",
        };
        _autopilotMasterTargetChoice.SelectionChanged += async (_, _) => await AutopilotMasterTargetChangedAsync();
        controls.Children.Add(_autopilotMasterTargetChoice);

        controls.Children.Add(new TextBlock
        {
            Text = "scheduled",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
        });
        outer.Children.Add(controls);

        _autopilotMasterHintText = new TextBlock
        {
            FontSize = 10,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 5, 0, 0),
        };
        outer.Children.Add(_autopilotMasterHintText);

        healthCard.Child = outer;
        RefreshAutopilotMasterUiState();

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        if (preferences.AutoFillEnabled)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(async () => await EvaluateAutomaticScheduleFillAsync()));
        }
    }

    private void RetryAutopilotMasterUi()
    {
        if (++_autopilotMasterUiAttempts >= 80)
            return;

        _autopilotMasterUiTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _autopilotMasterUiTimer.Tick -= AutopilotMasterUiTimer_Tick;
        _autopilotMasterUiTimer.Tick += AutopilotMasterUiTimer_Tick;
        _autopilotMasterUiTimer.Start();
    }

    private void AutopilotMasterUiTimer_Tick(object? sender, EventArgs e)
    {
        _autopilotMasterUiTimer?.Stop();
        EnsureAutopilotMasterUi();
    }

    private async Task SetAutopilotMasterEnabledAsync(bool enabled)
    {
        if (_autopilotMasterUiSyncing)
            return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        if (preferences.AutoFillEnabled != enabled)
        {
            preferences.AutoFillEnabled = enabled;
            AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, preferences);
        }

        ApplyAutopilotMasterState(enabled);
        RefreshAutopilotMasterUiState();
        UpdateScheduleTargetStatus();

        if (enabled)
        {
            // ON means automatic. Evaluate the schedule immediately; the startup check
            // and five-minute supervisor continue maintaining it afterwards.
            await EvaluateAutomaticScheduleFillAsync();
            await RunFullAutopilotAsync();
        }

        await RefreshAutopilotHomeAsync();
        RefreshAutopilotMasterUiState();
    }

    private async Task AutopilotMasterTargetChangedAsync()
    {
        if (_autopilotMasterUiSyncing || _autopilotMasterTargetChoice?.SelectedItem is not int target)
            return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        preferences.TargetDays = target;
        AutopilotSchedulePreferencesStore.Save(_data.SettingsPath, preferences);
        RefreshAutopilotMasterUiState();
        UpdateScheduleTargetStatus();

        if (preferences.AutoFillEnabled)
            await EvaluateAutomaticScheduleFillAsync();
    }

    private void RefreshAutopilotMasterUiState()
    {
        if (_autopilotMasterToggle is null || _autopilotMasterTargetChoice is null)
            return;

        var preferences = AutopilotSchedulePreferencesStore.Load(_data.SettingsPath);
        _autopilotMasterUiSyncing = true;
        try
        {
            _autopilotMasterToggle.IsChecked = preferences.AutoFillEnabled;
            _autopilotMasterToggle.Content = preferences.AutoFillEnabled ? "ON" : "OFF";
            _autopilotMasterToggle.Background = preferences.AutoFillEnabled
                ? new SolidColorBrush(Color.FromRgb(2, 122, 72))
                : new SolidColorBrush(Color.FromRgb(185, 28, 28));
            _autopilotMasterToggle.BorderBrush = preferences.AutoFillEnabled
                ? new SolidColorBrush(Color.FromRgb(18, 183, 106))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            _autopilotMasterToggle.Foreground = Brushes.White;
            _autopilotMasterTargetChoice.SelectedItem = preferences.TargetDays;

            if (_autopilotMasterHintText is not null)
            {
                _autopilotMasterHintText.Text = preferences.AutoFillEnabled
                    ? "Automatic refill is active — no Fill Schedule click is required."
                    : "Autopilot refill is paused.";
            }
        }
        finally
        {
            _autopilotMasterUiSyncing = false;
        }
    }
}
