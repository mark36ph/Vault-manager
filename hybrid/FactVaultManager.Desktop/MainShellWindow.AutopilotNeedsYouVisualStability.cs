using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public static class AutopilotNeedsYouVisualStability
{
    public static string ResolveHealth(bool running, int pendingTasks) =>
        pendingTasks > 0 ? "Needs you" : running ? "Working" : "Healthy";

    public static int ParsePendingCount(string? value)
    {
        var text = (value ?? "").Trim();
        return int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var count)
            ? Math.Max(0, count)
            : 0;
    }
}

public partial class MainShellWindow
{
    private bool _autopilotNeedsYouVisualStabilityInitialized;
    private DispatcherTimer? _autopilotNeedsYouVisualStabilityRetryTimer;
    private DependencyPropertyDescriptor? _autopilotHealthTextDescriptor;
    private DependencyPropertyDescriptor? _autopilotNeedsTextDescriptor;

    public void InitializeAutopilotNeedsYouVisualStability()
    {
        if (_autopilotNeedsYouVisualStabilityInitialized)
            return;

        _autopilotNeedsYouVisualStabilityInitialized = true;
        Loaded += (_, _) => QueueAutopilotNeedsYouVisualStability();
        Closed += (_, _) => RemoveAutopilotNeedsYouVisualStabilityHooks();
        QueueAutopilotNeedsYouVisualStability();
    }

    private void QueueAutopilotNeedsYouVisualStability()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureAutopilotNeedsYouVisualStability));
    }

    private void EnsureAutopilotNeedsYouVisualStability()
    {
        if (_autopilotHealthText is null || _autopilotNeedsText is null)
        {
            _autopilotNeedsYouVisualStabilityRetryTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _autopilotNeedsYouVisualStabilityRetryTimer.Tick -= AutopilotNeedsYouVisualStabilityRetryTimer_Tick;
            _autopilotNeedsYouVisualStabilityRetryTimer.Tick += AutopilotNeedsYouVisualStabilityRetryTimer_Tick;
            _autopilotNeedsYouVisualStabilityRetryTimer.Start();
            return;
        }

        _autopilotNeedsYouVisualStabilityRetryTimer?.Stop();
        if (_autopilotHealthTextDescriptor is not null)
            return;

        _autopilotHealthTextDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        _autopilotNeedsTextDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        _autopilotHealthTextDescriptor?.AddValueChanged(_autopilotHealthText, AutopilotNeedsYouVisualStateChanged);
        _autopilotNeedsTextDescriptor?.AddValueChanged(_autopilotNeedsText, AutopilotNeedsYouVisualStateChanged);
        ReconcileAutopilotNeedsYouVisualState();
    }

    private void AutopilotNeedsYouVisualStabilityRetryTimer_Tick(object? sender, EventArgs e) =>
        EnsureAutopilotNeedsYouVisualStability();

    private void AutopilotNeedsYouVisualStateChanged(object? sender, EventArgs e) =>
        ReconcileAutopilotNeedsYouVisualState();

    private void ReconcileAutopilotNeedsYouVisualState()
    {
        if (_autopilotHealthText is null || _autopilotNeedsText is null)
            return;

        var pending = AutopilotNeedsYouVisualStability.ParsePendingCount(_autopilotNeedsText.Text);
        var expected = AutopilotNeedsYouVisualStability.ResolveHealth(_fullAutopilotRunning, pending);
        if (!string.Equals(_autopilotHealthText.Text, expected, StringComparison.Ordinal))
            _autopilotHealthText.Text = expected;
    }

    private void RemoveAutopilotNeedsYouVisualStabilityHooks()
    {
        _autopilotNeedsYouVisualStabilityRetryTimer?.Stop();
        if (_autopilotHealthText is not null)
            _autopilotHealthTextDescriptor?.RemoveValueChanged(_autopilotHealthText, AutopilotNeedsYouVisualStateChanged);
        if (_autopilotNeedsText is not null)
            _autopilotNeedsTextDescriptor?.RemoveValueChanged(_autopilotNeedsText, AutopilotNeedsYouVisualStateChanged);
        _autopilotHealthTextDescriptor = null;
        _autopilotNeedsTextDescriptor = null;
    }
}
