namespace FactVaultManager.Desktop;

public sealed record NativeProviderReadiness(bool Ready, IReadOnlyList<string> Lines);

public static class NativeProductionProviderWorkflow
{
    public static NativeProviderSettings Load(string projectFolder) =>
        new NativeProviderSettingsStore(projectFolder).Load();

    public static NativeProviderSettings Save(
        string projectFolder,
        AppSettingsModel appSettings,
        bool usePexels,
        bool usePixabay,
        bool useVoice,
        string assetKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(appSettings);

        var providers = new List<string>();
        if (usePexels) providers.Add("pexels");
        if (usePixabay) providers.Add("pixabay");
        if (providers.Count == 0)
            throw new InvalidOperationException("Select at least one media provider.");

        var settings = new NativeProviderSettings
        {
            AssetProviders = providers,
            VoiceProvider = useVoice ? "openai" : "none",
            OpenAiModel = string.IsNullOrWhiteSpace(appSettings.OpenAiModel) ? "gpt-5-mini" : appSettings.OpenAiModel,
            AssetKind = string.Equals(assetKind, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image",
        };

        new NativeProviderSettingsStore(projectFolder).Save(settings);
        return settings;
    }

    public static NativeProviderReadiness CheckReadiness(
        AppSettingsModel appSettings,
        bool usePexels,
        bool usePixabay)
    {
        ArgumentNullException.ThrowIfNull(appSettings);

        var credentials = NativeProviderCredentials.FromSettings(appSettings);
        var lines = new List<string>();
        var ready = true;

        Add("OpenAI", !string.IsNullOrWhiteSpace(credentials.Get("openai", required: false)));
        if (usePexels)
            Add("Pexels", !string.IsNullOrWhiteSpace(credentials.Get("pexels", required: false)));
        if (usePixabay)
            Add("Pixabay", !string.IsNullOrWhiteSpace(credentials.Get("pixabay", required: false)));

        if (!usePexels && !usePixabay)
        {
            ready = false;
            lines.Add("✗ Select at least one media provider");
        }

        return new NativeProviderReadiness(ready, lines);

        void Add(string label, bool configured)
        {
            lines.Add($"{(configured ? "✓" : "✗")} {label}");
            ready &= configured;
        }
    }

    public static void ValidateProject(string projectFolder, AppSettingsModel appSettings)
    {
        using var providers = NativeProductionProviders.FromProject(projectFolder, appSettings);
    }
}
