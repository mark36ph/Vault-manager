using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteSettingsShortcutInitialized;

    public void InitializeWebsiteSettingsShortcut()
    {
        if (_websiteSettingsShortcutInitialized) return;
        _websiteSettingsShortcutInitialized = true;

        AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(WebsiteSettingsShortcut_Click),
            handledEventsToo: true);
    }

    private void WebsiteSettingsShortcut_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (eventArgs.Source is not Button button ||
            !string.Equals(Convert.ToString(button.Content), "Website settings", StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                // Preserve the existing Link Tracker configuration page, but make
                // the consolidated Website page the landing page for this shortcut.
                InitializeFactburstTrackerUi();
                InitializeWebsiteSettingsAdministrationPage();
                EnsureWebsiteSettingsAdministrationPage();
                if (_settingsPages.ContainsKey("website"))
                {
                    SelectSettingsPage("website");
                    _ = RefreshWebsiteAdministrationSettingsAsync(false);
                }
            }));
    }
}
