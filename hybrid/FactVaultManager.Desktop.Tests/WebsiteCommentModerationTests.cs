namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteCommentModerationTests
{
    [Fact]
    public void Build77_WiresDedicatedCommentModerationSettingsPage()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.WebsiteCommentModeration.cs");

        Assert.Contains("CurrentBuildNumber = 77", buildInfo, StringComparison.Ordinal);
        Assert.Contains("InitializeWebsiteCommentModerationPage", buildInfo, StringComparison.Ordinal);
        Assert.Contains("Comment moderation", source, StringComparison.Ordinal);
        Assert.Contains("Reported", source, StringComparison.Ordinal);
        Assert.Contains("Visible", source, StringComparison.Ordinal);
        Assert.Contains("Hidden", source, StringComparison.Ordinal);
        Assert.Contains("Dismiss reports", source, StringComparison.Ordinal);
        Assert.Contains("Open quiz", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build77_DesktopClientUsesSecuredSiteCommentsAdminRoute()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/FactburstWebsiteCommentsAdminClient.cs");

        Assert.Contains("/api/site/comments?", source, StringComparison.Ordinal);
        Assert.Contains("/api/site/comments/{commentId}", source, StringComparison.Ordinal);
        Assert.Contains("AuthenticationHeaderValue(\"Bearer\", key)", source, StringComparison.Ordinal);
        Assert.Contains("dismiss_reports", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build77_AdminWorkerExposesCommentModerationBehindExistingApiKeyGuard()
    {
        var worker = ReadRepositoryFile("cloudflare/factburst-link-tracker/admin-worker-entry.js");
        var admin = ReadRepositoryFile("cloudflare/factburst-link-tracker/site-comment-admin.js");

        Assert.Contains("handleSiteCommentAdmin", worker, StringComparison.Ordinal);
        Assert.Contains("pathname === \"/api/site/comments\"", worker, StringComparison.Ordinal);
        Assert.Contains("pathname.startsWith(\"/api/site/comments/\")", worker, StringComparison.Ordinal);
        Assert.Contains("requireApiKey(request, env)", worker, StringComparison.Ordinal);
        Assert.Contains("reported", admin, StringComparison.Ordinal);
        Assert.Contains("status = 'hidden'", admin, StringComparison.Ordinal);
        Assert.Contains("status = 'deleted'", admin, StringComparison.Ordinal);
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
