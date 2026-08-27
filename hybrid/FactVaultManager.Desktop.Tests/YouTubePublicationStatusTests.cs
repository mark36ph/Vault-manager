namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubePublicationStatusTests
{
    [Fact]
    public void ParseResponse_PublicVideoHasNoSchedule()
    {
        const string json = """
            {
              "items": [
                {
                  "id": "public-video",
                  "status": {
                    "privacyStatus": "public"
                  }
                }
              ]
            }
            """;

        var result = Assert.Single(YouTubePublicationStatusService.ParseResponse(json));

        Assert.Equal("public-video", result.VideoId);
        Assert.Equal("public", result.PrivacyStatus);
        Assert.Null(result.PublishAt);
    }

    [Fact]
    public void ParseResponse_ScheduledPrivateVideoKeepsPublishAt()
    {
        const string json = """
            {
              "items": [
                {
                  "id": "scheduled-video",
                  "status": {
                    "privacyStatus": "private",
                    "publishAt": "2026-09-04T08:00:00Z"
                  }
                }
              ]
            }
            """;

        var result = Assert.Single(YouTubePublicationStatusService.ParseResponse(json));

        Assert.Equal("private", result.PrivacyStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-09-04T08:00:00Z"), result.PublishAt);
    }

    [Fact]
    public void ParseResponse_IgnoresRowsWithoutSupportedVisibility()
    {
        const string json = """
            {
              "items": [
                { "id": "missing-status" },
                { "id": "missing-privacy", "status": {} },
                { "id": "valid", "status": { "privacyStatus": "unlisted" } }
              ]
            }
            """;

        var result = Assert.Single(YouTubePublicationStatusService.ParseResponse(json));

        Assert.Equal("valid", result.VideoId);
        Assert.Equal("unlisted", result.PrivacyStatus);
        Assert.Null(result.PublishAt);
    }
}
