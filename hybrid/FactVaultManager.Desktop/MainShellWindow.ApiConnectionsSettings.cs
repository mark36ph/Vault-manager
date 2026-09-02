using System.Diagnostics;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly HttpClient ApiCredentialTestClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private bool _apiConnectionsSettingsInitialized;
    private bool _apiConnectionsTestAllRunning;
    private readonly Dictionary<string, TextBlock> _apiConnectionStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<Task<string>>> _apiConnectionTests = new(StringComparer.OrdinalIgnoreCase);
    private Button? _apiConnectionsTestAllButton;

    public void InitializeApiConnectionsSettings()
    {
        if (_apiConnectionsSettingsInitialized)
            return;

        InitializeSettingsWorkflow();
        if (!_settingsWorkflowInitialized || _settingsContentHost is null)
            return;

        _apiConnectionsSettingsInitialized = true;

        foreach (var key in new[] { "ai", "images", "youtube", "facebook", "instagram" })
        {
            if (_settingsNavButtons.TryGetValue(key, out var oldButton))
                oldButton.Visibility = Visibility.Collapsed;
        }

        Detach(OpenAiKeyPasswordBox);
        Detach(OpenAiModelTextBox);
        Detach(YouTubeApiKeyPasswordBox);
        DetachIfPresent(_settingsYouTubeClientId);
        DetachIfPresent(_settingsYouTubeClientSecret);
        DetachIfPresent(_settingsYouTubeConnectionStatus);
        DetachIfPresent(_settingsYouTubeConnectButton);
        DetachIfPresent(_settingsFacebookPageAccessToken);
        DetachIfPresent(_settingsInstagramAccessToken);

        _settingsPages["connections"] = BuildApiConnectionsSettingsPage();
        AddApiConnectionsNavigationButton();
        InitializeApiConnectionStatuses();
    }

    private void AddApiConnectionsNavigationButton()
    {
        if (_settingsNavButtons.ContainsKey("connections"))
            return;
        if (!_settingsNavButtons.TryGetValue("general", out var general) || general.Parent is not Panel panel)
            return;

        AddSettingsNav(panel, "connections", "API & Connections");
        var button = _settingsNavButtons["connections"];
        panel.Children.Remove(button);
        var insertAfter = _settingsNavButtons.TryGetValue("integrity", out var integrity)
            ? panel.Children.IndexOf(integrity) + 1
            : panel.Children.IndexOf(general) + 1;
        panel.Children.Insert(Math.Max(0, insertAfter), button);
    }

    private static void DetachIfPresent(FrameworkElement? element)
    {
        if (element is not null)
            Detach(element);
    }

    private FrameworkElement BuildApiConnectionsSettingsPage()
    {
        _apiConnectionStatuses.Clear();
        _apiConnectionTests.Clear();

        var page = SettingsPageStack(
            "API & Connections",
            "Keep every external service credential in one place and verify each connection without leaving Settings.");

        var openAi = SettingsSection("OpenAI");
        page.Children.Add(openAi);
        var openAiStack = (StackPanel)openAi.Child;
        AddApiCredentialRow(
            openAiStack,
            "OpenAI API key",
            OpenAiKeyPasswordBox,
            "openai",
            TestOpenAiConnectionAsync,
            "Used for quiz question generation and related Factburst quiz text tasks.");
        openAiStack.Children.Add(SettingsFieldLabel("Text model"));
        OpenAiModelTextBox.Margin = new Thickness(0, 5, 0, 0);
        openAiStack.Children.Add(OpenAiModelTextBox);

        var youtube = SettingsSection("YouTube");
        page.Children.Add(youtube);
        var youtubeStack = (StackPanel)youtube.Child;
        AddApiCredentialRow(
            youtubeStack,
            "YouTube Data API v3 key",
            YouTubeApiKeyPasswordBox,
            "youtube-api",
            TestYouTubeApiConnectionAsync,
            "Create this in Google Cloud and restrict it to YouTube Data API v3.");

        youtubeStack.Children.Add(SettingsFieldLabel("OAuth desktop client ID"));
        _settingsYouTubeClientId ??= new TextBox();
        _settingsYouTubeClientId.Margin = new Thickness(0, 5, 0, 8);
        youtubeStack.Children.Add(_settingsYouTubeClientId);
        youtubeStack.Children.Add(SettingsFieldLabel("OAuth client secret"));
        _settingsYouTubeClientSecret ??= new PasswordBox();
        _settingsYouTubeClientSecret.Margin = new Thickness(0, 5, 0, 8);
        youtubeStack.Children.Add(_settingsYouTubeClientSecret);

        _settingsYouTubeConnectionStatus ??= NewApiStatusText();
        _apiConnectionStatuses["youtube-oauth"] = _settingsYouTubeConnectionStatus;
        _apiConnectionTests["youtube-oauth"] = TestYouTubeOAuthConnectionAsync;
        var youtubeActions = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        youtubeActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 3; index++)
            youtubeActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        youtubeActions.Children.Add(_settingsYouTubeConnectionStatus);

        _settingsYouTubeConnectButton ??= new Button { Content = "Connect Google account", MinWidth = 154 };
        _settingsYouTubeConnectButton.Click += ApiConnectionsConnectYouTube_Click;
        Grid.SetColumn(_settingsYouTubeConnectButton, 1);
        youtubeActions.Children.Add(_settingsYouTubeConnectButton);

        var testYouTubeOAuth = new Button { Content = "Test", MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) };
        testYouTubeOAuth.Click += async (_, _) => await RunApiConnectionTestAsync("youtube-oauth");
        Grid.SetColumn(testYouTubeOAuth, 2);
        youtubeActions.Children.Add(testYouTubeOAuth);

        var disconnectYouTube = new Button { Content = "Disconnect", MinWidth = 92, Margin = new Thickness(8, 0, 0, 0) };
        disconnectYouTube.Click += async (_, _) => await DisconnectYouTubeAsync();
        Grid.SetColumn(disconnectYouTube, 3);
        youtubeActions.Children.Add(disconnectYouTube);
        youtubeStack.Children.Add(youtubeActions);
        AddApprovedYouTubeDestinationControls(youtubeStack);

        var meta = SettingsSection("Meta: Facebook & Instagram");
        page.Children.Add(meta);
        var metaStack = (StackPanel)meta.Child;
        _settingsFacebookPageAccessToken ??= new PasswordBox();
        AddApiCredentialRow(
            metaStack,
            "Facebook Page access token",
            _settingsFacebookPageAccessToken,
            "facebook",
            TestFacebookConnectionAsync,
            "Used for Facebook Page analytics, comments and social publishing.");
        AddApprovedFacebookDestinationControls(metaStack);

        _settingsInstagramAccessToken ??= new PasswordBox();
        AddApiCredentialRow(
            metaStack,
            "Instagram user access token",
            _settingsInstagramAccessToken,
            "instagram",
            TestInstagramConnectionAsync,
            "Use Instagram API with Instagram Login for a Business or Creator account. Required permissions: instagram_business_basic, instagram_business_manage_comments and instagram_business_content_publish.");

        var metaLinks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var openMetaApps = new Button { Content = "Open Meta App Dashboard", MinWidth = 148 };
        openMetaApps.Click += (_, _) => OpenSettingsExternalLink("https://developers.facebook.com/apps/");
        metaLinks.Children.Add(openMetaApps);
        var instagramSetup = new Button { Content = "Instagram token setup", MinWidth = 142, Margin = new Thickness(8, 0, 0, 0) };
        instagramSetup.Click += (_, _) => OpenSettingsExternalLink(
            "https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login");
        metaLinks.Children.Add(instagramSetup);
        metaStack.Children.Add(metaLinks);

        var checks = SettingsSection("Connection checks");
        page.Children.Add(checks);
        var checksStack = (StackPanel)checks.Child;
        checksStack.Children.Add(new TextBlock
        {
            Text = "Tests make small read-only API requests. They do not upload, publish, delete or modify content.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8),
        });
        _apiConnectionsTestAllButton = new Button
        {
            Content = "Test all connections",
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _apiConnectionsTestAllButton.Click += async (_, _) => await TestAllApiConnectionsAsync();
        checksStack.Children.Add(_apiConnectionsTestAllButton);

        page.Children.Add(SettingsFooter("Save API settings", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private async void ApiConnectionsConnectYouTube_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsYouTubeConnectButton is null)
            return;
        _settingsYouTubeConnectButton.Click -= ApiConnectionsConnectYouTube_Click;
        try
        {
            await ConnectYouTubeAsync();
        }
        finally
        {
            _settingsYouTubeConnectButton.Click += ApiConnectionsConnectYouTube_Click;
        }
    }

    private void AddApiCredentialRow(
        StackPanel parent,
        string label,
        FrameworkElement input,
        string key,
        Func<Task<string>> test,
        string help)
    {
        parent.Children.Add(SettingsFieldLabel(label));
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(input);
        var button = new Button { Content = "Test", MinWidth = 76 };
        button.Click += async (_, _) => await RunApiConnectionTestAsync(key);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        parent.Children.Add(row);

        var status = NewApiStatusText();
        status.Margin = new Thickness(0, 5, 0, 0);
        parent.Children.Add(status);
        _apiConnectionStatuses[key] = status;
        _apiConnectionTests[key] = test;

        parent.Children.Add(new TextBlock
        {
            Text = help,
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 10),
        });
    }

    private static TextBlock NewApiStatusText() => new()
    {
        Text = "Not tested",
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
        TextWrapping = TextWrapping.Wrap,
    };

    private void InitializeApiConnectionStatuses()
    {
        SetConfiguredStatus("openai", OpenAiKeyPasswordBox.Password);
        SetConfiguredStatus("youtube-api", YouTubeApiKeyPasswordBox.Password);
        SetConfiguredStatus("facebook", _settingsFacebookPageAccessToken?.Password);
        SetConfiguredStatus("instagram", _settingsInstagramAccessToken?.Password);

        var settings = _data.LoadSettings();
        if (_apiConnectionStatuses.TryGetValue("youtube-oauth", out var oauthStatus))
        {
            oauthStatus.Text = settings.YouTubeOAuthRefreshToken.Length > 0
                ? "Saved — connected token present; click Test to verify"
                : "Not connected";
            oauthStatus.Foreground = SettingsMutedBrush();
        }
    }

    private void SetConfiguredStatus(string key, string? value)
    {
        if (!_apiConnectionStatuses.TryGetValue(key, out var status))
            return;
        status.Text = string.IsNullOrWhiteSpace(value)
            ? "Not configured"
            : "Saved — click Test to verify";
        status.Foreground = SettingsMutedBrush();
    }

    private async Task TestAllApiConnectionsAsync()
    {
        if (_apiConnectionsTestAllRunning)
            return;
        _apiConnectionsTestAllRunning = true;
        if (_apiConnectionsTestAllButton is not null)
        {
            _apiConnectionsTestAllButton.IsEnabled = false;
            _apiConnectionsTestAllButton.Content = "Testing...";
        }
        try
        {
            foreach (var key in _apiConnectionTests.Keys.ToArray())
                await RunApiConnectionTestAsync(key);
        }
        finally
        {
            _apiConnectionsTestAllRunning = false;
            if (_apiConnectionsTestAllButton is not null)
            {
                _apiConnectionsTestAllButton.IsEnabled = true;
                _apiConnectionsTestAllButton.Content = "Test all connections";
            }
        }
    }

    private async Task RunApiConnectionTestAsync(string key)
    {
        if (!_apiConnectionStatuses.TryGetValue(key, out var status) ||
            !_apiConnectionTests.TryGetValue(key, out var test))
            return;

        status.Text = "Testing...";
        status.Foreground = SettingsMutedBrush();
        try
        {
            var detail = await test();
            status.Text = "✓ " + detail;
            status.Foreground = new SolidColorBrush(Color.FromRgb(25, 140, 75));
        }
        catch (Exception error)
        {
            status.Text = "✕ " + FriendlyApiTestError(error);
            status.Foreground = new SolidColorBrush(Color.FromRgb(190, 45, 55));
        }
    }

    private static string FriendlyApiTestError(Exception error)
    {
        var message = (error.Message ?? "Connection test failed.").Trim();
        if (message.Length > 180)
            message = message[..180].TrimEnd() + "…";
        return message;
    }

    private async Task<string> TestOpenAiConnectionAsync()
    {
        var key = RequireApiValue(OpenAiKeyPasswordBox.Password, "OpenAI API key");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await ApiCredentialTestClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI rejected the key (HTTP {(int)response.StatusCode}).");
        return "Working — OpenAI accepted the API key";
    }

    private async Task<string> TestYouTubeApiConnectionAsync()
    {
        var key = RequireApiValue(YouTubeApiKeyPasswordBox.Password, "YouTube Data API key");
        var service = new YouTubeVideoAnalyticsService();
        await service.FetchAsync(key, new[] { "dQw4w9WgXcQ" });
        return "Working — YouTube Data API v3 accepted the key";
    }

    private async Task<string> TestYouTubeOAuthConnectionAsync()
    {
        var clientId = RequireApiValue(_settingsYouTubeClientId?.Text, "YouTube OAuth client ID");
        var clientSecret = _settingsYouTubeClientSecret?.Password.Trim() ?? "";
        var saved = _data.LoadSettings();
        var refreshToken = RequireApiValue(saved.YouTubeOAuthRefreshToken, "YouTube OAuth refresh token");
        var accessToken = await _youtubeOAuth.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken);
        var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
        return $"Working — connected to {channel.Title}";
    }

    private async Task<string> TestFacebookConnectionAsync()
    {
        var token = RequireApiValue(_settingsFacebookPageAccessToken?.Password, "Facebook Page access token");
        var identity = await _facebookAnalytics.GetPageIdentityAsync(token);
        return identity.PageName.Length > 0
            ? $"Working — {identity.PageName}"
            : $"Working — Page {identity.PageId}";
    }

    private async Task<string> TestInstagramConnectionAsync()
    {
        var token = RequireApiValue(_settingsInstagramAccessToken?.Password, "Instagram user access token");
        var identity = await _instagramManagement.GetAccountIdentityAsync(token);
        var account = identity.Username.Length > 0 ? "@" + identity.Username : identity.UserId;
        return identity.AccountType.Length > 0
            ? $"Working — {account} ({identity.AccountType})"
            : $"Working — {account}";
    }

    private static string RequireApiValue(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidOperationException($"{name} is not configured.");
        return text;
    }

    private void AddApprovedYouTubeDestinationControls(StackPanel parent)
    {
        var settings = _data.LoadSettings();
        var approved = new TextBlock
        {
            Text = settings.ApprovedYouTubeChannelId.Length == 0
                ? "Approved upload destination: set on the next confirmed upload"
                : $"Approved upload destination: {settings.ApprovedYouTubeChannelName} ({settings.ApprovedYouTubeChannelId})",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 10, 0, 5),
            TextWrapping = TextWrapping.Wrap,
        };
        parent.Children.Add(approved);
        var reset = new Button
        {
            Content = "Reset approved upload channel",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        reset.Click += (_, _) =>
        {
            ResetApprovedYouTubeAccount();
            approved.Text = "Approved upload destination: set on the next confirmed upload";
        };
        parent.Children.Add(reset);
    }

    private void AddApprovedFacebookDestinationControls(StackPanel parent)
    {
        var settings = _data.LoadSettings();
        var approved = new TextBlock
        {
            Text = settings.ApprovedFacebookPageId.Length == 0
                ? "Approved upload destination: set on the next confirmed upload"
                : $"Approved upload destination: {settings.ApprovedFacebookPageName} ({settings.ApprovedFacebookPageId})",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 2, 0, 5),
            TextWrapping = TextWrapping.Wrap,
        };
        parent.Children.Add(approved);
        var reset = new Button
        {
            Content = "Reset approved upload Page",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        reset.Click += (_, _) =>
        {
            ResetApprovedFacebookAccount();
            approved.Text = "Approved upload destination: set on the next confirmed upload";
        };
        parent.Children.Add(reset);
    }

    private static void OpenSettingsExternalLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
