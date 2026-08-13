using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _windowsPolish2Applied;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ApplyWindowsPolish2));
    }

    private void ApplyWindowsPolish2()
    {
        if (_windowsPolish2Applied)
        {
            return;
        }

        _windowsPolish2Applied = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(8, 4, 12, 10);

        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            if (tab.Content is ScrollViewer scroll && scroll.Content is FrameworkElement scrollChild)
            {
                scrollChild.Margin = new Thickness(8, 0, 6, 6);
            }
            else if (tab.Content is FrameworkElement page)
            {
                page.Margin = new Thickness(8, 0, 6, 6);
            }
        }

        PolishDashboard();
        PolishProjects();
        PolishDataPages();
        PolishSettings();
    }

    private void PolishDashboard()
    {
        if (MainTabs.Items.Count == 0 || MainTabs.Items[0] is not TabItem tab ||
            tab.Content is not ScrollViewer scroll || scroll.Content is not StackPanel panel)
        {
            return;
        }

        panel.Margin = new Thickness(8, 0, 6, 6);
        if (panel.Children.Count > 0 && panel.Children[0] is TextBlock title)
        {
            title.FontSize = 22;
            title.Margin = new Thickness(0, 0, 0, 2);
        }
        if (panel.Children.Count > 1 && panel.Children[1] is TextBlock subtitle)
        {
            subtitle.Margin = new Thickness(0, 0, 0, 12);
        }

        foreach (var count in new[] { TotalCountText, InProgressCountText, CompletedCountText, ScheduledCountText, PublishedCountText })
        {
            count.FontSize = 26;
        }

        if (panel.Children.OfType<UniformGrid>().FirstOrDefault() is { } stats)
        {
            stats.Margin = new Thickness(0, 0, 0, 12);
            foreach (var card in stats.Children.OfType<Border>())
            {
                SetSurface(card, "#1C1C1C", "#343434", 4);
                card.Padding = new Thickness(14, 11, 14, 11);
            }
        }

        foreach (var card in panel.Children.OfType<Border>())
        {
            SetSurface(card, "#1C1C1C", "#343434", 4);
        }
    }

    private void PolishProjects()
    {
        if (MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem tab || tab.Content is not Grid grid)
        {
            return;
        }

        grid.Margin = new Thickness(8, 0, 6, 6);
        if (grid.ColumnDefinitions.Count >= 3)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(330);
            grid.ColumnDefinitions[1].Width = new GridLength(8);
        }

        var panels = grid.Children.OfType<Border>().OrderBy(item => Grid.GetColumn(item)).ToList();
        foreach (var panel in panels)
        {
            SetSurface(panel, "#1C1C1C", "#343434", 4);
            panel.Padding = new Thickness(12);
        }

        ProjectsGrid.Background = Brush("#181818");
        ProjectsGrid.BorderBrush = Brush("#343434");
        ProjectsGrid.RowHeight = 36;
        ProjectsGrid.ColumnHeaderHeight = 34;
        ProjectsGrid.HorizontalGridLinesBrush = Brush("#2D2D2D");
        ProjectsGrid.RowBackground = Brush("#181818");
        ProjectsGrid.AlternatingRowBackground = Brush("#1D1D1D");
        ProjectsGrid.SelectionMode = DataGridSelectionMode.Single;
        ProjectsGrid.SelectionUnit = DataGridSelectionUnit.FullRow;

        NewProjectTitleTextBox.MinHeight = 32;
        ProjectCategoryTextBox.MinHeight = 32;
        ProjectStatusComboBox.MinHeight = 32;
        ProjectEditorTitle.FontSize = 20;
        ProjectEditorTitle.Margin = new Thickness(0, 0, 0, 1);
        ProjectEditorFolderText.FontSize = 11;
        ProjectEditorFolderText.Foreground = Brush("#A6A6A6");
        ProjectEditorFolderText.Margin = new Thickness(0, 1, 0, 12);

        ProjectScriptTextBox.MinHeight = 170;
        ProjectDescriptionTextBox.MinHeight = 76;
        ProjectPinnedCommentTextBox.MinHeight = 76;
        ProjectNotesTextBox.MinHeight = 96;
        ProjectSourcesTextBox.MinHeight = 88;

        foreach (var textBox in new[]
                 {
                     NewProjectTitleTextBox, ProjectCategoryTextBox, ProjectScriptTextBox,
                     ProjectDescriptionTextBox, ProjectPinnedCommentTextBox, ProjectTagsTextBox,
                     ProjectNotesTextBox, ProjectSourcesTextBox,
                 })
        {
            textBox.Background = Brush("#181818");
            textBox.BorderBrush = Brush("#4A4A4A");
            textBox.Foreground = Brush("#F2F2F2");
            textBox.Padding = new Thickness(8, 5, 8, 5);
        }

        if (panels.Count > 0 && panels[0].Child is Grid explorer)
        {
            if (explorer.Children.OfType<TextBlock>().FirstOrDefault() is { } heading)
            {
                heading.FontSize = 18;
                heading.Margin = new Thickness(2, 0, 0, 2);
            }

            if (explorer.Children.OfType<Grid>().FirstOrDefault(item => Grid.GetRow(item) == 1) is { } commandBar)
            {
                commandBar.Margin = new Thickness(0, 8, 0, 10);
            }
        }

        if (panels.Count > 1 && panels[1].Child is ScrollViewer editorScroll && editorScroll.Content is StackPanel editor)
        {
            editorScroll.Padding = new Thickness(2, 0, 2, 0);
            foreach (var label in editor.Children.OfType<TextBlock>().Where(item => item != ProjectEditorTitle && item != ProjectEditorFolderText))
            {
                label.FontSize = 12;
                label.FontWeight = FontWeights.SemiBold;
                label.Foreground = Brush("#D8D8D8");
            }
        }
    }

    private void PolishDataPages()
    {
        foreach (var grid in new[] { MediaGrid, AssetReviewGrid })
        {
            grid.RowHeight = 36;
            grid.ColumnHeaderHeight = 34;
            grid.Background = Brush("#181818");
            grid.BorderBrush = Brush("#343434");
            grid.RowBackground = Brush("#181818");
            grid.AlternatingRowBackground = Brush("#1D1D1D");
        }
    }

    private void PolishSettings()
    {
        if (MainTabs.Items.Count < 6 || MainTabs.Items[5] is not TabItem tab ||
            tab.Content is not ScrollViewer scroll || scroll.Content is not Border panel)
        {
            return;
        }

        SetSurface(panel, "#1C1C1C", "#343434", 4);
        panel.Padding = new Thickness(18);
        panel.Margin = new Thickness(8, 0, 6, 6);
    }

    private static void SetSurface(Border border, string background, string outline, double radius)
    {
        border.Background = Brush(background);
        border.BorderBrush = Brush(outline);
        border.CornerRadius = new CornerRadius(radius);
    }

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));
}
