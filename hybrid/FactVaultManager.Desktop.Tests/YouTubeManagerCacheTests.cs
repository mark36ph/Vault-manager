namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeManagerCacheTests
{
    [Fact]
    public void AccountKey_IsStableAndDoesNotExposeTheRefreshToken()
    {
        var first = YouTubeManagerCacheStore.CreateAccountKey("client-id", "refresh-secret");
        var second = YouTubeManagerCacheStore.CreateAccountKey("client-id", "refresh-secret");
        var different = YouTubeManagerCacheStore.CreateAccountKey("client-id", "other-secret");

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("refresh-secret", first);
    }

    [Fact]
    public void Cache_RoundTripsPlaylistsAndRejectsAnotherAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new YouTubeManagerCacheStore(Path.Combine(directory, "factvault.db"));
            var refreshedAt = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
            store.Save(new YouTubeManagerCacheSnapshot
            {
                AccountKey = "account-one",
                RefreshedAtUtc = refreshedAt,
                Playlists =
                [
                    new YouTubePlaylistItem("playlist-1", "History Quizzes", "History videos", "private", 1),
                ],
                PlaylistVideos = new Dictionary<string, List<YouTubePlaylistVideo>>(StringComparer.Ordinal)
                {
                    ["playlist-1"] =
                    [
                        new YouTubePlaylistVideo("item-1", "video-1", "History Quiz 001", 0),
                    ],
                },
            });

            var result = Assert.IsType<YouTubeManagerCacheSnapshot>(store.Load("account-one"));

            Assert.Equal(refreshedAt, result.RefreshedAtUtc);
            Assert.Equal("History Quizzes", Assert.Single(result.Playlists).Title);
            Assert.Equal("video-1", Assert.Single(result.PlaylistVideos["playlist-1"]).VideoId);
            Assert.Null(store.Load("account-two"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Freshness_ExpiresAfterTenMinutes()
    {
        var now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        var fresh = new YouTubeManagerCacheSnapshot { RefreshedAtUtc = now.AddMinutes(-9) };
        var stale = new YouTubeManagerCacheSnapshot { RefreshedAtUtc = now.AddMinutes(-11) };
        var future = new YouTubeManagerCacheSnapshot { RefreshedAtUtc = now.AddMinutes(1) };

        Assert.True(YouTubeManagerCacheStore.IsFresh(fresh, now));
        Assert.False(YouTubeManagerCacheStore.IsFresh(stale, now));
        Assert.False(YouTubeManagerCacheStore.IsFresh(future, now));
    }
}
