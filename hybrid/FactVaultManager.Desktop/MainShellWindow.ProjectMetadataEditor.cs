using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _projectMetadataEditorInitialized;
    private TextBox? _projectOnScreenTextBox;
    private TextBox? _projectVisualPlanTextBox;

    private void InitializeProjectMetadataEditor()
    {
        if (_projectMetadataEditorInitialized || _projectsWorkspaceTabs is null || _projectsWorkspaceTabs.Items.Count < 2)
            return;

        if (_projectsWorkspaceTabs.Items[1] is not TabItem editorTab || editorTab.Content is not DependencyObject editorContent)
            return;

        var editorSections = FindEditorSections(editorContent);
        if (editorSections is null)
            return;

        if (editorSections.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "Production Content", StringComparison.OrdinalIgnoreCase)))
        {
            _projectMetadataEditorInitialized = true;
            return;
        }

        var productionTab = new TabItem { Header = "Production Content" };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            productionTab.Style = sectionStyle;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var content = new StackPanel { Margin = new Thickness(16) };
        scroll.Content = content;

        content.Children.Add(new TextBlock
        {
            Text = "On-screen Text",
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Timed captions and on-screen wording used by Production.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 6),
        });
        _projectOnScreenTextBox = new TextBox
        {
            Height = 190,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 16),
        };
        content.Children.Add(_projectOnScreenTextBox);

        content.Children.Add(new TextBlock
        {
            Text = "Visual Plan",
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Scene and visual guidance kept separate from Notes and search instructions.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 6),
        });
        _projectVisualPlanTextBox = new TextBox
        {
            Height = 190,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        content.Children.Add(_projectVisualPlanTextBox);

        productionTab.Content = scroll;
        editorSections.Items.Insert(1, productionTab);
        _projectMetadataEditorInitialized = true;

        if (ProjectsGrid.SelectedItem is DesktopProject project)
            ApplyProjectProductionMetadata(project);
    }

    private static TabControl? FindEditorSections(DependencyObject root)
    {
        if (root is TabControl tabs)
            return tabs;

        if (root is ContentControl contentControl && contentControl.Content is DependencyObject contentChild)
        {
            var found = FindEditorSections(contentChild);
            if (found is not null)
                return found;
        }

        if (root is Decorator decorator && decorator.Child is DependencyObject decoratorChild)
        {
            var found = FindEditorSections(decoratorChild);
            if (found is not null)
                return found;
        }

        if (root is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                var found = FindEditorSections(child);
                if (found is not null)
                    return found;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;
            var found = FindEditorSections(dependencyObject);
            if (found is not null)
                return found;
        }

        return null;
    }

    private void ApplyProjectProductionMetadata(DesktopProject project)
    {
        if (_projectOnScreenTextBox is not null)
            _projectOnScreenTextBox.Text = project.OnScreenText;
        if (_projectVisualPlanTextBox is not null)
            _projectVisualPlanTextBox.Text = project.VisualPlan;
    }

    private string CurrentProjectOnScreenText() =>
        _projectOnScreenTextBox?.Text ?? _editingProject?.OnScreenText ?? "";

    private string CurrentProjectVisualPlan() =>
        _projectVisualPlanTextBox?.Text ?? _editingProject?.VisualPlan ?? "";
}
