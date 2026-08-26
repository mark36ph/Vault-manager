using System.Net;
using System.Text.Json;

namespace FactVaultManager.Desktop.Tests;

public sealed class PromoTrackingBackfillTests
{
    [Fact]
    public async Task YouTubeReadThenUpdate_PreservesRequiredSnippetFieldsAndReplacesOnlyTheLink()
    {
        const string responseJson = """
            {
              "items": [{
                "id": "promo-1",
                "snippet": {
                  "channelId": "channel-1",
                  "title": "Space Quiz | Final Question #Shorts",
                  "description": "Think you can beat it?\n\nWatch the full quiz: https://youtu.be/full123\n\n#Shorts #Quiz",
                  "categoryId": "27",
                  "tags": ["quiz", "space"],
                  "defaultLanguage": "en",
                  "defaultAudioLanguage": "en-GB"
                }
              }]
            }
            """;
        var handler = new RecordingHttpHandler((request, call) =>
            call == 1
                ? JsonResponse(responseJson)
                : JsonResponse("{\"id\":\"promo-1\"}"));
        var service = new YouTubePromoMetadataService(new HttpClient(handler));

        var current = await service.ReadAsync("token", "promo-1", "channel-1");
        var description = FactburstPromoBackfillDescription.Apply(
            current.Description,
            "https://go.factburstquiz.workers.dev/yt/space-021");
        await service.UpdateDescriptionAsync("token", current, description);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("part=snippet", handler.Requests[0].Uri);
        Assert.Contains("id=promo-1", handler.Requests[0].Uri);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        using var document = JsonDocument.Parse(handler.Requests[1].Body);
        var root = document.RootElement;
        Assert.Equal("promo-1", root.GetProperty("id").GetString());
        var snippet = root.GetProperty("snippet");
        Assert.Equal("Space Quiz | Final Question #Shorts", snippet.GetProperty("title").GetString());
        Assert.Equal("27", snippet.GetProperty("categoryId").GetString());
        Assert.Equal(["quiz", "space"], snippet.GetProperty("tags").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("en", snippet.GetProperty("defaultLanguage").GetString());
        Assert.Equal("en-GB", snippet.GetProperty("defaultAudioLanguage").GetString());
        var updated = snippet.GetProperty("description").GetString() ?? "";
        Assert.Contains("https://go.factburstquiz.workers.dev/yt/space-021", updated);
        Assert.DoesNotContain("https://youtu.be/full123", updated);
        Assert.Contains("Think you can beat it?", updated);
        Assert.Contains("#Shorts #Quiz", updated);
    }

    [Fact]
    public async Task YouTubeRead_RejectsPromoOnAnotherChannel()
    {
        const string responseJson = """
            {"items":[{"id":"promo-1","snippet":{"channelId":"wrong-channel","title":"Promo","description":"Text","categoryId":"27"}}]}
            """;
        var service = new YouTubePromoMetadataService(new HttpClient(new RecordingHttpHandler((_, _) => JsonResponse(responseJson))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReadAsync("token", "promo-1", "approved-channel"));

        Assert.Contains("wrong-channel", error.Message);
        Assert.Contains("approved-channel", error.Message);
    }

    [Fact]
    public async Task FacebookReadThenUpdate_ReplacesTheDirectQuizLink()
    {
        const string responseJson = """
            {"id":"12345","description":"Think you can beat it?\n\nWatch the full quiz: https://www.youtube.com/watch?v=full123\n\n#Quiz"}
            """;
        var handler = new RecordingHttpHandler((_, call) =>
            call == 1 ? JsonResponse(responseJson) : JsonResponse("{\"success\":true}"));
        var service = new FacebookPromoMetadataService(new HttpClient(handler));

        var current = await service.ReadAsync("page-token", "12345");
        var description = FactburstPromoBackfillDescription.Apply(
            current.Description,
            "https://go.factburstquiz.workers.dev/fb/space-021");
        await service.UpdateDescriptionAsync("page-token", current.Id, description);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("fields=id%2Cdescription", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        var form = ParseForm(handler.Requests[1].Body);
        Assert.Equal("page-token", form["access_token"]);
        Assert.Contains("https://go.factburstquiz.workers.dev/fb/space-021", form["description"]);
        Assert.DoesNotContain("youtube.com/watch", form["description"]);
        Assert.Contains("Think you can beat it?", form["description"]);
        Assert.Contains("#Quiz", form["description"]);
    }

    [Fact]
    public void DescriptionApply_IsIdempotentWhenDesiredTrackingLinkAlreadyExists()
    {
        const string existing = "Watch the full quiz: https://go.factburstquiz.workers.dev/yt/space-021\n\n#Shorts";

        var result = FactburstPromoBackfillDescription.Apply(
            existing,
            "https://go.factburstquiz.workers.dev/yt/space-021");

        Assert.Equal(existing, result);
    }

    [Fact]
    public void DescriptionApply_AppendsTrackingLinkWhenNoQuizUrlExists()
    {
        var result = FactburstPromoBackfillDescription.Apply(
            "Think you can beat it? #Shorts",
            "https://go.factburstquiz.workers.dev/yt/space-021");

        Assert.Contains("Think you can beat it? #Shorts", result);
        Assert.Contains("Watch the full quiz: https://go.factburstquiz.workers.dev/yt/space-021", result);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private static Dictionary<string, string> ParseForm(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var item = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            result[key] = item;
        }
        return result;
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);

    private sealed class RecordingHttpHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _call;
        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri?.ToString() ?? "", body));
            return Task.FromResult(respond(request, ++_call));
        }
    }
}
