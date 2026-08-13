using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class ProductionWindow : Window
{
    private readonly string _repositoryRoot;
    private readonly PythonWorkerClient _worker;
    private readonly List<HybridProject> _projects = new();
    private bool _productionRunning;
    private string _lastStageMessage = "";

    public ProductionWindow()
    {
        InitializeComponent();
        _repositoryRoot = LocateRepositoryRoot();
        RepositoryText.Text = $"Repository: {_repositoryRoot}";

        _worker = new PythonWorkerClient(_repositoryRoot);
        _worker.MessageReceived += line => Dispatcher.BeginInvoke(() => HandleWorkerLine(line));
        _worker.ErrorReceived += line => Dispatcher.BeginInvoke(() => AppendLog($"worker stderr: {line}"));

        Loaded += ProductionWindow_Loaded;
        Closed += async (_, _) => await _worker.DisposeAsync();
    }

    private static string LocateRepositoryRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "main.py")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "common")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the FactVaultManager repository root. Run the desktop shell from the repository checkout."
        );
    }

    private async void ProductionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _worker.StartAsync();
            AppendLog("Python production worker started.");
        }
        catch (Exception error)
        {
            WorkerStatusText.Text = "Connection failed";
            AppendLog($"Worker connection failed: {error.Message}");
        }
    }

    private async Task RefreshProjectsAsync()
    {
        if (!_worker.IsRunning)
        {
            return;
        }

        await _worker.SendAsync(new
        {
            command = "list_projects",
            request_id = Guid.NewGuid().ToString("N"),
        });
    }

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e)
    {
        await RefreshProjectsAsync();
    }

    private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySelectedProject();
    }

    private HybridProject? SelectedProject => ProjectComboBox.SelectedItem as HybridProject;

    private void ApplySelectedProject()
    {
        var project = SelectedProject;
        if (project is null)
        {
            ProjectStatusText.Text = "No project selected.";
            ProjectFolderText.Text = "";
            ProductionActionButton.IsEnabled = false;
            ResumeProductionButton.IsEnabled = false;
            return;
        }

        var readiness = project.FolderExists ? "Project folder ready" : "Project folder missing";
        if (project.CheckpointExists)
        {
            readiness += " • resume available";
        }
        if (project.TimelineExists)
        {
            readiness += " • timeline available";
        }

        ProjectStatusText.Text = $"{project.Status} • {project.Category} • {readiness}";
        ProjectFolderText.Text = project.Folder;
        ProductionActionButton.Content = project.Status == "Completed" ? "Reproduce Video" : "Produce Video";
        ProductionActionButton.IsEnabled = !_productionRunning && project.FolderExists &&
                                           project.Status is "In Progress" or "Completed";
        ResumeProductionButton.IsEnabled = !_productionRunning && project.FolderExists && project.CheckpointExists;
        ProjectComboBox.IsEnabled = !_productionRunning;
        RefreshProjectsButton.IsEnabled = !_productionRunning && _worker.IsRunning;
        CancelProductionButton.IsEnabled = _productionRunning;
    }

    private async void ProductionAction_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        var mode = project.Status == "Completed" ? "reproduce" : "produce";
        await StartProductionAsync(project, mode);
    }

    private async void ResumeProduction_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        await StartProductionAsync(project, "resume");
    }

    private async Task StartProductionAsync(HybridProject project, string mode)
    {
        if (!_worker.IsRunning || _productionRunning)
        {
            return;
        }

        _productionRunning = true;
        _lastStageMessage = "";
        CurrentStageText.Text = $"Starting {mode}...";
        ProductionProgressBar.Value = 0;
        ProductionProgressText.Text = "0%";
        ApplySelectedProject();
        AppendLog($"{(mode == "reproduce" ? "Reproducing" : mode == "resume" ? "Resuming" : "Producing")}: {project.Title}");

        try
        {
            await _worker.SendAsync(new
            {
                command = "start_production",
                request_id = Guid.NewGuid().ToString("N"),
                project_id = project.Id,
                mode,
                topic = project.Title,
            });
        }
        catch (Exception error)
        {
            _productionRunning = false;
            ApplySelectedProject();
            AppendLog($"Could not start production: {error.Message}");
        }
    }

    private async void CancelProduction_Click(object sender, RoutedEventArgs e)
    {
        if (!_worker.IsRunning || !_productionRunning)
        {
            return;
        }

        await _worker.SendAsync(new
        {
            command = "cancel_production",
            request_id = Guid.NewGuid().ToString("N"),
        });
    }

    private void HandleWorkerLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? ""
                : "";

            switch (type)
            {
                case "ready":
                    WorkerStatusText.Text = "Connected";
                    RefreshProjectsButton.IsEnabled = true;
                    AppendLog($"Worker connected (protocol {ReadInt(root, "protocol")}).");
                    _ = RefreshProjectsAsync();
                    break;

                case "pong":
                    WorkerStatusText.Text = "Connected";
                    break;

                case "projects":
                    LoadProjects(root);
                    break;

                case "production_started":
                    AppendLog($"Production started: {ReadString(root, "title")} [{ReadString(root, "mode")}]");
                    break;

                case "production_state":
                    ApplyProductionState(root);
                    break;

                case "project_updated":
                    AppendLog("Project status changed to Completed.");
                    _ = RefreshProjectsAsync();
                    break;

                case "warning":
                    AppendLog($"Warning: {ReadString(root, "message")}");
                    break;

                case "error":
                    _productionRunning = false;
                    ApplySelectedProject();
                    CurrentStageText.Text = "Production error";
                    AppendLog($"Worker error: {ReadString(root, "message")}");
                    break;

                case "shutdown":
                    WorkerStatusText.Text = "Disconnected";
                    break;

                default:
                    if (!string.IsNullOrWhiteSpace(type) && type != "accepted" && type != "cancel_requested")
                    {
                        AppendLog($"worker: {line}");
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            AppendLog($"worker: {line}");
        }
    }

    private void LoadProjects(JsonElement root)
    {
        var selectedId = SelectedProject?.Id;
        _projects.Clear();

        if (root.TryGetProperty("projects", out var projectsElement) && projectsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in projectsElement.EnumerateArray())
            {
                _projects.Add(new HybridProject(
                    ReadInt(item, "id"),
                    ReadString(item, "title"),
                    ReadString(item, "status"),
                    ReadString(item, "category"),
                    ReadString(item, "folder"),
                    ReadBool(item, "folder_exists"),
                    ReadBool(item, "checkpoint_exists"),
                    ReadBool(item, "timeline_exists")
                ));
            }
        }

        ProjectComboBox.ItemsSource = null;
        ProjectComboBox.ItemsSource = _projects;
        ProjectComboBox.IsEnabled = !_productionRunning && _projects.Count > 0;

        if (_projects.Count == 0)
        {
            ProjectStatusText.Text = "No In Progress or Completed projects found.";
            ProjectFolderText.Text = "";
            ProductionActionButton.IsEnabled = false;
            ResumeProductionButton.IsEnabled = false;
            return;
        }

        ProjectComboBox.SelectedItem = selectedId is int id
            ? _projects.FirstOrDefault(project => project.Id == id) ?? _projects[0]
            : _projects[0];
        ApplySelectedProject();
        AppendLog($"Loaded {_projects.Count} production project(s).");
    }

    private void ApplyProductionState(JsonElement root)
    {
        _productionRunning = ReadBool(root, "running");
        var progress = root.TryGetProperty("progress", out var progressElement) && progressElement.TryGetDouble(out var value)
            ? Math.Clamp(value, 0.0, 1.0)
            : 0.0;
        var message = ReadString(root, "message");
        var stage = ReadString(root, "current_stage");
        var error = ReadString(root, "error");

        ProductionProgressBar.Value = progress * 100.0;
        ProductionProgressText.Text = $"{Math.Round(progress * 100.0):0}%";
        CurrentStageText.Text = string.IsNullOrWhiteSpace(stage) ? message : $"{stage}: {message}";

        var logMessage = string.IsNullOrWhiteSpace(error) ? message : $"{message}: {error}";
        if (!string.IsNullOrWhiteSpace(logMessage) && logMessage != _lastStageMessage)
        {
            AppendLog(logMessage);
            _lastStageMessage = logMessage;
        }

        ApplySelectedProject();
        if (!_productionRunning)
        {
            _ = RefreshProjectsAsync();
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return "";
        }
        return element.ToString();
    }

    private static int ReadInt(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.TryGetInt32(out var value) ? value : 0;
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False && element.GetBoolean();
    }
}

public sealed record HybridProject(
    int Id,
    string Title,
    string Status,
    string Category,
    string Folder,
    bool FolderExists,
    bool CheckpointExists,
    bool TimelineExists)
{
    public string DisplayName => $"{Title}  •  {Status}";
}
