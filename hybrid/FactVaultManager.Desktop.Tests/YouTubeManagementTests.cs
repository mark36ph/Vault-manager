namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeManagementTests
{
    [Fact]
    public void AuthorizationUri_UsesDesktopPkceAndManagementScope()
    {
        var uri = YouTubeOAuthService.CreateAuthorizationUri(
            "client.apps.googleusercontent.com",
            "http://127.0.0.1:54321/",
            "secure-state",
            "pkce-challenge");

        Assert.Contains("response_type=code", uri);
        Assert.Contains("access_type=offline", uri);
        Assert.Contains("prompt=consent", uri);
        Assert.Contains("code_challenge_method=S256", uri);
        Assert.Contains("state=secure-state", uri);
        Assert.Contains(Uri.EscapeDataString(YouTubeOAuthService.ManagementScope), uri);
    }

    [Fact]
    public void TokenResponse_ParsesAccessAndRefreshTokens()
    {
        const string json = """
            {"access_token":"access","refresh_token":"refresh","expires_in":3599,"scope":"youtube-scope"}
            """;

        var result = YouTubeOAuthService.ParseTokenResponse(json);

        Assert.Equal("access", result.AccessToken);
        Assert.Equal("refresh", result.RefreshToken);
        Assert.Equal(3599, result.ExpiresInSeconds);
        Assert.Equal("youtube-scope", result.Scope);
    }

    [Fact]
    public void CommentsResponse_ParsesModerationRows()
    {
        const string json = """
            {"items":[{"id":"thread-1","snippet":{"totalReplyCount":2,"topLevelComment":{"id":"comment-1","snippet":{"videoId":"video-1","authorDisplayName":"Viewer","authorChannelUrl":"http://www.youtube.com/channel/viewer-channel","authorChannelId":{"value":"viewer-channel"},"textDisplay":"Great quiz!","publishedAt":"2026-08-22T12:30:00Z","likeCount":3,"moderationStatus":"published"}}}}]}
            """;

        var result = Assert.Single(YouTubeManagementService.ParseComments(json));

        Assert.Equal("comment-1", result.Id);
        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal("video-1", result.VideoId);
        Assert.Equal("Viewer", result.Author);
        Assert.Equal("Great quiz!", result.Text);
        Assert.Equal(3, result.LikeCount);
        Assert.Equal(2, result.ReplyCount);
        Assert.Equal("published", result.ModerationStatus);
        Assert.Equal("Active", result.StatusDisplay);
        Assert.Equal("https://www.youtube.com/channel/viewer-channel", result.AuthorProfileUrl.TrimEnd('/'));
        Assert.Equal("viewer-channel", result.AuthorChannelId);
    }

    [Theory]
    [InlineData("published", "Active")]
    [InlineData("heldForReview", "Needs approval")]
    [InlineData("likelySpam", "Needs approval (spam)")]
    [InlineData("rejected", "Rejected")]
    public void CommentStatus_UsesReadableModerationLabels(string status, string expected)
    {
        var comment = new YouTubeCommentItem(
            "comment", "thread", "video", "Viewer", "Text", DateTime.UtcNow,
            0, 0, status);

        Assert.Equal(expected, comment.StatusDisplay);
    }

    [Theory]
    [InlineData("published", "Active")]
    [InlineData("heldForReview", "Needs approval")]
    [InlineData("likelySpam", "Needs approval (spam)")]
    public void CommentStatus_UsesRequestedFilterWhenYouTubeOmitsTheField(
        string requestedStatus,
        string expected)
    {
        var comment = new YouTubeCommentItem(
            "comment", "thread", "video", "Viewer", "Text", DateTime.UtcNow,
            0, 0, "");

        var result = Assert.Single(YouTubeManagementService.ApplyRequestedModerationStatus(
            [comment], requestedStatus));

        Assert.Equal(requestedStatus, result.ModerationStatus);
        Assert.Equal(expected, result.StatusDisplay);
    }

    [Fact]
    public void CommentsResponse_UsesAuthorChannelIdWhenProfileUrlIsMissing()
    {
        const string json = """
            {"items":[{"id":"thread-1","snippet":{"totalReplyCount":0,"topLevelComment":{"id":"comment-1","snippet":{"videoId":"video-1","authorDisplayName":"Viewer","authorChannelId":{"value":"fallback-channel"},"textDisplay":"Great quiz!","publishedAt":"2026-08-22T12:30:00Z","likeCount":0,"moderationStatus":"published"}}}}]}
            """;

        var result = Assert.Single(YouTubeManagementService.ParseComments(json));

        Assert.Equal("https://www.youtube.com/channel/fallback-channel", result.AuthorProfileUrl);
    }

    [Fact]
    public void VideoTitlesResponse_LabelsCommentsWithTheirVideo()
    {
        const string commentsJson = """
            {"items":[{"id":"thread-1","snippet":{"totalReplyCount":0,"topLevelComment":{"id":"comment-1","snippet":{"videoId":"video-1","authorDisplayName":"Viewer","textDisplay":"Great quiz!","publishedAt":"2026-08-22T12:30:00Z","likeCount":1,"moderationStatus":"published"}}}}]}
            """;
        const string videosJson = """
            {"items":[{"id":"video-1","snippet":{"title":"Can You Get 10/10? | History Quiz #001"}}]}
            """;

        var comments = YouTubeManagementService.ParseComments(commentsJson);
        var titles = YouTubeManagementService.ParseVideoTitles(videosJson);
        var result = Assert.Single(YouTubeManagementService.AttachVideoTitles(comments, titles));

        Assert.Equal("Can You Get 10/10? | History Quiz #001", result.VideoTitle);
    }

    [Fact]
    public void CommentUrl_OpensTheExactCommentOnItsVideo()
    {
        var url = YouTubeManagementService.BuildCommentUrl("video 1", "comment+1");

        Assert.Equal("https://www.youtube.com/watch?v=video%201&lc=comment%2B1", url);
    }

    [Fact]
    public void NeedsReplyInbox_HidesOwnRepliedAndHandledComments()
    {
        var comments = YouTubeManagementService.MarkOwnComments(
        [
            new YouTubeCommentItem(
                "own", "thread-own", "video", "Factburst", "Pinned", DateTime.UtcNow,
                0, 0, "published", AuthorChannelId: "owner-channel"),
            new YouTubeCommentItem(
                "viewer", "thread-viewer", "video", "Viewer", "Please reply", DateTime.UtcNow,
                0, 0, "published", AuthorChannelId: "viewer-channel"),
            new YouTubeCommentItem(
                "replied", "thread-replied", "video", "Viewer", "Already answered", DateTime.UtcNow,
                0, 1, "published", AuthorChannelId: "viewer-channel"),
            new YouTubeCommentItem(
                "handled", "thread-handled", "video", "Viewer", "Just answered", DateTime.UtcNow,
                0, 0, "published", AuthorChannelId: "viewer-channel"),
        ],
        "owner-channel");

        var published = YouTubeCommentInbox.Filter(comments, needsReply: false);
        var result = Assert.Single(YouTubeCommentInbox.Filter(
            comments,
            needsReply: true,
            handledCommentIds: new HashSet<string>(StringComparer.Ordinal) { "handled" }));

        Assert.Contains(published, comment => comment.Id == "own");
        Assert.Equal(4, published.Count);
        Assert.Equal("viewer", result.Id);
    }

    [Fact]
    public void Moderation_RemovesHeldCommentFromPublishedViewImmediately()
    {
        var comment = new YouTubeCommentItem(
            "viewer", "thread", "video", "Viewer", "Please reply", DateTime.UtcNow,
            0, 0, "published");

        var result = YouTubeCommentInbox.ApplyModeration(
            [comment], comment.Id, "heldForReview", "published");

        Assert.Empty(result);
    }

    [Fact]
    public void Moderation_KeepsCommentWhenMovedIntoCurrentApprovalView()
    {
        var comment = new YouTubeCommentItem(
            "viewer", "thread", "video", "Viewer", "Please review", DateTime.UtcNow,
            0, 0, "published");

        var result = Assert.Single(YouTubeCommentInbox.ApplyModeration(
            [comment], comment.Id, "heldForReview", "heldForReview"));

        Assert.Equal("Needs approval", result.StatusDisplay);
    }

    [Theory]
    [InlineData("published", false)]
    [InlineData("heldForReview", false)]
    [InlineData("likelySpam", true)]
    [InlineData("rejected", false)]
    public void HoldForReview_IsOnlyOfferedForLikelySpam(string status, bool expected)
    {
        var comment = new YouTubeCommentItem(
            "comment", "thread", "video", "Viewer", "Text", DateTime.UtcNow,
            0, 0, status);

        Assert.Equal(expected, YouTubeCommentInbox.CanMoveToHeldForReview(comment));
    }

    [Fact]
    public void PlaylistsResponse_ParsesPrivacyAndVideoCount()
    {
        const string json = """
            {"items":[{"id":"playlist-1","snippet":{"title":"History Quizzes","description":"History videos"},"status":{"privacyStatus":"public"},"contentDetails":{"itemCount":4}}]}
            """;

        var result = Assert.Single(YouTubeManagementService.ParsePlaylists(json));

        Assert.Equal("playlist-1", result.Id);
        Assert.Equal("History Quizzes", result.Title);
        Assert.Equal("public", result.Privacy);
        Assert.Equal(4, result.VideoCount);
    }

    [Fact]
    public void PlaylistVideosResponse_ParsesItemIdAndPosition()
    {
        const string json = """
            {"items":[{"id":"playlist-item-1","snippet":{"title":"History Quiz 001","position":2,"resourceId":{"videoId":"video-1"}}}]}
            """;

        var result = Assert.Single(YouTubeManagementService.ParsePlaylistVideos(json));

        Assert.Equal("playlist-item-1", result.PlaylistItemId);
        Assert.Equal("video-1", result.VideoId);
        Assert.Equal("History Quiz 001", result.Title);
        Assert.Equal(2, result.Position);
    }

    [Fact]
    public void CategoryPlaylistPlanner_CreatesOnlyMissingCategories()
    {
        var playlists = new[]
        {
            new YouTubePlaylistItem("history", "History Quizzes", "", "private", 0),
            new YouTubePlaylistItem("science", "Science Quiz", "", "public", 3),
            new YouTubePlaylistItem("music", "Music", "", "private", 1),
        };

        var result = YouTubeCategoryPlaylistPlanner.MissingCategories(
            ["History", "Science", "Music", "Film", "film"],
            playlists);

        Assert.Equal(["Film"], result);
        Assert.Equal("Nature & Animals Quizzes", YouTubeCategoryPlaylistPlanner.PlaylistTitle("Nature & Animals"));
    }

    [Fact]
    public void CategoryPlaylistPlanner_RejectsAnEmptyCategory()
    {
        Assert.Throws<ArgumentException>(() => YouTubeCategoryPlaylistPlanner.PlaylistTitle(" "));
    }

    [Theory]
    [InlineData("published")]
    [InlineData("heldForReview")]
    [InlineData("rejected")]
    public void ModerationStatus_AcceptsWritableStates(string status)
    {
        YouTubeManagementService.ValidateModerationStatus(status, allowSpam: false);
    }

    [Fact]
    public void ModerationStatus_RejectsSpamAsWritableState()
    {
        Assert.Throws<ArgumentException>(() =>
            YouTubeManagementService.ValidateModerationStatus("likelySpam", allowSpam: false));
    }

    [Fact]
    public async Task SetModerationStatus_SendsABodylessPost()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        var service = new YouTubeManagementService(new HttpClient(handler));

        await service.SetModerationStatusAsync("token", "comment-1", "heldForReview");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.False(handler.HadContent);
        Assert.Contains("id=comment-1", handler.RequestUri);
        Assert.Contains("moderationStatus=heldForReview", handler.RequestUri);
    }

    [Fact]
    public async Task ModerationQueueConfirmation_FindsTheCommentInYouTubesTargetQueue()
    {
        const string json = """
            {"items":[{"id":"thread-1","snippet":{"totalReplyCount":0,"topLevelComment":{"id":"comment-1","snippet":{"videoId":"video-1","authorDisplayName":"Viewer","textDisplay":"Review me","publishedAt":"2026-08-24T12:30:00Z","likeCount":0}}}}]}
            """;
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });
        var service = new YouTubeManagementService(new HttpClient(handler));

        var found = await service.IsCommentInModerationQueueAsync(
            "token", "channel-1", "heldForReview", "comment-1");

        Assert.True(found);
        Assert.Contains("moderationStatus=heldForReview", handler.RequestUri);
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string RequestUri { get; private set; } = "";
        public bool HadContent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString() ?? "";
            HadContent = request.Content is not null;
            return Task.FromResult(respond(request));
        }
    }
}
