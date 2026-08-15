using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private WrapPanel? _projectCardsPanel;
    private ScrollViewer? _projectCardsScrollViewer;
    private bool _projectCardsInitialized;

    private void InitializeProjectCardsWorkflow()
    {
        if (_projectCardsInitialized || _projectsWorkspaceTabs is null || _projectsWorkspaceTabs.Items.Count == 0)
            return;

        if (_projectsWorkspaceTabs.Items[0] is not TabItem browseTab)
            return;

        var host = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        ProjectsGrid.Visibility = Visibility.Collapsed;
        Detach(ProjectsGrid);
        host.Children.Add(ProjectsGrid);

        _projectCardsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _projectCardsScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _projectCardsPanel,
        };
        host.Children.Add(_projectCardsScrollViewer);
        browseTab.Content = host;
        _projectCardsInitialized = true;
    }

    private void RenderProjectCards(IReadOnlyList<DesktopProject> projects)
    {
        InitializeProjectCardsWorkflow();
        if (_projectCardsPanel is null)
            return;

        _projectCardsPanel.Children.Clear();

        if (projects.Count == 0)
        {
            _projectCardsPanel.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = ProjectCardBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22),
                Margin = new Thickness(4),
                Width = 640,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "No projects found", FontSize = 16, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = "Try another search or status filter.", Foreground = ProjectCardMutedBrush(), Margin = new Thickness(0, 4, 0, 0) },
                    }
                }
            });
            return;
        }

        foreach (var project in projects)
            _projectCardsPanel.Children.Add(BuildProjectCard(project));

        UpdateProjectCardSelectionStyles();
    }

    private Border BuildProjectCard(DesktopProject project)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = ProjectCardBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(4),
            Width = 500,
            MinHeight = 164,
            Cursor = Cursors.Hand,
            Tag = project,
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = project.Title,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0),
        });
        var status = new Border
        {
            Background = ProjectStatusBackground(project.Status),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = project.Status,
                FontSize = 11,
                Foreground = ProjectStatusForeground(project.Status),
                FontWeight = FontWeights.SemiBold,
            },
        };
        Grid.SetColumn(status, 1);
        heading.Children.Add(status);
        content.Children.Add(heading);

        var meta = new TextBlock
        {
            Text = project.Pinned ? $"★  {project.Category}   •   {project.Created}" : $"{project.Category}   •   {project.Created}",
            Foreground = ProjectCardMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(meta, 1);
        content.Children.Add(meta);

        var detail = new TextBlock
        {
            Text = ProjectCardSummary(project),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 42,
            Margin = new Thickness(0, 11, 0, 12),
        };
        Grid.SetRow(detail, 2);
        content.Children.Add(detail);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var edit = ProjectCardButton("Edit");
        edit.Click += (_, e) =>
        {
            e.Handled = true;
            OpenProjectEditor(project);
        };
        actions.Children.Add(edit);

        var production = ProjectCardButton("Production");
        production.Click += (_, e) =>
        {
            e.Handled = true;
            OpenProjectInProduction(project);
        };
        actions.Children.Add(production);

        var folder = ProjectCardButton("Open folder");
        folder.Click += (_, e) =>
        {
            e.Handled = true;
            OpenProjectFolderFromCard(project);
        };
        actions.Children.Add(folder);
        Grid.SetRow(actions, 3);
        content.Children.Add(actions);

        card.Child = content;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is Button)
                return;
            ProjectsGrid.SelectedItem = project;
            UpdateProjectBrowserStatus();
            UpdateProjectCardSelectionStyles();
        };
        return card;
    }

    private void UpdateProjectCardSelectionStyles()
    {
        if (_projectCardsPanel is null)
            return;

        var selectedId = (ProjectsGrid.SelectedItem as DesktopProject)?.Id;
        foreach (var card in _projectCardsPanel.Children.OfType<Border>())
        {
            if (card.Tag is not DesktopProject project)
                continue;

            var selected = selectedId == project.Id;
            card.Background = selected
                ? new SolidColorBrush(Color.FromRgb(242, 247, 255))
                : Brushes.White;
            card.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(15, 108, 189))
                : ProjectCardBorderBrush();
            card.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void OpenProjectEditor(DesktopProject project)
    {
        ProjectsGrid.SelectedItem = project;
        UpdateProjectCardSelectionStyles();
        if (_projectsWorkspaceTabs is not null && _projectsWorkspaceTabs.Items.Count > 1)
        {
            _projectsWorkspaceTabs.SelectedIndex = 1;
            UpdateProjectsModeLayout();
        }
    }

    private void OpenProjectInProduction(DesktopProject project)
    {
        ProjectsGrid.SelectedItem = project;
        UpdateProjectCardSelectionStyles();
        MainTabs.SelectedIndex = 2;
        ApplyNavigationSelection(2);

        if (_productionProjectComboBox is not null)
        {
            RefreshNativeProductionProjects(project.Id);
            var match = _productionProjects.FirstOrDefault(item => item.Id == project.Id);
            if (match is not null)
                _productionProjectComboBox.SelectedItem = match;
        }
    }

    private void OpenProjectFolderFromCard(DesktopProject project)
    {
        ProjectsGrid.SelectedItem = project;
        UpdateProjectCardSelectionStyles();
        try
        {
            var folder = _data.ResolveProjectFolder(project);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Open Project Folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ProjectCardSummary(DesktopProject project)
    {
        var script = (project.Script ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(script))
            return script.Length <= 150 ? script : script[..147].TrimEnd() + "…";
        return "No script content yet.";
    }

    private static Button ProjectCardButton(string text) => new()
    {
        Content = text,
        Height = 31,
        Padding = new Thickness(11, 0, 11, 0),
        Margin = new Thickness(0, 0, 7, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
        Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
    };

    private static Brush ProjectCardBorderBrush() => new SolidColorBrush(Color.FromRgb(228, 231, 236));
    private static Brush ProjectCardMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));

    private static Brush ProjectStatusBackground(string status) => status switch
    {
        "Completed" => new SolidColorBrush(Color.FromRgb(236, 253, 243)),
        "Published" => new SolidColorBrush(Color.FromRgb(238, 244, 255)),
        "Scheduled" => new SolidColorBrush(Color.FromRgb(255, 250, 235)),
        _ => new SolidColorBrush(Color.FromRgb(239, 244, 255)),
    };

    private static Brush ProjectStatusForeground(string status) => status switch
    {
        "Completed" => new SolidColorBrush(Color.FromRgb(2, 122, 72)),
        "Published" => new SolidColorBrush(Color.FromRgb(53, 56, 205)),
        "Scheduled" => new SolidColorBrush(Color.FromRgb(181, 71, 8)),
        _ => new SolidColorBrush(Color.FromRgb(23, 92, 211)),
    };
}
