using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly Brush NavSelectedBackground = new SolidColorBrush(Color.FromRgb(232, 240, 248));
    private static readonly Brush NavSelectedBorder = new SolidColorBrush(Color.FromRgb(15, 108, 189));
    private static readonly Brush NavTransparent = Brushes.Transparent;

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

        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index))
        {
            MainTabs.SelectedIndex = index;
            ApplyNavigationSelection(index);
        }
    }

    private void ApplyNavigationSelection(int selectedIndex)
    {
        if (Content is not DependencyObject root)
        {
            return;
        }

        foreach (var button in FindVisualChildren<Button>(root))
        {
            if (!int.TryParse(button.Tag?.ToString(), out var index))
            {
                continue;
            }

            var isSelected = index == selectedIndex;
            button.Background = isSelected ? NavSelectedBackground : NavTransparent;
            button.BorderBrush = isSelected ? NavSelectedBorder : NavTransparent;
            button.BorderThickness = isSelected
                ? new Thickness(3, 0, 0, 0)
                : new Thickness(3, 0, 0, 0);
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
