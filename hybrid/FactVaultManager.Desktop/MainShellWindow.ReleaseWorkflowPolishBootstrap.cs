using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool ReleaseWorkflowPolishAutoRegistered = RegisterReleaseWorkflowPolish();

    private static bool RegisterReleaseWorkflowPolish()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ReleaseWorkflowPolishWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void ReleaseWorkflowPolishWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MainShellWindow window)
            window.InitializeReleaseWorkflowPolishForApp();
    }
}
