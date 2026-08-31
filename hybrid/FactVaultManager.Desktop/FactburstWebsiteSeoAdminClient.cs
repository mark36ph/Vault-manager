using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteSeoQuiz(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("seo_title")] string SeoTitle,
    [property: JsonPropertyName("seo_description")] string SeoDescription,
    [property: JsonPropertyName("social_title")] string SocialTitle,
    [property: JsonPropertyName("social_description")] string SocialDescription,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("publish_at")] string PublishAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("question_count")] int QuestionCount);

public sealed record FactburstWebsiteSeoValues(
    [property: JsonPropertyName("seo_title")] string SeoTitle,
    [property: JsonPropertyName("seo_description")] string SeoDescription,
    [property: JsonPropertyName("social_title")] string SocialTitle,
    [property: JsonPropertyName("social_description")] string SocialDescription);

internal sealed record FactburstWebsiteSeoListResponse(
    [property: JsonPropertyName("quizzes")] IReadOnlyList<FactburstWebsiteSeoQuiz>? Quizzes);

internal sealed record FactburstWebsiteSeoErrorResponse(
    [property: JsonPropertyName("error")] string? Error);

public static class FactburstWebsiteSeoDefaults
{
    public const int RecommendedTitleLength = 65;
    public const int RecommendedDescriptionLength = 160;
    public const int RecommendedSocialTitleLength = 100;
    public const int RecommendedSocialDescriptionLength = 200;

    public static FactburstWebsiteSeoValues Create(FactburstWebsiteSeoQuiz quiz)
    {
        ArgumentNullException.ThrowIfNull(quiz);
        return Create(quiz.Title, quiz.Category, quiz.Description, quiz.QuestionCount);
    }

    public static FactburstWebsiteSeoValues Create(
        string? title,
        string? category,
        string? description,
        int questionCount)
    {
        var quizTitle = Compact(title);
        if (quizTitle.Length == 0) quizTitle = "Factburst Quiz";
        var quizCategory = Compact(category);
        if (quizCategory.Length == 0) quizCategory = "General Knowledge";
        var count = Math.Max(1, questionCount);

        const string suffix = " | Factburst Quiz";
        var seoTitle = quizTitle.EndsWith("Factburst Quiz", StringComparison.OrdinalIgnoreCase)
            ? TrimAtWord(quizTitle, RecommendedTitleLength)
            : TrimAtWord(quizTitle, Math.Max(18, RecommendedTitleLength - suffix.Length)) + suffix;

        var sourceDescription = Compact(description);
        var generatedDescription =
            $"Take this {count}-question {quizCategory} quiz from Factburst Quiz. Test your knowledge, see your score and discover the facts behind each answer.";
        var seoDescription = sourceDescription.Length >= 80
            ? TrimAtWord(sourceDescription, RecommendedDescriptionLength)
            : TrimAtWord(generatedDescription, RecommendedDescriptionLength);

        var socialTitle = TrimAtWord(quizTitle, RecommendedSocialTitleLength);
        var socialDescription = TrimAtWord(
            $"{count} questions on {quizCategory}. Can you score {count}/{count}? Play the Factburst Quiz and compare your result.",
            RecommendedSocialDescriptionLength);

        return new FactburstWebsiteSeoValues(seoTitle, seoDescription, socialTitle, socialDescription);
    }

    public static FactburstWebsiteSeoValues Effective(FactburstWebsiteSeoQuiz quiz)
    {
        var suggested = Create(quiz);
        return new FactburstWebsiteSeoValues(
            ValueOrDefault(quiz.SeoTitle, suggested.SeoTitle),
            ValueOrDefault(quiz.SeoDescription, suggested.SeoDescription),
            ValueOrDefault(quiz.SocialTitle, suggested.SocialTitle),
            ValueOrDefault(quiz.SocialDescription, suggested.SocialDescription));
    }

    public static string CleanQuizUrl(string? slug) =>
        "https://factburstquiz.com/quiz/" + Compact(slug).ToLowerInvariant();

    public static string SocialImageUrl(string? slug) =>
        "https://factburstquiz.com/social/quiz/" + Compact(slug).ToLowerInvariant() + ".png";

    private static string ValueOrDefault(string? value, string fallback)
    {
        var normalized = Compact(value);
        return normalized.Length > 0 ? normalized : fallback;
    }

    private static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string TrimAtWord(string value, int maxLength)
    {
        var text = Compact(value);
        if (text.Length <= maxLength) return text;
        if (maxLength < 4) return text[..Math.Max(0, maxLength)];

        var candidate = text[..maxLength].TrimEnd();
        var lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace >= Math.Max(12, maxLength / 2)) candidate = candidate[..lastSpace];
        return candidate.TrimEnd(' ', '-', ':', ';', ',', '.') + "…";
    }
}

public sealed class FactburstWebsiteSeoAdminClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public FactburstWebsiteSeoAdminClient(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<FactburstWebsiteSeoQuiz>> FetchAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, baseUrl, apiKey, "");
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(ErrorMessage(body, response.StatusCode));

        var parsed = JsonSerializer.Deserialize<FactburstWebsiteSeoListResponse>(body, JsonOptions);
        return parsed?.Quizzes ?? Array.Empty<FactburstWebsiteSeoQuiz>();
    }

    public async Task UpdateAsync(
        string baseUrl,
        string apiKey,
        string slug,
        FactburstWebsiteSeoValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var cleanSlug = (slug ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(cleanSlug, "^[a-z0-9][a-z0-9-]{0,79}$"))
            throw new ArgumentException("The website quiz slug is not valid.", nameof(slug));

        using var request = CreateRequest(HttpMethod.Patch, baseUrl, apiKey, "/" + Uri.EscapeDataString(cleanSlug));
        request.Content = new StringContent(
            JsonSerializer.Serialize(values, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(ErrorMessage(body, response.StatusCode));
    }

    public void Dispose() => _client.Dispose();

    private static HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string apiKey, string suffix)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new ArgumentException("The Link Tracker API key is required.", nameof(apiKey));

        var request = new HttpRequestMessage(method, normalized + "/api/site/quiz-seo" + suffix);
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
            var parsed = JsonSerializer.Deserialize<FactburstWebsiteSeoErrorResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(parsed?.Error)) return parsed.Error.Trim();
        }
        catch (JsonException)
        {
        }
        return $"Website SEO returned HTTP {(int)status}.";
    }
}
