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

        var credentials = NativeProviderCredentials.FromSettings(appSettings);
        var providers = new List<string>();
        if (usePexels && !string.IsNullOrWhiteSpace(credentials.Get("pexels", required: false)))
            providers.Add("pexels");
        if (usePixabay && !string.IsNullOrWhiteSpace(credentials.Get("pixabay", required: false)))
            providers.Add("pixabay");
        providers.Add("openverse");
        providers.Add("wikimedia");

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
        var openAiConfigured = !string.IsNullOrWhiteSpace(credentials.Get("openai", required: false));
        lines.Add($"{(openAiConfigured ? "✓" : "✗")} OpenAI");

        AddOptionalStockProvider("Pexels", "pexels", usePexels);
        AddOptionalStockProvider("Pixabay", "pixabay", usePixabay);
        lines.Add("✓ Openverse (no API key)");
        lines.Add("✓ Wikimedia Commons (no API key)");

        return new NativeProviderReadiness(openAiConfigured, lines);

        void AddOptionalStockProvider(string label, string provider, bool selected)
        {
            if (!selected)
                return;

            var configured = !string.IsNullOrWhiteSpace(credentials.Get(provider, required: false));
            lines.Add(configured
                ? $"✓ {label}"
                : $"— {label} selected but no API key; free providers will be used instead");
        }
    }

    public static void ValidateProject(string projectFolder, AppSettingsModel appSettings)
    {
        using var providers = NativeProductionProviders.FromProject(projectFolder, appSettings);
    }
}
