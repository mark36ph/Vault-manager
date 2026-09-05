namespace FactVaultManager.Desktop.Tests;

public sealed class InstagramBusinessLoginTests
{
    [Fact]
    public void Build178_InstagramConnectionUsesBusinessLoginAndLocalCallback()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/InstagramBusinessLoginService.cs");
        var ui = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramBusinessLogin.cs");

        Assert.Contains("https://www.instagram.com/oauth/authorize", source, StringComparison.Ordinal);
        Assert.Contains("http://localhost:53682/instagram/callback/", source, StringComparison.Ordinal);
        Assert.Contains("instagram_business_basic", source, StringComparison.Ordinal);
        Assert.Contains("instagram_business_manage_comments", source, StringComparison.Ordinal);
        Assert.Contains("instagram_business_content_publish", source, StringComparison.Ordinal);
        Assert.Contains("enable_fb_login", source, StringComparison.Ordinal);
        Assert.Contains("ig_exchange_token", source, StringComparison.Ordinal);
        Assert.Contains("Connect Instagram", ui, StringComparison.Ordinal);
        Assert.Contains("LocalSecretProtector.Protect", ui, StringComparison.Ordinal);
        Assert.Contains("_settingsInstagramAccessToken.Password = result.AccessToken", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void Build178_InstagramConnectDoesNotUseTheDocumentationLinkAsTheAction()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsSettings.cs");
        var ui = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramBusinessLogin.cs");

        Assert.Contains("Instagram token setup", source, StringComparison.Ordinal);
        Assert.Contains("Connect Instagram", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login", ui, StringComparison.Ordinal);
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
