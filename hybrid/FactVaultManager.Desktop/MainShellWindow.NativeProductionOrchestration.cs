using System.Windows;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _nativeProductionWired;
    private CancellationTokenSource? _nativeProductionCancellation;

    private void WireNativeProductionOrchestration()
    {
        if (_nativeProductionWired || _productionActionButton is null)
            return;

        _nativeProductionWired = true;
        _productionWorkerStatusText.Text = "Native C# engine ready";
        _productionRefreshButton.IsEnabled = true;

        _productionProjectComboBox.SelectionChanged += (_, _) => UpdateNativeProductionActionAvailability();
        _productionPexelsCheckBox.Click += (_, _) => UpdateNativeProductionActionAvailability();
        _productionPixabayCheckBox.Click += (_, _) => UpdateNativeProductionActionAvailability();
        _productionVoiceCheckBox.Click += (_, _) => UpdateNativeProductionActionAvailability();

        AppendEmbeddedProductionLog("Native C# production orchestration enabled.");
        RefreshNativeProductionProjects();
        UpdateNativeProductionActionAvailability();
    }

    private void EmbeddedNativeProductionAction_Click(object sender, RoutedEventArgs e)
    {
        AppendEmbeddedProductionLog("Produce Video clicked.");

        var project = EmbeddedSelectedProject;
        if (project is null)
        {
            AppendEmbeddedProductionLog("Produce Video could not start: no Production project is selected.");
            RefreshNativeProductionProjects();
            UpdateNativeProductionActionAvailability();
            return;
        }

        AppendEmbeddedProductionLog($"Production request: {project.Title} • {project.Status}");
        _ = StartNativeProductionAsync(project, project.Status == "Completed" ? "reproduce" : "produce");
    }

    private void EmbeddedNativeResumeProduction_Click(object sender, RoutedEventArgs e)
    {
        if (EmbeddedSelectedProject is { } project)
            _ = StartNativeProductionAsync(project, "resume");
    }

    private void EmbeddedNativeCancelProduction_Click(object sender, RoutedEventArgs e)
    {
        if (!_embeddedProductionRunning || _nativeProductionCancellation is null)
            return;
        _productionCancelButton.IsEnabled = false;
        AppendEmbeddedProductionLog("Cancellation requested...");
        _nativeProductionCancellation.Cancel();
    }

    private void EmbeddedNativeRefreshProjects_Click(object sender, RoutedEventArgs e) =>
        RefreshNativeProductionProjects();

    private async Task StartNativeProductionAsync(HybridProject project, string mode)
    {
        if (_embeddedProductionRunning)
        {
            AppendEmbeddedProductionLog("Production request ignored because another production run is already active.");
            return;
        }

        var topic = _productionTopicTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            _productionTopicTextBox.Focus();
            AppendEmbeddedProductionLog("Enter a topic before starting production.");
            return;
        }

        var desktopProject = _data.GetProjects().FirstOrDefault(item => item.Id == project.Id);
        if (desktopProject is null)
        {
            AppendEmbeddedProductionLog($"Project {project.Id} was not found in the database.");
            RefreshNativeProductionProjects();
            UpdateNativeProductionActionAvailability();
            return;
        }

        string folder;
        try
        {
            folder = _data.ResolveProjectFolder(desktopProject);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Project folder was not found: {folder}");

            AppendEmbeddedProductionLog($"Preparing production in: {folder}");
            SaveEmbeddedProviderSettings(project);
            NativeProductionProviderWorkflow.ValidateProject(folder, _data.LoadSettings());
        }
        catch (Exception error)
        {
            AppendEmbeddedProductionLog($"Production setup failed: {error.Message}");
            UpdateNativeProductionActionAvailability();
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
        _productionActionButton.IsEnabled = false;
        _productionResumeButton.IsEnabled = false;
        _productionExportButton.IsEnabled = false;
        _productionRefreshButton.IsEnabled = false;
        _productionCancelButton.IsEnabled = true;
        AppendEmbeddedProductionLog($"{(mode == "reproduce" ? "Reproducing" : mode == "resume" ? "Resuming" : "Producing")}: {topic}");

        _nativeProductionCancellation?.Dispose();
        _nativeProductionCancellation = new CancellationTokenSource();
        var token = _nativeProductionCancellation.Token;
        NativeProductionRunResult? result = null;
        var completedSuccessfully = false;

        try
        {
            var settings = _data.LoadSettings();
            var orchestrator = new NativeProductionOrchestrator(
                settings,
                progress => Dispatcher.BeginInvoke(() => ApplyNativeProductionProgress(progress)));

            result = await orchestrator.RunAsync(desktopProject, folder, topic, mode, token);
            foreach (var warning in result.Warnings.Distinct(StringComparer.Ordinal))
                AppendEmbeddedProductionLog($"Warning: {warning}");

            completedSuccessfully = result.Succeeded;
            if (!completedSuccessfully)
                throw new NativeProductionException("Production stopped before all stages completed.");

            _productionProgressBar.Value = 100;
            _productionPercentText.Text = "100%";
            _productionCurrentStageText.Text = "Production complete";
            AppendEmbeddedProductionLog("Native C# production complete.");

            if (string.Equals(desktopProject.Status, "In Progress", StringComparison.Ordinal))
            {
                var oldFolder = folder;
                var updated = _data.ChangeStatus(desktopProject, "Completed");
                var newFolder = _data.ResolveProjectFolder(updated);
                try
                {
                    NativeProductionOrchestrator.RebaseProjectPaths(oldFolder, newFolder);
                }
                catch (Exception error)
                {
                    AppendEmbeddedProductionLog($"Warning: project completed but timeline path rebasing failed: {error.Message}");
                }
                AppendEmbeddedProductionLog("Project status changed to Completed.");
                RefreshAll();
            }
        }
        catch (OperationCanceledException)
        {
            _productionCurrentStageText.Text = "Production cancelled";
            MarkRunningNativeStage("cancelled", "Cancelled");
            AppendEmbeddedProductionLog("Production cancelled.");
        }
        catch (Exception error)
        {
            _productionCurrentStageText.Text = "Production error";
            MarkRunningNativeStage("failed", "Failed");
            AppendEmbeddedProductionLog($"Production failed: {error.Message}");
        }
        finally
        {
            FinishEmbeddedElapsed(completedSuccessfully ? "Completed in" : "Stopped after");
            _embeddedProductionRunning = false;
            _nativeProductionCancellation?.Dispose();
            _nativeProductionCancellation = null;
            RefreshNativeProductionProjects(project.Id);
            SetEmbeddedSetupEnabled(true);
            ApplyEmbeddedSelectedProject();
            _productionRefreshButton.IsEnabled = true;
            _productionCancelButton.IsEnabled = false;
            UpdateNativeProductionActionAvailability();
        }
    }

    private void UpdateNativeProductionActionAvailability()
    {
        var project = EmbeddedSelectedProject;
        var validStatus = project is not null &&
            (string.Equals(project.Status, "In Progress", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var canStart = !_embeddedProductionRunning && project is { FolderExists: true } && validStatus;

        _productionActionButton.IsEnabled = canStart;
        _productionActionButton.ToolTip = canStart
            ? "Start production for the selected project"
            : project is null
                ? "Select an In Progress or Completed project"
                : !project.FolderExists
                    ? "The selected project folder is missing"
                    : _embeddedProductionRunning
                        ? "Production is already running"
                        : $"Project status '{project.Status}' cannot be produced";
    }

    private void ApplyNativeProductionProgress(NativeProductionProgress progress)
    {
        if (!_embeddedProductionRunning)
            return;

        _productionProgressBar.Value = Math.Clamp(progress.Progress * 100, 0, 100);
        _productionPercentText.Text = $"{Math.Round(_productionProgressBar.Value):0}%";
        _productionCurrentStageText.Text = $"{ProductionStageLabels.GetValueOrDefault(progress.Stage, progress.Stage)}: {progress.Message}";

        var currentIndex = Array.IndexOf(ProductionStageOrder, progress.Stage);
        if (currentIndex >= 0)
        {
            for (var index = 0; index < currentIndex; index++)
                SetNativeStageRow(ProductionStageOrder[index], "complete", "Complete");
            SetNativeStageRow(progress.Stage, progress.Status, progress.Message);
        }

        if (!string.IsNullOrWhiteSpace(progress.Message) && progress.Message != _embeddedLastStageMessage)
        {
            AppendEmbeddedProductionLog(progress.Message);
            _embeddedLastStageMessage = progress.Message;
        }
    }

    private void SetNativeStageRow(string stage, string status, string detail)
    {
        if (!_productionStageRows.TryGetValue(stage, out var row))
            return;

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
        row.Detail.Text = string.IsNullOrWhiteSpace(detail)
            ? (status.Length == 0 ? "Waiting" : char.ToUpperInvariant(status[0]) + status[1..])
            : detail;
    }

    private void MarkRunningNativeStage(string status, string detail)
    {
        foreach (var stage in ProductionStageOrder)
        {
            if (_productionStageRows.TryGetValue(stage, out var row) && row.Icon.Text == "▶")
            {
                SetNativeStageRow(stage, status, detail);
                return;
            }
        }
    }

    private void RefreshNativeProductionProjects(int? preferredProjectId = null)
    {
        var selectedId = preferredProjectId ?? EmbeddedSelectedProject?.Id;
        IReadOnlyList<HybridProject> projects;
        try
        {
            projects = new ProductionProjectCatalog(_data).GetProjects();
        }
        catch (Exception error)
        {
            AppendEmbeddedProductionLog($"Could not refresh projects: {error.Message}");
            UpdateNativeProductionActionAvailability();
            return;
        }

        _productionProjects.Clear();
        _productionProjects.AddRange(projects);
        _productionProjectComboBox.ItemsSource = null;
        _productionProjectComboBox.ItemsSource = _productionProjects;

        if (_productionShowCompletedCheckBox is not null)
        {
            ApplyProductionProjectVisibility();
            var visible = _productionProjectComboBox.ItemsSource?.Cast<HybridProject>().ToList() ?? new List<HybridProject>();
            if (selectedId is int id)
                _productionProjectComboBox.SelectedItem = visible.FirstOrDefault(item => item.Id == id) ?? visible.FirstOrDefault();
        }
        else if (_productionProjects.Count > 0)
        {
            _productionProjectComboBox.SelectedItem = selectedId is int id
                ? _productionProjects.FirstOrDefault(item => item.Id == id) ?? _productionProjects[0]
                : _productionProjects[0];
        }

        if (_productionProjects.Count == 0)
        {
            _productionProjectStatusText.Text = "No In Progress or Completed projects found.";
            _productionProjectFolderText.Text = "";
        }
        else
        {
            ApplyEmbeddedSelectedProject();
        }

        _productionRefreshButton.IsEnabled = !_embeddedProductionRunning;
        UpdateNativeProductionActionAvailability();
        AppendEmbeddedProductionLog($"Loaded {_productionProjects.Count} production project(s) from C# catalog.");
    }
}
