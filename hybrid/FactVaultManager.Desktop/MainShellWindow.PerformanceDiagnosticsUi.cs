using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsUiInitialized;
    internal TextBlock? _performanceDiagnosticsStatus;
    private Button? _performanceDiagnosticsToggleButton;
    internal TextBox? _performanceDiagnosticsResults;

    private void InitializePerformanceDiagnosticsUi()
    {
        if (_performanceDiagnosticsUiInitialized)
            return;

        if (!_settingsWorkflowInitialized || !_settingsNavButtons.TryGetValue("about", out var aboutButton))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(InitializePerformanceDiagnosticsUi));
            return;
        }

        if (aboutButton.Parent is not Panel sidebar)
            return;

        _settingsPages["performance"] = BuildPerformanceDiagnosticsPage();