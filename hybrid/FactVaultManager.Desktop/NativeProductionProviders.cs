namespace FactVaultManager.Desktop;

public sealed class NativeProductionProviders : IDisposable
{
    private readonly List<IDisposable> _owned = new();

    public NativeProviderSettings Settings { get; }
    public NativeProviderCredentials Credentials { get; }
    public NativeOpenAITextProvider Research { get; }
    public NativeOpenAITextProvider Facts { get; }
    public NativeOpenAITextProvider Script { get; }
    public NativeOpenAITextProvider ImagePrompts { get; }
    public NativeOpenAISpeechProvider? Voice { get; }
    public NativeAssetProviderRegistry Assets { get; }
    public NativeAssetAcquisitionEngine AssetAcquisition { get; }
    public INativeAssetVerifier AssetVerifier { get; }
    public NativeVerifiedAssetAcquisitionEngine VerifiedAssetAcquisition { get; }

    private NativeProductionProviders(
        NativeProviderSettings settings,
        NativeProviderCredentials credentials,
        NativeOpenAITextProvider research,
        NativeOpenAITextProvider facts,
        NativeOpenAITextProvider script,
        NativeOpenAITextProvider imagePrompts,
        NativeOpenAISpeechProvider? voice,
        NativeAssetProviderRegistry assets,
        NativeAssetAcquisitionEngine assetAcquisition,
        INativeAssetVerifier assetVerifier,
        NativeVerifiedAssetAcquisitionEngine verifiedAssetAcquisition,
        IDisposable ownedVerifier)
    {
        Settings = settings;
        Credentials = credentials;
        Research = research;
        Facts = facts;
        Script = script;
        ImagePrompts = imagePrompts;
        Voice = voice;
        Assets = assets;
        AssetAcquisition = assetAcquisition;
        AssetVerifier = assetVerifier;
        VerifiedAssetAcquisition = verifiedAssetAcquisition;

        _owned.Add(research);
        _owned.Add(facts);
        _owned.Add(script);
        _owned.Add(imagePrompts);
        if (voice is not null)
            _owned.Add(voice);
        _owned.Add(assets);
        _owned.Add(assetAcquisition);
        _owned.Add(ownedVerifier);
    }

    public static NativeProductionProviders FromProject(string projectFolder, AppSettingsModel appSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(appSettings);

        var settings = new NativeProviderSettingsStore(projectFolder).Load();
        var credentials = NativeProviderCredentials.FromSettings(appSettings);
        var openAiKey = credentials.Get("openai");

        var research = new NativeOpenAITextProvider(
            openAiKey,
            "Research accurately and clearly.",
            settings.OpenAiModel);
        var facts = new NativeOpenAITextProvider(
            openAiKey,
            "Extract only strong factual claims.",
            settings.OpenAiModel);
        var script = new NativeOpenAITextProvider(
            openAiKey,
            "Write engaging factual narration.",
            settings.OpenAiModel);
        var imagePrompts = new NativeOpenAITextProvider(
            openAiKey,
            "Generate highly specific literal stock-photo search queries. Every query must visually match its exact narration scene and remain anchored to the video's main subject. Avoid generic, abstract, symbolic, or loosely related imagery. Prefer realistic documentary photography. Return only one search query per line. For abstract concepts such as heat, cold, expansion, measurement, or engineering, combine the concept with the video's main physical subject rather than searching for the concept by itself.",
            settings.OpenAiModel);

        NativeOpenAISpeechProvider? voice = null;
        if (string.Equals(settings.VoiceProvider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            voice = new NativeOpenAISpeechProvider(
                openAiKey,
                settings.OpenAiVoiceModel,
                settings.OpenAiVoice);
        }

        var assets = NativeAssetProviderRegistry.FromSettings(appSettings);
        var configuredAssets = assets.Resolve(settings.AssetProviders);
        var assetAcquisition = new NativeAssetAcquisitionEngine(configuredAssets);
        var openAiVerifier = new NativeOpenAIImageRelevanceVerifier(openAiKey, settings.OpenAiModel);
        INativeAssetVerifier assetVerifier = new NativeNamedSubjectVerifier(openAiVerifier);
        var verifiedAssetAcquisition = new NativeVerifiedAssetAcquisitionEngine(assetAcquisition, assetVerifier);

        return new NativeProductionProviders(
            settings,
            credentials,
            research,
            facts,
            script,
            imagePrompts,
            voice,
            assets,
            assetAcquisition,
            assetVerifier,
            verifiedAssetAcquisition,
            openAiVerifier);
    }

    public void Dispose()
    {
        for (var index = _owned.Count - 1; index >= 0; index--)
            _owned[index].Dispose();
        _owned.Clear();
    }
}
