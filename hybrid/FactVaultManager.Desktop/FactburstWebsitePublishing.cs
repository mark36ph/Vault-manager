using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteQuizQuestion(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answers")] IReadOnlyList<string> Answers,
    [property: JsonPropertyName("correct_answer")] string CorrectAnswer,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("image_data_url")] string ImageDataUrl);

public sealed record FactburstWebsiteQuizPayload(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("youtube_url")] string YouTubeUrl,
    [property: JsonPropertyName("publish_at")] string PublishAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("questions")] IReadOnlyList<FactburstWebsiteQuizQuestion> Questions);

public sealed record FactburstWebsiteQuizSummary(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("publish_at")] string PublishAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("question_count")] int QuestionCount);

internal sealed record FactburstWebsiteQuizListResponse(
    [property: JsonPropertyName("quizzes")] IReadOnlyList<FactburstWebsiteQuizSummary>? Quizzes);

internal sealed record FactburstWebsiteErrorResponse(
    [property: JsonPropertyName("error")] string? Error);

public sealed class FactburstWebsitePublishingClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public FactburstWebsitePublishingClient(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<FactburstWebsiteQuizSummary>> FetchQuizzesAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, baseUrl, apiKey);
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(ErrorMessage(body, response.StatusCode));

        var parsed = JsonSerializer.Deserialize<FactburstWebsiteQuizListResponse>(body, JsonOptions);
        return parsed?.Quizzes ?? Array.Empty<FactburstWebsiteQuizSummary>();
    }

    public async Task PublishQuizAsync(
        string baseUrl,
        string apiKey,
        FactburstWebsiteQuizPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var request = CreateRequest(HttpMethod.Post, baseUrl, apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(ErrorMessage(body, response.StatusCode));
    }

    public void Dispose() => _client.Dispose();

    private static HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string apiKey)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new ArgumentException("The Link Tracker API key is required.", nameof(apiKey));

        var request = new HttpRequestMessage(method, normalized + "/api/site/quizzes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string NormalizeBaseUrl(string value)
    {
        var text = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Link Tracker base URL must be a complete HTTPS address.", nameof(value));
        }
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string ErrorMessage(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<FactburstWebsiteErrorResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(parsed?.Error)) return parsed.Error.Trim();
        }
        catch (JsonException)
        {
        }
        return $"Website publishing returned HTTP {(int)status}.";
    }
}

public static class FactburstWebsiteQuizBuilder
{
    private const int WebsiteImageDecodeWidth = 640;
    private const long MaxSourceImageBytes = 12L * 1024 * 1024;
    private const int MaxEncodedImageBytes = 900_000;

