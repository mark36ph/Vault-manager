using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class NativeProviderIntegrationException : Exception
{
    public NativeProviderIntegrationException(string message) : base(message) { }
}

public sealed record NativeProviderCredentials(string OpenAiKey)
{
    public static NativeProviderCredentials FromSettings(
        AppSettingsModel settings,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        environment ??= Environment.GetEnvironmentVariable;

        return new NativeProviderCredentials(
            PreferStored(settings.OpenAiKey, environment("OPENAI_API_KEY")));
    }

    public string Get(string provider, bool required = true)
    {
        if (!string.Equals(provider.Trim(), "openai", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"unknown provider: {provider}", nameof(provider));

        if (required && string.IsNullOrWhiteSpace(OpenAiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        return OpenAiKey;
    }

    private static string PreferStored(string? stored, string? fallback)
    {
        var saved = (stored ?? "").Trim();
        return saved.Length > 0 ? saved : (fallback ?? "").Trim();
    }
}

public sealed class NativeOpenAITextProvider : IDisposable
{
    private readonly string _apiKey;
    private readonly string _instructions;
    private readonly string _model;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public NativeOpenAITextProvider(
        string apiKey,
        string instructions,
        string model = "gpt-5-mini",
        HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "OpenAI API key");
        _instructions = Required(instructions, "instructions");
        _model = Required(model, "model");
        _client = client ?? CreateClient(TimeSpan.FromSeconds(45));
        _ownsClient = client is null;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        prompt = Required(prompt, "provider prompt");
        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["instructions"] = _instructions,
            ["input"] = prompt,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new NativeProviderIntegrationException(
                    $"HTTP {(int)response.StatusCode}\nURL: {request.RequestUri}\nResponse:\n{content}");

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var text = ReadString(root, "output_text");
            if (string.IsNullOrWhiteSpace(text) &&
                root.TryGetProperty("output", out var output) &&
                output.ValueKind == JsonValueKind.Array)
            {
                var chunks = new List<string>();
                foreach (var item in output.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("content", out var contentArray) ||
                        contentArray.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var entry in contentArray.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object)
                            continue;
                        var chunk = ReadString(entry, "text");
                        if (!string.IsNullOrWhiteSpace(chunk))
                            chunks.Add(chunk);
                    }
                }
                text = string.Join("\n", chunks);
            }

            if (string.IsNullOrWhiteSpace(text))
                throw new NativeProviderIntegrationException("OpenAI response did not contain text");
            return text.Trim();
        }
        catch (NativeProviderIntegrationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new NativeProviderIntegrationException(error.Message);
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException($"{name} is required");
        return text;
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactburstQuizManager/1.0");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
