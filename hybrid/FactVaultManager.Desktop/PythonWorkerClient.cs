using System.Diagnostics;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class PythonWorkerClient : IAsyncDisposable
{
    private const int ProtocolVersion = 2;
    private readonly string _runtimeRoot;
    private readonly ProductionProjectCatalog _projectCatalog;
    private Process? _process;

    public event Action<string>? MessageReceived;
    public event Action<string>? ErrorReceived;

    public bool IsRunning => _process is { HasExited: false };

    public PythonWorkerClient(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
        _projectCatalog = new ProductionProjectCatalog(new DesktopDataService());
    }

    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var bundledWorker = Path.Combine(AppContext.BaseDirectory, "FactVaultWorker.exe");
        var developmentWorker = Path.Combine(_runtimeRoot, "hybrid", "python_worker.py");
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager"
        );
        Directory.CreateDirectory(appDataRoot);
        ProcessStartInfo startInfo;

        if (File.Exists(bundledWorker))
        {
            MigrateDevelopmentDataIfNeeded(appDataRoot);
            startInfo = new ProcessStartInfo
            {
                FileName = bundledWorker,
                WorkingDirectory = appDataRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        }
        else
        {
            if (!File.Exists(developmentWorker))
            {
                throw new FileNotFoundException("Python worker was not found.", developmentWorker);
            }

            File.WriteAllText(Path.Combine(appDataRoot, "development-root.txt"), _runtimeRoot);
            startInfo = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = $"-u \"{developmentWorker}\"",
                WorkingDirectory = _runtimeRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) => MessageReceived?.Invoke("Python worker exited.");

        if (!_process.Start())
        {
            throw new InvalidOperationException("Could not start the Python worker.");
        }

        _ = PumpOutputAsync(_process.StandardOutput, MessageReceived);
        _ = PumpOutputAsync(_process.StandardError, ErrorReceived);

        MessageReceived?.Invoke(JsonSerializer.Serialize(new
        {
            type = "ready",
            protocol = ProtocolVersion,
            executor = File.Exists(bundledWorker) ? "bundled" : "python",
        }));
        return Task.CompletedTask;
    }

    private static void MigrateDevelopmentDataIfNeeded(string appDataRoot)
    {
        var destination = Path.Combine(appDataRoot, "data");
        if (File.Exists(Path.Combine(destination, "factvault.db")))
        {
            return;
        }

        var developmentRoot = FindDevelopmentRoot(appDataRoot);
        if (string.IsNullOrWhiteSpace(developmentRoot))
        {
            return;
        }

        var source = Path.Combine(developmentRoot, "data");
        if (!Directory.Exists(source))
        {
            return;
        }

        CopyDirectory(source, destination);
    }

    private static string? FindDevelopmentRoot(string appDataRoot)
    {
        var marker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(marker))
        {
            var markedRoot = File.ReadAllText(marker).Trim();
            if (File.Exists(Path.Combine(markedRoot, "data", "factvault.db")))
            {
                return markedRoot;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var folderName in new[] { "FactVaultManager", "Vault-manager" })
        {
            var candidate = Path.Combine(documents, folderName);
            if (File.Exists(Path.Combine(candidate, "data", "factvault.db")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public async Task SendAsync(object payload)
    {
        if (!IsRunning || _process is null)
        {
            throw new InvalidOperationException("Python worker is not running.");
        }

        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var command = root.TryGetProperty("command", out var commandElement)
            ? commandElement.GetString() ?? ""
            : "";

        if (string.Equals(command, "list_projects", StringComparison.OrdinalIgnoreCase))
        {
            var requestId = root.TryGetProperty("request_id", out var requestIdElement)
                ? requestIdElement.ToString()
                : "";
            var projects = _projectCatalog.GetProjects()
                .Select(project => new
                {
                    id = project.Id,
                    title = project.Title,
                    status = project.Status,
                    category = project.Category,
                    folder = project.Folder,
                    folder_exists = project.FolderExists,
                    checkpoint_exists = project.CheckpointExists,
                    timeline_exists = project.TimelineExists,
                })
                .ToList();
            MessageReceived?.Invoke(JsonSerializer.Serialize(new
            {
                type = "projects",
                request_id = requestId,
                projects,
            }));
            return;
        }

        await _process.StandardInput.WriteLineAsync(json);
        await _process.StandardInput.FlushAsync();
    }

    private static async Task PumpOutputAsync(StreamReader reader, Action<string>? callback)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            callback?.Invoke(line);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        if (!_process.HasExited)
        {
            try
            {
                await SendAsync(new { command = "shutdown", request_id = Guid.NewGuid().ToString("N") });
                await Task.WhenAny(_process.WaitForExitAsync(), Task.Delay(1500));
            }
            catch
            {
                // App shutdown should never be blocked by worker cleanup.
            }
        }

        _process.Dispose();
        _process = null;
    }
}
