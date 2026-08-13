using System;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void FinalizeExplorerNavigation()
    {
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(0);
        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            tab.Width = 220;
            tab.Height = 38;
            tab.Margin = new Thickness(8, 2, 8, 2);
            tab.Padding = new Thickness(12, 0, 12, 0);
            tab.FontSize = 13;
            tab.FontWeight = FontWeights.Normal;
            tab.Background = System.Windows.Media.Brushes.Transparent;
            tab.BorderThickness = new Thickness(0);
        }
    }
}
