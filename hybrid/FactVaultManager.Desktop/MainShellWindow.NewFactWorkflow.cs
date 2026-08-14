using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _newFactWorkflowInitialized;
    private object? _projectsPageOriginalContent;
    private TextBox _newFactTitle = null!;
    private ComboBox _newFactCategory = null!;
    private ComboBox _newFactStatus = null!;
    private DatePicker _newFactScheduleDate = null!;
    private TextBox _newFactScheduleTime = null!;
    private StackPanel _newFactSchedulePanel = null!;
    private TextBox _newFactImport = null!;
    private TextBox _newFactScript = null!;
    private TextBox _newFactOnScreen = null!;
    private TextBox _newFactVisualPlan = null!;
    private TextBox _newFactDescription = null!;
    private TextBox _newFactPinnedComment = null!;
    private TextBox _newFactTags = null!;
    private TextBox _newFactNotes = null!;
    private TextBox _newFactSources = null!;
    private TextBlock _newFactStatusText = null!;
    private TextBlock _newFactPreview = null!;
    private CheckBox _newFactOpenAfter = null!;
    private Button _newFactCreateButton = null!;

    private void InitializeNewFactWorkflow()
    {
        if (_newFactWorkflowInitialized || MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab)
            return;

        _newFactWorkflowInitialized = true;
        _projectsPageOriginalContent = projectsTab.Content;

        foreach (var button in FindVisualChildren<Button>(projectsTab).ToList())
        {
            var text = button.Content?.ToString() ?? "";
            if (!text.Contains("New project", StringComparison.OrdinalIgnoreCase)) continue;
            button.Click -= CreateProject_Click;
            button.Click += (_, _) => ShowNewFactWorkspace();
            button.Content = "+  New Fact";
        }
    }

    private void ShowNewFactWorkspace()
    {
        if (MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab) return;
        if (_projectsPageOriginalContent is null) _projectsPageOriginalContent = projectsTab.Content;

        projectsTab.Content = BuildNewFactWorkspace();
        MainTabs.SelectedIndex = 1;
        ApplyNavigationSelection(1);
        Dispatcher.BeginInvoke(new Action(() => _newFactTitle.Focus()));
    }

    private void RestoreProjectsWorkspace()
    {
        if (MainTabs.Items.Count < 2 || MainTabs.Items[1] is not TabItem projectsTab || _projectsPageOriginalContent is null)
            return;
        projectsTab.Content = _projectsPageOriginalContent;
        MainTabs.SelectedIndex = 1;
        ApplyNavigationSelection(1);
        ApplyProjectsFilter();
    }

    private FrameworkElement BuildNewFactWorkspace()
    {
        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = new Button { Content = "←  Back to Projects", Margin = new Thickness(0, 0, 14, 0) };
        back.Click += (_, _) => RestoreProjectsWorkspace();
        header.Children.Add(back);

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "New Fact",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Create a project, import its core content, and choose how it enters the workflow.",
            Foreground = NewFactMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        _newFactCreateButton = new Button { Content = "Create Fact", MinWidth = 118 };
        if (FindResource("PrimaryButton") is Style primaryStyle) _newFactCreateButton.Style = primaryStyle;
        _newFactCreateButton.Click += CreateNewFact_Click;
        Grid.SetColumn(_newFactCreateButton, 2);
        header.Children.Add(_newFactCreateButton);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 600 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var editorBorder = NewFactCard(new Thickness(16));
        body.Children.Add(editorBorder);
        var editorScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        editorBorder.Child = editorScroll;
        var editor = new StackPanel();
        editorScroll.Content = editor;

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        _newFactTitle = NewFactTextBox();
        _newFactTitle.TextChanged += (_, _) => UpdateNewFactPreview();
        AddNewFactField(titleRow, 0, "Title", _newFactTitle, new Thickness(0, 0, 8, 0));

        _newFactCategory = new ComboBox { IsEditable = true };
        foreach (var category in _data.GetCategories()) _newFactCategory.Items.Add(category);
        if (_newFactCategory.Items.Count > 0) _newFactCategory.SelectedIndex = 0;
        _newFactCategory.SelectionChanged += (_, _) => UpdateNewFactPreview();
        AddNewFactField(titleRow, 1, "Category", _newFactCategory, new Thickness(0, 0, 8, 0));

        _newFactStatus = new ComboBox();
        foreach (var status in new[] { "In Progress", "Scheduled", "Completed" }) _newFactStatus.Items.Add(status);
        _newFactStatus.SelectedIndex = 0;
        _newFactStatus.SelectionChanged += (_, _) => NewFactStatusChanged();
        AddNewFactField(titleRow, 2, "Status", _newFactStatus, new Thickness(0));
        editor.Children.Add(titleRow);

        _newFactSchedulePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        _newFactSchedulePanel.Children.Add(NewFactFieldLabel("Schedule"));
        var scheduleRow = new StackPanel { Orientation = Orientation.Horizontal };
        _newFactScheduleDate = new DatePicker { Width = 180, SelectedDate = DateTime.Today.AddDays(1) };
        _newFactScheduleDate.SelectedDateChanged += (_, _) => UpdateNewFactPreview();
        _newFactScheduleTime = new TextBox { Width = 90, Text = "18:00", Margin = new Thickness(8, 0, 0, 0) };
        _newFactScheduleTime.TextChanged += (_, _) => UpdateNewFactPreview();
        scheduleRow.Children.Add(_newFactScheduleDate);
        scheduleRow.Children.Add(_newFactScheduleTime);
        _newFactSchedulePanel.Children.Add(scheduleRow);
        editor.Children.Add(_newFactSchedulePanel);

        _newFactStatusText = new TextBlock
        {
            Foreground = NewFactMutedBrush(),
            Margin = new Thickness(0, 9, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        editor.Children.Add(_newFactStatusText);

        editor.Children.Add(NewFactSectionTitle("Paste from ChatGPT"));
        _newFactImport = NewFactMultiline(130);
        editor.Children.Add(_newFactImport);
        var importButton = new Button { Content = "Import ChatGPT Text", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        importButton.Click += (_, _) => ImportNewFactText();
        editor.Children.Add(importButton);

        editor.Children.Add(NewFactSectionTitle("Content"));
        _newFactScript = AddNewFactEditor(editor, "Script", 155);
        _newFactOnScreen = AddNewFactEditor(editor, "On-screen text", 105);
        _newFactDescription = AddNewFactEditor(editor, "Description", 85);
        _newFactPinnedComment = AddNewFactEditor(editor, "Pinned Comment", 72);
        _newFactTags = AddNewFactEditor(editor, "Tags", 66);
        _newFactNotes = AddNewFactEditor(editor, "Notes", 110);
        _newFactSources = AddNewFactEditor(editor, "Sources", 95);

        editor.Children.Add(NewFactSectionTitle("Production metadata"));
        _newFactVisualPlan = AddNewFactEditor(editor, "Visual Plan", 90);
        _newFactOpenAfter = new CheckBox
        {
            Content = "Open project after creating",
            IsChecked = true,
            Margin = new Thickness(0, 12, 0, 4),
        };
        editor.Children.Add(_newFactOpenAfter);

        var previewBorder = NewFactCard(new Thickness(14));
        Grid.SetColumn(previewBorder, 2);
        body.Children.Add(previewBorder);
        var previewStack = new StackPanel();
        previewBorder.Child = previewStack;
        previewStack.Children.Add(new TextBlock { Text = "Project preview", FontSize = 15, FontWeight = FontWeights.SemiBold });
        previewStack.Children.Add(new TextBlock { Text = "What will be created", Foreground = NewFactMutedBrush(), FontSize = 11, Margin = new Thickness(0, 2, 0, 10) });
        _newFactPreview = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        };
        previewStack.Children.Add(_newFactPreview);
        UpdateNewFactPreview();
        return root;
    }

    private async void CreateNewFact_Click(object sender, RoutedEventArgs e)
    {
        var title = _newFactTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            _newFactStatusText.Text = "Enter a project title before creating the fact.";
            _newFactTitle.Focus();
            return;
        }

        DateTime? scheduledFor = null;
        if ((_newFactStatus.SelectedItem?.ToString() ?? "") == "Scheduled")
        {
            if (_newFactScheduleDate.SelectedDate is not DateTime date ||
                !TimeSpan.TryParse(_newFactScheduleTime.Text.Trim(), CultureInfo.InvariantCulture, out var time))
            {
                _newFactStatusText.Text = "Choose a valid schedule date and time, for example 18:00.";
                return;
            }
            scheduledFor = date.Date + time;
        }

        _newFactCreateButton.IsEnabled = false;
        _newFactCreateButton.Content = "Creating...";
        _newFactStatusText.Text = "Creating project...";
        await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        try
        {
            var created = _data.CreateFactProject(new NewFactData(
                title,
                _newFactCategory.Text.Trim(),
                _newFactStatus.SelectedItem?.ToString() ?? "In Progress",
                scheduledFor,
                _newFactScript.Text,
                _newFactOnScreen.Text,
                _newFactVisualPlan.Text,
                _newFactDescription.Text,
                _newFactPinnedComment.Text,
                _newFactTags.Text,
                _newFactNotes.Text,
                _newFactSources.Text));

            RefreshAll();
            RestoreProjectsWorkspace();
            ProjectsGrid.SelectedItem = _projects.FirstOrDefault(project => project.Id == created.Id);
            if (_newFactOpenAfter.IsChecked == true && _projectsWorkspaceTabs is not null && _projectsWorkspaceTabs.Items.Count > 1)
                _projectsWorkspaceTabs.SelectedIndex = 1;
            HeaderStatusText.Text = $"Created {created.Title}";
        }
        catch (Exception error)
        {
            _newFactStatusText.Text = error.Message;
            _newFactCreateButton.IsEnabled = true;
            _newFactCreateButton.Content = "Create Fact";
        }
    }

    private void NewFactStatusChanged()
    {
        var scheduled = (_newFactStatus.SelectedItem?.ToString() ?? "") == "Scheduled";
        _newFactSchedulePanel.Visibility = scheduled ? Visibility.Visible : Visibility.Collapsed;
        UpdateNewFactPreview();
    }

    private void ImportNewFactText()
    {
        var raw = _newFactImport.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            _newFactStatusText.Text = "Paste ChatGPT text before importing.";
            _newFactImport.Focus();
            return;
        }

        var parsed = ParseNewFactImport(raw);
        if (!string.IsNullOrWhiteSpace(parsed.GetValueOrDefault("title"))) _newFactTitle.Text = parsed["title"];
        if (!string.IsNullOrWhiteSpace(parsed.GetValueOrDefault("category"))) _newFactCategory.Text = parsed["category"];
        SetNewFactText(_newFactScript, parsed.GetValueOrDefault("script"));
        SetNewFactText(_newFactOnScreen, parsed.GetValueOrDefault("on_screen_text"));
        SetNewFactText(_newFactVisualPlan, parsed.GetValueOrDefault("visual_plan"));
        SetNewFactText(_newFactDescription, parsed.GetValueOrDefault("description"));
        SetNewFactText(_newFactPinnedComment, parsed.GetValueOrDefault("pinned_comment"));
        SetNewFactText(_newFactTags, parsed.GetValueOrDefault("tags"));
        SetNewFactText(_newFactNotes, parsed.GetValueOrDefault("notes"));
        SetNewFactText(_newFactSources, parsed.GetValueOrDefault("sources"));
        var count = parsed.Values.Count(value => !string.IsNullOrWhiteSpace(value));
        _newFactStatusText.Text = $"ChatGPT content imported ({count} populated fields).";
        UpdateNewFactPreview();
    }

    private static Dictionary<string, string> ParseNewFactImport(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "", ["category"] = "", ["template"] = "", ["script"] = "",
            ["on_screen_text"] = "", ["visual_plan"] = "", ["description"] = "",
            ["pinned_comment"] = "", ["tags"] = "", ["notes"] = "", ["sources"] = "",
        };

        var timelineIndex = raw.IndexOf("Visual Timeline:", StringComparison.OrdinalIgnoreCase);
        var metadataText = timelineIndex >= 0 ? raw[..timelineIndex] : raw;
        ParseNewFactStandard(metadataText, result);

        var timelineText = timelineIndex >= 0 ? raw[timelineIndex..] : raw;
        if (timelineText.Contains("Narration:", StringComparison.OrdinalIgnoreCase))
            ParseNewFactTimeline(timelineText, result);
        return result;
    }

    private static void ParseNewFactStandard(string text, Dictionary<string, string> result)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "title", ["category"] = "category", ["template"] = "template",
            ["script"] = "script", ["on-screen text"] = "on_screen_text", ["on screen"] = "on_screen_text",
            ["onscreen text"] = "on_screen_text", ["visual plan"] = "visual_plan", ["visual"] = "visual_plan",
            ["description"] = "description", ["pinned comment"] = "pinned_comment", ["tags"] = "tags",
            ["notes"] = "notes", ["sources"] = "sources",
        };
        string? current = null;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon >= 0)
            {
                var label = line[..colon].Trim();
                if (labels.TryGetValue(label, out var field))
                {
                    current = field;
                    var inline = line[(colon + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(inline)) result[field] = AppendImportValue(result[field], inline);
                    continue;
                }
            }
            if (current is not null) result[current] = AppendImportValue(result[current], rawLine);
        }
        foreach (var key in result.Keys.ToList()) result[key] = result[key].Trim();
    }

    private static void ParseNewFactTimeline(string text, Dictionary<string, string> result)
    {
        var blocks = text.Replace("────────────────────────", "-----BLOCK-----").Split("-----BLOCK-----");
        var script = new List<string>();
        var onScreen = new List<string>();
        var visual = new List<string>();
        var notes = new List<string>();
        foreach (var block in blocks)
        {
            if (!block.Contains("Narration:", StringComparison.OrdinalIgnoreCase)) continue;
            var time = block.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("sec", StringComparison.OrdinalIgnoreCase)) ?? "";
            var narration = ExtractNewFactTimelineSection(block, "Narration");
            var visualText = ExtractNewFactTimelineSection(block, "Visual");
            var search = ExtractNewFactTimelineSection(block, "Search");
            var freeSources = ExtractNewFactTimelineSection(block, "Free Sources");
            var onScreenText = ExtractNewFactTimelineSection(block, "On Screen");
            if (!string.IsNullOrWhiteSpace(narration)) script.Add(narration);
            if (!string.IsNullOrWhiteSpace(onScreenText)) onScreen.Add(string.Join(Environment.NewLine, new[] { time, onScreenText }.Where(value => !string.IsNullOrWhiteSpace(value))));
            if (!string.IsNullOrWhiteSpace(visualText)) visual.Add(string.Join(Environment.NewLine, new[] { time, visualText }.Where(value => !string.IsNullOrWhiteSpace(value))));
            var noteParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(time)) noteParts.Add(time);
            if (!string.IsNullOrWhiteSpace(search)) noteParts.Add("Search:" + Environment.NewLine + search);
            if (!string.IsNullOrWhiteSpace(freeSources)) noteParts.Add("Free Sources:" + Environment.NewLine + freeSources);
            if (noteParts.Count > 0) notes.Add(string.Join(Environment.NewLine, noteParts));
        }
        if (script.Count > 0) result["script"] = string.Join(Environment.NewLine + Environment.NewLine, script);
        if (onScreen.Count > 0) result["on_screen_text"] = string.Join(Environment.NewLine + Environment.NewLine, onScreen);
        if (visual.Count > 0) result["visual_plan"] = string.Join(Environment.NewLine + Environment.NewLine, visual);
        if (notes.Count > 0)
        {
            var timelineNotes = string.Join(Environment.NewLine + Environment.NewLine, notes);
            result["notes"] = string.Join(Environment.NewLine + Environment.NewLine, new[] { result["notes"], timelineNotes }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private static string ExtractNewFactTimelineSection(string block, string section)
    {
        var known = new[] { "Narration:", "Visual:", "Search:", "Free Sources:", "On Screen:" };
        var wanted = section + ":";
        var collecting = false;
        var lines = new List<string>();
        foreach (var raw in block.Replace("\r\n", "\n").Split('\n'))
        {
            var clean = raw.Trim();
            if (clean.Equals(wanted, StringComparison.OrdinalIgnoreCase)) { collecting = true; continue; }
            if (collecting && known.Any(item => clean.Equals(item, StringComparison.OrdinalIgnoreCase))) break;
            if (collecting) lines.Add(raw);
        }
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private void UpdateNewFactPreview()
    {
        if (_newFactPreview is null) return;
        var title = string.IsNullOrWhiteSpace(_newFactTitle?.Text) ? "New Project" : _newFactTitle.Text.Trim();
        var category = _newFactCategory?.Text?.Trim() ?? "Misc";
        var status = _newFactStatus?.SelectedItem?.ToString() ?? "In Progress";
        var schedule = "";
        if (status == "Scheduled" && _newFactScheduleDate?.SelectedDate is DateTime date)
            schedule = $"\nScheduled\n{date:yyyy-MM-dd} {_newFactScheduleTime?.Text?.Trim()}\n";
        _newFactPreview.Text =
            $"📁  {title}\n\nCategory\n{category}\n\nStatus\n{status}\n{schedule}\n" +
            "──────────────\nDATABASE\n\n✓ Script\n✓ On-screen text\n✓ Description\n✓ Pinned Comment\n✓ Tags\n✓ Notes\n✓ Sources\n\n" +
            "──────────────\nPROJECT FOLDERS\n\n✓ Assets / Images\n✓ Assets / Videos\n✓ Assets / Music\n✓ Voice\n✓ Export";
    }

    private static string AppendImportValue(string existing, string value) => string.IsNullOrEmpty(existing) ? value : existing + Environment.NewLine + value;
    private static void SetNewFactText(TextBox box, string? value) { if (!string.IsNullOrWhiteSpace(value)) box.Text = value; }
    private static Brush NewFactMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));
    private static Border NewFactCard(Thickness padding) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
    };
    private static TextBlock NewFactFieldLabel(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)), Margin = new Thickness(0, 0, 0, 4) };
    private static TextBlock NewFactSectionTitle(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 7) };
    private static TextBox NewFactTextBox() => new() { MinHeight = 34 };
    private static TextBox NewFactMultiline(double height) => new() { Height = height, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static TextBox AddNewFactEditor(Panel parent, string label, double height)
    {
        parent.Children.Add(NewFactFieldLabel(label));
        var box = NewFactMultiline(height);
        box.Margin = new Thickness(0, 0, 0, 9);
        parent.Children.Add(box);
        return box;
    }
    private static void AddNewFactField(Grid grid, int column, string label, Control control, Thickness margin)
    {
        var stack = new StackPanel { Margin = margin };
        stack.Children.Add(NewFactFieldLabel(label));
        stack.Children.Add(control);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }
}
