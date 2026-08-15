namespace FactVaultManager.Desktop;

public sealed class NativeAssetProviderRegistry : IDisposable
{
    private readonly List<IDisposable> _ownedProviders = new();
    private readonly Dictionary<string, INativeAssetProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _providers.Keys.ToArray();

    public static NativeAssetProviderRegistry FromSettings(AppSettingsModel settings)
    {
        var credentials = NativeProviderCredentials.FromSettings(settings);
        var registry = new NativeAssetProviderRegistry();

        var pexelsKey = credentials.Get("pexels", required: false);
        if (!string.IsNullOrWhiteSpace(pexelsKey))
        {
            var provider = new NativePexelsAssetProvider(pexelsKey);
            registry.Add(provider);
        }

        var pixabayKey = credentials.Get("pixabay", required: false);
        if (!string.IsNullOrWhiteSpace(pixabayKey))
        {
            var provider = new NativePixabayAssetProvider(pixabayKey);
            registry.Add(provider);
        }

        registry.Add(new NativeOpenverseAssetProvider());
        registry.Add(new NativeWikimediaCommonsAssetProvider());

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
