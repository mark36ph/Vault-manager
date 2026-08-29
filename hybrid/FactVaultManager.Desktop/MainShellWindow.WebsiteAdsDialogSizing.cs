using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool WebsiteAdsDialogSizingRegistered = RegisterWebsiteAdsDialogSizing();

    private static bool RegisterWebsiteAdsDialogSizing()
    {
        EventManager.RegisterClassHandler(
            typeof(WebsiteAdsSettingsDialog),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WebsiteAdsSettingsDialog_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void WebsiteAdsSettingsDialog_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not WebsiteAdsSettingsDialog dialog) return;
        dialog.Width = 660;
        dialog.Height = 650;
        dialog.MinWidth = 600;
        dialog.MinHeight = 590;
    }
}
