using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class LibraryPublicationStatusTests
{
    [Fact]
    public void FullQuizStatus_UsesCompactPlatformState()
    {
        var entries = new[]
        {
            Entry(PublicationPlatform.YouTube, PublicationContentKind.Quiz, PublicationStateStatus.Scheduled),
            Entry(PublicationPlatform.Facebook, PublicationContentKind.Quiz, PublicationStateStatus.Uploaded),
            Entry(PublicationPlatform.Instagram, PublicationContentKind.Quiz, PublicationStateStatus.Failed, issue: true),
        };

        Assert.Equal("Scheduled", LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.YouTube));
        Assert.Equal("Uploaded", LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Facebook));
        Assert.Equal("Failed", LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Instagram));
        Assert.Equal("Public", LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.YouTube, verifiedPublic: true));
    }

    [Fact]
    public void InstagramPromoStatus_FlagsMissingPromoAfterYouTubeIsPublic()
    {
        Assert.Equal(
            "Needs upload",
            LibraryPublicationStatusPlanner.InstagramPromoStatus([], true, true, false));
        Assert.Equal(
            "Promo missing",
            LibraryPublicationStatusPlanner.InstagramPromoStatus([], true, false, false));
        Assert.Equal(
            "Waiting",
            LibraryPublicationStatusPlanner.InstagramPromoStatus([], false, true, false));
        Assert.Equal(
            "Posted",
            LibraryPublicationStatusPlanner.InstagramPromoStatus([], true, true, true));
    }

    [Fact]
    public void LibrarySource_AddsSeparateFullPlatformAndInstagramPromoColumns()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPublicationStatus.cs");

        Assert.Contains("StatusColumn(\"YT\"", source, StringComparison.Ordinal);
        Assert.Contains("StatusColumn(\"FB\"", source, StringComparison.Ordinal);
        Assert.Contains("StatusColumn(\"IG\"", source, StringComparison.Ordinal);
        Assert.Contains("StatusColumn(\"IG promo\"", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
    }

    private static PublicationStateEntry Entry(
        string platform,
        string kind,
        string state,
        bool issue = false) =>
        new(
            1,
            platform,
            kind,
            state,
            "",
            "",
            "",
            "",
            "",
            issue ? "upload" : "",
            issue ? "failed" : "",
            "",
            "test",
            "2026-09-01T10:00:00Z");

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
