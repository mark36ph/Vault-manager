using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _safeProjectEditorInitialized;
    private TextBlock? _safeEditorTitle;
    private TextBlock? _safeEditorFolder;
    private TextBox? _safeEditorCategory;
    private ComboBox? _safeEditorStatus;
    private CheckBox? _safeEditorPinned;
    private TextBox? _safeEditorScript;
    private TextBox? _safeEditorOnScreen;
    private TextBox? _safeEditorVisualPlan;
    private TextBox? _safeEditorDescription;
    private TextBox? _safeEditorPinnedComment;
    private TextBox? _safeEditorTags;
    private TextBox? _safeEditorSources;
    private TextBox? _safeEditorNotes;
    private TextBlock? _safeScriptCounter;
    private TextBlock? _safeOnScreenCounter;

    private void InitializeSafeProjectEditor()
    {
        if (_safeProjectEditorInitialized || _projectsWorkspaceTabs is null || _projectsWorkspaceTabs.Items.Count < 2)
            return;
        if (_projectsWorkspaceTabs.Items[1] is not TabItem editorTab)
            return;

        editorTab.Content = BuildSafeProjectEditorPage();
        _safeProjectEditorInitialized = true;
        LoadSafeProjectEditor(ProjectsGrid.SelectedItem as DesktopProject ?? _editingProject);
    }

    private FrameworkElement BuildSafeProjectEditorPage()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var page = new StackPanel { Margin = new Thickness(2, 2, 4, 24) };
        scroll.Content = page;

        page.Children.Add(BuildSafeEditorHeader());
        page.Children.Add(BuildSafeEditorDetails());
        page.Children.Add(BuildSafeEditorColumns());
        return scroll;
    }

    private FrameworkElement BuildSafeEditorHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = SafeEditorButton("←  Back to Projects");
        back.Margin = new Thickness(0, 0, 14, 0);
        back.Click += (_, _) =>
        {
            if (_projectsWorkspaceTabs is null) return;
            _projectsWorkspaceTabs.SelectedIndex = 0;
            UpdateProjectsModeLayout();
        };
        header.Children.Add(back);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _safeEditorTitle = new TextBlock
        {
            Text = "Select a project",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
        };
        _safeEditorFolder = new TextBlock
        {
            Foreground = SafeEditorMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        };
        identity.Children.Add(_safeEditorTitle);
        identity.Children.Add(_safeEditorFolder);
        Grid.SetColumn(identity, 1);
        header.Children.Add(identity);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var save = SafeEditorPrimaryButton("Save");
        save.Click += SafeProjectEditorSave_Click;
        actions.Children.Add(save);

        var production = SafeEditorButton("Production");
        production.Click += (_, _) =>
        {
            if (CurrentSafeEditorProject() is { } project) OpenProjectInProduction(project);
        };
        actions.Children.Add(production);

        var folder = SafeEditorButton("Open folder");
        folder.Click += (_, _) =>
        {
            if (CurrentSafeEditorProject() is { } project) OpenProjectFolderFromCard(project);
        };
        actions.Children.Add(folder);

        var delete = SafeEditorDangerButton("Delete");
        delete.Click += SafeProjectEditorDelete_Click;
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);
        return header;
    }

    private FrameworkElement BuildSafeEditorDetails()
    {
        var card = SafeEditorCard();
        card.Margin = new Thickness(0, 0, 0, 12);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Project Details", FontSize = 16, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = "Edit content and publishing information here. Run assets, voice, captions and Resolve export from Production.",
            Foreground = SafeEditorMutedBrush(),
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
        category.Children.Add(SafeEditorLabel("Category"));
        _safeEditorCategory = SafeEditorTextBox();
        _safeEditorCategory.Margin = new Thickness(0, 4, 0, 0);
        category.Children.Add(_safeEditorCategory);
        fields.Children.Add(category);

        var status = new StackPanel();
        status.Children.Add(SafeEditorLabel("Status"));
        _safeEditorStatus = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MinHeight = 34 };
        foreach (var value in new[] { "In Progress", "Scheduled", "Completed", "Published" })
            _safeEditorStatus.Items.Add(value);
        status.Children.Add(_safeEditorStatus);
        Grid.SetColumn(status, 2);
        fields.Children.Add(status);

        _safeEditorPinned = new CheckBox { Content = "Pinned", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 8) };
        Grid.SetColumn(_safeEditorPinned, 4);
        fields.Children.Add(_safeEditorPinned);

        var apply = SafeEditorButton("Apply status");
        apply.Margin = new Thickness(0, 20, 0, 0);
        apply.Click += SafeProjectEditorApplyStatus_Click;
        Grid.SetColumn(apply, 5);
        fields.Children.Add(apply);

        content.Children.Add(fields);
        card.Child = content;
        return card;
    }

    private FrameworkElement BuildSafeEditorColumns()
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 500 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 350 });

        var production = SafeEditorCard();
        var productionContent = new StackPanel();
        productionContent.Children.Add(SafeEditorHeading("Production Content"));
        productionContent.Children.Add(SafeEditorHelp("Script, timed on-screen wording and visual guidance used by Production."));
        _safeEditorScript = SafeEditorMultilineBox(255);
        AddSafeEditorField(productionContent, "Script", _safeEditorScript);
        _safeScriptCounter = SafeEditorCounter();
        productionContent.Children.Add(_safeScriptCounter);
        _safeEditorScript.TextChanged += (_, _) => UpdateSafeEditorCounters();

        _safeEditorOnScreen = SafeEditorMultilineBox(205);
        AddSafeEditorField(productionContent, "On-Screen Text", _safeEditorOnScreen);
        _safeOnScreenCounter = SafeEditorCounter();
        productionContent.Children.Add(_safeOnScreenCounter);
        _safeEditorOnScreen.TextChanged += (_, _) => UpdateSafeEditorCounters();

        _safeEditorVisualPlan = SafeEditorMultilineBox(155);
        AddSafeEditorField(productionContent, "Visual Plan", _safeEditorVisualPlan);
        production.Child = productionContent;
        columns.Children.Add(production);

        var publishing = SafeEditorCard();
        var publishingContent = new StackPanel();
        publishingContent.Children.Add(SafeEditorHeading("Publishing"));
        publishingContent.Children.Add(SafeEditorHelp("Description, social metadata, sources and project notes."));
        _safeEditorDescription = SafeEditorMultilineBox(105);
        AddSafeEditorField(publishingContent, "Description", _safeEditorDescription);
        _safeEditorPinnedComment = SafeEditorMultilineBox(90);
        AddSafeEditorField(publishingContent, "Pinned Comment", _safeEditorPinnedComment);
        _safeEditorTags = SafeEditorMultilineBox(70);
        AddSafeEditorField(publishingContent, "Tags", _safeEditorTags);
        _safeEditorSources = SafeEditorMultilineBox(100);
        AddSafeEditorField(publishingContent, "Sources", _safeEditorSources);
        _safeEditorNotes = SafeEditorMultilineBox(125);
        AddSafeEditorField(publishingContent, "Notes", _safeEditorNotes);
        publishing.Child = publishingContent;
        Grid.SetColumn(publishing, 2);
        columns.Children.Add(publishing);
        return columns;
    }

    private DesktopProject? CurrentSafeEditorProject() => ProjectsGrid.SelectedItem as DesktopProject ?? _editingProject;

    private void LoadSafeProjectEditor(DesktopProject? project)
    {
        if (!_safeProjectEditorInitialized || project is null) return;
        _safeEditorTitle!.Text = project.Title;
        try { _safeEditorFolder!.Text = _data.ResolveProjectFolder(project); }
        catch { _safeEditorFolder!.Text = project.Folder; }
        _safeEditorCategory!.Text = project.Category;
        _safeEditorStatus!.SelectedItem = project.Status;
        if (_safeEditorStatus.SelectedItem is null) _safeEditorStatus.SelectedIndex = 0;
        _safeEditorPinned!.IsChecked = project.Pinned;
        _safeEditorScript!.Text = project.Script;
        _safeEditorOnScreen!.Text = project.OnScreenText;
        _safeEditorVisualPlan!.Text = project.VisualPlan;
        _safeEditorDescription!.Text = project.Description;
        _safeEditorPinnedComment!.Text = project.PinnedComment;
        _safeEditorTags!.Text = project.Tags;
        _safeEditorSources!.Text = project.Sources;
        _safeEditorNotes!.Text = project.Notes;
        UpdateSafeEditorCounters();
    }

    private void SafeProjectEditorSave_Click(object sender, RoutedEventArgs e)
    {
        var project = CurrentSafeEditorProject();
        if (project is null) return;
        try
        {
            var updated = project with
            {
                Category = _safeEditorCategory!.Text.Trim(),
                Script = _safeEditorScript!.Text,
                OnScreenText = _safeEditorOnScreen!.Text,
                VisualPlan = _safeEditorVisualPlan!.Text,
                Description = _safeEditorDescription!.Text,
                PinnedComment = _safeEditorPinnedComment!.Text,
                Tags = _safeEditorTags!.Text,
                Sources = _safeEditorSources!.Text,
                Notes = _safeEditorNotes!.Text,
                Pinned = _safeEditorPinned!.IsChecked == true,
            };
            _data.SaveProject(updated);
            RefreshAndReselectSafeProject(updated.Id);
            HeaderStatusText.Text = $"Saved {updated.Title}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SafeProjectEditorApplyStatus_Click(object sender, RoutedEventArgs e)
    {
        var project = CurrentSafeEditorProject();
        if (project is null) return;
        var status = _safeEditorStatus?.SelectedItem?.ToString() ?? project.Status;
        try
        {
            var updated = _data.ChangeStatus(project, status);
            RefreshAndReselectSafeProject(updated.Id);
            HeaderStatusText.Text = $"Moved {updated.Title} to {updated.Status}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Change Status", MessageBoxButton.OK, MessageBoxImage.Error);
            LoadSafeProjectEditor(project);
        }
    }

    private void SafeProjectEditorDelete_Click(object sender, RoutedEventArgs e)
    {
        var project = CurrentSafeEditorProject();
        if (project is null) return;
        var answer = MessageBox.Show(this, $"Delete '{project.Title}' and its project folder?\n\nThis cannot be undone.", "Delete Project", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _data.DeleteProject(project, deleteFolder: true);
            _editingProject = null;
            RefreshAll();
            if (_projectsWorkspaceTabs is not null) _projectsWorkspaceTabs.SelectedIndex = 0;
            UpdateProjectsModeLayout();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshAndReselectSafeProject(int id)
    {
        RefreshAll();
        var selected = _projects.FirstOrDefault(project => project.Id == id);
        ProjectsGrid.SelectedItem = selected;
        if (selected is not null)
        {
            _editingProject = selected;
            LoadSafeProjectEditor(selected);
        }
    }

    private void UpdateSafeEditorCounters()
    {
        if (_safeScriptCounter is not null && _safeEditorScript is not null)
            _safeScriptCounter.Text = SafeEditorCountText(_safeEditorScript.Text);
        if (_safeOnScreenCounter is not null && _safeEditorOnScreen is not null)
            _safeOnScreenCounter.Text = SafeEditorCountText(_safeEditorOnScreen.Text);
    }

    private static string SafeEditorCountText(string text)
    {
        var trimmed = (text ?? "").Trim();
        var words = string.IsNullOrWhiteSpace(trimmed) ? 0 : trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return $"Words: {words}  |  Characters: {trimmed.Length}";
    }

    private static void AddSafeEditorField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(SafeEditorLabel(label));
        box.Margin = new Thickness(0, 5, 0, 12);
        panel.Children.Add(box);
    }

    private static TextBox SafeEditorTextBox() => new() { MinHeight = 34 };
    private static TextBox SafeEditorMultilineBox(double height) => new()
    {
        Height = height,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static Border SafeEditorCard() => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
    };

    private static TextBlock SafeEditorHeading(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Display"),
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock SafeEditorHelp(string text) => new()
    {
        Text = text,
        Foreground = SafeEditorMutedBrush(),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 14),
    };

    private static TextBlock SafeEditorLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock SafeEditorCounter() => new()
    {
        Foreground = SafeEditorMutedBrush(),
        FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, -7, 0, 10),
    };

    private static Brush SafeEditorMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));

    private static Button SafeEditorPrimaryButton(string text) => new()
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

    private static Button SafeEditorButton(string text) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(13, 0, 13, 0),
        Margin = new Thickness(0, 0, 7, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
        Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
    };

    private static Button SafeEditorDangerButton(string text) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(13, 0, 13, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(253, 162, 155)),
        Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24)),
    };
}
