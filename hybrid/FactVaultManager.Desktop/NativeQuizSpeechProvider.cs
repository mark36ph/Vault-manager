using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizNarrationAsset(int QuestionId, string Path, double Duration);

public sealed record QuizNarrationDelivery(string Input, string Instructions);

public static class QuizNarrationScript
{
    public static string Create(QuizQuestion question, bool includeAnswers)
    {
        ArgumentNullException.ThrowIfNull(question);
        return Build(question.Question.Trim(), question, includeAnswers);
    }

    public static QuizNarrationDelivery CreateDelivery(QuizQuestion question, bool includeAnswers)
    {
        ArgumentNullException.ThrowIfNull(question);
        var difficulty = QuizDifficultyCatalog.Parse(question.Difficulty);
        var questionText = AddDifficultyPunctuation(question.Question.Trim(), difficulty);
        var input = Build(questionText, question, includeAnswers);
        return new QuizNarrationDelivery(input, InstructionsFor(difficulty));
    }

    private static string Build(string questionText, QuizQuestion question, bool includeAnswers)
    {
        var builder = new StringBuilder();
        builder.Append(questionText);
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

    private static string AddDifficultyPunctuation(string text, QuizDifficulty difficulty)
    {
        var pauseCount = difficulty switch
        {
            QuizDifficulty.Hard => 1,
            QuizDifficulty.Insane => 2,
            _ => 0,
        };
        if (pauseCount == 0 || text.Length == 0)
            return text;

        var result = text;
        var searchFrom = 0;
        for (var pause = 0; pause < pauseCount; pause++)
        {
            var match = FindNextNaturalBreak(result, searchFrom);
            if (match.Index < 0)
                break;

            result = result[..match.Index] + "… " + result[(match.Index + match.Length)..];
            searchFrom = match.Index + 2;
        }
        return result;
    }

    private static (int Index, int Length) FindNextNaturalBreak(string text, int startIndex)
    {
        var separators = new[] { ", ", "; ", ": ", " — ", " – " };
        var bestIndex = -1;
        var bestLength = 0;
        foreach (var separator in separators)
        {
            var index = text.IndexOf(separator, startIndex, StringComparison.Ordinal);
            if (index < 0 || (bestIndex >= 0 && index >= bestIndex))
                continue;
            bestIndex = index;
            bestLength = separator.Length;
        }
        return (bestIndex, bestLength);
    }

    private static string InstructionsFor(QuizDifficulty difficulty)
    {
        const string fidelity =
            "Read exactly the supplied quiz text. Do not add, omit, explain, answer, or paraphrase any words. " +
            "No greeting, setup, polite filler, difficulty label, or commentary. ";

        return difficulty switch
        {
            QuizDifficulty.Easy => fidelity +
                "Use a brisk, upbeat quiz-host delivery. Keep the pace quick, confident, and clear, with tight natural pauses.",
            QuizDifficulty.Medium => fidelity +
                "Use a confident quiz-host delivery with light controlled suspense. Add a small natural pause before the final clause while keeping momentum.",
            QuizDifficulty.Hard => fidelity +
                "Use a high-stakes quiz-host delivery. Build controlled tension, slow slightly on the key clause, and make one deliberate pause before the final phrase. Keep it focused, not theatrical.",
            QuizDifficulty.Insane => fidelity +
                "Use maximum controlled suspense for a final-round quiz question. Let the key clause breathe, make a deliberate pause before the final phrase, then finish firmly. Keep it tense, not melodramatic.",
            _ => fidelity + "Use a confident, clear quiz-host delivery.",
        };
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
        string voice = QuizVoiceCatalog.DefaultVoice,
        HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "OpenAI API key");
        _model = Required(model, "voice model");
        _voice = QuizVoiceCatalog.Validate(voice);
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

        var delivery = QuizNarrationScript.CreateDelivery(question, includeAnswers);
        return await GenerateAsync(
            delivery.Input,
            "narration",
            voiceFolder,
            delivery.Instructions,
            cancellationToken);
    }

    public Task<string> GeneratePromoCallToActionAsync(
        string callToAction,
        string voiceFolder,
        CancellationToken cancellationToken = default)
    {
        var input = QuizPromoShortScript.Normalize(callToAction);
        voiceFolder = Required(voiceFolder, "voice folder");
        voiceFolder = Path.GetFullPath(voiceFolder);
        Directory.CreateDirectory(voiceFolder);
        return GenerateAsync(input, "promo_cta", voiceFolder, instructions: null, cancellationToken);
    }

    private async Task<string> GenerateAsync(
        string input,
        string prefix,
        string voiceFolder,
        string? instructions,
        CancellationToken cancellationToken)
    {
        var effectiveInstructions = SupportsInstructions(_model)
            ? (instructions ?? "").Trim()
            : "";
        var identity = $"{_model}\n{_voice}\n{effectiveInstructions}\n{input}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..16];
        var destination = Path.Combine(voiceFolder, $"{prefix}_{_voice}_{digest}.mp3");
        var scriptCopy = Path.Combine(voiceFolder, $"{prefix}_{_voice}_{digest}.txt");

        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            return destination;

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["voice"] = _voice,
            ["input"] = input,
            ["response_format"] = "mp3",
        };
        if (effectiveInstructions.Length > 0)
            body["instructions"] = effectiveInstructions;

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

    private static bool SupportsInstructions(string model) =>
        !string.Equals(model, "tts-1", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(model, "tts-1-hd", StringComparison.OrdinalIgnoreCase);

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
