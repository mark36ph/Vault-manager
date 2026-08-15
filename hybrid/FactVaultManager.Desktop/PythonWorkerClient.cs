namespace FactVaultManager.Desktop;

[Obsolete("Python production has been retired. Native C# production is the only supported runtime.")]
public sealed class PythonWorkerClient : IAsyncDisposable
{
    public event Action<string>? MessageReceived;
    public event Action<string>? ErrorReceived;

    public bool IsRunning => false;

    public PythonWorkerClient(string runtimeRoot)
    {
    }

    public Task StartAsync() => Task.FromException(
        new NotSupportedException("Python production has been retired. Use the native C# production engine."));

    public Task SendAsync(object payload) => Task.FromException(
        new NotSupportedException("Python production has been retired. Use the native C# production engine."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
