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
}
