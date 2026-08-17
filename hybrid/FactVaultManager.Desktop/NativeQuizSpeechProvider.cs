using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizNarrationAsset(int QuestionId, string Path, double Duration);

public static class QuizNarrationScript
{
    public static string Create(QuizQuestion question, bool includeAnswers)
    {
        ArgumentNullException.ThrowIfNull(question);
        var builder = new StringBuilder();
        builder.Append(question.Question.Trim());
        if (includeAnswers)
        {
            for (var index = 0; index < question.Answers.Count; index++)
            {
                var answer = question.Answers[index].Trim();
                builder.Append(' ');
                builder.Append((char)('A' + index));
                builder.Append(". ");
                builder.Append(answer);
                builder.Append('.');
            }
        }
        return builder.ToString().Trim();
    }
}

public sealed class NativeQuizSpeechProvider : IDisposable
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _voice;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public NativeQuizSpeechProvider(
        string apiKey,
        string model = "gpt-4o-mini-tts",
        string voice = "alloy",
        HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "OpenAI API key");
        _model = Required(model, "voice model");
        _voice = Required(voice, "voice");
        _client = client ?? CreateClient();
        _ownsClient = client is null;
    }

    public async Task<string> GenerateQuestionAsync(
        QuizQuestion question,
        int number,
        bool includeAnswers,
        string voiceFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number));
        voiceFolder = Required(voiceFolder, "voice folder");
        voiceFolder = Path.GetFullPath(voiceFolder);
        Directory.CreateDirectory(voiceFolder);

        var input = QuizNarrationScript.Create(question, includeAnswers);
        var identity = $"{_model}\n{_voice}\n{input}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..16];
        var destination = Path.Combine(voiceFolder, $"question_{number:000}_{digest}.mp3");
        var scriptCopy = Path.Combine(voiceFolder, $"question_{number:000}_{digest}.txt");

        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            return destination;

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["voice"] = _voice,
            ["input"] = input,
            ["response_format"] = "mp3",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        byte[] data;
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = Encoding.UTF8.GetString(data);
                throw new NativeProviderIntegrationException(
                    $"HTTP {(int)response.StatusCode}\nURL: {request.RequestUri}\nResponse:\n{message}");
            }
            if (data.Length == 0)
                throw new NativeProviderIntegrationException("OpenAI quiz speech response was empty");
        }
        catch (NativeProviderIntegrationException)
        {
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new NativeProviderIntegrationException(error.Message);
        }

        await File.WriteAllTextAsync(scriptCopy, input, new UTF8Encoding(false), cancellationToken);
        var temporary = destination + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, data, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
        return destination;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0 (+quiz narration)");
        return client;
    }

    private static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException($"{name} is required");
        return text;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
