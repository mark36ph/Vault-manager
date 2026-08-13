using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private PythonWorkerClient? _productionWorker;
    private readonly List<HybridProject> _productionProjects = new();
    private bool _embeddedProductionRunning;
    private string _embeddedLastStageMessage = "";

    private ComboBox _productionProjectComboBox = null!;
    private TextBlock _productionWorkerStatusText = null!;
    private TextBlock _productionProjectStatusText = null!;
    private TextBlock _productionProjectFolderText = null!;
    private Button _productionActionButton = null!;
    private Button _productionResumeButton = null!;
    private Button _productionCancelButton = null!;
    private Button _productionRefreshButton = null!;
    private TextBlock _productionCurrentStageText = null!;
    private TextBlock _productionPercentText = null!;
    private ProgressBar _productionProgressBar = null!;
    private TextBox _productionLogTextBox = null!;

    private async Task InitializeEmbeddedProductionAsync()
    {
        BuildEmbeddedProductionPage();

        try
        {
            var repositoryRoot = LocateProductionRepositoryRoot();
            _productionWorker = new PythonWorkerClient(repositoryRoot);
            _productionWorker.MessageReceived += line => Dispatcher.BeginInvoke(() => HandleEmbeddedWorkerLine(line));
            _productionWorker.ErrorReceived += line => Dispatcher.BeginInvoke(() => AppendEmbeddedProductionLog($"worker stderr: {line}"));
            await _productionWorker.StartAsync();
            AppendEmbeddedProductionLog("Python production worker started.");
        }
        catch (Exception error)
        {
            _productionWorkerStatusText.Text = "Connection failed";
            AppendEmbeddedProductionLog($"Worker connection failed: {error.Message}");
        }
    }

    private async Task DisposeEmbeddedProductionAsync()
    {
        if (_productionWorker is not null)
        {
            await _productionWorker.DisposeAsync();
            _productionWorker = null;
        }
    }

    private void BuildEmbeddedProductionPage()
    {
        if (MainTabs.Items.Count <= 2 || MainTabs.Items[2] is not TabItem productionTab)
        {
            return;
        }

        var page = new Grid { Margin = new Thickness(26, 22, 26, 24) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Production",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Build media, narration and timeline assets, then export to DaVinci Resolve Free.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        page.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 330 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star), MinWidth = 500 });
        Grid.SetRow(body, 1);
        page.Children.Add(body);

        body.Children.Add(BuildEmbeddedSetupPanel());
        var progressPanel = BuildEmbeddedProgressPanel();
        Grid.SetColumn(progressPanel, 2);
        body.Children.Add(progressPanel);

        productionTab.Content = page;
    }

    private Border ProductionPanel()
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
        };
    }

    private Border BuildEmbeddedSetupPanel()
    {
        var panel = ProductionPanel();
        var content = new StackPanel();
        panel.Child = content;

        content.Children.Add(ProductionHeading("Setup", 16));
        content.Children.Add(ProductionSectionLabel("PROJECT"));

        _productionProjectComboBox = new ComboBox { DisplayMemberPath = "DisplayName" };
        _productionProjectComboBox.SelectionChanged += (_, _) => ApplyEmbeddedSelectedProject();
        content.Children.Add(_productionProjectComboBox);

        _productionProjectStatusText = new TextBlock
        {
            Text = "Connecting to production worker...",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        };
        content.Children.Add(_productionProjectStatusText);

        _productionProjectFolderText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        content.Children.Add(_productionProjectFolderText);

        content.Children.Add(ProductionSectionLabel("WORKER"));
        _productionWorkerStatusText = new TextBlock { Text = "Connecting...", FontWeight = FontWeights.SemiBold };
        content.Children.Add(_productionWorkerStatusText);

        content.Children.Add(ProductionSectionLabel("ACTIONS"));
        _productionActionButton = ProductionButton("▶  Produce Video", true, EmbeddedProductionAction_Click);
        _productionResumeButton = ProductionButton("↻  Resume Production", false, EmbeddedResumeProduction_Click);
        _productionCancelButton = ProductionButton("■  Cancel", false, EmbeddedCancelProduction_Click);
        _productionRefreshButton = ProductionButton("Refresh projects", false, EmbeddedRefreshProjects_Click);
        _productionCancelButton.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));

        content.Children.Add(_productionActionButton);
        content.Children.Add(_productionResumeButton);
        content.Children.Add(_productionCancelButton);
        content.Children.Add(_productionRefreshButton);

        return panel;
    }

    private Border BuildEmbeddedProgressPanel()
    {
        var panel = ProductionPanel();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
        panel.Child = grid;

        var overview = new Grid();
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _productionCurrentStageText = ProductionHeading("Ready", 18);
        _productionPercentText = ProductionHeading("0%", 18);
        _productionPercentText.Foreground = new SolidColorBrush(Color.FromRgb(23, 92, 211));
        Grid.SetColumn(_productionPercentText, 1);
        overview.Children.Add(_productionCurrentStageText);
        overview.Children.Add(_productionPercentText);
        grid.Children.Add(overview);

        _productionProgressBar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 12, 0, 14) };
        Grid.SetRow(_productionProgressBar, 1);
        grid.Children.Add(_productionProgressBar);

        var workflow = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
        };
        var workflowText = new TextBlock
        {
            Text = "Workflow\n\n○  Research\n○  Select Facts\n○  Write Script\n○  Find Visuals\n○  Generate Voice\n○  Build Timeline\n○  Create Resolve Export",
            LineHeight = 26,
        };
        workflow.Child = workflowText;
        Grid.SetRow(workflow, 2);
        grid.Children.Add(workflow);

        var logGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var logHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        logHeader.ColumnDefinitions.Add(new ColumnDefinition());
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logHeader.Children.Add(ProductionHeading("Production log", 13));
        var clear = ProductionButton("Clear", false, (_, _) => _productionLogTextBox.Clear());
        clear.Width = 70;
        clear.Margin = new Thickness(0);
        Grid.SetColumn(clear, 1);
        logHeader.Children.Add(clear);
        logGrid.Children.Add(logHeader);

        _productionLogTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
        };
        Grid.SetRow(_productionLogTextBox, 1);
        logGrid.Children.Add(_productionLogTextBox);
        Grid.SetRow(logGrid, 3);
        grid.Children.Add(logGrid);

        return panel;
    }

    private static TextBlock ProductionHeading(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock ProductionSectionLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        FontWeight = FontWeights.SemiBold,
        FontSize = 11,
        Margin = new Thickness(0, 18, 0, 6),
    };

    private Button ProductionButton(string text, bool primary, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            Height = primary ? 38 : 34,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            IsEnabled = false,
        };
        if (primary && FindResource("PrimaryButton") is Style primaryStyle)
        {
            button.Style = primaryStyle;
        }
        button.Click += handler;
        return button;
    }

    private static string LocateProductionRepositoryRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "main.py")) && Directory.Exists(Path.Combine(directory.FullName, "common")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the FactVaultManager repository root.");
    }

    private HybridProject? EmbeddedSelectedProject => _productionProjectComboBox.SelectedItem as HybridProject;

    private async Task RefreshEmbeddedProductionProjectsAsync()
    {
        if (_productionWorker is not { IsRunning: true }) return;
        await _productionWorker.SendAsync(new { command = "list_projects", request_id = Guid.NewGuid().ToString("N") });
    }

    private async void EmbeddedRefreshProjects_Click(object sender, RoutedEventArgs e) => await RefreshEmbeddedProductionProjectsAsync();

    private void ApplyEmbeddedSelectedProject()
    {
        var project = EmbeddedSelectedProject;
        if (project is null)
        {
            _productionProjectStatusText.Text = "No project selected.";
            _productionProjectFolderText.Text = "";
            _productionActionButton.IsEnabled = false;
            _productionResumeButton.IsEnabled = false;
            return;
        }

        var readiness = project.FolderExists ? "Project folder ready" : "Project folder missing";
        if (project.CheckpointExists) readiness += " • resume available";
        if (project.TimelineExists) readiness += " • timeline available";
        _productionProjectStatusText.Text = $"{project.Status} • {project.Category} • {readiness}";
        _productionProjectFolderText.Text = project.Folder;
        _productionActionButton.Content = project.Status == "Completed" ? "▶  Reproduce Video" : "▶  Produce Video";
        _productionActionButton.IsEnabled = !_embeddedProductionRunning && project.FolderExists && project.Status is "In Progress" or "Completed";
        _productionResumeButton.IsEnabled = !_embeddedProductionRunning && project.FolderExists && project.CheckpointExists;
        _productionProjectComboBox.IsEnabled = !_embeddedProductionRunning;
        _productionRefreshButton.IsEnabled = !_embeddedProductionRunning && _productionWorker is { IsRunning: true };
        _productionCancelButton.IsEnabled = _embeddedProductionRunning;
    }

    private async void EmbeddedProductionAction_Click(object sender, RoutedEventArgs e)
    {
        var project = EmbeddedSelectedProject;
        if (project is null) return;
        await StartEmbeddedProductionAsync(project, project.Status == "Completed" ? "reproduce" : "produce");
    }

    private async void EmbeddedResumeProduction_Click(object sender, RoutedEventArgs e)
    {
        if (EmbeddedSelectedProject is { } project) await StartEmbeddedProductionAsync(project, "resume");
    }

    private async Task StartEmbeddedProductionAsync(HybridProject project, string mode)
    {
        if (_productionWorker is not { IsRunning: true } || _embeddedProductionRunning) return;
        _embeddedProductionRunning = true;
        _embeddedLastStageMessage = "";
        _productionCurrentStageText.Text = $"Starting {mode}...";
        _productionProgressBar.Value = 0;
        _productionPercentText.Text = "0%";
        ApplyEmbeddedSelectedProject();
        AppendEmbeddedProductionLog($"{(mode == "reproduce" ? "Reproducing" : mode == "resume" ? "Resuming" : "Producing")}: {project.Title}");
        try
        {
            await _productionWorker.SendAsync(new { command = "start_production", request_id = Guid.NewGuid().ToString("N"), project_id = project.Id, mode, topic = project.Title });
        }
        catch (Exception error)
        {
            _embeddedProductionRunning = false;
            ApplyEmbeddedSelectedProject();
            AppendEmbeddedProductionLog($"Could not start production: {error.Message}");
        }
    }

    private async void EmbeddedCancelProduction_Click(object sender, RoutedEventArgs e)
    {
        if (_productionWorker is not { IsRunning: true } || !_embeddedProductionRunning) return;
        await _productionWorker.SendAsync(new { command = "cancel_production", request_id = Guid.NewGuid().ToString("N") });
    }

    private void HandleEmbeddedWorkerLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = ReadEmbeddedString(root, "type");
            switch (type)
            {
                case "ready":
                    _productionWorkerStatusText.Text = "Connected";
                    _productionRefreshButton.IsEnabled = true;
                    AppendEmbeddedProductionLog($"Worker connected (protocol {ReadEmbeddedInt(root, "protocol")}).");
                    _ = RefreshEmbeddedProductionProjectsAsync();
                    break;
                case "projects": LoadEmbeddedProjects(root); break;
                case "production_started": AppendEmbeddedProductionLog($"Production started: {ReadEmbeddedString(root, "title")} [{ReadEmbeddedString(root, "mode")}]"); break;
                case "production_state": ApplyEmbeddedProductionState(root); break;
                case "project_updated": AppendEmbeddedProductionLog("Project status changed to Completed."); _ = RefreshEmbeddedProductionProjectsAsync(); RefreshAll(); break;
                case "warning": AppendEmbeddedProductionLog($"Warning: {ReadEmbeddedString(root, "message")}"); break;
                case "error":
                    _embeddedProductionRunning = false;
                    ApplyEmbeddedSelectedProject();
                    _productionCurrentStageText.Text = "Production error";
                    AppendEmbeddedProductionLog($"Worker error: {ReadEmbeddedString(root, "message")}");
                    break;
                case "shutdown": _productionWorkerStatusText.Text = "Disconnected"; break;
            }
        }
        catch (JsonException)
        {
            AppendEmbeddedProductionLog($"worker: {line}");
        }
    }

    private void LoadEmbeddedProjects(JsonElement root)
    {
        var selectedId = EmbeddedSelectedProject?.Id;
        _productionProjects.Clear();
        if (root.TryGetProperty("projects", out var projectsElement) && projectsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in projectsElement.EnumerateArray())
            {
                _productionProjects.Add(new HybridProject(
                    ReadEmbeddedInt(item, "id"), ReadEmbeddedString(item, "title"), ReadEmbeddedString(item, "status"),
                    ReadEmbeddedString(item, "category"), ReadEmbeddedString(item, "folder"), ReadEmbeddedBool(item, "folder_exists"),
                    ReadEmbeddedBool(item, "checkpoint_exists"), ReadEmbeddedBool(item, "timeline_exists")));
            }
        }
        _productionProjectComboBox.ItemsSource = null;
        _productionProjectComboBox.ItemsSource = _productionProjects;
        if (_productionProjects.Count == 0)
        {
            _productionProjectStatusText.Text = "No In Progress or Completed projects found.";
            return;
        }
        _productionProjectComboBox.SelectedItem = selectedId is int id ? _productionProjects.FirstOrDefault(p => p.Id == id) ?? _productionProjects[0] : _productionProjects[0];
        ApplyEmbeddedSelectedProject();
        AppendEmbeddedProductionLog($"Loaded {_productionProjects.Count} production project(s).");
    }

    private void ApplyEmbeddedProductionState(JsonElement root)
    {
        _embeddedProductionRunning = ReadEmbeddedBool(root, "running");
        var progress = root.TryGetProperty("progress", out var progressElement) && progressElement.TryGetDouble(out var value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        var message = ReadEmbeddedString(root, "message");
        var stage = ReadEmbeddedString(root, "current_stage");
        var error = ReadEmbeddedString(root, "error");
        _productionProgressBar.Value = progress * 100;
        _productionPercentText.Text = $"{Math.Round(progress * 100):0}%";
        _productionCurrentStageText.Text = string.IsNullOrWhiteSpace(stage) ? message : $"{stage}: {message}";
        var logMessage = string.IsNullOrWhiteSpace(error) ? message : $"{message}: {error}";
        if (!string.IsNullOrWhiteSpace(logMessage) && logMessage != _embeddedLastStageMessage)
        {
            AppendEmbeddedProductionLog(logMessage);
            _embeddedLastStageMessage = logMessage;
        }
        ApplyEmbeddedSelectedProject();
        if (!_embeddedProductionRunning) _ = RefreshEmbeddedProductionProjectsAsync();
    }

    private void AppendEmbeddedProductionLog(string message)
    {
        _productionLogTextBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _productionLogTextBox.ScrollToEnd();
    }

    private static string ReadEmbeddedString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element.ToString() : "";
    private static int ReadEmbeddedInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.TryGetInt32(out var value) ? value : 0;
    private static bool ReadEmbeddedBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False && element.GetBoolean();
}
