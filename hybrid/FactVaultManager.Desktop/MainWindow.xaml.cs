using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainWindow : Window
{
    private readonly string _repositoryRoot;
    private readonly PythonWorkerClient _worker;
    private Process? _legacyProcess;

    public MainWindow()
    {
        InitializeComponent();
        _repositoryRoot = LocateRepositoryRoot();
        RepositoryText.Text = $"Repository: {_repositoryRoot}";

        _worker = new PythonWorkerClient(_repositoryRoot);
        _worker.MessageReceived += line => Dispatcher.Invoke(() => HandleWorkerLine(line));
        _worker.ErrorReceived += line => Dispatcher.Invoke(() => AppendLog($"worker stderr: {line}"));

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

    private async void ConnectWorker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectWorkerButton.IsEnabled = false;
            WorkerStatusText.Text = "Connecting...";
            await _worker.StartAsync();
            AppendLog("Python worker started.");
        }
        catch (Exception error)
        {
            WorkerStatusText.Text = "Connection failed";
            AppendLog($"Worker connection failed: {error.Message}");
            ConnectWorkerButton.IsEnabled = true;
        }
    }

    private void LaunchLegacy_Click(object sender, RoutedEventArgs e)
    {
        if (_legacyProcess is { HasExited: false })
        {
            AppendLog("Current Python app is already running.");
            return;
        }

        var mainPath = Path.Combine(_repositoryRoot, "main.py");
        var startInfo = new ProcessStartInfo
        {
            FileName = "py",
            Arguments = $"-u \"{mainPath}\"",
            WorkingDirectory = _repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            _legacyProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _legacyProcess.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    Dispatcher.Invoke(() => AppendLog($"python: {args.Data}"));
                }
            };
            _legacyProcess.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    Dispatcher.Invoke(() => AppendLog($"python stderr: {args.Data}"));
                }
            };
            _legacyProcess.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                AppendLog("Current Python app exited.");
                StopLegacyButton.IsEnabled = false;
                LaunchLegacyButton.IsEnabled = true;
            });

            _legacyProcess.Start();
            _legacyProcess.BeginOutputReadLine();
            _legacyProcess.BeginErrorReadLine();
            StopLegacyButton.IsEnabled = true;
            LaunchLegacyButton.IsEnabled = false;
            AppendLog($"Opened current Python app (PID {_legacyProcess.Id}).");
        }
        catch (Exception error)
        {
            AppendLog($"Could not launch current Python app: {error.Message}");
        }
    }

    private void StopLegacy_Click(object sender, RoutedEventArgs e)
    {
        if (_legacyProcess is null || _legacyProcess.HasExited)
        {
            return;
        }

        if (_legacyProcess.CloseMainWindow())
        {
            AppendLog("Asked the current Python app to close normally.");
        }
        else
        {
            AppendLog("The Python app did not expose a closable main window; close it normally from its own window.");
        }
    }

    private void HandleWorkerLine(string line)
    {
        AppendLog($"worker: {line}");
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();
                if (type is "ready" or "pong")
                {
                    WorkerStatusText.Text = "Connected";
                    ConnectWorkerButton.IsEnabled = false;
                }
                else if (type == "error")
                {
                    WorkerStatusText.Text = "Worker error";
                }
            }
        }
        catch (JsonException)
        {
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
}
