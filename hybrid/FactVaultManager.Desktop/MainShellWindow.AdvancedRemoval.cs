using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool AdvancedRemovalHandlerRegistered = RegisterAdvancedRemovalHandler();

    private static bool RegisterAdvancedRemovalHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowAdvancedRemoval_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowAdvancedRemoval_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(window.RemoveRetiredAdvancedPage));
    }

    private void RemoveRetiredAdvancedPage()
    {
        if (_autopilotNavContainer is not null &&
            _autopilotNavButtons.TryGetValue("Advanced", out var advancedButton))
        {
            _autopilotNavContainer.Children.Remove(advancedButton);
            _autopilotNavButtons.Remove("Advanced");
        }

        if (_autopilotAdvancedTabIndex >= 0 &&
            _autopilotAdvancedTabIndex < MainTabs.Items.Count)
        {
            if (MainTabs.SelectedIndex == _autopilotAdvancedTabIndex)
                MainTabs.SelectedIndex = _autopilotHomeTabIndex >= 0 ? _autopilotHomeTabIndex : 0;

            MainTabs.Items.RemoveAt(_autopilotAdvancedTabIndex);
            _autopilotAdvancedTabIndex = -1;
        }
    }
}
