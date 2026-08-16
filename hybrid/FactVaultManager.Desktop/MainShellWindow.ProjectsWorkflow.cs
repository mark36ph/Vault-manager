using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private TextBox? _projectsSearchBox;
    private TextBlock? _projectsResultCount;
    private readonly Dictionary<string, Button> _projectFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private string _projectsStatusFilter = "All";
    private TabControl? _projectsWorkspaceTabs;
    private UIElement? _projectsHeaderArea;
    private UIElement? _projectsFilterArea;
    private bool _projectsWorkflowInitialized;
    private bool _productBrandApplied;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ApplyProductBranding();
        InitializeProjectsWorkflow();
        ConfigureNewFactToolbar();
        InitializeProjectCardsWorkflow();
        InitializeProjectMetadataEditor();
        InitializeNewFactWorkflow();
        InitializeMediaLibraryWorkflow();
        InitializeQuizWorkflow();
        InitializeQuizQuestionBankPage();
        InitializeQuizExportWorkflow();
        Dispatcher.BeginInvoke(new Action(InitializeAssetReviewWorkflow));
        Dispatcher.BeginInvoke(new Action(InitializeSettingsWorkflow));
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(ApplyProductionPolish));
    }

    private void ApplyProductBranding()
    {
        if (_productBrandApplied)
            return;

        _productBrandApplied = true;
        Title = "Content Vault Manager";
        if (Content is not DependencyObject root)
            return;

        var brand = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(block => string.Equals(block.Text, "FactVaultManager", StringComparison.Ordinal));
        if (brand is not null)
            brand.Text = "Content Vault Manager";
    }

    private void InitializeProjectsWorkflow()
    {
        if (_projectsWorkflowInitialized || MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsPage)
            return;

        if (projectsPage.Content is not Grid projectsRoot)
            return;

        _projectsHeaderArea = projectsRoot.Children
            .Cast<UIElement>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        _projectsFilterArea = projectsRoot.Children
            .Cast<UIElement>()
            .FirstOrDefault(child => Grid.GetRow(child) == 1);
        _projectsWorkspaceTabs = projectsRoot.Children
            .OfType<TabControl>()
            .FirstOrDefault(control => Grid.GetRow(control) == 2);

        if (_projectsWorkspaceTabs is null)
            return;

        _projectsWorkflowInitialized = true;
        _projectsWorkspaceTabs.SelectionChanged += ProjectsWorkspaceTabs_SelectionChanged;
        ConfigureNewFactToolbar();

        var textBoxes = FindVisualChildren<TextBox>(projectsPage).ToList();
        _projectsSearchBox = textBoxes.FirstOrDefault(box =>
            box.IsReadOnly &&
            (box.Text.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
             (box.ToolTip?.ToString() ?? "").Contains("Search", StringComparison.OrdinalIgnoreCase)));

        if (_projectsSearchBox is not null)
        {
            _projectsSearchBox.IsReadOnly = false;
            _projectsSearchBox.Text = "";
            _projectsSearchBox.ToolTip = "Search by title or category";
            _projectsSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31));
            _projectsSearchBox.TextChanged += (_, _) => ApplyProjectsFilter();
        }

        foreach (var button in FindVisualChildren<Button>(projectsPage))
        {
            var label = button.Content?.ToString()?.Trim() ?? "";
            if (label is "All" or "In Progress" or "Scheduled" or "Completed" or "Published")
            {
                _projectFilterButtons[label] = button;
                button.Click += ProjectFilter_Click;
            }
        }

        var filterRow = FindVisualChildren<StackPanel>(projectsPage)
            .FirstOrDefault(panel =>
                panel.Orientation == Orientation.Horizontal &&
                panel.Children.OfType<Button>().Any(button =>
                    _projectFilterButtons.ContainsKey(button.Content?.ToString()?.Trim() ?? "")));

        if (filterRow is not null)
        {
            _projectsResultCount = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            filterRow.Children.Add(_projectsResultCount);
        }

        ProjectsGrid.MouseDoubleClick += ProjectsGrid_MouseDoubleClick;
        ProjectsGrid.SelectionChanged += (_, _) =>
        {
            UpdateProjectBrowserStatus();
            UpdateProjectCardSelectionStyles();
            if (_safeProjectEditorInitialized)
            {
                LoadSafeProjectEditor(ProjectsGrid.SelectedItem as DesktopProject);
                UpdateProjectWorkspaceActions();
            }
        };

        InitializeProjectCardsWorkflow();
        UpdateProjectFilterStyles();
        ApplyProjectsFilter();
        UpdateProjectsModeLayout();
        Dispatcher.BeginInvoke(new Action(InitializeProjectMetadataEditor));
    }

    private void ProjectsWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _projectsWorkspaceTabs))
            return;

        UpdateProjectsModeLayout();
    }

    private void UpdateProjectsModeLayout()
    {
        if (_projectsWorkspaceTabs is null)
            return;

        var editing = _projectsWorkspaceTabs.SelectedIndex == 1;
        if (_projectsHeaderArea is not null)
            _projectsHeaderArea.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        if (_projectsFilterArea is not null)
            _projectsFilterArea.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

        if (editing)
        {
            InitializeSafeProjectEditor();
            LoadSafeProjectEditor(ProjectsGrid.SelectedItem as DesktopProject ?? _editingProject);
            EnsureProjectWorkspaceActions();
            UpdateProjectWorkspaceActions();
        }
    }

    private void ProjectFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var requested = button.Content?.ToString()?.Trim() ?? "All";
        _projectsStatusFilter = _projectFilterButtons.ContainsKey(requested) ? requested : "All";
        UpdateProjectFilterStyles();
        ApplyProjectsFilter();
    }

    private void UpdateProjectFilterStyles()
    {
        foreach (var pair in _projectFilterButtons)
        {
            var selected = string.Equals(pair.Key, _projectsStatusFilter, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected
                ? new SolidColorBrush(Color.FromRgb(234, 242, 255))
                : Brushes.Transparent;
            pair.Value.Foreground = selected
                ? new SolidColorBrush(Color.FromRgb(23, 92, 211))
                : new SolidColorBrush(Color.FromRgb(71, 84, 103));
            pair.Value.BorderThickness = new Thickness(0);
            pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void ApplyProjectsFilter()
    {
        if (ProjectsGrid is null)
            return;

        var search = (_projectsSearchBox?.Text ?? "").Trim();
        var selectedId = (ProjectsGrid.SelectedItem as DesktopProject)?.Id;

        var filtered = _projects
            .Where(project =>
                (string.IsNullOrWhiteSpace(search) ||
                 project.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 project.Category.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (_projectsStatusFilter == "All" ||
                 string.Equals(project.Status, _projectsStatusFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(project => project.Pinned)
            .ThenBy(project => project.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProjectsGrid.ItemsSource = filtered;

        if (selectedId is int id)
            ProjectsGrid.SelectedItem = filtered.FirstOrDefault(project => project.Id == id);
        if (ProjectsGrid.SelectedItem is null && filtered.Count > 0)
            ProjectsGrid.SelectedIndex = 0;

        RenderProjectCards(filtered);

        if (_projectsResultCount is not null)
            _projectsResultCount.Text = filtered.Count == 1 ? "1 project" : $"{filtered.Count} projects";

        UpdateProjectBrowserStatus();
    }

    private void ProjectsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not DesktopProject)
            return;

        if (_projectsWorkspaceTabs is not null && _projectsWorkspaceTabs.Items.Count > 1)
        {
            _projectsWorkspaceTabs.SelectedIndex = 1;
            UpdateProjectsModeLayout();
        }
    }

    private void UpdateProjectBrowserStatus()
    {
        if (ProjectsGrid.SelectedItem is not DesktopProject project)
            return;

        HeaderStatusText.Text = $"Selected: {project.Title} • {project.Status}";
    }
}
