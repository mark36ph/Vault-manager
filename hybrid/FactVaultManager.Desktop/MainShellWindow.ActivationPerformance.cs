using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _activationPerformanceInitialized;
    private bool _activationWorkQueued;
    private DateTime _lastActivationWorkUtc = DateTime.MinValue;

    private void InitializeActivationPerformance()
    {
        if (_activationPerformanceInitialized)
            return;

        _activationPerformanceInitialized = true;
        Activated += MainShellWindow_Activated;
        Closed += (_, _) => Activated -= MainShellWindow_Activated;
    }

    private void MainShellWindow_Activated(object? sender, EventArgs e)
    {
        if (_activationWorkQueued || (DateTime.UtcNow - _lastActivationWorkUtc).TotalMilliseconds < 750)
            return;

        _activationWorkQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                try
                {
                    if (_autopilotShellActivationFixApplied)
                        _ = RefreshAutopilotHomeAsync();
                }
                finally
                {
                    _lastActivationWorkUtc = DateTime.UtcNow;
                    _activationWorkQueued = false;
                }
            }));
    }
}
