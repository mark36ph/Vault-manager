using System.Net;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstagramManagementServiceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(900)]
    public void InstagramDuration_AcceptsSupportedReelRange(double seconds) =>
        SocialVideoUploadRules.ValidateInstagramDuration(seconds);

    [Theory]
    [InlineData(2.99)]
    [InlineData(900.01)]
    public void InstagramDuration_RejectsUnsupportedReelRange(double seconds) =>
        Assert.Throws<ArgumentException>(() => SocialVideoUploadRules.ValidateInstagramDuration(seconds));

    [Theory]
    [InlineData("https://www.instagram.com/reel/ABC123/")]
    [InlineData("https://instagram.com/p/ABC123/")]
    public void InstagramPublication_AcceptsInstagramHttpsLinks(string value) =>
        Assert.Equal(value, QuizInstagramPublication.NormalizeUrl(value));

    [Fact]
    public async Task ListMedia_UsesBearerHeadersAndParsesInsights()
    {
        var handler = new InstagramHandler();
        var service = new InstagramManagementService(new HttpClient(handler));

        var result = await service.ListMediaAsync("instagram-secret-token");

        Assert.Equal("factburstquiz", result.Username);
        var media = Assert.Single(result.Media);
        Assert.Equal("REELS", media.MediaType);
        Assert.Equal(1200, media.Views);
        Assert.Equal(35, media.Likes);
        Assert.Equal(8, media.Comments);
        Assert.All(handler.Requests.Where(request => request.Host == "graph.instagram.com"), request =>
        {
            Assert.Equal("Bearer instagram-secret-token", request.Authorization);
            Assert.DoesNotContain("instagram-secret-token", request.Url);
        });
    }

    [Fact]
    public async Task UploadReel_CreatesContainerUploadsBytesAndPublishes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"instagram-upload-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
        try
        {
            var handler = new InstagramHandler();
            var service = new InstagramManagementService(new HttpClient(handler));

            var result = await service.UploadReelAsync(
                "facebook-page-secret-token",
                path,
                "Can you get 1/1? #Quiz");

            Assert.Equal("ig-media-1", result.MediaId);
            Assert.Equal("https://www.instagram.com/reel/ABC123/", result.Url);
            var upload = Assert.Single(handler.Requests, request => request.Host == "rupload.facebook.com");
            Assert.Equal("OAuth facebook-page-secret-token", upload.Authorization);
            Assert.Equal("0", upload.Offset);
            Assert.Equal("4", upload.FileSize);
            Assert.Equal("video/mp4", upload.ContentType);
            var create = Assert.Single(handler.Requests, request =>
                new Uri(request.Url).AbsolutePath.EndsWith("/17890000000000000/media", StringComparison.Ordinal));
            Assert.Equal("application/json", create.ContentType);
            Assert.Equal("graph.facebook.com", create.Host);
            Assert.Contains("\"media_type\":\"REELS\"", create.Body);
            Assert.Contains("\"upload_type\":\"resumable\"", create.Body);
            Assert.Contains("\"caption\":\"Can you get 1/1? #Quiz\"", create.Body);
            Assert.Contains("\"share_to_feed\":true", create.Body);
            Assert.DoesNotContain(handler.Requests, request => request.Host == "graph.instagram.com");
            Assert.All(handler.Requests.Where(request => request.Host == "graph.facebook.com"), request =>
            {
                Assert.Equal("Bearer facebook-page-secret-token", request.Authorization);
                Assert.DoesNotContain("facebook-page-secret-token", request.Url);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UploadReel_ShowsResumableUploadDebugMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"instagram-upload-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
        try
        {
            var handler = new InstagramHandler
            {
                UploadFailureJson =
                    "{\"debug_info\":{\"retriable\":false,\"message\":\"Video file format is not supported\"}}",
            };
            var service = new InstagramManagementService(new HttpClient(handler));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadReelAsync("facebook-page-secret-token", path, "Quiz caption"));

            Assert.Contains("Video file format is not supported", error.Message);
            Assert.Contains("HTTP 400", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class InstagramHandler : HttpMessageHandler
    {
        public List<Request> Requests { get; } = new();
        public string? UploadFailureJson { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new Request(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? "",
                request.RequestUri?.Host ?? "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString() ?? request.Headers.GetValuesOrEmpty("Authorization").FirstOrDefault() ?? "",
                request.Headers.GetValuesOrEmpty("offset").FirstOrDefault() ?? "",
                request.Headers.GetValuesOrEmpty("file_size").FirstOrDefault() ?? "",
                request.Content?.Headers.ContentType?.MediaType ?? "");
            Requests.Add(captured);

            var url = captured.Url;
            if (url.Contains("/me?fields=user_id%2Cusername", StringComparison.Ordinal))
                return Json("{\"user_id\":\"17890000000000000\",\"username\":\"factburstquiz\",\"account_type\":\"BUSINESS\",\"media_count\":1}");
            if (url.Contains("/me?fields=user_id", StringComparison.Ordinal))
                return Json("{\"user_id\":\"17890000000000000\"}");
            if (url.Contains("/me?fields=instagram_business_account", StringComparison.Ordinal))
                return Json("{\"id\":\"facebook-page-1\",\"instagram_business_account\":{\"id\":\"17890000000000000\"}}");
            if (request.Method == HttpMethod.Get && url.Contains("/17890000000000000/media?", StringComparison.Ordinal))
                return Json("{\"data\":[{\"id\":\"ig-media-1\",\"caption\":\"Quiz caption\",\"media_type\":\"VIDEO\",\"media_product_type\":\"REELS\",\"permalink\":\"https://www.instagram.com/reel/ABC123/\",\"timestamp\":\"2026-08-23T12:00:00+0000\",\"like_count\":35,\"comments_count\":8}]}");
            if (url.Contains("/ig-media-1/insights", StringComparison.Ordinal))
                return Json("{\"data\":[{\"name\":\"views\",\"values\":[{\"value\":1200}]},{\"name\":\"reach\",\"values\":[{\"value\":900}]},{\"name\":\"saved\",\"values\":[{\"value\":12}]},{\"name\":\"shares\",\"values\":[{\"value\":9}]},{\"name\":\"total_interactions\",\"values\":[{\"value\":64}]}]}");
            if (request.Method == HttpMethod.Post &&
                new Uri(url).AbsolutePath.EndsWith("/17890000000000000/media", StringComparison.Ordinal))
                return Json("{\"id\":\"ig-container-1\",\"uri\":\"https://rupload.facebook.com/ig-api-upload/v26.0/ig-container-1\"}");
            if (request.Method == HttpMethod.Post && captured.Host == "rupload.facebook.com")
            {
                if (UploadFailureJson is not null)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(UploadFailureJson, Encoding.UTF8, "application/json"),
                    };
                return Json("{\"success\":true}");
            }
            if (request.Method == HttpMethod.Get && url.Contains("/ig-container-1?fields=status_code", StringComparison.Ordinal))
                return Json("{\"status_code\":\"FINISHED\",\"status\":\"Finished\"}");
            if (request.Method == HttpMethod.Post && url.EndsWith("/17890000000000000/media_publish", StringComparison.Ordinal))
                return Json("{\"id\":\"ig-media-1\"}");
            if (request.Method == HttpMethod.Get && url.Contains("/ig-media-1?fields=permalink", StringComparison.Ordinal))
                return Json("{\"permalink\":\"https://www.instagram.com/reel/ABC123/\"}");
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Unexpected test request\"}}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record Request(
        HttpMethod Method,
        string Url,
        string Host,
        string Body,
        string Authorization,
        string Offset,
        string FileSize,
        string ContentType);
}

public sealed class InstagramManagerPlanningTests
{
    [Fact]
    public void Matcher_UsesSavedReelUrlBeforeCaption()
    {
        var history = new[]
        {
            Short(1, "Geography", 2, instagramUrl: "https://www.instagram.com/reel/ABC123"),
        };
        var media = new[]
        {
            Reel("media-1", "A completely different caption", "https://www.instagram.com/reel/ABC123/"),
        };

        var matches = InstagramShortMatcher.Match(history, media);

        Assert.Equal("media-1", matches[1].MediaId);
    }

    [Fact]
    public void Matcher_RecognisesQuizCategoryAndEpisodeInCaption()
    {
        var history = new[] { Short(7, "Geography", 2) };
        var media = new[]
        {
            Reel(
                "media-7",
                "Test your knowledge with 1 question in Geography Quiz #002.",
                "https://www.instagram.com/reel/GEO002/"),
        };

        var matches = InstagramShortMatcher.Match(history, media);

        Assert.Equal("media-7", matches[7].MediaId);
    }

    [Fact]
    public void Planner_RecommendsCategoryWithNoInstagramShorts()
    {
        var history = new[]
        {
            Short(1, "Geography", 1, published: true),
            Short(2, "Science", 1),
        };

        var recommendation = InstagramNextShortPlanner.Recommend(
            history,
            new[] { "Geography", "Science" });

        Assert.Equal("Science", recommendation.Category);
        Assert.Contains("does not yet have", recommendation.Reason);
    }

    private static QuizHistorySummary Short(
        int id,
        string category,
        int episode,
        bool published = false,
        string instagramUrl = "") =>
        new(
            id,
            $"{category} - Short - {episode:000}",
            "2026-08-23",
            1,
            category,
            "9:16",
            10,
            false,
            "",
            category,
            episode,
            $"Can You Get 1/1? | {category} Quiz #{episode:000}",
            "",
            "",
            "",
            true,
            "https://youtu.be/example",
            PublishedOnInstagram: published || instagramUrl.Length > 0,
            InstagramUrl: instagramUrl);

    private static InstagramMediaItem Reel(string id, string caption, string url) =>
        new(
            id,
            "REELS",
            caption,
            url,
            new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc),
            100,
            90,
            10,
            2,
            3,
            4,
            19);
}
