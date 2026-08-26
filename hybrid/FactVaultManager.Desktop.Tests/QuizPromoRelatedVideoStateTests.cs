namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPromoRelatedVideoStateTests
{
    [Fact]
    public void MarkSet_PersistsExactPromoAndLongVideoWithoutTouchingPromoMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-related-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            const string metadata = "{\"source_video\":\"quiz.mp4\",\"youtube_upload\":{\"video_id\":\"promo-1\"}}";
            File.WriteAllText(QuizPromoShortPaths.Metadata(root), metadata);
            var markedAt = new DateTimeOffset(2026, 8, 26, 20, 30, 0, TimeSpan.Zero);

            QuizPromoRelatedVideoStore.MarkSet(root, "promo-1", "long-1", markedAt);

            var state = Assert.IsType<QuizPromoRelatedVideoState>(QuizPromoRelatedVideoStore.Load(root));
            Assert.True(state.IsSet);
            Assert.Equal("promo-1", state.PromoVideoId);
            Assert.Equal("long-1", state.LongVideoId);
            Assert.Equal(markedAt.ToString("O"), state.MarkedAt);
            Assert.True(QuizPromoRelatedVideoStore.IsSetFor(root, "promo-1", "long-1"));
            Assert.False(QuizPromoRelatedVideoStore.IsSetFor(root, "promo-2", "long-1"));
            Assert.False(QuizPromoRelatedVideoStore.IsSetFor(root, "promo-1", "long-2"));
            Assert.Equal(metadata, File.ReadAllText(QuizPromoShortPaths.Metadata(root)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkNeedsSetting_RemovesOnlyChecklistState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"promo-related-clear-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(QuizPromoShortPaths.Folder(root));
            File.WriteAllText(QuizPromoShortPaths.Metadata(root), "{\"source_video\":\"keep.mp4\"}");
            QuizPromoRelatedVideoStore.MarkSet(root, "promo-1", "long-1", DateTimeOffset.UtcNow);

            QuizPromoRelatedVideoStore.MarkNeedsSetting(root);

            Assert.Null(QuizPromoRelatedVideoStore.Load(root));
            Assert.False(File.Exists(QuizPromoRelatedVideoStore.PathFor(root)));
            Assert.Equal("{\"source_video\":\"keep.mp4\"}", File.ReadAllText(QuizPromoShortPaths.Metadata(root)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RelatedVideoLinks_OpenExactPromoStudioEditorAndLongFormVideo()
    {
        Assert.Equal(
            "https://studio.youtube.com/video/promo_ABC-123/edit",
            QuizPromoRelatedVideoLinks.StudioEditUrl("promo_ABC-123"));
        Assert.Equal(
            "https://www.youtube.com/watch?v=long_XYZ-789",
            QuizPromoRelatedVideoLinks.WatchUrl("long_XYZ-789"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData("https://youtube.com/watch?v=abc")]
    public void RelatedVideoLinks_RejectNonVideoIds(string value)
    {
        Assert.Throws<ArgumentException>(() => QuizPromoRelatedVideoLinks.StudioEditUrl(value));
    }
}
