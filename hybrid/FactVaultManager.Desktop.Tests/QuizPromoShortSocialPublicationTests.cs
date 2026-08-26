using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPromoShortSocialPublicationTests
{
    [Fact]
    public void PublicationStore_TracksEachPromoPlatformSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-social-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            File.WriteAllText(
                QuizPromoShortPaths.Metadata(root),
                "{\"source_video\":\"quiz.mp4\"}");

            QuizPromoShortPublicationStore.RecordYouTube(
                root,
                new YouTubeVideoUploadResult("yt123", "https://www.youtube.com/watch?v=yt123"),
                "private",
                new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
            QuizPromoShortSocialPublicationStore.RecordFacebook(
                root,
                new FacebookReelUploadResult("fb123", "https://www.facebook.com/reel/fb123"),
                new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            QuizPromoShortSocialPublicationStore.RecordInstagram(
                root,
                new InstagramReelUploadResult("ig123", "https://www.instagram.com/reel/ig123/"),
                new DateTimeOffset(2026, 8, 26, 10, 2, 0, TimeSpan.Zero));

            var youtube = QuizPromoShortPublicationStore.LoadYouTube(root);
            var facebook = QuizPromoShortSocialPublicationStore.LoadFacebook(root);
            var instagram = QuizPromoShortSocialPublicationStore.LoadInstagram(root);

            Assert.NotNull(youtube);
            Assert.Equal("yt123", youtube.VideoId);
            Assert.NotNull(facebook);
            Assert.Equal("fb123", facebook.VideoId);
            Assert.NotNull(instagram);
            Assert.Equal("ig123", instagram.MediaId);
            Assert.True(QuizPromoShortSocialPublicationStore.AllUploaded(root));

            var json = JsonNode.Parse(File.ReadAllText(QuizPromoShortPaths.Metadata(root)))!.AsObject();
            Assert.Equal("quiz.mp4", json["source_video"]!.GetValue<string>());
            Assert.NotNull(json["youtube_upload"]);
            Assert.NotNull(json["facebook_upload"]);
            Assert.NotNull(json["instagram_upload"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllUploaded_RequiresYouTubeFacebookAndInstagram()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-social-required-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            File.WriteAllText(QuizPromoShortPaths.Metadata(root), "{}");

            Assert.False(QuizPromoShortSocialPublicationStore.AllUploaded(root));

            QuizPromoShortPublicationStore.RecordYouTube(
                root,
                new YouTubeVideoUploadResult("yt123", "https://www.youtube.com/watch?v=yt123"),
                "unlisted",
                DateTimeOffset.UtcNow);
            Assert.False(QuizPromoShortSocialPublicationStore.AllUploaded(root));

            QuizPromoShortSocialPublicationStore.RecordFacebook(
                root,
                new FacebookReelUploadResult("fb123", "https://www.facebook.com/reel/fb123"),
                DateTimeOffset.UtcNow);
            Assert.False(QuizPromoShortSocialPublicationStore.AllUploaded(root));

            QuizPromoShortSocialPublicationStore.RecordInstagram(
                root,
                new InstagramReelUploadResult("ig123", ""),
                DateTimeOffset.UtcNow);
            Assert.True(QuizPromoShortSocialPublicationStore.AllUploaded(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UploadState_DisplayTracksReadyThroughThreePlatforms()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-state-display-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal("Not created", QuizPromoShortUploadState.Display(root));

            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            File.WriteAllBytes(QuizPromoShortPaths.Video(root), [1]);
            File.WriteAllText(QuizPromoShortPaths.Metadata(root), "{}");

            Assert.Equal(0, QuizPromoShortUploadState.UploadedCount(root));
            Assert.Equal("Ready", QuizPromoShortUploadState.Display(root));

            QuizPromoShortPublicationStore.RecordYouTube(
                root,
                new YouTubeVideoUploadResult("yt-progress", "https://www.youtube.com/watch?v=yt-progress"),
                "private",
                DateTimeOffset.UtcNow);
            Assert.Equal(1, QuizPromoShortUploadState.UploadedCount(root));
            Assert.Equal("Uploaded 1/3", QuizPromoShortUploadState.Display(root));

            QuizPromoShortSocialPublicationStore.RecordFacebook(
                root,
                new FacebookReelUploadResult("fb-progress", "https://www.facebook.com/reel/fb-progress"),
                DateTimeOffset.UtcNow);
            Assert.Equal(2, QuizPromoShortUploadState.UploadedCount(root));
            Assert.Equal("Uploaded 2/3", QuizPromoShortUploadState.Display(root));

            QuizPromoShortSocialPublicationStore.RecordInstagram(
                root,
                new InstagramReelUploadResult("ig-progress", "https://www.instagram.com/reel/ig-progress/"),
                DateTimeOffset.UtcNow);
            Assert.Equal(3, QuizPromoShortUploadState.UploadedCount(root));
            Assert.Equal("Uploaded 3/3", QuizPromoShortUploadState.Display(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UploadState_RestoreKeepsAllPlatformRecordsAfterRegeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-state-restore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            File.WriteAllBytes(QuizPromoShortPaths.Video(root), [1]);
            File.WriteAllText(
                QuizPromoShortPaths.Metadata(root),
                "{\"source_video\":\"original.mp4\",\"visual_style\":\"old\"}");

            QuizPromoShortPublicationStore.RecordYouTube(
                root,
                new YouTubeVideoUploadResult("yt-keep", "https://www.youtube.com/watch?v=yt-keep"),
                "unlisted",
                DateTimeOffset.UtcNow);
            QuizPromoShortSocialPublicationStore.RecordFacebook(
                root,
                new FacebookReelUploadResult("fb-keep", "https://www.facebook.com/reel/fb-keep"),
                DateTimeOffset.UtcNow);
            QuizPromoShortSocialPublicationStore.RecordInstagram(
                root,
                new InstagramReelUploadResult("ig-keep", "https://www.instagram.com/reel/ig-keep/"),
                DateTimeOffset.UtcNow);

            var snapshot = QuizPromoShortUploadState.Capture(root);

            File.WriteAllText(
                QuizPromoShortPaths.Metadata(root),
                "{\"source_video\":\"regenerated.mp4\",\"visual_style\":\"native_factburst_short\"}");
            QuizPromoShortUploadState.Restore(root, snapshot);

            var json = JsonNode.Parse(File.ReadAllText(QuizPromoShortPaths.Metadata(root)))!.AsObject();
            Assert.Equal("regenerated.mp4", json["source_video"]!.GetValue<string>());
            Assert.Equal("native_factburst_short", json["visual_style"]!.GetValue<string>());
            Assert.Equal("yt-keep", json["youtube_upload"]!["video_id"]!.GetValue<string>());
            Assert.Equal("fb-keep", json["facebook_upload"]!["video_id"]!.GetValue<string>());
            Assert.Equal("ig-keep", json["instagram_upload"]!["media_id"]!.GetValue<string>());
            Assert.True(QuizPromoShortSocialPublicationStore.AllUploaded(root));
            Assert.Equal("Uploaded 3/3", QuizPromoShortUploadState.Display(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
