namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeThumbnailServiceTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc_DEF-123", "abc_DEF-123")]
    [InlineData("https://youtu.be/abc_DEF-123?t=12", "abc_DEF-123")]
    [InlineData("https://www.youtube.com/shorts/abc_DEF-123", "abc_DEF-123")]
    [InlineData("https://www.youtube.com/embed/abc_DEF-123", "abc_DEF-123")]
    [InlineData("https://www.youtube.com/live/abc_DEF-123?feature=share", "abc_DEF-123")]
    public void VideoReference_ParsesSupportedYouTubeLinks(string url, string expected)
    {
        Assert.Equal(expected, YouTubeVideoReference.ParseVideoId(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/watch?v=abc_DEF-123")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("http://youtu.be/abc_DEF-123")]
    public void VideoReference_RejectsInvalidYouTubeLinks(string url)
    {
        Assert.Throws<ArgumentException>(() => YouTubeVideoReference.ParseVideoId(url));
    }

    [Fact]
    public async Task SetAsync_UploadsPngToTheThumbnailEndpointWithBearerAuthentication()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FactVaultThumbnailTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Thumbnail.png");
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
        File.WriteAllBytes(path, bytes);
        try
        {
            var handler = new RecordingHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
            var service = new YouTubeThumbnailService(new HttpClient(handler));

            await service.SetAsync("secret-token", "abc_DEF-123", path);

            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(
                "https://www.googleapis.com/upload/youtube/v3/thumbnails/set?videoId=abc_DEF-123",
                handler.RequestUri);
            Assert.Equal("Bearer", handler.AuthorizationScheme);
            Assert.Equal("secret-token", handler.AuthorizationParameter);
            Assert.Equal("image/png", handler.ContentType);
            Assert.Equal(bytes, handler.Body);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetAsync_RejectsAThumbnailOverYouTubesTwoMegabyteLimit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FactVaultThumbnailTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Thumbnail.png");
        using (var stream = File.Create(path))
            stream.SetLength(YouTubeThumbnailService.MaximumThumbnailBytes + 1);
        try
        {
            var service = new YouTubeThumbnailService(new HttpClient(new RecordingHandler(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK))));

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.SetAsync("token", "video-id", path));

            Assert.Contains("2 MB", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string RequestUri { get; private set; } = "";
        public string AuthorizationScheme { get; private set; } = "";
        public string AuthorizationParameter { get; private set; } = "";
        public string ContentType { get; private set; } = "";
        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString() ?? "";
            AuthorizationScheme = request.Headers.Authorization?.Scheme ?? "";
            AuthorizationParameter = request.Headers.Authorization?.Parameter ?? "";
            ContentType = request.Content?.Headers.ContentType?.MediaType ?? "";
            Body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return response;
        }
    }
}
