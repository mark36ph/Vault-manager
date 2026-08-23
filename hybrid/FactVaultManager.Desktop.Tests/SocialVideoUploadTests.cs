using System.Net;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class SocialVideoUploadTests
{
    [Fact]
    public void UploadRules_AllowAllVideosOnYouTubeButOnlyShortsOnFacebook()
    {
        Assert.True(SocialVideoUploadRules.CanUploadToFacebook(History("9:16")));
        Assert.False(SocialVideoUploadRules.CanUploadToFacebook(History("16:9")));
    }

    [Fact]
    public void UploadQueuePlanner_OffersEveryMissingShortDestination()
    {
        var remaining = SocialUploadQueuePlanner.RemainingDestinations(History("9:16"));

        Assert.Equal(
            SocialUploadDestination.YouTube |
            SocialUploadDestination.Facebook |
            SocialUploadDestination.Instagram,
            remaining);
        Assert.Equal("YouTube + Facebook + Instagram", SocialUploadQueuePlanner.Display(remaining));
    }

    [Fact]
    public void UploadQueuePlanner_SkipsPublishedDestinations()
    {
        var history = History("9:16") with
        {
            PublishedOnYouTube = true,
            PublishedOnFacebook = true,
        };

        Assert.Equal(
            SocialUploadDestination.Instagram,
            SocialUploadQueuePlanner.RemainingDestinations(history));
    }

    [Fact]
    public void UploadQueuePlanner_LimitsFullVideosToYouTube()
    {
        var history = History("16:9");
        Assert.Equal(
            SocialUploadDestination.YouTube,
            SocialUploadQueuePlanner.RemainingDestinations(history));
        Assert.Equal(
            SocialUploadDestination.None,
            SocialUploadQueuePlanner.RemainingDestinations(
                history with { PublishedOnYouTube = true }));
    }

    [Fact]
    public void UploadDescription_AppendsSavedHashtagsOnce()
    {
        var history = History("9:16") with
        {
            YouTubeDescription = "Try this quiz.",
            Hashtags = "#Quiz #Shorts",
        };

        var result = SocialVideoUploadRules.UploadDescription(history);

        Assert.Equal("Try this quiz." + Environment.NewLine + Environment.NewLine + "#Quiz #Shorts", result);
        Assert.Equal("Try this quiz. #Quiz #Shorts",
            SocialVideoUploadRules.UploadDescription(history with { YouTubeDescription = "Try this quiz. #Quiz #Shorts" }));
    }

    [Fact]
    public void InstagramCaption_ReplacesYouTubeLinkWithLinkInBioCallToAction()
    {
        var description =
            "Can you get 1/1?\n\n" +
            "To try the full quiz, go to this URL: https://youtu.be/full-video-1\n\n" +
            "#Quiz #Shorts";

        var result = SocialVideoUploadRules.InstagramCaption(description);

        Assert.Contains(SocialVideoUploadRules.InstagramFullQuizCallToAction, result);
        Assert.DoesNotContain("youtu.be", result);
        Assert.Contains("Can you get 1/1?", result);
        Assert.Contains("#Quiz #Shorts", result);
    }

    [Fact]
    public void ShortMetadata_RequiresFullVideoYouTubeLink()
    {
        SocialVideoUploadRules.ValidateUploadMetadata(
            "Short", "Film Quiz Short",
            "Try the full quiz: https://www.youtube.com/watch?v=full-video-1\n\n#Shorts");

        var error = Assert.Throws<ArgumentException>(() =>
            SocialVideoUploadRules.ValidateUploadMetadata("Short", "Film Quiz Short", "Can you get 1/1? #Shorts"));
        Assert.Contains("full YouTube video", error.Message);
    }

    [Fact]
    public void ShortMetadata_AllowsInstagramOnlyUploadWithoutYouTubeLink()
    {
        SocialVideoUploadRules.ValidateUploadMetadata(
            "Short",
            "Film Quiz Short",
            "Can you get 1/1? #Shorts",
            requireFullYouTubeVideoLink: false);
    }

    [Theory]
    [InlineData("https://youtu.be/full-video-1")]
    [InlineData("https://youtube.com/watch?v=full-video-1")]
    [InlineData("https://www.youtube.com/watch?v=full-video-1&t=3s")]
    public void FullVideoLink_AcceptsSupportedYouTubeUrls(string url)
    {
        Assert.True(SocialVideoUploadRules.ContainsFullYouTubeVideoLink("Full quiz: " + url));
    }

    [Fact]
    public void ThumbnailValidation_AcceptsPngAndAllowsNoSelection()
    {
        var path = TemporaryThumbnail();
        try
        {
            Assert.Equal(Path.GetFullPath(path), SocialVideoUploadRules.ValidateThumbnailFile(path));
            Assert.Null(SocialVideoUploadRules.ValidateThumbnailFile(""));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(90)]
    public void FacebookDuration_AcceptsCurrentReelRange(double seconds)
    {
        SocialVideoUploadRules.ValidateFacebookDuration(seconds);
    }

    [Theory]
    [InlineData(2.99)]
    [InlineData(90.01)]
    public void FacebookDuration_RejectsVideosOutsideReelRange(double seconds)
    {
        Assert.Throws<ArgumentException>(() => SocialVideoUploadRules.ValidateFacebookDuration(seconds));
    }

    [Fact]
    public void ScheduleRules_ParseLocalDateAndTimeAndProtectFacebookWindow()
    {
        var localNoon = DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Unspecified);
        var now = new DateTimeOffset(localNoon, TimeZoneInfo.Local.GetUtcOffset(localNoon));

        var scheduled = SocialVideoUploadRules.ResolveScheduledPublishAt(
            true, localNoon.Date.AddDays(1), "18:30", now, includesFacebook: true);

        Assert.NotNull(scheduled);
        Assert.Equal(18, scheduled.Value.Hour);
        Assert.Equal(30, scheduled.Value.Minute);
        Assert.Null(SocialVideoUploadRules.ResolveScheduledPublishAt(
            false, null, null, now, includesFacebook: true));
        Assert.Throws<ArgumentException>(() => SocialVideoUploadRules.ResolveScheduledPublishAt(
            true, localNoon.Date.AddDays(31), "18:30", now, includesFacebook: true));
    }

    [Fact]
    public async Task YouTubeUpload_UsesResumableSessionAndReturnsWatchUrl()
    {
        var path = TemporaryVideo();
        try
        {
            var handler = new YouTubeUploadHandler();
            var service = new YouTubeVideoUploadService(new HttpClient(handler));

            var result = await service.UploadAsync(
                "youtube-token",
                path,
                new YouTubeVideoUpload("Film Quiz", "Description", "private", false));

            Assert.Equal("yt-video-1", result.VideoId);
            Assert.Equal("https://www.youtube.com/watch?v=yt-video-1", result.Url);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
            Assert.Contains("uploadType=resumable", handler.Requests[0].Url);
            Assert.Contains("notifySubscribers=false", handler.Requests[0].Url);
            Assert.Contains("\"privacyStatus\":\"private\"", handler.Requests[0].Body);
            Assert.Equal("Bearer youtube-token", handler.Requests[0].Authorization);
            Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
            Assert.Equal("https://upload.youtube.test/session/1", handler.Requests[1].Url);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task YouTubeThumbnail_PostsSelectedImageForUploadedVideo()
    {
        var path = TemporaryThumbnail();
        try
        {
            var handler = new YouTubeUploadHandler();
            var service = new YouTubeVideoUploadService(new HttpClient(handler));
            await service.SetThumbnailAsync("youtube-token", "yt-video-1", path);

            var request = Assert.Single(handler.Requests);
            Assert.Contains("/thumbnails/set?videoId=yt-video-1&uploadType=media", request.Url);
            Assert.Equal("Bearer youtube-token", request.Authorization);
            Assert.Equal("image/png", request.ContentType);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task YouTubeUpload_SendsPrivateUtcPublishTimeWhenScheduled()
    {
        var path = TemporaryVideo();
        try
        {
            var handler = new YouTubeUploadHandler();
            var service = new YouTubeVideoUploadService(new HttpClient(handler));
            var scheduled = DateTimeOffset.Now.AddDays(1);

            await service.UploadAsync(
                "youtube-token",
                path,
                new YouTubeVideoUpload("Film Quiz", "Description", "private", true, scheduled));

            var expected = scheduled.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("\"privacyStatus\":\"private\"", handler.Requests[0].Body);
            Assert.Contains($"\"publishAt\":\"{expected}\"", handler.Requests[0].Body);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FacebookUpload_StartsTransfersAndPublishesReel()
    {
        var path = TemporaryVideo();
        try
        {
            var handler = new FacebookUploadHandler();
            var service = new FacebookReelUploadService(new HttpClient(handler));

            var result = await service.UploadAsync(
                "page-token",
                path,
                "Film Quiz",
                "Can you get 1/1?");

            Assert.Equal("1051847137549312", result.VideoId);
            Assert.Equal("https://www.facebook.com/reel/1051847137549312", result.Url);
            Assert.Equal(4, handler.Requests.Count);
            Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
            Assert.Contains("/me?fields=id", handler.Requests[0].Url);
            Assert.Contains("upload_phase=start", handler.Requests[1].Body);
            Assert.Equal("OAuth page-token", handler.Requests[2].Authorization);
            Assert.Equal("0", handler.Requests[2].Offset);
            Assert.Equal("4", handler.Requests[2].FileSize);
            Assert.Contains("upload_phase=finish", handler.Requests[3].Body);
            Assert.Contains("video_state=PUBLISHED", handler.Requests[3].Body);
            Assert.Contains("description=Can+you+get+1%2F1%3F", handler.Requests[3].Body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FacebookThumbnail_PostsPreferredReelCover()
    {
        var path = TemporaryThumbnail();
        try
        {
            var handler = new FacebookUploadHandler();
            var service = new FacebookReelUploadService(new HttpClient(handler));
            await service.SetThumbnailAsync("page-token", "1051847137549312", path);

            var request = Assert.Single(handler.Requests);
            Assert.EndsWith("/1051847137549312/thumbnails", request.Url);
            Assert.Contains("name=access_token", request.Body);
            Assert.Contains("page-token", request.Body);
            Assert.Contains("name=is_preferred", request.Body);
            Assert.Contains("name=source", request.Body);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FacebookUpload_SendsScheduledStateAndUnixPublishTime()
    {
        var path = TemporaryVideo();
        try
        {
            var handler = new FacebookUploadHandler();
            var service = new FacebookReelUploadService(new HttpClient(handler));
            var scheduled = DateTimeOffset.Now.AddDays(1);

            await service.UploadAsync(
                "page-token",
                path,
                "Film Quiz",
                "Can you get 1/1?",
                scheduled);

            Assert.Contains("video_state=SCHEDULED", handler.Requests[3].Body);
            Assert.Contains(
                "scheduled_publish_time=" + scheduled.ToUnixTimeSeconds(),
                handler.Requests[3].Body);
        }
        finally { File.Delete(path); }
    }

    private static QuizHistorySummary History(string format) => new(
        1, "Film Quiz", "2026-08-23", 1, "Film", format, 8, false, "C:\\Quiz",
        "Film Quiz", 1, "Can You Get 1/1? | Film Quiz #001", "Description", "#Quiz",
        "Pinned", false, "");

    private static string TemporaryVideo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"factburst-upload-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }

    private static string TemporaryThumbnail()
    {
        var path = Path.Combine(Path.GetTempPath(), $"factburst-thumbnail-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private sealed class YouTubeUploadHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(await Capture(request, cancellationToken));
            if (request.Method == HttpMethod.Post)
            {
                var response = Json("{}");
                response.Headers.Location = new Uri("https://upload.youtube.test/session/1");
                return response;
            }
            return Json("{\"id\":\"yt-video-1\"}");
        }
    }

    private sealed class FacebookUploadHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = await Capture(request, cancellationToken);
            Requests.Add(captured);
            if (request.Method == HttpMethod.Get)
                return Json("{\"id\":\"1260107160523207\"}");
            if (captured.Body.Contains("upload_phase=start", StringComparison.Ordinal))
                return Json("{\"video_id\":\"1051847137549312\",\"upload_url\":\"https://rupload.facebook.com/video-upload/v26.0/1051847137549312\"}");
            return Json("{\"success\":true}");
        }
    }

    private static async Task<CapturedRequest> Capture(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        new(
            request.Method,
            request.RequestUri?.AbsoluteUri ?? "",
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.Authorization?.ToString() ?? request.Headers.GetValuesOrEmpty("Authorization").FirstOrDefault() ?? "",
            request.Headers.GetValuesOrEmpty("offset").FirstOrDefault() ?? "",
            request.Headers.GetValuesOrEmpty("file_size").FirstOrDefault() ?? "",
            request.Content?.Headers.ContentType?.MediaType ?? "");

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        string Body,
        string Authorization,
        string Offset,
        string FileSize,
        string ContentType);
}
internal static class HttpRequestHeadersTestExtensions
{
    public static IEnumerable<string> GetValuesOrEmpty(this System.Net.Http.Headers.HttpRequestHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values : Array.Empty<string>();
}
