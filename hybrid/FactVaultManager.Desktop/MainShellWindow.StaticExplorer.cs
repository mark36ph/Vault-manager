using System;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        AllowsTransparency = false;
        WindowState = WindowState.Maximized;
        base.OnInitialized(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        MainTabs.Margin = new Thickness(0);
        MainTabs.Padding = new Thickness(10);
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

        ProjectsGrid.RowHeight = 42;
        ProjectsGrid.ColumnHeaderHeight = 38;
        ProjectsGrid.Margin = new Thickness(0, 0, 18, 0);

        if (ProjectsGrid.Parent is Grid projectsWorkspace && projectsWorkspace.ColumnDefinitions.Count >= 3)
        {
            projectsWorkspace.ColumnDefinitions[0].Width = new GridLength(460);

            var editor = projectsWorkspace.Children
                .OfType<Grid>()
                .FirstOrDefault(child => Grid.GetColumn(child) == 2);
            if (editor is not null)
            {
                editor.Margin = new Thickness(24, 0, 0, 0);
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
