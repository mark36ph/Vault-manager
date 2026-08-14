using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly string[] ProductionStageOrder =
    [
        "research", "facts", "script", "image_prompts", "voice", "timeline", "resolve"
    ];

    private static readonly Dictionary<string, string> ProductionStageLabels = new()
    {
        ["research"] = "Research",
        ["facts"] = "Select Facts",
        ["script"] = "Write Script",
        ["image_prompts"] = "Find Visuals",
        ["voice"] = "Generate Voice",
        ["timeline"] = "Build Timeline",
        ["resolve"] = "Create Resolve Export",
    };

    private PythonWorkerClient? _productionWorker;
    private readonly List<HybridProject> _productionProjects = new();
    private readonly Dictionary<string, (TextBlock Icon, TextBlock Detail)> _productionStageRows = new();
    private bool _embeddedProductionRunning;
    private string _embeddedLastStageMessage = "";
    private DateTime? _productionRunStartedAt;
    private readonly DispatcherTimer _productionElapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private ComboBox _productionProjectComboBox = null!;
    private TextBox _productionTopicTextBox = null!;
    private CheckBox _productionPexelsCheckBox = null!;
    private CheckBox _productionPixabayCheckBox = null!;
    private ComboBox _productionAssetKindComboBox = null!;
    private CheckBox _productionVoiceCheckBox = null!;
    private TextBlock _productionCredentialText = null!;
    private TextBlock _productionWorkerStatusText = null!;
    private TextBlock _productionProjectStatusText = null!;
    private TextBlock _productionProjectFolderText = null!;
    private Button _productionActionButton = null!;
    private Button _productionResumeButton = null!;
    private Button _productionExportButton = null!;
    private Button _productionCancelButton = null!;
    private Button _productionOpenFolderButton = null!;
    private Button _productionRefreshButton = null!;
    private TextBlock _productionCurrentStageText = null!;
    private TextBlock _productionPercentText = null!;
    private TextBlock _productionElapsedText = null!;
    private ProgressBar _productionProgressBar = null!;
    private TextBox _productionLogTextBox = null!;

    private async Task InitializeEmbeddedProductionAsync()
    {
        BuildEmbeddedProductionPage();
        _productionElapsedTimer.Tick += (_, _) => UpdateEmbeddedElapsed();

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
        _productionElapsedTimer.Stop();
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
            Foreground = ProductionMutedBrush(),
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

    private static Brush ProductionMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));
    private static Brush ProductionReadyBrush() => new SolidColorBrush(Color.FromRgb(2, 122, 72));
    private static Brush ProductionWarningBrush() => new SolidColorBrush(Color.FromRgb(181, 71, 8));

    private Border ProductionPanel() => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
    };

    private Border BuildEmbeddedSetupPanel()
    {
        var panel = ProductionPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        panel.Child = scroll;
        var content = new StackPanel();
        scroll.Content = content;

        content.Children.Add(ProductionHeading("Setup", 16));
        content.Children.Add(ProductionSectionLabel("PROJECT"));

        _productionProjectComboBox = new ComboBox { DisplayMemberPath = "DisplayName", Height = 34 };
        _productionProjectComboBox.SelectionChanged += (_, _) => ApplyEmbeddedSelectedProject();
        content.Children.Add(_productionProjectComboBox);

        _productionTopicTextBox = new TextBox { Height = 34, Margin = new Thickness(0, 7, 0, 0) };
        content.Children.Add(_productionTopicTextBox);

        _productionProjectStatusText = new TextBlock
        {
            Text = "Select a project to check production readiness.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ProductionMutedBrush(),
            Margin = new Thickness(0, 9, 0, 0),
        };
        content.Children.Add(_productionProjectStatusText);

        _productionProjectFolderText = new TextBlock
        {
            Foreground = ProductionMutedBrush(),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        content.Children.Add(_productionProjectFolderText);

        content.Children.Add(ProductionSectionLabel("MEDIA"));
        var providers = new StackPanel { Orientation = Orientation.Horizontal };
        _productionPexelsCheckBox = new CheckBox { Content = "Pexels", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        _productionPixabayCheckBox = new CheckBox { Content = "Pixabay", IsChecked = true };
        _productionPexelsCheckBox.Click += (_, _) => RefreshEmbeddedProviderReadiness();
        _productionPixabayCheckBox.Click += (_, _) => RefreshEmbeddedProviderReadiness();
        providers.Children.Add(_productionPexelsCheckBox);
        providers.Children.Add(_productionPixabayCheckBox);
        content.Children.Add(providers);

        _productionAssetKindComboBox = new ComboBox { Height = 34, Margin = new Thickness(0, 9, 0, 0) };
        _productionAssetKindComboBox.Items.Add("image");
        _productionAssetKindComboBox.Items.Add("video");
        _productionAssetKindComboBox.SelectedIndex = 0;
        content.Children.Add(_productionAssetKindComboBox);

        _productionVoiceCheckBox = new CheckBox
        {
            Content = "Generate OpenAI narration",
            IsChecked = true,
            Margin = new Thickness(0, 9, 0, 0),
        };
        _productionVoiceCheckBox.Click += (_, _) => RefreshEmbeddedProviderReadiness();
        content.Children.Add(_productionVoiceCheckBox);

        content.Children.Add(ProductionSectionLabel("PROVIDER STATUS"));
        var credentialBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
        };
        _productionCredentialText = new TextBlock
        {
            Text = "Checking credentials...",
            Foreground = ProductionMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
        };
        credentialBorder.Child = _productionCredentialText;
        content.Children.Add(credentialBorder);

        content.Children.Add(ProductionSectionLabel("WORKER"));
        _productionWorkerStatusText = new TextBlock { Text = "Connecting...", FontWeight = FontWeights.SemiBold };
        content.Children.Add(_productionWorkerStatusText);

        content.Children.Add(ProductionSectionLabel("ACTIONS"));
        _productionActionButton = ProductionButton("▶  Produce Video", true, EmbeddedProductionAction_Click);
        _productionResumeButton = ProductionButton("↻  Resume Production", false, EmbeddedResumeProduction_Click);
        _productionExportButton = ProductionButton("⬆  Create Resolve Export", false, EmbeddedExportResolve_Click);
        _productionCancelButton = ProductionButton("■  Cancel", false, EmbeddedCancelProduction_Click);
        _productionOpenFolderButton = ProductionButton("📂  Open Project Folder", false, EmbeddedOpenProjectFolder_Click);
        _productionRefreshButton = ProductionButton("Refresh projects", false, EmbeddedRefreshProjects_Click);
        _productionCancelButton.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));

        content.Children.Add(_productionActionButton);
        content.Children.Add(_productionResumeButton);
        content.Children.Add(_productionExportButton);
        content.Children.Add(_productionCancelButton);
        content.Children.Add(_productionOpenFolderButton);
        content.Children.Add(_productionRefreshButton);
        return panel;
    }

    private Border BuildEmbeddedProgressPanel()
    {
        var panel = ProductionPanel();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 150 });
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
        _productionElapsedText = new TextBlock
        {
            Text = "Elapsed 00:00",
            Foreground = ProductionMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        };
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_productionElapsedText, 1);
        overview.Children.Add(_productionElapsedText);
        grid.Children.Add(overview);

        _productionProgressBar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 12, 0, 12) };
        Grid.SetRow(_productionProgressBar, 1);
        grid.Children.Add(_productionProgressBar);

        var workflowBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
        };
        var workflow = new StackPanel();
        workflow.Children.Add(ProductionHeading("Workflow", 13));
        _productionStageRows.Clear();
        foreach (var stage in ProductionStageOrder)
        {
            var row = new Grid { Height = 38, Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = "○", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = ProductionStageLabels[stage], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var detail = new TextBlock { Text = "Waiting", Foreground = ProductionMutedBrush(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(detail, 2);
            row.Children.Add(icon);
            row.Children.Add(label);
            row.Children.Add(detail);
            workflow.Children.Add(row);
            _productionStageRows[stage] = (icon, detail);
        }
        workflowBorder.Child = workflow;
        Grid.SetRow(workflowBorder, 2);
        grid.Children.Add(workflowBorder);

        var logGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var logHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        logHeader.ColumnDefinitions.Add(new ColumnDefinition());
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logHeader.Children.Add(ProductionHeading("Production log", 13));
        var clear = ProductionButton("Clear", false, (_, _) => _productionLogTextBox.Clear());
        clear.Width = 70;
        clear.IsEnabled = true;
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
        _productionLogTextBox.AppendText("Ready to start production." + Environment.NewLine);
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
        Foreground = ProductionMutedBrush(),
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
            _productionExportButton.IsEnabled = false;
            _productionOpenFolderButton.IsEnabled = false;
            return;
        }

        _productionTopicTextBox.Text = project.Title;
        LoadEmbeddedProviderSettings(project);
        var readiness = project.FolderExists ? "Project folder ready" : "Project folder missing";
        if (project.CheckpointExists) readiness += " • resume available";
        if (project.TimelineExists) readiness += " • Resolve export available";
        _productionProjectStatusText.Text = readiness;
        _productionProjectStatusText.Foreground = project.FolderExists ? ProductionReadyBrush() : ProductionWarningBrush();
        _productionProjectFolderText.Text = project.Folder;
        _productionActionButton.Content = project.Status == "Completed" ? "▶  Reproduce Video" : "▶  Produce Video";
        _productionResumeButton.IsEnabled = !_embeddedProductionRunning && project.FolderExists && project.CheckpointExists;
        _productionExportButton.IsEnabled = !_embeddedProductionRunning && project.FolderExists && project.TimelineExists;
        _productionOpenFolderButton.IsEnabled = !_embeddedProductionRunning && project.FolderExists;
        _productionProjectComboBox.IsEnabled = !_embeddedProductionRunning;
        _productionRefreshButton.IsEnabled = !_embeddedProductionRunning && _productionWorker is { IsRunning: true };
        _productionCancelButton.IsEnabled = _embeddedProductionRunning;
        SetEmbeddedSetupEnabled(!_embeddedProductionRunning);
        RefreshEmbeddedProviderReadiness();
    }

    private void LoadEmbeddedProviderSettings(HybridProject project)
    {
        _productionPexelsCheckBox.IsChecked = true;
        _productionPixabayCheckBox.IsChecked = true;
        _productionVoiceCheckBox.IsChecked = true;
        _productionAssetKindComboBox.SelectedItem = "image";
        try
        {
            var settings = NativeProductionProviderWorkflow.Load(project.Folder);
            var selected = settings.AssetProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _productionPexelsCheckBox.IsChecked = selected.Contains("pexels");
            _productionPixabayCheckBox.IsChecked = selected.Contains("pixabay");
            _productionAssetKindComboBox.SelectedItem = string.Equals(settings.AssetKind, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
            _productionVoiceCheckBox.IsChecked = !string.Equals(settings.VoiceProvider, "none", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error)
        {
            AppendEmbeddedProductionLog($"Could not load provider settings: {error.Message}");
        }
    }

    private void SaveEmbeddedProviderSettings(HybridProject project)
    {
        NativeProductionProviderWorkflow.Save(
            project.Folder,
            _data.LoadSettings(),
            _productionPexelsCheckBox.IsChecked == true,
            _productionPixabayCheckBox.IsChecked == true,
            _productionVoiceCheckBox.IsChecked == true,
            _productionAssetKindComboBox.SelectedItem?.ToString() ?? "image");
    }

    private void RefreshEmbeddedProviderReadiness()
    {
        if (_productionCredentialText is null) return;
        try
        {
            var readiness = NativeProductionProviderWorkflow.CheckReadiness(
                _data.LoadSettings(),
                _productionPexelsCheckBox.IsChecked == true,
                _productionPixabayCheckBox.IsChecked == true);

            _productionCredentialText.Text = string.Join(Environment.NewLine, readiness.Lines);
            _productionCredentialText.Foreground = readiness.Ready ? ProductionReadyBrush() : ProductionWarningBrush();
            var project = EmbeddedSelectedProject;
            _productionActionButton.IsEnabled = readiness.Ready && !_embeddedProductionRunning && project is { FolderExists: true } && project.Status is "In Progress" or "Completed";
        }
        catch (Exception error)
        {
            _productionCredentialText.Text = $"Provider setup error: {error.Message}";
            _productionCredentialText.Foreground = ProductionWarningBrush();
            _productionActionButton.IsEnabled = false;
        }
    }

    private void SetEmbeddedSetupEnabled(bool enabled)
    {
        _productionProjectComboBox.IsEnabled = enabled;
        _productionTopicTextBox.IsEnabled = enabled;
        _productionPexelsCheckBox.IsEnabled = enabled;
        _productionPixabayCheckBox.IsEnabled = enabled;
        _productionAssetKindComboBox.IsEnabled = enabled;
        _productionVoiceCheckBox.IsEnabled = enabled;
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
        var topic = _productionTopicTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            _productionTopicTextBox.Focus();
            AppendEmbeddedProductionLog("Enter a topic before starting production.");
            return;
        }

        try
        {
            SaveEmbeddedProviderSettings(project);
            NativeProductionProviderWorkflow.ValidateProject(project.Folder, _data.LoadSettings());
        }
        catch (Exception error)
        {
            AppendEmbeddedProductionLog($"Production setup failed: {error.Message}");
            return;
        }

        _embeddedProductionRunning = true;
        _embeddedLastStageMessage = "";
        _productionRunStartedAt = DateTime.Now;
        _productionElapsedText.Text = "Elapsed 00:00";
        _productionElapsedTimer.Start();
        ResetEmbeddedStageRows();
        _productionCurrentStageText.Text = $"Starting {mode}...";
        _productionProgressBar.Value = 0;
        _productionPercentText.Text = "0%";
        SetEmbeddedSetupEnabled(false);
        ApplyEmbeddedSelectedProject();
        AppendEmbeddedProductionLog($"{(mode == "reproduce" ? "Reproducing" : mode == "resume" ? "Resuming" : "Producing")}: {topic}");
        try
        {
            await _productionWorker.SendAsync(new { command = "start_production", request_id = Guid.NewGuid().ToString("N"), project_id = project.Id, mode, topic });
        }
        catch (Exception error)
        {
            FinishEmbeddedElapsed("Stopped after");
            _embeddedProductionRunning = false;
            ApplyEmbeddedSelectedProject();
            AppendEmbeddedProductionLog($"Could not start production: {error.Message}");
        }
    }

    private async void EmbeddedExportResolve_Click(object sender, RoutedEventArgs e)
    {
        if (_productionWorker is not { IsRunning: true } || EmbeddedSelectedProject is not { } project) return;
        _productionExportButton.IsEnabled = false;
        _productionCurrentStageText.Text = "Creating Resolve export...";
        AppendEmbeddedProductionLog("Creating Resolve export...");
        await _productionWorker.SendAsync(new { command = "export_resolve", request_id = Guid.NewGuid().ToString("N"), project_id = project.Id });
    }

    private void EmbeddedOpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = EmbeddedSelectedProject?.Folder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private async void EmbeddedCancelProduction_Click(object sender, RoutedEventArgs e)
    {
        if (_productionWorker is not { IsRunning: true } || !_embeddedProductionRunning) return;
        _productionCancelButton.IsEnabled = false;
        AppendEmbeddedProductionLog("Cancellation requested...");
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
                case "resolve_export_ready":
                    var exportPath = ReadEmbeddedString(root, "path");
                    _productionCurrentStageText.Text = "Resolve export ready";
                    AppendEmbeddedProductionLog($"Resolve FCPXML created: {exportPath}");
                    _productionExportButton.IsEnabled = EmbeddedSelectedProject?.TimelineExists == true;
                    var exportFolder = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrWhiteSpace(exportFolder) && Directory.Exists(exportFolder))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exportFolder}\"") { UseShellExecute = true });
                    break;
                case "warning": AppendEmbeddedProductionLog($"Warning: {ReadEmbeddedString(root, "message")}"); break;
                case "error":
                    FinishEmbeddedElapsed("Stopped after");
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
        var wasRunning = _embeddedProductionRunning;
        _embeddedProductionRunning = ReadEmbeddedBool(root, "running");
        var progress = root.TryGetProperty("progress", out var progressElement) && progressElement.TryGetDouble(out var value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        var message = ReadEmbeddedString(root, "message");
        var stage = ReadEmbeddedString(root, "current_stage");
        var error = ReadEmbeddedString(root, "error");
        _productionProgressBar.Value = progress * 100;
        _productionPercentText.Text = $"{Math.Round(progress * 100):0}%";
        _productionCurrentStageText.Text = string.IsNullOrWhiteSpace(stage) ? message : $"{ProductionStageLabels.GetValueOrDefault(stage, stage)}: {message}";
        ApplyEmbeddedStageRows(root);
        var logMessage = string.IsNullOrWhiteSpace(error) ? message : $"{message}: {error}";
        if (!string.IsNullOrWhiteSpace(logMessage) && logMessage != _embeddedLastStageMessage)
        {
            AppendEmbeddedProductionLog(logMessage);
            _embeddedLastStageMessage = logMessage;
        }
        if (wasRunning && !_embeddedProductionRunning)
        {
            FinishEmbeddedElapsed(string.IsNullOrWhiteSpace(error) ? "Completed in" : "Stopped after");
        }
        ApplyEmbeddedSelectedProject();
        if (!_embeddedProductionRunning) _ = RefreshEmbeddedProductionProjectsAsync();
    }

    private void ApplyEmbeddedStageRows(JsonElement root)
    {
        if (!root.TryGetProperty("stages", out var stages) || stages.ValueKind != JsonValueKind.Array) return;
        foreach (var stage in stages.EnumerateArray())
        {
            var name = ReadEmbeddedString(stage, "name");
            if (!_productionStageRows.TryGetValue(name, out var row)) continue;
            var status = ReadEmbeddedString(stage, "status");
            row.Icon.Text = status switch
            {
                "running" => "▶",
                "complete" => "✓",
                "failed" => "✗",
                "cancelled" => "■",
                _ => "○",
            };
            row.Icon.Foreground = status switch
            {
                "complete" => ProductionReadyBrush(),
                "failed" => new SolidColorBrush(Color.FromRgb(180, 35, 24)),
                "running" => new SolidColorBrush(Color.FromRgb(23, 92, 211)),
                _ => ProductionMutedBrush(),
            };
            var detail = ReadEmbeddedString(stage, "message");
            row.Detail.Text = string.IsNullOrWhiteSpace(detail) ? (string.IsNullOrWhiteSpace(status) ? "Waiting" : char.ToUpperInvariant(status[0]) + status[1..]) : detail;
        }
    }

    private void ResetEmbeddedStageRows()
    {
        foreach (var row in _productionStageRows.Values)
        {
            row.Icon.Text = "○";
            row.Icon.Foreground = ProductionMutedBrush();
            row.Detail.Text = "Waiting";
        }
    }

    private void UpdateEmbeddedElapsed()
    {
        if (_productionRunStartedAt is not DateTime started) return;
        _productionElapsedText.Text = $"Elapsed {FormatEmbeddedElapsed(DateTime.Now - started)}";
    }

    private void FinishEmbeddedElapsed(string prefix)
    {
        if (_productionRunStartedAt is not DateTime started) return;
        _productionElapsedTimer.Stop();
        _productionElapsedText.Text = $"{prefix} {FormatEmbeddedElapsed(DateTime.Now - started)}";
        _productionRunStartedAt = null;
    }

    private static string FormatEmbeddedElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
        : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

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
