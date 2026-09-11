using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _apiConnectionsWebsiteInitialized;
    private TextBox? _apiConnectionsTrackerBaseUrl;
    private PasswordBox? _apiConnectionsTrackerApiKey;
    private PasswordBox? _apiConnectionsSocialStatsApiKey;

    public void InitializeApiConnectionsWebsite()
    {
        if (_apiConnectionsWebsiteInitialized)
            return;

        InitializeApiConnectionsSettings();
        if (!_settingsPages.TryGetValue("connections", out var connectionsPage) ||
            connectionsPage is not ScrollViewer scrollViewer ||
            scrollViewer.Content is not StackPanel page)
        {
            return;
        }

        _apiConnectionsWebsiteInitialized = true;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        var socialReporting = FactburstSocialReportingSettingsStore.Load(_data.SettingsPath);

        var website = SettingsSection("Website & Link Tracker");
        var stack = (StackPanel)website.Child;
        stack.Children.Add(new TextBlock
        {
            Text = "Connect Factburst Quiz Manager to the website administration API. The desktop app uses the TRACKER_API_KEY stored as a secret on the Cloudflare tracker Worker.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10),
        });

        stack.Children.Add(SettingsFieldLabel("Tracker base URL"));
        _apiConnectionsTrackerBaseUrl = new TextBox
        {
            Text = tracker.BaseUrl.Length > 0 ? tracker.BaseUrl : FactburstTrackerSettingsStore.DefaultBaseUrl,
            Margin = new Thickness(0, 5, 0, 8),
        };
        stack.Children.Add(_apiConnectionsTrackerBaseUrl);

        _apiConnectionsTrackerApiKey = new PasswordBox { Password = tracker.ApiKey };
        AddApiCredentialRow(
            stack,
            "Website tracker API key (TRACKER_API_KEY)",
            _apiConnectionsTrackerApiKey,
            "website",
            TestWebsiteConnectionAsync,
            "The value must exactly match the TRACKER_API_KEY secret on the Cloudflare tracker Worker. It is encrypted when stored on this PC.");

        stack.Children.Add(SettingsFieldLabel("Website social reporting API key (SOCIAL_STATS_API_KEY)"));
        _apiConnectionsSocialStatsApiKey = new PasswordBox
        {
            Password = socialReporting.ApiKey,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var socialRow = new Grid
        {
            Margin = new Thickness(0, 5, 0, 2),
        };
        socialRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        socialRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_apiConnectionsSocialStatsApiKey, 0);
        socialRow.Children.Add(_apiConnectionsSocialStatsApiKey);
        var testSocial = new Button
        {
            Content = "Test Social Reporting",
            MinWidth = 145,
            Margin = new Thickness(8, 0, 0, 0),
        };
        testSocial.Click += TestSocialReportingConnection_Click;
        Grid.SetColumn(testSocial, 1);
        socialRow.Children.Add(testSocial);
        stack.Children.Add(socialRow);
        stack.Children.Add(new TextBlock
        {
            Text = "Dedicated desktop → website reporting secret. It is used only to send upload status and platform statistics to the Factburst website and is encrypted when stored on this PC.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10),
        });

        var backup = new Button
        {
            Content = "Back up API settings to Cloudflare",
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 4),
        };
        backup.Click += BackupApiSettingsToCloudflare_Click;
        stack.Children.Add(backup);
        stack.Children.Add(new TextBlock
        {
            Text = "Encrypted backup of the configured API credentials and connection identifiers. Existing backup values are replaced.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10),
        });

        var cloudflare = new Button
        {
            Content = "Open Cloudflare Dashboard",
            MinWidth = 158,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        cloudflare.Click += (_, _) => OpenSettingsExternalLink("https://dash.cloudflare.com/");
        stack.Children.Add(cloudflare);

        var insertIndex = page.Children.Count;
        for (var index = 0; index < page.Children.Count; index++)
        {
            if (page.Children[index] is Border border &&
                border.Child is StackPanel section &&
                section.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "Connection checks")
            {
                insertIndex = index;
                break;
            }
        }
        page.Children.Insert(insertIndex, website);

        SetConfiguredStatus("website", tracker.ApiKey);
        WireWebsiteTrackerSaveIntoUnifiedFooter(page);
    }

    private async void TestSocialReportingConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiConnectionsSocialStatsApiKey is null)
            return;

        try
        {
            var key = RequireApiValue(_apiConnectionsSocialStatsApiKey.Password, "Website social reporting API key");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, FactburstWebsiteSocialStatsClient.DefaultWebsiteBaseUrl + "/api/social/stats?test=1");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Social reporting returned HTTP {(int)response.StatusCode}: {body}");
            }

            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "Social reporting API key is working.";
            MessageBox.Show(this, "Social reporting connection successful. The SOCIAL_STATS_API_KEY is accepted by the Factburst website.", "Social Reporting Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "Social reporting test failed: " + FriendlyApiTestError(error);
            MessageBox.Show(this, error.Message, "Social Reporting Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BackupApiSettingsToCloudflare_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiConnectionsTrackerBaseUrl is null || _apiConnectionsTrackerApiKey is null)
            return;

        try
        {
            var trackerApiKey = RequireApiValue(_apiConnectionsTrackerApiKey.Password, "Website tracker API key");
            var baseUrl = RequireApiValue(_apiConnectionsTrackerBaseUrl.Text, "Website tracker base URL");
            FactburstTrackerSettingsStore.Save(_data.SettingsPath, baseUrl, trackerApiKey);
            if (_apiConnectionsSocialStatsApiKey is not null && _apiConnectionsSocialStatsApiKey.Password.Trim().Length >= 16)
                FactburstSocialReportingSettingsStore.Save(_data.SettingsPath, _apiConnectionsSocialStatsApiKey.Password);
            var settings = _data.LoadSettings();
            using var client = new FactburstApiSettingsBackupClient();
            await client.BackupAsync(trackerApiKey, settings, FactburstApiSettingsBackupClient.DefaultWebsiteBaseUrl);
            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "API settings were encrypted and backed up to Cloudflare.";
            MessageBox.Show(this, "The configured API settings were encrypted and backed up to Cloudflare successfully.", "Cloudflare API Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "API backup failed: " + FriendlyApiTestError(error);
            MessageBox.Show(this, error.Message, "Cloudflare API Backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WireWebsiteTrackerSaveIntoUnifiedFooter(StackPanel page)
    {
        var saveButton = page.Children.OfType<Grid>().SelectMany(grid => grid.Children.OfType<Button>()).FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Save API settings", StringComparison.Ordinal));
        if (saveButton is null)
            return;
        saveButton.Content = "Save API & website settings";
        saveButton.Click += SaveApiConnectionsWebsite_Click;
    }

    private void SaveApiConnectionsWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_apiConnectionsTrackerBaseUrl is null || _apiConnectionsTrackerApiKey is null)
            return;

        var apiKey = _apiConnectionsTrackerApiKey.Password.Trim();
        if (apiKey.Length == 0)
        {
            SetConfiguredStatus("website", "");
            return;
        }

        try
        {
            FactburstTrackerSettingsStore.Save(_data.SettingsPath, _apiConnectionsTrackerBaseUrl.Text, apiKey);
            if (_apiConnectionsSocialStatsApiKey is not null && _apiConnectionsSocialStatsApiKey.Password.Trim().Length >= 16)
                FactburstSocialReportingSettingsStore.Save(_data.SettingsPath, _apiConnectionsSocialStatsApiKey.Password);
            SetConfiguredStatus("website", apiKey);
            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "API and website settings saved.";
        }
        catch (Exception error)
        {
            if (_apiConnectionStatuses.TryGetValue("website", out var status))
                status.Text = "✕ " + FriendlyApiTestError(error);
            MessageBox.Show(this, error.Message, "Website Connection", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<string> TestWebsiteConnectionAsync()
    {
        var baseUrl = RequireApiValue(_apiConnectionsTrackerBaseUrl?.Text, "Website tracker base URL");
        var apiKey = RequireApiValue(_apiConnectionsTrackerApiKey?.Password, "Website tracker API key");
        var client = new FactburstLinkTrackerClient();
        var healthy = await client.HealthAsync(baseUrl);
        if (!healthy)
            throw new InvalidOperationException("The Factburst tracker health check did not report OK.");
        await client.FetchStatsAsync(baseUrl, apiKey);
        return "Working — Factburst website/tracker authenticated";
    }
}
