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
    private bool _dashboardLayoutApplied;
    private bool _embeddedProductionInitialized;

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

        ApplyPythonDashboardLayout();
        EnsureEmbeddedProductionHost();
        InitializeProjectsWorkflow();
        InitializeNewFactWorkflow();
        InitializeMediaLibraryWorkflow();

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

    private void EnsureEmbeddedProductionHost()
    {
        if (_embeddedProductionInitialized)
        {
            return;
        }

        _embeddedProductionInitialized = true;
        _ = InitializeEmbeddedProductionAsync();
        Closed += async (_, _) => await DisposeEmbeddedProductionAsync();

        if (Content is not DependencyObject root)
        {
            return;
        }

        foreach (var button in FindVisualChildren<Button>(root))
        {
            if (button.Tag is not null)
            {
                continue;
            }

            var text = button.Content?.ToString() ?? "";
            if (!text.Contains("Production", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            button.Click -= OpenProduction_Click;
            button.Click += EmbeddedProductionNavigation_Click;
        }
    }

    private void EmbeddedProductionNavigation_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 2;
        ApplyNavigationSelection(2);
    }

    private void ApplyPythonDashboardLayout()
    {
        if (_dashboardLayoutApplied || MainTabs.Items.Count == 0 || MainTabs.Items[0] is not TabItem tab)
        {
            return;
        }

        Detach(TotalCountText);
        Detach(InProgressCountText);
        Detach(CompletedCountText);
        Detach(ScheduledCountText);
        Detach(PublishedCountText);

        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var page = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        root.Content = page;

        page.Children.Add(new TextBlock
        {
            Text = "Dashboard",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
        });
        page.Children.Add(new TextBlock
        {
            Text = "Overview of your content workspace",
            Margin = new Thickness(0, 4, 0, 18),
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 13,
        });

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        for (var i = 0; i < 5; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        AddDashboardCard(stats, 0, "Projects", TotalCountText);
        AddDashboardCard(stats, 1, "In Progress", InProgressCountText);
        AddDashboardCard(stats, 2, "Completed", CompletedCountText);
        AddDashboardCard(stats, 3, "Scheduled", ScheduledCountText);
        AddDashboardCard(stats, 4, "Published", PublishedCountText, true);
        page.Children.Add(stats);

        page.Children.Add(DashboardSectionTitle("Quick actions"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 22) };
        var newProject = DashboardPrimaryButton("New Fact");
        newProject.Click += (_, _) => ShowNewFactWorkspace();
        actions.Children.Add(newProject);
        var projects = DashboardSecondaryButton("Projects");
        projects.Click += (_, _) =>
        {
            MainTabs.SelectedIndex = 1;
            ApplyNavigationSelection(1);
        };
        actions.Children.Add(projects);
        var production = DashboardSecondaryButton("Production");
        production.Click += EmbeddedProductionNavigation_Click;
        actions.Children.Add(production);
        page.Children.Add(actions);

        var recentHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        recentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        recentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        recentHeader.Children.Add(DashboardSectionTitle("Recent projects"));
        var viewAll = new Button
        {
            Content = "View all  →",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            Padding = new Thickness(8, 3, 8, 3),
        };
        viewAll.Click += (_, _) =>
        {
            MainTabs.SelectedIndex = 1;
            ApplyNavigationSelection(1);
        };
        Grid.SetColumn(viewAll, 1);
        recentHeader.Children.Add(viewAll);
        page.Children.Add(recentHeader);

        var recent = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.None,
            RowHeight = 46,
            MaxHeight = 250,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)),
            ItemsSource = _projects.Take(5).ToList(),
        };
        recent.Columns.Add(new DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding(nameof(DesktopProject.Title)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        recent.Columns.Add(new DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding(nameof(DesktopProject.Category)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        recent.Columns.Add(new DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding(nameof(DesktopProject.Status)),
            Width = new DataGridLength(150),
        });
        page.Children.Add(recent);

        tab.Content = root;
        _dashboardLayoutApplied = true;
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
            button.BorderThickness = new Thickness(3, 0, 0, 0);
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static void AddDashboardCard(Grid grid, int column, string label, TextBlock value, bool last = false)
    {
        value.FontFamily = new FontFamily("Segoe UI Variable Display");
        value.FontSize = 27;
        value.FontWeight = FontWeights.SemiBold;
        value.Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31));
        value.Margin = new Thickness(0, 0, 0, 3);

        var content = new StackPanel();
        content.Children.Add(value);
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 12,
        });

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 13, 14, 12),
            Margin = last ? new Thickness(0) : new Thickness(0, 0, 8, 0),
            Child = content,
        };
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static TextBlock DashboardSectionTitle(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Display"),
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
    };

    private static Button DashboardPrimaryButton(string text) => new()
    {
        Content = text,
        Height = 36,
        Padding = new Thickness(14, 0, 14, 0),
        Margin = new Thickness(0, 0, 8, 0),
        Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
    };

    private static Button DashboardSecondaryButton(string text) => new()
    {
        Content = text,
        Height = 36,
        Padding = new Thickness(14, 0, 14, 0),
        Margin = new Thickness(0, 0, 8, 0),
        Background = Brushes.White,
        Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
    };

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
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
