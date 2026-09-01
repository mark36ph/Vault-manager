namespace FactVaultManager.Desktop;

// Temporary compile-time bridge while the legacy XAML/API settings shell is removed.
// Build 143 hides these controls and no active quiz workflow calls these providers.
internal sealed class NativePexelsAssetProvider : IDisposable
{
    public NativePexelsAssetProvider(string apiKey) { }

    public Task<IReadOnlyList<object>> SearchAsync(
        string query,
        string kind,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Pexels support has been retired from Factburst Quiz Manager.");

    public void Dispose() { }
}

internal sealed class NativePixabayAssetProvider : IDisposable
{
    public NativePixabayAssetProvider(string apiKey) { }

    public Task<IReadOnlyList<object>> SearchAsync(
        string query,
        string kind,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Pixabay support has been retired from Factburst Quiz Manager.");

    public void Dispose() { }
}
