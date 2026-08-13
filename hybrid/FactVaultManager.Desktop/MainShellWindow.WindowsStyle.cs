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
        StyleProjectsWorkspace();
        ApplyCommandPolish();
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
        if (MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab || projectsTab.Content is not Grid projectsGrid)
        {
            return;
        }

        projectsGrid.Margin = new Thickness(10, 0, 8, 8);
        if (projectsGrid.ColumnDefinitions.Count >= 3)
        {
            projectsGrid.ColumnDefinitions[0].Width = new GridLength(355);
            projectsGrid.ColumnDefinitions[1].Width = new GridLength(10);
        }

        foreach (var border in projectsGrid.Children.OfType<Border>())
        {
            border.Background = MakeBrush("#202020");
            border.BorderBrush = MakeBrush("#343434");
            border.CornerRadius = new CornerRadius(6);
        }

        ProjectsGrid.Background = MakeBrush("#1B1B1B");
        ProjectsGrid.BorderBrush = MakeBrush("#343434");
        ProjectsGrid.RowHeight = 38;
        ProjectsGrid.ColumnHeaderHeight = 36;
        ProjectsGrid.HorizontalGridLinesBrush = MakeBrush("#303030");
        ProjectsGrid.RowBackground = MakeBrush("#1B1B1B");
        ProjectsGrid.AlternatingRowBackground = MakeBrush("#202020");

        NewProjectTitleTextBox.MinHeight = 34;
        ProjectCategoryTextBox.MinHeight = 34;
        ProjectStatusComboBox.MinHeight = 34;
        ProjectEditorTitle.FontSize = 21;
        ProjectEditorFolderText.FontSize = 11;
        ProjectEditorFolderText.Foreground = MakeBrush("#9D9D9D");

        ProjectScriptTextBox.MinHeight = 180;
        ProjectDescriptionTextBox.MinHeight = 82;
        ProjectPinnedCommentTextBox.MinHeight = 82;
        ProjectNotesTextBox.MinHeight = 105;
        ProjectSourcesTextBox.MinHeight = 95;

        foreach (var textBox in new[]
                 {
                     NewProjectTitleTextBox,
                     ProjectCategoryTextBox,
                     ProjectScriptTextBox,
                     ProjectDescriptionTextBox,
                     ProjectPinnedCommentTextBox,
                     ProjectTagsTextBox,
                     ProjectNotesTextBox,
                     ProjectSourcesTextBox,
                 })
        {
            textBox.Background = MakeBrush("#1A1A1A");
            textBox.BorderBrush = MakeBrush("#464646");
            textBox.Foreground = MakeBrush("#F5F5F5");
            textBox.Padding = new Thickness(8, 5, 8, 5);
        }
    }

    private static SolidColorBrush MakeBrush(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
