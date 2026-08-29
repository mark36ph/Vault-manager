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
        if (eventArgs.OriginalSource is not Button button ||
            !string.Equals(Convert.ToString(button.Content), "Website settings", StringComparison.Ordinal))
        {
            return;
        }

        // The Website button's existing handler already navigates to Settings.
        // Select the injected Link Tracker page after that navigation completes.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                InitializeFactburstTrackerUi();
                if (_settingsPages.ContainsKey("tracker"))
                    SelectSettingsPage("tracker");
            }));
    }
}
