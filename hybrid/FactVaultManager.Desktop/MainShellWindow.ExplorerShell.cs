using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _explorerHostBuilt;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_explorerHostBuilt || MainTabs.Parent is not Grid root)
        {
            return;
        }

        _explorerHostBuilt = true;
        root.Children.Remove(MainTabs);

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(host, 2);
        root.Children.Add(host);

        var navigation = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)) };
        Grid.SetColumn(navigation, 0);
        host.Children.Add(navigation);

        AddExplorerNavigation(navigation, "⌂  Home", 0);
        AddExplorerNavigation(navigation, "▤  Projects", 1);
        AddExplorerNavigation(navigation, "▷  Production", 2);
        AddExplorerNavigation(navigation, "□  Media Library", 3);
        AddExplorerNavigation(navigation, "◉  Asset Review", 4);
        navigation.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)), Margin = new Thickness(8, 10, 8, 10) });
        AddExplorerNavigation(navigation, "⚙  Settings", 5);

        var divider = new Border { Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)) };
        Grid.SetColumn(divider, 1);
        host.Children.Add(divider);

        var hiddenHeaderStyle = new Style(typeof(TabItem));
        hiddenHeaderStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        hiddenHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        hiddenHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(17, 17, 17))));
        hiddenHeaderStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        hiddenHeaderStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            tab.Style = hiddenHeaderStyle;
        }

        MainTabs.Margin = new Thickness(0);
        MainTabs.Padding = new Thickness(0);
        MainTabs.BorderThickness = new Thickness(0);
        MainTabs.Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        MainTabs.Foreground = Brushes.White;
        MainTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        MainTabs.VerticalAlignment = VerticalAlignment.Stretch;
        MainTabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(MainTabs, 2);
        host.Children.Add(MainTabs);
    }

    private void AddExplorerNavigation(Panel panel, string label, int index)
    {
        var button = new Button
        {
            Content = label,
            Height = 36,
            Margin = new Thickness(8, 1, 8, 1),
            Padding = new Thickness(10, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => MainTabs.SelectedIndex = index;
        panel.Children.Add(button);
    }
}
