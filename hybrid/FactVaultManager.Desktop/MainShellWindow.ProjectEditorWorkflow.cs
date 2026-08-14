using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _projectEditorWorkflowInitialized;
    private TextBlock? _projectScriptCounter;
    private TextBlock? _projectOnScreenCounter;

    private void InitializeProjectEditorWorkflow()
    {
        if (_projectEditorWorkflowInitialized || _projectsWorkspaceTabs is null || _projectsWorkspaceTabs.Items.Count < 2)
            return;

        InitializeProjectMetadataEditor();
        if (_projectOnScreenTextBox is null || _projectVisualPlanTextBox is null)
            return;

        if (_projectsWorkspaceTabs.Items[1] is not TabItem editorTab)
            return;

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
        Detach(_projectOnScreenTextBox);
        Detach(_projectVisualPlanTextBox);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var page = new StackPanel { Margin = new Thickness(2, 12, 4, 22) };
        scroll.Content = page;

        page.Children.Add(BuildProjectEditorHeader());
        page.Children.Add(BuildProjectEditorDetails());
        page.Children.Add(BuildProjectEditorColumns());

        editorTab.Content = scroll;
        _projectEditorWorkflowInitialized = true;

        ProjectScriptTextBox.TextChanged += (_, _) => UpdateProjectEditorCounters();
        _projectOnScreenTextBox.TextChanged += (_, _) => UpdateProjectEditorCounters();
        UpdateProjectEditorCounters();

        if (ProjectsGrid.SelectedItem is DesktopProject project)
            ApplyProjectProductionMetadata(project);
    }

    private FrameworkElement BuildProjectEditorHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = ProjectEditorSecondaryButton("←  Back to Projects");
        back.Margin = new Thickness(0, 0, 14, 0);
        back.Click += (_, _) =>
        {
            if (_projectsWorkspaceTabs is null) return;
            _projectsWorkspaceTabs.SelectedIndex = 0;
            UpdateProjectsModeLayout();
        };
        header.Children.Add(back);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ProjectEditorTitle.FontFamily = new FontFamily("Segoe UI Variable Display");
        ProjectEditorTitle.FontSize = 25;
        ProjectEditorTitle.FontWeight = FontWeights.SemiBold;
        ProjectEditorTitle.Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31));
        ProjectEditorFolderText.Foreground = ProjectEditorMutedBrush();
        ProjectEditorFolderText.FontSize = 11;
        ProjectEditorFolderText.Margin = new Thickness(0, 2, 0, 0);
        identity.Children.Add(ProjectEditorTitle);
        identity.Children.Add(ProjectEditorFolderText);
        Grid.SetColumn(identity, 1);
        header.Children.Add(identity);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var save = ProjectEditorPrimaryButton("Save");
        save.Click += SaveProject_Click;
        actions.Children.Add(save);

        var production = ProjectEditorSecondaryButton("Production");
        production.Click += (_, _) =>
        {
            if (_editingProject is not null) OpenProjectInProduction(_editingProject);
        };
        actions.Children.Add(production);

        var folder = ProjectEditorSecondaryButton("Open folder");
        folder.Click += (_, _) =>
        {
            if (_editingProject is not null) OpenProjectFolderFromCard(_editingProject);
        };
        actions.Children.Add(folder);

        var delete = ProjectEditorDangerButton("Delete");
        delete.Click += DeleteProject_Click;
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);
        return header;
    }

    private FrameworkElement BuildProjectEditorDetails()
    {
        var card = ProjectEditorCard(new Thickness(16));
        card.Margin = new Thickness(0, 0, 0, 12);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Project Details",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Edit content and publishing information here. Run assets, voice, captions and Resolve export from Production.",
            Foreground = ProjectEditorMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        });

        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var category = new StackPanel();
        category.Children.Add(ProjectEditorLabel("Category"));
        ProjectCategoryTextBox.Margin = new Thickness(0, 4, 0, 0);
        category.Children.Add(ProjectCategoryTextBox);
        fields.Children.Add(category);

        var status = new StackPanel();
        status.Children.Add(ProjectEditorLabel("Status"));
        ProjectStatusComboBox.Margin = new Thickness(0, 4, 0, 0);
        status.Children.Add(ProjectStatusComboBox);
        Grid.SetColumn(status, 2);
        fields.Children.Add(status);

        ProjectPinnedCheckBox.VerticalAlignment = VerticalAlignment.Bottom;
        ProjectPinnedCheckBox.Margin = new Thickness(0, 0, 10, 8);
        Grid.SetColumn(ProjectPinnedCheckBox, 4);
        fields.Children.Add(ProjectPinnedCheckBox);

        var apply = ProjectEditorSecondaryButton("Apply status");
        apply.Margin = new Thickness(0, 20, 0, 0);
        apply.Click += ApplyStatus_Click;
        Grid.SetColumn(apply, 5);
        fields.Children.Add(apply);

        content.Children.Add(fields);
        card.Child = content;
        return card;
    }

    private FrameworkElement BuildProjectEditorColumns()
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 520 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 360 });

        var production = ProjectEditorCard(new Thickness(16));
        var productionContent = new StackPanel();
        productionContent.Children.Add(ProjectEditorSectionHeading("Production Content"));
        productionContent.Children.Add(ProjectEditorSectionHelp("Script and timed on-screen wording used by the production engine."));
        AddProjectEditorBox(productionContent, "Script", ProjectScriptTextBox, 255);
        _projectScriptCounter = ProjectEditorCounter();
        productionContent.Children.Add(_projectScriptCounter);
        AddProjectEditorBox(productionContent, "On-Screen Text", _projectOnScreenTextBox!, 205);
        _projectOnScreenCounter = ProjectEditorCounter();
        productionContent.Children.Add(_projectOnScreenCounter);
        AddProjectEditorBox(productionContent, "Visual Plan", _projectVisualPlanTextBox!, 150);
        production.Child = productionContent;
        columns.Children.Add(production);

        var publishing = ProjectEditorCard(new Thickness(16));
        var publishingContent = new StackPanel();
        publishingContent.Children.Add(ProjectEditorSectionHeading("Publishing"));
        publishingContent.Children.Add(ProjectEditorSectionHelp("Social metadata, source notes and supporting project information."));
        AddProjectEditorBox(publishingContent, "Description", ProjectDescriptionTextBox, 105);
        AddProjectEditorBox(publishingContent, "Pinned Comment", ProjectPinnedCommentTextBox, 90);
        AddProjectEditorBox(publishingContent, "Tags", ProjectTagsTextBox, 70);
        AddProjectEditorBox(publishingContent, "Sources", ProjectSourcesTextBox, 100);
        AddProjectEditorBox(publishingContent, "Notes", ProjectNotesTextBox, 125);
        publishing.Child = publishingContent;
        Grid.SetColumn(publishing, 2);
        columns.Children.Add(publishing);
        return columns;
    }

    private void UpdateProjectEditorCounters()
    {
        if (_projectScriptCounter is not null)
            _projectScriptCounter.Text = ProjectEditorCountText(ProjectScriptTextBox.Text);
        if (_projectOnScreenCounter is not null && _projectOnScreenTextBox is not null)
            _projectOnScreenCounter.Text = ProjectEditorCountText(_projectOnScreenTextBox.Text);
    }

    private static string ProjectEditorCountText(string text)
    {
        var trimmed = (text ?? "").Trim();
        var words = string.IsNullOrWhiteSpace(trimmed)
            ? 0
            : trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return $"Words: {words}  |  Characters: {trimmed.Length}";
    }

    private static void AddProjectEditorBox(Panel parent, string label, TextBox box, double height)
    {
        parent.Children.Add(ProjectEditorLabel(label));
        box.Height = height;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.Margin = new Thickness(0, 5, 0, 12);
        parent.Children.Add(box);
    }

    private static Border ProjectEditorCard(Thickness padding) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
    };

    private static TextBlock ProjectEditorSectionHeading(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Display"),
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock ProjectEditorSectionHelp(string text) => new()
    {
        Text = text,
        Foreground = ProjectEditorMutedBrush(),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 14),
    };

    private static TextBlock ProjectEditorLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock ProjectEditorCounter() => new()
    {
        Foreground = ProjectEditorMutedBrush(),
        FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, -7, 0, 10),
    };

    private static Brush ProjectEditorMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));

    private static Button ProjectEditorPrimaryButton(string text) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(13, 0, 13, 0),
        Margin = new Thickness(0, 0, 7, 0),
        Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
        Foreground = Brushes.White,
        FontWeight = FontWeights.SemiBold,
    };

    private static Button ProjectEditorSecondaryButton(string text) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(13, 0, 13, 0),
        Margin = new Thickness(0, 0, 7, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
        Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
    };

    private static Button ProjectEditorDangerButton(string text) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(13, 0, 13, 0),
        Margin = new Thickness(0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(253, 162, 155)),
        Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24)),
    };
}
