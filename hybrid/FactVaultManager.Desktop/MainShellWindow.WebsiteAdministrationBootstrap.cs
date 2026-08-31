using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool WebsiteAdministrationAutoRegistered = RegisterWebsiteAdministration();

    private static bool RegisterWebsiteAdministration()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WebsiteAdministrationWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void WebsiteAdministrationWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not MainShellWindow window) return;
        window.InitializeWebsiteUsersPage();
        window.InitializeWebsiteAnalyticsPage();
        window.InitializeWebsiteAdministrationEnhancements();
    }
}
