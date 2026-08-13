using System.Diagnostics;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class PythonWorkerClient : IAsyncDisposable
{
    private readonly string _repositoryRoot;
    private Process? _process;

    public event Action<string>? MessageReceived;
    public event Action<string>? ErrorReceived;

    public bool IsRunning => _process is { HasExited: false };

    public PythonWorkerClient(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var workerPath = Path.Combine(_repositoryRoot, "hybrid", "python_worker.py");
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("Python worker was not found.", workerPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "py",
            Arguments = $"-u \"{workerPath}\"",
            WorkingDirectory = _repositoryRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

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
                if (!await Task.Run(() => _process.WaitForExit(1500)))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }

        _process.Dispose();
        _process = null;
    }
}
