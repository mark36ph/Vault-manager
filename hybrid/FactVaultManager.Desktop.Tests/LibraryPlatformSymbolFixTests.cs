using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class LibraryPlatformSymbolFixTests
{
    [Theory]
    [InlineData("Posted", "✓")]
    [InlineData("Uploaded", "✓")]
    [InlineData("Public", "✓")]
    [InlineData("Needs upload", "✕")]
    [InlineData("Promo missing", "✕")]
    [InlineData("Failed", "✕")]
    [InlineData("Waiting", "—")]
    [InlineData("—", "—")]
    public void SocialStatus_UsesCompactTickCrossOrDash(string status, string expected)
    {
        Assert.Equal(expected, LibraryPlatformSymbolPlanner.Symbol(status));
    }

    [Fact]
    public void SocialPromoStatus_MarksPostedAndMissingReleaseFollowthrough()
    {
        Assert.Equal(
            "Posted",
            LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                [], PublicationPlatform.Instagram, youtubePublic: true, promoFileReady: true, metadataUploaded: true));
        Assert.Equal(
            "Needs upload",
            LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                [], PublicationPlatform.Instagram, youtubePublic: true, promoFileReady: true, metadataUploaded: false));
        Assert.Equal(
            "Promo missing",
            LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                [], PublicationPlatform.Facebook, youtubePublic: true, promoFileReady: false, metadataUploaded: false));
        Assert.Equal(
            "Waiting",
            LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                [], PublicationPlatform.Facebook, youtubePublic: false, promoFileReady: true, metadataUploaded: false));
    }

    [Fact]
    public void LibraryFix_RemovesRedundantColumnsAndUsesPromoStoresForVideoRows()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformStatusFix.cs");
        var symbols = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformSymbolFix.cs");

        Assert.Contains("\"IG promo\"", source, StringComparison.Ordinal);
        Assert.Contains("_quizHistoryGrid.Columns.Remove(instagramPromo)", source, StringComparison.Ordinal);
        Assert.Contains("_quizHistoryGrid.Columns.Remove(legacyStatus)", source, StringComparison.Ordinal);
        Assert.Contains("QuizPromoShortSocialPublicationStore.LoadFacebook", source, StringComparison.Ordinal);
        Assert.Contains("QuizPromoShortSocialPublicationStore.LoadInstagram", source, StringComparison.Ordinal);
        Assert.Contains("RebindLibrarySocialSymbolColumn(\"FB\"", symbols, StringComparison.Ordinal);
        Assert.Contains("RebindLibrarySocialSymbolColumn(\"IG\"", symbols, StringComparison.Ordinal);
        Assert.Contains("new DataGridLength(52)", symbols, StringComparison.Ordinal);
        Assert.Contains("SetLibraryColumnWidth(\"Stage\", 128)", symbols, StringComparison.Ordinal);
        Assert.Contains("SetLibraryColumnWidth(\"Next action\", 180)", symbols, StringComparison.Ordinal);
    }

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