    public static FactburstWebsiteQuizPayload Build(
        QuizHistorySummary history,
        DateTimeOffset publishAt,
        IReadOnlyDictionary<int, string>? questionImagePaths = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (string.IsNullOrWhiteSpace(history.ProjectFolder))
            throw new DirectoryNotFoundException("The quiz project folder is not available.");

        var projectFolder = Path.GetFullPath(history.ProjectFolder);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException($"The quiz project folder is unavailable: {projectFolder}");

        var quizPath = Path.Combine(projectFolder, "quiz.json");
        if (!File.Exists(quizPath))
            throw new FileNotFoundException("The saved quiz.json is not available yet.", quizPath);

        using var document = JsonDocument.Parse(File.ReadAllText(quizPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("questions", out var savedQuestions) ||
            savedQuestions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The saved quiz.json does not contain its question list.");
        }

        var questionCount = savedQuestions.GetArrayLength();
        if (questionCount == 0)
            throw new InvalidDataException("The saved quiz has no questions.");
        if (history.QuestionCount > 0 && questionCount != history.QuestionCount)
        {
            throw new InvalidDataException(
                $"The saved project has {questionCount} question(s), but quiz history expects {history.QuestionCount}. " +
                "The website copy was not staged because the project may be incomplete.");
        }

        var category = history.AnalyticsCategory.Trim();
        if (category.Length == 0) category = "General Knowledge";
        var logoQuiz = string.Equals(category, "Logos", StringComparison.OrdinalIgnoreCase);

        var questions = new List<FactburstWebsiteQuizQuestion>(questionCount);
        var index = 0;
        foreach (var saved in savedQuestions.EnumerateArray())
        {
            index++;
            if (!saved.TryGetProperty("answers", out var answersElement) ||
                answersElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Question {index} does not contain its saved answers.");
            }

            var answers = answersElement.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "")
                .ToArray();

            var imageDataUrl = "";
            var questionId = Int(saved, "id", 0);
            if (questionId > 0 && questionImagePaths is not null &&
                questionImagePaths.TryGetValue(questionId, out var imagePath) &&
                !string.IsNullOrWhiteSpace(imagePath))
            {
                imageDataUrl = EncodeQuestionImageDataUrl(imagePath);
            }
            else if (logoQuiz)
            {
                throw new InvalidDataException(
                    $"Logo question {index} has no local image available. Restore or relink its question image before syncing this quiz to the website.");
            }

            questions.Add(BuildQuestion(
                RequiredText(saved, "question", index),
                answers,
                Int(saved, "correct_index", -1),
                Text(saved, "explanation"),
                imageDataUrl));
        }

        var title = history.UploadTitleDisplay.Trim();
        if (title.Length == 0) title = history.Title.Trim();
        if (title.Length == 0) title = "Factburst Quiz";
        var youtubeUrl = QuizYouTubePublication.NormalizeUrl(history.YouTubeUrl);
        if (youtubeUrl.Length == 0)
            throw new InvalidDataException("The long-form YouTube link is missing.");

        // The long-form YouTube release is authoritative for the website. A caller can no
        // longer publish an unscheduled private/unlisted upload early or preserve stale
        // Cloudflare timing by passing a different publishAt value.
        publishAt = WebsiteYouTubeSchedulePlanner.ResolvePublishAtOrThrow(
            history,
            publishAt,
            DateTimeOffset.Now);

        return new FactburstWebsiteQuizPayload(
            FactburstLinkTrackerClient.CampaignSlug(history),
            title,
            category,
            $"Test yourself with this {category} quiz and see how many you can get right.",
            youtubeUrl,
            publishAt.ToUniversalTime().ToString("O"),
            "published",
            questions);
    }

    public static FactburstWebsiteQuizQuestion BuildQuestion(
        string question,
        IReadOnlyList<string> answers,
        int correctIndex,
        string explanation,
        string imageDataUrl = "")
    {
        var prompt = (question ?? "").Trim();
        if (prompt.Length == 0)
            throw new InvalidDataException("A website quiz question is blank.");
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Count != 4)
            throw new InvalidDataException("Every website quiz question must contain exactly four answers.");

        var normalized = answers.Select(answer => (answer ?? "").Trim()).ToArray();
        if (normalized.Any(answer => answer.Length == 0))
            throw new InvalidDataException("A website quiz answer is blank.");
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new InvalidDataException("Every website quiz answer must be distinct.");
        if (correctIndex is < 0 or > 3)
            throw new InvalidDataException("A website quiz question has an invalid correct-answer index.");

        var image = (imageDataUrl ?? "").Trim();
        if (image.Length > 0 && !image.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
            throw new InvalidDataException("Website quiz images must be PNG data URLs.");

        return new FactburstWebsiteQuizQuestion(
            prompt,
            normalized,
            "ABCD"[correctIndex].ToString(),
            (explanation ?? "").Trim(),
            image);
    }

    public static string EncodeQuestionImageDataUrl(string? imagePath)
    {
        var value = (imagePath ?? "").Trim();
        if (value.Length == 0) return "";

        var fullPath = Path.GetFullPath(value);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The quiz question image is unavailable.", fullPath);

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new InvalidDataException("Website quiz images must be PNG, JPG, JPEG or BMP files.");
        if (new FileInfo(fullPath).Length > MaxSourceImageBytes)
            throw new InvalidDataException("The quiz question image is too large to prepare for the website.");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = WebsiteImageDecodeWidth;
        bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > MaxEncodedImageBytes)
            throw new InvalidDataException("The prepared quiz question image is too large for website storage.");

        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static string RequiredText(JsonElement element, string propertyName, int questionNumber)
    {
        var value = Text(element, propertyName);
        if (value.Length == 0)
            throw new InvalidDataException($"Question {questionNumber} has no question text.");
        return value;
    }

    private static string Text(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? ""
            : "";
    }

    private static int Int(JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }
}
