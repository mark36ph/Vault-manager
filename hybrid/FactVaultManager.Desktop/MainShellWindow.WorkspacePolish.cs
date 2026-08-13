using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ApplyWorkspacePolish()
    {
        FontFamily = new FontFamily("Segoe UI");
        StyleDataWorkspace(3, MediaGrid, MediaProjectComboBox);
        StyleDataWorkspace(4, AssetReviewGrid, AssetProjectComboBox);
        StyleSettingsWorkspace();
        StyleProductionLauncher();
    }

    private void StyleDataWorkspace(int tabIndex, DataGrid grid, ComboBox selector)
    {
        if (MainTabs.Items.Count <= tabIndex || MainTabs.Items[tabIndex] is not TabItem tab || tab.Content is not Grid page)
        {
            return;
        }

        page.Margin = new Thickness(8, 0, 6, 8);
        var toolbar = page.Children.OfType<Border>().FirstOrDefault();
        if (toolbar is not null)
        {
            toolbar.Background = MakeBrush("#1F1F1F");
            toolbar.BorderBrush = MakeBrush("#353535");
            toolbar.CornerRadius = new CornerRadius(4);
            toolbar.Padding = new Thickness(10, 8, 10, 8);
            toolbar.Margin = new Thickness(0, 0, 0, 10);
        }

        selector.MinHeight = 34;
        selector.FontFamily = new FontFamily("Segoe UI");
        selector.FontSize = 12.5;

        grid.Background = MakeBrush("#191919");
        grid.BorderBrush = MakeBrush("#353535");
        grid.RowBackground = MakeBrush("#191919");
        grid.AlternatingRowBackground = MakeBrush("#1F1F1F");
        grid.HorizontalGridLinesBrush = MakeBrush("#2F2F2F");
        grid.RowHeight = 36;
        grid.ColumnHeaderHeight = 34;
        grid.FontFamily = new FontFamily("Segoe UI");
        grid.FontSize = 12.5;
    }

    private void StyleSettingsWorkspace()
    {
        if (MainTabs.Items.Count <= 5 || MainTabs.Items[5] is not TabItem settingsTab ||
            settingsTab.Content is not ScrollViewer scroll || scroll.Content is not Border panel)
        {
            return;
        }

        scroll.Margin = new Thickness(8, 0, 6, 8);
        panel.Background = MakeBrush("#1F1F1F");
        panel.BorderBrush = MakeBrush("#353535");
        panel.CornerRadius = new CornerRadius(4);
        panel.Padding = new Thickness(20, 16, 20, 18);
        panel.MaxWidth = 980;

        ProjectsFolderTextBox.MinHeight = 34;
        OpenAiModelTextBox.MinHeight = 34;
        ResolvePathTextBox.MinHeight = 34;
        TimelineWidthTextBox.MinHeight = 34;
        TimelineHeightTextBox.MinHeight = 34;
        FrameRateTextBox.MinHeight = 34;

        foreach (var box in new[] { ProjectsFolderTextBox, OpenAiModelTextBox, ResolvePathTextBox, TimelineWidthTextBox, TimelineHeightTextBox, FrameRateTextBox })
        {
            box.Background = MakeBrush("#191919");
            box.BorderBrush = MakeBrush("#454545");
            box.FontFamily = new FontFamily("Segoe UI");
            box.FontSize = 12.5;
        }

        foreach (var password in new[] { OpenAiKeyPasswordBox, PexelsKeyPasswordBox, PixabayKeyPasswordBox })
        {
            password.MinHeight = 34;
            password.Background = MakeBrush("#191919");
            password.BorderBrush = MakeBrush("#454545");
            password.FontFamily = new FontFamily("Segoe UI");
        }
    }

    private void StyleProductionLauncher()
    {
        if (MainTabs.Items.Count <= 2 || MainTabs.Items[2] is not TabItem productionTab || productionTab.Content is not Grid page)
        {
            return;
        }

        page.Margin = new Thickness(8, 0, 6, 8);
        var panel = page.Children.OfType<Border>().FirstOrDefault();
        if (panel is null)
        {
            return;
        }

        panel.Background = MakeBrush("#1F1F1F");
        panel.BorderBrush = MakeBrush("#353535");
        panel.CornerRadius = new CornerRadius(4);
        panel.Padding = new Thickness(28, 24, 28, 26);
        panel.Width = 680;
    }
}
