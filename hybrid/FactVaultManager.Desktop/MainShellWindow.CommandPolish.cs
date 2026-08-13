using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _commandPolishApplied;
    private StackPanel? _projectEditorStack;

    private void ApplyCommandPolish()
    {
        if (_commandPolishApplied)
        {
            return;
        }
        _commandPolishApplied = true;

        StyleGridSelection(ProjectsGrid);
        StyleGridSelection(MediaGrid);
        StyleGridSelection(AssetReviewGrid);
        AddProjectEditorModes();
    }

    private void StyleGridSelection(DataGrid grid)
    {
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.RowHeight = 36;
        grid.ColumnHeaderHeight = 34;
        grid.BorderThickness = new Thickness(1);
        grid.BorderBrush = MakeBrush("#343434");

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#1B1B1B")));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, MakeBrush("#F2F2F2")));
        rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#153E75")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        rowStyle.Triggers.Add(selected);
        grid.RowStyle = rowStyle;
    }

    private void AddProjectEditorModes()
    {
        if (MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab ||
            projectsTab.Content is not Grid projectsGrid)
        {
            return;
        }

        var editorBorder = projectsGrid.Children.OfType<Border>()
            .FirstOrDefault(border => Grid.GetColumn(border) == 2);
        if (editorBorder?.Child is not ScrollViewer editorScroll || editorScroll.Content is not StackPanel editorStack)
        {
            return;
        }

        _projectEditorStack = editorStack;
        var commandBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 12),
        };

        commandBar.Children.Add(MakeModeButton("Overview", "overview"));
        commandBar.Children.Add(MakeModeButton("Content", "content"));
        commandBar.Children.Add(MakeModeButton("Social", "social"));
        commandBar.Children.Add(MakeModeButton("Notes & Sources", "notes"));

        var insertIndex = editorStack.Children.IndexOf(ProjectEditorFolderText) + 1;
        editorStack.Children.Insert(insertIndex, commandBar);
        ShowProjectEditorMode("overview");
    }

    private Button MakeModeButton(string label, string mode)
    {
        var button = new Button
        {
            Content = label,
            Tag = mode,
            Height = 34,
            MinWidth = 88,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Background = MakeBrush("#252525"),
            BorderBrush = MakeBrush("#3A3A3A"),
            Foreground = MakeBrush("#F2F2F2"),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
        };
        button.Click += (_, _) => ShowProjectEditorMode(mode);
        return button;
    }

    private void ShowProjectEditorMode(string mode)
    {
        if (_projectEditorStack is null)
        {
            return;
        }

        SetFieldVisibility(ProjectScriptTextBox, mode == "content");
        SetFieldVisibility(ProjectDescriptionTextBox, mode == "content");
        SetFieldVisibility(ProjectPinnedCommentTextBox, mode == "social");
        SetFieldVisibility(ProjectTagsTextBox, mode == "social");
        SetFieldVisibility(ProjectNotesTextBox, mode == "notes");
        SetFieldVisibility(ProjectSourcesTextBox, mode == "notes");

        ProjectCategoryTextBox.Visibility = mode == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ProjectStatusComboBox.Visibility = mode == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ProjectPinnedCheckBox.Visibility = mode == "overview" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var label in _projectEditorStack.Children.OfType<TextBlock>())
        {
            label.Visibility = label.Text switch
            {
                "Script" or "Description" => mode == "content" ? Visibility.Visible : Visibility.Collapsed,
                "Pinned Comment" or "Tags" => mode == "social" ? Visibility.Visible : Visibility.Collapsed,
                "Notes" or "Sources" => mode == "notes" ? Visibility.Visible : Visibility.Collapsed,
                _ => label == ProjectEditorTitle || label == ProjectEditorFolderText ? Visibility.Visible : label.Visibility,
            };
        }

        foreach (var grid in _projectEditorStack.Children.OfType<Grid>())
        {
            if (grid.Children.Contains(ProjectCategoryTextBox) || grid.Children.OfType<StackPanel>().Any(p => p.Children.Contains(ProjectCategoryTextBox)))
            {
                grid.Visibility = mode == "overview" ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static void SetFieldVisibility(FrameworkElement field, bool visible)
    {
        field.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
