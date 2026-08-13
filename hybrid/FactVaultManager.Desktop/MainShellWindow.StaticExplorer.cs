using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _windowControlsAdded;

    protected override void OnInitialized(EventArgs e)
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        AllowsTransparency = false;
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

        EnsureWindowControls();
    }

    private void EnsureWindowControls()
    {
        if (_windowControlsAdded || Content is not Grid root)
        {
            return;
        }

        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 0);
        if (headerBorder?.Child is not Grid headerGrid)
        {
            return;
        }

        var toolbar = headerGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
        if (toolbar is null)
        {
            return;
        }

        toolbar.Children.Add(CreateCaptionButton("—", MinimizeWindow_Click, "Minimize"));
        toolbar.Children.Add(CreateCaptionButton("□", MaximizeRestoreWindow_Click, "Maximize / Restore"));
        toolbar.Children.Add(CreateCaptionButton("×", CloseWindow_Click, "Close", isClose: true));
        _windowControlsAdded = true;
    }

    private static Button CreateCaptionButton(
        string glyph,
        RoutedEventHandler handler,
        string toolTip,
        bool isClose = false)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 44,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = isClose
                ? new SolidColorBrush(Color.FromRgb(196, 43, 28))
                : new SolidColorBrush(Color.FromRgb(31, 31, 31)),
            FontSize = isClose ? 20 : 16,
            ToolTip = toolTip,
        };
        button.Click += handler;
        return button;
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index))
        {
            MainTabs.SelectedIndex = index;
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
