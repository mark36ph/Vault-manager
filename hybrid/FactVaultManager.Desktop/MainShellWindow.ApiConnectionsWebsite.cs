using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _apiConnectionsWebsiteInitialized;
    private TextBox? _apiConnectionsTrackerBaseUrl;
    private PasswordBox? _apiConnectionsTrackerApiKey;

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

        var website = SettingsSection("Website & Link Tracker");
        var stack = (StackPanel)website.Child;
        stack.Children.Add(new TextBlock
        {
            Text = "Connect Factburst Quiz Manager to the website administration API. The desktop app uses the same TRACKER_API_KEY stored as a secret on the Cloudflare tracker Worker.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10),
        });

        stack.Children.Add(SettingsFieldLabel("Tracker base URL"));
        _apiConnectionsTrackerBaseUrl = new TextBox
        {
            Text = tracker.BaseUrl.Length > 0
                ? tracker.BaseUrl
                : FactburstTrackerSettingsStore.DefaultBaseUrl,
            Margin = new Thickness(0, 5, 0, 8),
        };
        stack.Children.Add(_apiConnectionsTrackerBaseUrl);

        _apiConnectionsTrackerApiKey = new PasswordBox
        {
            Password = tracker.ApiKey,
        };
        AddApiCredentialRow(
            stack,
            "Website tracker API key (TRACKER_API_KEY)",
            _apiConnectionsTrackerApiKey,
            "website",
            TestWebsiteConnectionAsync,
            "The value must exactly match the TRACKER_API_KEY secret on the Cloudflare tracker Worker. It is encrypted when stored on this PC.");

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

    private async void BackupApiSettingsToCloudflare_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiConnectionsTrackerBaseUrl is null || _apiConnectionsTrackerApiKey is null)
            return;

        try
        {
            var trackerApiKey = RequireApiValue(_apiConnectionsTrackerApiKey.Password, "Website tracker API key");
            var baseUrl = RequireApiValue(_apiConnectionsTrackerBaseUrl.Text, "Website tracker base URL");
            FactburstTrackerSettingsStore.Save(_data.SettingsPath, baseUrl, trackerApiKey);
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
        var saveButton = page.Children
            .OfType<Grid>()
            .SelectMany(grid => grid.Children.OfType<Button>())
            .FirstOrDefault(button => string.Equals(
                button.Content?.ToString(),
                "Save API settings",
                StringComparison.Ordinal));
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
            FactburstTrackerSettingsStore.Save(
                _data.SettingsPath,
                _apiConnectionsTrackerBaseUrl.Text,
                apiKey);
            SetConfiguredStatus("website", apiKey);
            if (_settingsPageStatus is not null)
                _settingsPageStatus.Text = "API and website settings saved.";
        }
        catch (Exception error)
        {
            if (_apiConnectionStatuses.TryGetValue("website", out var status))
                status.Text = "✕ " + FriendlyApiTestError(error);
            MessageBox.Show(
                this,
                error.Message,
                "Website Connection",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
