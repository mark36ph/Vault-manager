using System.Diagnostics;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class PythonWorkerClient : IAsyncDisposable
{
    private readonly string _runtimeRoot;
    private Process? _process;

    public event Action<string>? MessageReceived;
    public event Action<string>? ErrorReceived;

    public bool IsRunning => _process is { HasExited: false };

    public PythonWorkerClient(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var bundledWorker = Path.Combine(AppContext.BaseDirectory, "FactVaultWorker.exe");
        var developmentWorker = Path.Combine(_runtimeRoot, "hybrid", "python_worker.py");
        ProcessStartInfo startInfo;

        if (File.Exists(bundledWorker))
        {
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FactVaultManager"
            );
            Directory.CreateDirectory(dataRoot);
            startInfo = new ProcessStartInfo
            {
                FileName = bundledWorker,
                WorkingDirectory = dataRoot,
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

        await SendAsync(new { command = "ping", request_id = Guid.NewGuid().ToString("N") });
    }

    public async Task SendAsync(object payload)
    {
        if (!IsRunning || _process is null)
        {
            throw new InvalidOperationException("Python worker is not running.");
        }

        var json = JsonSerializer.Serialize(payload);
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
