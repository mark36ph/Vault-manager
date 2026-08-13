using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void MainShellWindow_LoadedLayout(object sender, RoutedEventArgs e)
    {
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(12, 10, 14, 12);
    }
}
