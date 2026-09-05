namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteCommentModerationTests
{
    [Fact]
    public void CommentModeration_StaysAfterWebsiteUsersInMainSidebar()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var moderation = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.WebsiteCommentModeration.cs");
        var navigation = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.WebsiteCommentModerationNavigation.cs");

        Assert.Contains("InitializeWebsiteCommentModerationNavigation();", buildInfo, StringComparison.Ordinal);
        Assert.DoesNotContain("window.InitializeWebsiteCommentModerationPage();", buildInfo, StringComparison.Ordinal);

        Assert.Contains("_autopilotNavButtons.TryGetValue(\"Users\"", navigation, StringComparison.Ordinal);
        Assert.Contains("_autopilotNavButtons[\"Comments\"]", navigation, StringComparison.Ordinal);
        Assert.Contains("Content = \"☵   Comments\"", navigation, StringComparison.Ordinal);
        Assert.Contains("finalCommentsIndex == finalUsersIndex + 1", navigation, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex = _websiteCommentModerationTabIndex", navigation, StringComparison.Ordinal);
        Assert.Contains("SelectAutopilotNav(\"Comments\")", navigation, StringComparison.Ordinal);

        Assert.Contains("Comment moderation", moderation, StringComparison.Ordinal);
        Assert.Contains("Reported", moderation, StringComparison.Ordinal);
        Assert.Contains("Visible", moderation, StringComparison.Ordinal);
        Assert.Contains("Hidden", moderation, StringComparison.Ordinal);
        Assert.Contains("Dismiss reports", moderation, StringComparison.Ordinal);
        Assert.Contains("Open quiz", moderation, StringComparison.Ordinal);
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
