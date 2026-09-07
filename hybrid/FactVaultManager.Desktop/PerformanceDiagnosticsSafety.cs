using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly HashSet<Button> _performanceDiagnosticsSafeButtons = new();

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void InitializePerformanceDiagnosticsSafety()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPerformanceDiagnosticsButtonLoaded),
            handledEventsToo: true);
    }

    private static void OnPerformanceDiagnosticsButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var content = button.Content?.ToString();
        if (content == "Profile navigation by section")
        {
            button.Visibility = Visibility.Collapsed;
            return;
        }

        if (content is not ("Profile current sidebar" or "Run full performance profile"))
            return;

        if (Window.GetWindow(button) is MainShellWindow window)
            window.AttachSafePerformanceDiagnosticsAction(button);
    }

    private void AttachSafePerformanceDiagnosticsAction(Button button)
    {
        if (!_performanceDiagnosticsSafeButtons.Add(button))
            return;

        button.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(PerformanceDiagnosticsButtonPreviewMouseDown),
            handledEventsToo: true);
    }

    private async void PerformanceDiagnosticsButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button ||
            button.Content?.ToString() is not ("Profile current sidebar" or "Run full performance profile"))
            return;

        if (_performanceDiagnosticsDashboardBusy)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        SetDiagnosticsDashboardBusy(true, "Running a safe diagnostic profile. Pages that are still starting will be skipped instead of opening pop-ups...");
        try
        {
            if (button.Content?.ToString() == "Run full performance profile" && AreAllCurrentNavigationRoutesReadyForDiagnostics())
                await RunFullPerformanceProfileAsync();
            else if (button.Content?.ToString() == "Run full performance profile")
                await RunSafeFullPerformanceProfileAsync();
            else
                await RunSafeNavigationHotspotProfileAsync();

            UpdatePerformanceDiagnosticsDashboardSummary();
        }
        catch (Exception error)
        {
            _performanceDiagnosticsStatus!.Text = $"Diagnostic profile stopped safely: {error.Message}";
        }
        finally
        {
            SetDiagnosticsDashboardBusy(false);
        }
    }
}
