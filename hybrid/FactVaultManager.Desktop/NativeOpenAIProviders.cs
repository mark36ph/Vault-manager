using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

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

        using var request = CreateJsonRequest("https://api.openai.com/v1/responses", body);
        using var document = await SendJsonAsync(request, cancellationToken);
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
                    !item.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var entry in content.EnumerateArray())
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

    private HttpRequestMessage CreateJsonRequest(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new NativeProviderIntegrationException(
                    $"HTTP {(int)response.StatusCode}\nURL: {request.RequestUri}\nResponse:\n{content}");
            }

            try
            {
                var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new NativeProviderIntegrationException("provider response must be a JSON object");
                }
                return document;
            }
            catch (JsonException error)
            {
                throw new NativeProviderIntegrationException(error.Message);
            }
        }
        catch (NativeProviderIntegrationException)
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
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} is required");
        return text;
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0 (+desktop media downloader)");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}

public sealed class NativeOpenAISpeechProvider : IDisposable
{
    private const string OutroText = "Fact unlocked. Follow for more";
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _voice;
    private readonly string _responseFormat;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public NativeOpenAISpeechProvider(
        string apiKey,
        string model = "gpt-4o-mini-tts",
        string voice = "alloy",
        string responseFormat = "mp3",
        HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "OpenAI API key");
        _model = model;
        _voice = voice;
        _responseFormat = responseFormat;
        _client = client ?? CreateClient(TimeSpan.FromSeconds(90));
        _ownsClient = client is null;
    }

    public async Task<string> GenerateAsync(
        string script,
        string projectFolder,
        CancellationToken cancellationToken = default)
    {
        script = Required(script, "script");
        projectFolder = Required(projectFolder, "project folder");

        var suffix = "." + _responseFormat.TrimStart('.');
        var scriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)))
            .ToLowerInvariant()[..12];

        var voiceFolder = Path.Combine(projectFolder, "Voice");
        Directory.CreateDirectory(voiceFolder);

        var destination = Path.Combine(voiceFolder, $"narration_{scriptHash}{suffix}");
        var scriptCopy = Path.Combine(voiceFolder, $"narration_{scriptHash}.txt");
        await File.WriteAllTextAsync(scriptCopy, script, Encoding.UTF8, cancellationToken);

        var narration = await RequestSpeechAsync(script, _responseFormat, cancellationToken);
        await WriteAtomicallyAsync(destination, narration, cancellationToken);

        var outroDestination = Path.Combine(voiceFolder, "fact_unlocked.mp3");
        var outro = await RequestSpeechAsync(OutroText, "mp3", cancellationToken);
        await WriteAtomicallyAsync(outroDestination, outro, cancellationToken);

        return destination;
    }

    private async Task<byte[]> RequestSpeechAsync(
        string input,
        string responseFormat,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["voice"] = _voice,
            ["input"] = input,
            ["response_format"] = responseFormat,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = Encoding.UTF8.GetString(data);
                throw new NativeProviderIntegrationException(
                    $"HTTP {(int)response.StatusCode}\nURL: {request.RequestUri}\nResponse:\n{message}");
            }
            if (data.Length == 0)
                throw new NativeProviderIntegrationException("OpenAI speech response was empty");
            return data;
        }
        catch (NativeProviderIntegrationException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new NativeProviderIntegrationException(error.Message);
        }
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        await File.WriteAllBytesAsync(temporary, data, cancellationToken);
        File.Move(temporary, destination, overwrite: true);
    }

    private static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} is required");
        return text;
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0 (+desktop media downloader)");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
