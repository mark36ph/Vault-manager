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
                "instagram-secret-token",
                path,
                "Can you get 1/1? #Quiz");

            Assert.Equal("ig-media-1", result.MediaId);
            Assert.Equal("https://www.instagram.com/reel/ABC123/", result.Url);
            var upload = Assert.Single(handler.Requests.Where(request => request.Host == "rupload.facebook.com"));
            Assert.Equal("OAuth instagram-secret-token", upload.Authorization);
            Assert.Equal("0", upload.Offset);
            Assert.Equal("4", upload.FileSize);
            Assert.Equal("video/mp4", upload.ContentType);
            var create = Assert.Single(handler.Requests.Where(request => request.Url.EndsWith("/17890000000000000/media", StringComparison.Ordinal)));
            Assert.Equal("application/json", create.ContentType);
            Assert.Contains("\"media_type\":\"REELS\"", create.Body);
            Assert.Contains("\"upload_type\":\"resumable\"", create.Body);
            Assert.Contains("\"caption\":\"Can you get 1/1? #Quiz\"", create.Body);
            Assert.Contains("\"share_to_feed\":true", create.Body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class InstagramHandler : HttpMessageHandler
    {
        public List<Request> Requests { get; } = new();

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
            if (request.Method == HttpMethod.Get && url.Contains("/17890000000000000/media?", StringComparison.Ordinal))
                return Json("{\"data\":[{\"id\":\"ig-media-1\",\"caption\":\"Quiz caption\",\"media_type\":\"VIDEO\",\"media_product_type\":\"REELS\",\"permalink\":\"https://www.instagram.com/reel/ABC123/\",\"timestamp\":\"2026-08-23T12:00:00+0000\",\"like_count\":35,\"comments_count\":8}]}");
            if (url.Contains("/ig-media-1/insights", StringComparison.Ordinal))
                return Json("{\"data\":[{\"name\":\"views\",\"values\":[{\"value\":1200}]},{\"name\":\"reach\",\"values\":[{\"value\":900}]},{\"name\":\"saved\",\"values\":[{\"value\":12}]},{\"name\":\"shares\",\"values\":[{\"value\":9}]},{\"name\":\"total_interactions\",\"values\":[{\"value\":64}]}]}");
            if (request.Method == HttpMethod.Post && url.EndsWith("/17890000000000000/media", StringComparison.Ordinal))
                return Json("{\"id\":\"ig-container-1\",\"uri\":\"https://rupload.facebook.com/ig-api-upload/v26.0/ig-container-1\"}");
            if (request.Method == HttpMethod.Post && captured.Host == "rupload.facebook.com")
                return Json("{\"success\":true}");
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
