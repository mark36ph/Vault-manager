using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool NativeProjectsClassHandlerRegistered = RegisterNativeProjectsClassHandler();
    private bool _nativeProjectsApplied;

    private static bool RegisterNativeProjectsClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not MainShellWindow window) return;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.SystemIdle,
                    new Action(window.ApplyNativeProjectsWorkspace));
            }));
        return true;
    }

    private void ApplyNativeProjectsWorkspace()
    {
        _ = NativeProjectsClassHandlerRegistered;
        if (_nativeProjectsApplied || MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab)
        {
            return;
        }

        _nativeProjectsApplied = true;

        var oldRoot = projectsTab.Content as Grid;
        var newProjectButton = FindButton(oldRoot, "New Project", "+  New project");
        var saveButton = FindButton(oldRoot, "Save Project");
        var applyStatusButton = FindButton(oldRoot, "Apply Status");
        var deleteButton = FindButton(oldRoot, "Delete Project");

        Detach(NewProjectTitleTextBox);
        Detach(ProjectsGrid);
        Detach(ProjectEditorTitle);
        Detach(ProjectEditorFolderText);
        Detach(ProjectCategoryTextBox);
        Detach(ProjectStatusComboBox);
        Detach(ProjectPinnedCheckBox);
        Detach(ProjectScriptTextBox);
        Detach(ProjectDescriptionTextBox);
        Detach(ProjectPinnedCommentTextBox);
        Detach(ProjectTagsTextBox);
        Detach(ProjectNotesTextBox);
        Detach(ProjectSourcesTextBox);
        Detach(newProjectButton);
        Detach(saveButton);
        Detach(applyStatusButton);
        Detach(deleteButton);

        var root = new Grid
        {
            Margin = new Thickness(10, 4, 8, 8),
            Background = Brushes.Transparent,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var commandBar = BuildProjectsCommandBar(newProjectButton);
        Grid.SetRow(commandBar, 0);
        root.Children.Add(commandBar);

        var separator = new Border { Background = NativeBrush("#303030"), Height = 1, Margin = new Thickness(0, 10, 0, 10) };
        Grid.SetRow(separator, 1);
        root.Children.Add(separator);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        var explorer = BuildProjectExplorer();
        Grid.SetColumn(explorer, 0);
        workspace.Children.Add(explorer);

        var divider = new Border { Background = NativeBrush("#303030") };
        Grid.SetColumn(divider, 1);
        workspace.Children.Add(divider);

        var details = BuildProjectDetails(saveButton, applyStatusButton, deleteButton);
        Grid.SetColumn(details, 2);
        workspace.Children.Add(details);

        projectsTab.Content = root;
    }

    private FrameworkElement BuildProjectsCommandBar(Button? newProjectButton)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "Projects",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Manage project content, status and publishing data",
            FontSize = 11.5,
            Foreground = NativeBrush("#9B9B9B"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        grid.Children.Add(heading);

        NewProjectTitleTextBox.Height = 34;
        NewProjectTitleTextBox.Margin = new Thickness(0, 0, 8, 0);
        NewProjectTitleTextBox.VerticalContentAlignment = VerticalAlignment.Center;
        NewProjectTitleTextBox.ToolTip = "Enter a project title";
        Grid.SetColumn(NewProjectTitleTextBox, 2);
        grid.Children.Add(NewProjectTitleTextBox);

        if (newProjectButton is not null)
        {
            newProjectButton.Content = "+  New project";
            newProjectButton.Height = 34;
            newProjectButton.Padding = new Thickness(12, 0, 12, 0);
            newProjectButton.Margin = new Thickness(0);
            Grid.SetColumn(newProjectButton, 3);
            grid.Children.Add(newProjectButton);
        }

        return grid;
    }

    private FrameworkElement BuildProjectExplorer()
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "All projects",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var count = new TextBlock
        {
            Text = _projects.Count.ToString(),
            FontSize = 11,
            Foreground = NativeBrush("#9B9B9B"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        panel.Children.Add(header);

        ProjectsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        ProjectsGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        ProjectsGrid.BorderThickness = new Thickness(1);
        ProjectsGrid.BorderBrush = NativeBrush("#333333");
        ProjectsGrid.Background = NativeBrush("#181818");
        ProjectsGrid.RowBackground = NativeBrush("#181818");
        ProjectsGrid.AlternatingRowBackground = NativeBrush("#1C1C1C");
        ProjectsGrid.RowHeight = 40;
        ProjectsGrid.ColumnHeaderHeight = 32;
        ProjectsGrid.Margin = new Thickness(0);
        ProjectsGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        ProjectsGrid.VerticalAlignment = VerticalAlignment.Stretch;
        if (ProjectsGrid.Columns.Count >= 2)
        {
            ProjectsGrid.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            ProjectsGrid.Columns[1].Width = new DataGridLength(105);
        }
        Grid.SetRow(ProjectsGrid, 1);
        panel.Children.Add(ProjectsGrid);

        return panel;
    }

    private FrameworkElement BuildProjectDetails(Button? saveButton, Button? applyStatusButton, Button? deleteButton)
    {
        var root = new Grid { Margin = new Thickness(18, 0, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel();
        ProjectEditorTitle.FontFamily = new FontFamily("Segoe UI Variable Display");
        ProjectEditorTitle.FontSize = 22;
        ProjectEditorTitle.FontWeight = FontWeights.SemiBold;
        ProjectEditorTitle.Margin = new Thickness(0);
        titles.Children.Add(ProjectEditorTitle);
        ProjectEditorFolderText.FontSize = 11;
        ProjectEditorFolderText.Foreground = NativeBrush("#9B9B9B");
        ProjectEditorFolderText.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(ProjectEditorFolderText);
        header.Children.Add(titles);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (saveButton is not null)
        {
            saveButton.Content = "Save";
            StyleWorkspaceButton(saveButton, true);
            actions.Children.Add(saveButton);
        }
        if (applyStatusButton is not null)
        {
            applyStatusButton.Content = "Apply status";
            StyleWorkspaceButton(applyStatusButton, false);
            actions.Children.Add(applyStatusButton);
        }
        if (deleteButton is not null)
        {
            deleteButton.Content = "Delete";
            StyleWorkspaceButton(deleteButton, false);
            actions.Children.Add(deleteButton);
        }
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var metadata = BuildMetadataStrip();
        Grid.SetRow(metadata, 1);
        root.Children.Add(metadata);

        var sections = BuildProjectSectionTabs();
        Grid.SetRow(sections, 2);
        root.Children.Add(sections);
        return root;
    }

    private FrameworkElement BuildMetadataStrip()
    {
        var border = new Border
        {
            Background = NativeBrush("#1B1B1B"),
            BorderBrush = NativeBrush("#333333"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var category = LabeledField("Category", ProjectCategoryTextBox);
        grid.Children.Add(category);

        var status = LabeledField("Status", ProjectStatusComboBox);
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);

        ProjectPinnedCheckBox.VerticalAlignment = VerticalAlignment.Bottom;
        ProjectPinnedCheckBox.Margin = new Thickness(0, 0, 0, 5);
        Grid.SetColumn(ProjectPinnedCheckBox, 4);
        grid.Children.Add(ProjectPinnedCheckBox);

        border.Child = grid;
        return border;
    }

    private FrameworkElement BuildProjectSectionTabs()
    {
        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderBrush = NativeBrush("#333333"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
        };
        tabs.Resources[typeof(TabItem)] = MakeWorkspaceTabStyle();

        tabs.Items.Add(MakeSectionTab("Content", StackFields(
            ("Script", ProjectScriptTextBox, 235),
            ("Description", ProjectDescriptionTextBox, 105))));
        tabs.Items.Add(MakeSectionTab("Social", StackFields(
            ("Pinned comment", ProjectPinnedCommentTextBox, 105),
            ("Tags", ProjectTagsTextBox, 34))));
        tabs.Items.Add(MakeSectionTab("Notes & Sources", StackFields(
            ("Notes", ProjectNotesTextBox, 150),
            ("Sources", ProjectSourcesTextBox, 135))));

        return tabs;
    }

    private static TabItem MakeSectionTab(string header, FrameworkElement content) =>
        new() { Header = header, Content = content };

    private FrameworkElement StackFields(params (string label, FrameworkElement field, double height)[] fields)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        foreach (var (label, field, height) in fields)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 12, 0, 5),
                Foreground = NativeBrush("#D8D8D8"),
            });
            field.Height = height;
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (field is TextBox textBox)
            {
                textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                textBox.TextWrapping = TextWrapping.Wrap;
            }
            panel.Children.Add(field);
        }
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private static FrameworkElement LabeledField(string label, FrameworkElement field)
    {
        field.Height = 32;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = NativeBrush("#B8B8B8"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(field);
        return panel;
    }

    private static Style MakeWorkspaceTabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 36.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 0, 14, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        style.Setters.Add(new Setter(Control.ForegroundProperty, NativeBrush("#D8D8D8")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, NativeBrush("#202020")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, NativeBrush("#333333")));
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, NativeBrush("#2A2A2A")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(selected);
        return style;
    }

    private static void StyleWorkspaceButton(Button button, bool primary)
    {
        button.Height = 32;
        button.Padding = new Thickness(11, 0, 11, 0);
        button.Margin = new Thickness(0, 0, 6, 0);
        button.FontSize = 12;
        button.FontWeight = FontWeights.Normal;
        button.Background = NativeBrush(primary ? "#2563EB" : "#232323");
        button.BorderBrush = NativeBrush(primary ? "#3B82F6" : "#3A3A3A");
    }

    private static Button? FindButton(DependencyObject? root, params string[] labels)
    {
        if (root is null) return null;
        if (root is Button current && labels.Any(label => string.Equals(current.Content?.ToString(), label, StringComparison.OrdinalIgnoreCase)))
        {
            return current;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindButton(VisualTreeHelper.GetChild(root, i), labels);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Detach(FrameworkElement? element)
    {
        if (element is null) return;
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
        }
    }

    private static SolidColorBrush NativeBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
