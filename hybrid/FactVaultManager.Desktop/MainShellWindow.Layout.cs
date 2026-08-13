using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ApplyWindowsShellLayout()
    {
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(12, 10, 14, 12);
    }
}
