using System.Diagnostics;
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

        content.Children.Add(ProductionSectionLabel("ENGINE"));
        _productionWorkerStatusText = new TextBlock { Text = "Native C# engine ready", FontWeight = FontWeights.SemiBold };
        content.Children.Add(_productionWorkerStatusText);

        content.Children.Add(ProductionSectionLabel("ACTIONS"));
        _productionActionButton = ProductionButton("▶  Produce Video", true, EmbeddedNativeProductionAction_Click);
        _productionResumeButton = ProductionButton("↻  Resume Production", false, EmbeddedNativeResumeProduction_Click);
        _productionExportButton = ProductionButton("⬆  Create Resolve Export", false, EmbeddedNativeResolveExport_Click);
        _productionCancelButton = ProductionButton("■  Cancel", false, EmbeddedNativeCancelProduction_Click);
        _productionOpenFolderButton = ProductionButton("📂  Open Project Folder", false, EmbeddedOpenProjectFolder_Click);
        _productionRefreshButton = ProductionButton("Refresh projects", false, EmbeddedNativeRefreshProjects_Click);
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

    private HybridProject? EmbeddedSelectedProject => _productionProjectComboBox.SelectedItem as HybridProject;

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
        _productionRefreshButton.IsEnabled = !_embeddedProductionRunning;
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

    private void EmbeddedOpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = EmbeddedSelectedProject?.Folder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
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
}
