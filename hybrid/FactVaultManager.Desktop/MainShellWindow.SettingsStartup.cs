using System;
using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowState = GetStartMaximizedSetting()
            ? WindowState.Maximized
            : WindowState.Normal;
    }
}
