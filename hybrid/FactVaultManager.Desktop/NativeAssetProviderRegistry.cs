namespace FactVaultManager.Desktop;

public sealed class NativeAssetProviderRegistry : IDisposable
{
    private readonly List<IDisposable> _ownedProviders = new();
    private readonly Dictionary<string, INativeAssetProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _providers.Keys.ToArray();

    public static NativeAssetProviderRegistry FromSettings(AppSettingsModel settings)
    {
        var registry = new NativeAssetProviderRegistry();
        if (!string.IsNullOrWhiteSpace(settings.PexelsKey))
        {
            var provider = new NativePexelsAssetProvider(settings.PexelsKey);
            registry.Add(provider);
        }
        if (!string.IsNullOrWhiteSpace(settings.PixabayKey))
        {
            var provider = new NativePixabayAssetProvider(settings.PixabayKey);
            registry.Add(provider);
        }
        return registry;
    }

    public INativeAssetProvider Require(string name)
    {
        if (_providers.TryGetValue(name, out var provider))
            return provider;
        throw new InvalidOperationException($"Native asset provider is not configured: {name}");
    }

    public IReadOnlyList<INativeAssetProvider> Resolve(IEnumerable<string> names)
    {
        var result = new List<INativeAssetProvider>();
        foreach (var name in names)
        {
            var normalized = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            result.Add(Require(normalized));
        }
        return result;
    }

    private void Add(INativeAssetProvider provider)
    {
        _providers[provider.Name] = provider;
        if (provider is IDisposable disposable)
            _ownedProviders.Add(disposable);
    }

    public void Dispose()
    {
        foreach (var provider in _ownedProviders)
            provider.Dispose();
        _ownedProviders.Clear();
        _providers.Clear();
    }
}
