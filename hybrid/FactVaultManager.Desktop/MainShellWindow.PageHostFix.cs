using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    static MainShellWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnShellLoaded));
    }

    private static void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
        {
            return;
        }

        window.MainTabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        window.MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;

        foreach (var tab in window.MainTabs.Items.OfType<TabItem>())
        {
            tab.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tab.VerticalContentAlignment = VerticalAlignment.Stretch;

            if (tab.Content is ScrollViewer scroll)
            {
                scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                scroll.VerticalContentAlignment = VerticalAlignment.Top;
            }
            else if (tab.Content is FrameworkElement page)
            {
                page.HorizontalAlignment = HorizontalAlignment.Stretch;
                page.VerticalAlignment = VerticalAlignment.Top;
            }
        }
    }
}
