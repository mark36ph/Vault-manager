using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record NativeProviderSettings
{
    public string TextProvider { get; init; } = "openai";
    public IReadOnlyList<string> AssetProviders { get; init; } = ["pexels", "pixabay"];
    public string VoiceProvider { get; init; } = "openai";
    public string OpenAiModel { get; init; } = "gpt-5-mini";
    public string OpenAiVoiceModel { get; init; } = "gpt-4o-mini-tts";
    public string OpenAiVoice { get; init; } = "alloy";
    public string AssetKind { get; init; } = "image";
    public int AssetLimit { get; init; } = 20;
    public int AssetAttempts { get; init; } = 3;

    public void Validate()
    {
        if (!string.Equals(TextProvider, "openai", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"unsupported text provider: {TextProvider}");

        if (!string.Equals(VoiceProvider, "openai", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(VoiceProvider, "none", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"unsupported voice provider: {VoiceProvider}");

        var unknownAssets = AssetProviders
            .Where(name => !string.Equals(name, "pexels", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(name, "pixabay", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownAssets.Length > 0)
            throw new ArgumentException($"unsupported asset providers: {string.Join(", ", unknownAssets)}");

        if (!string.Equals(AssetKind, "image", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(AssetKind, "video", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("asset_kind must be image or video");

        if (AssetLimit < 1)
            throw new ArgumentException("asset_limit must be at least 1");
        if (AssetAttempts < 1)
            throw new ArgumentException("asset_attempts must be at least 1");
    }
}

public sealed class NativeProviderSettingsStore
{
    public const string FileName = "provider_settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Path { get; }

    public NativeProviderSettingsStore(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("project folder is required", nameof(projectFolder));
        Path = System.IO.Path.Combine(projectFolder, FileName);
    }

    public NativeProviderSettings Load()
    {
        if (!File.Exists(Path))
            return new NativeProviderSettings();

        try
        {
            var json = File.ReadAllText(Path);
            var settings = JsonSerializer.Deserialize<NativeProviderSettings>(json, JsonOptions)
                ?? throw new InvalidOperationException("provider settings must contain a JSON object");
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException($"could not read provider settings: {Path}: {error.Message}", error);
        }
    }

    public string Save(NativeProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporary = System.IO.Path.ChangeExtension(Path, ".tmp");
        var json = JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine;
        File.WriteAllText(temporary, json);
        File.Move(temporary, Path, overwrite: true);
        return Path;
    }
}

public sealed record NativeProviderCredentials(string OpenAiKey, string PexelsKey, string PixabayKey)
{
    public static NativeProviderCredentials FromSettings(
        AppSettingsModel settings,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        environment ??= Environment.GetEnvironmentVariable;

        return new NativeProviderCredentials(
            PreferStored(settings.OpenAiKey, environment("OPENAI_API_KEY")),
            PreferStored(settings.PexelsKey, environment("PEXELS_API_KEY")),
            PreferStored(settings.PixabayKey, environment("PIXABAY_API_KEY")));
    }

    public string Get(string provider, bool required = true)
    {
        var value = provider.Trim().ToLowerInvariant() switch
        {
            "openai" => OpenAiKey,
            "pexels" => PexelsKey,
            "pixabay" => PixabayKey,
            _ => throw new ArgumentException($"unknown provider: {provider}", nameof(provider)),
        };

        if (required && string.IsNullOrWhiteSpace(value))
        {
            var variable = provider.Trim().ToLowerInvariant() switch
            {
                "openai" => "OPENAI_API_KEY",
                "pexels" => "PEXELS_API_KEY",
                "pixabay" => "PIXABAY_API_KEY",
                _ => provider,
            };
            throw new InvalidOperationException($"{variable} is not configured");
        }

        return value;
    }

    private static string PreferStored(string? stored, string? fallback)
    {
        var saved = (stored ?? "").Trim();
        return saved.Length > 0 ? saved : (fallback ?? "").Trim();
    }
}
