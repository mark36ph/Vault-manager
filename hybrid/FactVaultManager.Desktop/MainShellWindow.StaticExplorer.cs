using System;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        MainTabs.Margin = new Thickness(0);
        MainTabs.Padding = new Thickness(0);
        MainTabs.BorderThickness = new Thickness(0);
        MainTabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;

        if (FindResource("HiddenPageTabStyle") is Style hiddenPageStyle)
        {
            foreach (var tab in MainTabs.Items.OfType<TabItem>())
            {
                tab.Style = hiddenPageStyle;
            }
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index))
        {
            MainTabs.SelectedIndex = index;
        }
    }
}
