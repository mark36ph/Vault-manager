namespace FactVaultManager.Desktop.Tests;

public sealed class ApiConnectionsSettingsTests
{
    [Fact]
    public void Build141_ConsolidatesExternalCredentialsIntoOneSettingsPage()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsSettings.cs");

        Assert.Contains("API & Connections", source, StringComparison.Ordinal);
        Assert.Contains("OpenAiKeyPasswordBox", source, StringComparison.Ordinal);
        Assert.Contains("PixabayKeyPasswordBox", source, StringComparison.Ordinal);
        Assert.Contains("PexelsKeyPasswordBox", source, StringComparison.Ordinal);
        Assert.Contains("YouTubeApiKeyPasswordBox", source, StringComparison.Ordinal);
        Assert.Contains("_settingsYouTubeClientId", source, StringComparison.Ordinal);
        Assert.Contains("_settingsYouTubeClientSecret", source, StringComparison.Ordinal);
        Assert.Contains("_settingsFacebookPageAccessToken", source, StringComparison.Ordinal);
        Assert.Contains("_settingsInstagramAccessToken", source, StringComparison.Ordinal);
        Assert.Contains("new[] { \"ai\", \"youtube\", \"facebook\", \"instagram\" }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build141_ProvidesLiveReadOnlyChecksForEveryExternalCredential()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsSettings.cs");

        Assert.Contains("Test all connections", source, StringComparison.Ordinal);
        Assert.Contains("TestOpenAiConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestPixabayConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestPexelsConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestYouTubeApiConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestYouTubeOAuthConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestFacebookConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestInstagramConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", source, StringComparison.Ordinal);
        Assert.Contains("They do not upload, publish, delete or modify content", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build141_InstagramCheckUsesLightweightIdentityEndpoint()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/InstagramCredentialTestService.cs");

        Assert.Contains("GetAccountIdentityAsync", source, StringComparison.Ordinal);
        Assert.Contains("fields=user_id%2Cusername%2Caccount_type", source, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListMediaAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build141_YouTubeConnectButtonIsReplacedToAvoidDuplicateHandlers()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsYouTubeButton.cs");
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("parent.Children.Remove(oldButton);", source, StringComparison.Ordinal);
        Assert.Contains("connect.Click += async (_, _) => await ConnectYouTubeAsync();", source, StringComparison.Ordinal);
        Assert.Contains("FinalizeApiConnectionsYouTubeButton();", buildInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Build141_IsCurrentBuildAndInitializesUnifiedSettings()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("CurrentBuildNumber = 141", source, StringComparison.Ordinal);
        Assert.Contains("InitializeSettingsWorkflow();", source, StringComparison.Ordinal);
        Assert.Contains("InitializeApiConnectionsSettings();", source, StringComparison.Ordinal);
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
