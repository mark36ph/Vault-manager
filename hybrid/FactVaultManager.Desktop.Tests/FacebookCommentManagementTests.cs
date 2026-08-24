using System.Net;

namespace FactVaultManager.Desktop.Tests;

public sealed class FacebookCommentManagementTests
{
    private static readonly FacebookPageVideo Reel = new(
        "1051847137549312",
        "",
        "Test your knowledge with 1 question in Film Quiz.\n\nCan you get 1/1?",
        "/reel/1051847137549312/",
        new DateTime(2026, 8, 21, 23, 9, 41, DateTimeKind.Utc));

    [Fact]
    public void CommentsResponse_ParsesAuthorReelAndModerationDetails()
    {
        const string json = """
            {
              "data": [{
                "id": "comment-1",
                "message": "Great quiz!",
                "created_time": "2026-08-23T10:30:00+0000",
                "from": { "id": "viewer-1", "name": "Quiz Fan" },
                "like_count": 3,
                "comment_count": 2,
                "user_likes": true,
                "is_hidden": false
              }]
            }
            """;

        var result = Assert.Single(FacebookCommentManagementService.ParseComments(json, "page-1", Reel));

        Assert.Equal("comment-1", result.Id);
        Assert.Equal("Quiz Fan", result.Author);
        Assert.Equal("Great quiz!", result.Message);
        Assert.Equal(3, result.LikeCount);
        Assert.Equal(2, result.ReplyCount);
        Assert.True(result.IsLiked);
        Assert.False(result.IsHidden);
        Assert.False(result.IsPageComment);
        Assert.Equal("Test your knowledge with 1 question in Film Quiz.", result.ReelTitle);
        Assert.Equal("https://www.facebook.com/reel/1051847137549312", result.ReelUrl);
    }

    [Fact]
    public void NeedsReplyFilter_HidesPageRepliesHiddenAndHandledComments()
    {
        var newest = DateTime.UtcNow;
        var comments = new[]
        {
            Comment("needs-reply", newest),
            Comment("has-reply", newest.AddMinutes(-1), replies: 1),
            Comment("hidden", newest.AddMinutes(-2), hidden: true),
            Comment("page", newest.AddMinutes(-3), pageComment: true),
            Comment("handled", newest.AddMinutes(-4)),
        };

        var result = Assert.Single(FacebookCommentInbox.Filter(
            comments,
            "Needs reply",
            new HashSet<string>(StringComparer.Ordinal) { "handled" }));

        Assert.Equal("needs-reply", result.Id);
        Assert.Equal("hidden", Assert.Single(FacebookCommentInbox.Filter(comments, "Hidden")).Id);
        Assert.Equal("needs-reply", FacebookCommentInbox.Filter(comments, "Newest")[0].Id);
    }

    [Fact]
    public async Task CommentActions_UseTheExpectedGraphEdgesAndFormValues()
    {
        var handler = new FacebookCommentHandler();
        var service = new FacebookCommentManagementService(new HttpClient(handler));

        var firstCommentId = await service.PostTopLevelCommentAsync(
            "page-token", "video-1", "How did you score?");
        await service.ReplyAsync("page-token", "comment-1", "Thanks!");
        await service.SetLikedAsync("page-token", "comment-1", true);
        await service.SetLikedAsync("page-token", "comment-1", false);
        await service.SetHiddenAsync("page-token", "comment-1", true);
        await service.DeleteAsync("page-token", "comment-1");

        Assert.Equal("facebook-comment-1", firstCommentId);
        Assert.Collection(handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/video-1/comments", request.Path);
                Assert.Contains("message=How+did+you+score%3F", request.Form);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/comment-1/comments", request.Path);
                Assert.Contains("message=Thanks%21", request.Form);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/comment-1/likes", request.Path);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.EndsWith("/comment-1/likes", request.Path);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/comment-1", request.Path);
                Assert.Contains("is_hidden=true", request.Form);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.EndsWith("/comment-1", request.Path);
            });
        Assert.All(handler.Requests, request => Assert.Contains("access_token=page-token", request.Form));
    }

    private static FacebookCommentItem Comment(
        string id,
        DateTime created,
        int replies = 0,
        bool hidden = false,
        bool pageComment = false) =>
        new(id, "reel", "Reel", "https://www.facebook.com/reel/123", "Viewer", "123",
            "Comment", created, 0, replies, false, hidden, pageComment);

    private sealed class FacebookCommentHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"facebook-comment-1\"}"),
            };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Form);
}
