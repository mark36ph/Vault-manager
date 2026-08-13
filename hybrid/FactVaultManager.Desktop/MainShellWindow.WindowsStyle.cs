using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _windowsStyleApplied;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_windowsStyleApplied)
        {
            return;
        }

        _windowsStyleApplied = true;
        ApplyWindowsStyle();
    }

    private void ApplyWindowsStyle()
    {
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(10, 6, 12, 10);

        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            if (tab.Content is ScrollViewer scroll && scroll.Content is FrameworkElement scrollChild)
            {
                scrollChild.Margin = new Thickness(10, 0, 8, 8);
            }
            else if (tab.Content is FrameworkElement page)
            {
                page.Margin = new Thickness(10, 0, 8, 8);
            }
        }

        StyleDashboardWorkspace();

        // Projects has its own native Windows workspace. Rebuild it here, after the
        // shell has rendered, so older styling passes cannot restore the legacy form.
        _nativeProjectsApplied = false;
        ApplyNativeProjectsWorkspace();
    }

    private void StyleDashboardWorkspace()
    {
        TotalCountText.FontSize = 28;
        InProgressCountText.FontSize = 28;
        CompletedCountText.FontSize = 28;
        ScheduledCountText.FontSize = 28;
        PublishedCountText.FontSize = 28;
    }

    private void StyleProjectsWorkspace()
    {
        // Kept for source compatibility with earlier polish passes. The native
        // workspace is now the only code allowed to shape the Projects page.
        _nativeProjectsApplied = false;
        ApplyNativeProjectsWorkspace();
    }

    private static SolidColorBrush MakeBrush(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
