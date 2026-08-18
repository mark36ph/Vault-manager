using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizSharedAssetCacheTests
{
    [Fact]
    public void BackgroundPath_IsStableForSameRenderSettings()
    {
        var first = QuizSharedAssetCache.BackgroundPath(1920, 1080, 30);
        var second = QuizSharedAssetCache.BackgroundPath(1920, 1080, 30);

        Assert.Equal(first, second);
        Assert.NotEqual(first, QuizSharedAssetCache.BackgroundPath(1080, 1920, 30));
        Assert.NotEqual(first, QuizSharedAssetCache.BackgroundPath(1920, 1080, 60));
    }

    [Fact]
    public void OpeningCountdownPath_ChangesWhenReusableVisualIdentityChanges()
    {
        var first = QuizSharedAssetCache.OpeningCountdownPath(3, 1920, 1080, false, null, "cyan|gold|white");
        var same = QuizSharedAssetCache.OpeningCountdownPath(3, 1920, 1080, false, null, "cyan|gold|white");
        var changedStyle = QuizSharedAssetCache.OpeningCountdownPath(3, 1920, 1080, false, null, "cyan|purple|white");
        var changedSize = QuizSharedAssetCache.OpeningCountdownPath(3, 1080, 1920, true, null, "cyan|gold|white");

        Assert.Equal(first, same);
        Assert.NotEqual(first, changedStyle);
        Assert.NotEqual(first, changedSize);
    }

    [Fact]
    public void OpeningCountdownPath_UsesDifferentFilesForEachNumber()
    {
        var three = QuizSharedAssetCache.OpeningCountdownPath(3, 1920, 1080, false, null, "style");
        var two = QuizSharedAssetCache.OpeningCountdownPath(2, 1920, 1080, false, null, "style");
        var one = QuizSharedAssetCache.OpeningCountdownPath(1, 1920, 1080, false, null, "style");

        Assert.NotEqual(three, two);
        Assert.NotEqual(two, one);
        Assert.EndsWith("start_3.png", three, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("start_2.png", two, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("start_1.png", one, StringComparison.OrdinalIgnoreCase);
    }
}
