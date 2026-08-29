using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteAdsSettingsPageInitialized;
    private CheckBox? _settingsAdsEnabled;
    private TextBox? _settingsAdsClient;
    private TextBox? _settingsAdsLeftSlot;
    private TextBox? _settingsAdsRightSlot;
    private TextBlock? _settingsAdsStatus;
    private Button? _settingsAdsSaveButton;
    private Button? _settingsAdsRefreshButton;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeWebsiteAdsSettings();
        InitializeWebsiteAdsSettingsPage();
    }

    private void InitializeWebsiteAdsSettingsPage()
    {
        if (_websiteAdsSettingsPageInitialized) return;
        _websiteAdsSettingsPageInitialized = true;

        var settingsTabs = FindSettingsTabControl();
        if (settingsTabs is null)
        {
            _websiteAdsSettingsPageInitialized = false;
            Dispatcher.BeginInvoke(new Action(InitializeWebsiteAdsSettingsPage));
            return;
        }

        if (settingsTabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(Convert.ToString(item.Header), "Website Ads", StringComparison.Ordinal)))
        {
            return;
        }

        var tab = new TabItem { Header = "Website Ads" };
        if (TryFindResource("SectionTabStyle") is Style tabStyle)
            tab.Style = tabStyle;
        tab.Content = BuildWebsiteAdsSettingsContent();

        var saveTab = settingsTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(Convert.ToString(item.Header), "Save", StringComparison.Ordinal));
        var insertIndex = saveTab is null ? settingsTabs.Items.Count : settingsTabs.Items.IndexOf(saveTab);
        settingsTabs.Items.Insert(Math.Max(0, insertIndex), tab);

        _ = LoadWebsiteAdsSettingsPageAsync();
    }

    private TabControl? FindSettingsTabControl()
    {
        if (MainTabs.Items.Count < 6 || MainTabs.Items[5] is not TabItem settingsPage)
            return null;
        return FindLogicalDescendant<TabControl>(settingsPage.Content);
    }

    private static T? FindLogicalDescendant<T>(object? node) where T : DependencyObject
    {
        if (node is T match) return match;
        if (node is not DependencyObject dependency) return null;

        foreach (var child in LogicalTreeHelper.GetChildren(dependency))
        {
            var found = FindLogicalDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private FrameworkElement BuildWebsiteAdsSettingsContent()
    {
        var form = new StackPanel { Margin = new Thickness(4, 16, 4, 20), MaxWidth = 780 };
        form.Children.Add(new TextBlock
        {
            Text = "Website Ads",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        form.Children.Add(new TextBlock
        {
            Text = "Control optional Google AdSense side ads on Factburst Quiz. Ads stay disabled until you turn them on here.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 18),
        });

        _settingsAdsEnabled = new CheckBox
        {
            Content = "Enable Google AdSense side ads on wide-screen quiz pages",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        };
        form.Children.Add(_settingsAdsEnabled);

        _settingsAdsClient = AddAdsField(
            form,
            "AdSense publisher ID or AdSense code",
            "Paste ca-pub-1234567890123456, or paste an AdSense script/ad-unit snippet and FactVaultManager will extract the publisher ID.",
            multiline: true);
        _settingsAdsLeftSlot = AddAdsField(
            form,
            "Left side ad slot",
            "Digits from the AdSense display ad unit. If you paste an ad-unit snippet above, its data-ad-slot is used here when this field is empty.");
        _settingsAdsRightSlot = AddAdsField(
            form,
            "Right side ad slot",
            "Optional second display ad-unit slot. Leave blank if you only want one side rail.");

        form.Children.Add(new TextBlock
        {
            Text = "The website only loads the AdSense script when ads are enabled and configured. The current layout uses side rails on quiz pages at desktop widths; it does not add popup, overlay or mobile ad rails.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16),
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _settingsAdsSaveButton = new Button
        {
            Content = "Save website ads",
            MinWidth = 132,
        };
        if (TryFindResource("PrimaryButton") is Style primaryStyle)
            _settingsAdsSaveButton.Style = primaryStyle;
        _settingsAdsSaveButton.Click += async (_, _) => await SaveWebsiteAdsSettingsPageAsync();

        _settingsAdsRefreshButton = new Button
        {
            Content = "Refresh",
            MinWidth = 86,
        };
        _settingsAdsRefreshButton.Click += async (_, _) => await LoadWebsiteAdsSettingsPageAsync();
        actions.Children.Add(_settingsAdsSaveButton);
        actions.Children.Add(_settingsAdsRefreshButton);
        form.Children.Add(actions);

        _settingsAdsStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        form.Children.Add(_settingsAdsStatus);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = form,
        };
    }

    private static TextBox AddAdsField(Panel parent, string label, string hint, bool multiline = false)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
        });

        var box = new TextBox
        {
            MinHeight = multiline ? 70 : 34,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
        parent.Children.Add(box);
        parent.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 13),
        });
        return box;
    }

    private async Task LoadWebsiteAdsSettingsPageAsync()
    {
        if (_settingsAdsStatus is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            SetWebsiteAdsFieldsEnabled(false);
            _settingsAdsStatus.Text = "Configure Settings → Link Tracker first, then return here to load and save website ad settings.";
            return;
        }

        try
        {
            SetWebsiteAdsFieldsEnabled(false);
            _settingsAdsStatus.Text = "Loading website ad settings…";
            using var client = new FactburstWebsiteAdsAdminClient();
            var current = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey);
            if (_settingsAdsEnabled is not null) _settingsAdsEnabled.IsChecked = current.Enabled;
            if (_settingsAdsClient is not null) _settingsAdsClient.Text = current.Client;
            if (_settingsAdsLeftSlot is not null) _settingsAdsLeftSlot.Text = current.LeftSlot;
            if (_settingsAdsRightSlot is not null) _settingsAdsRightSlot.Text = current.RightSlot;
            _settingsAdsStatus.Text = current.Enabled
                ? "Website side ads are enabled."
                : "Website side ads are currently disabled.";
        }
        catch (Exception error)
        {
            _settingsAdsStatus.Text = error.Message;
        }
        finally
        {
            SetWebsiteAdsFieldsEnabled(true);
        }
    }

    private async Task SaveWebsiteAdsSettingsPageAsync()
    {
        if (_settingsAdsStatus is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            _settingsAdsStatus.Text = "Configure Settings → Link Tracker first.";
            return;
        }

        try
        {
            SetWebsiteAdsFieldsEnabled(false);
            _settingsAdsStatus.Text = "Saving website ad settings…";

            var rawClient = _settingsAdsClient?.Text.Trim() ?? "";
            var clientId = ExtractAdsenseClient(rawClient);
            var leftSlot = _settingsAdsLeftSlot?.Text.Trim() ?? "";
            var rightSlot = _settingsAdsRightSlot?.Text.Trim() ?? "";
            var snippetSlot = ExtractAdsenseSlot(rawClient);
            if (leftSlot.Length == 0 && snippetSlot.Length > 0)
                leftSlot = snippetSlot;

            if (rawClient.Length > 0 && clientId.Length == 0)
                throw new InvalidOperationException("Paste a valid AdSense publisher ID (ca-pub-…) or AdSense code containing one.");
            if (leftSlot.Length > 0 && !Regex.IsMatch(leftSlot, @"^\d{4,20}$"))
                throw new InvalidOperationException("Left AdSense slot must contain digits only.");
            if (rightSlot.Length > 0 && !Regex.IsMatch(rightSlot, @"^\d{4,20}$"))
                throw new InvalidOperationException("Right AdSense slot must contain digits only.");

            var enabled = _settingsAdsEnabled?.IsChecked == true;
            if (enabled && (clientId.Length == 0 || (leftSlot.Length == 0 && rightSlot.Length == 0)))
                throw new InvalidOperationException("Add an AdSense publisher ID and at least one ad-slot ID before enabling ads.");

            using var admin = new FactburstWebsiteAdsAdminClient();
            var saved = await admin.SaveAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                new FactburstWebsiteAdsSettings(enabled, clientId, leftSlot, rightSlot));

            if (_settingsAdsEnabled is not null) _settingsAdsEnabled.IsChecked = saved.Enabled;
            if (_settingsAdsClient is not null) _settingsAdsClient.Text = saved.Client;
            if (_settingsAdsLeftSlot is not null) _settingsAdsLeftSlot.Text = saved.LeftSlot;
            if (_settingsAdsRightSlot is not null) _settingsAdsRightSlot.Text = saved.RightSlot;
            _settingsAdsStatus.Text = saved.Enabled
                ? "Saved. Google AdSense side ads are enabled on wide-screen quiz pages."
                : "Saved. Website ads are disabled and the quiz site will not load the AdSense script.";
        }
        catch (Exception error)
        {
            _settingsAdsStatus.Text = error.Message;
        }
        finally
        {
            SetWebsiteAdsFieldsEnabled(true);
        }
    }

    private void SetWebsiteAdsFieldsEnabled(bool enabled)
    {
        if (_settingsAdsEnabled is not null) _settingsAdsEnabled.IsEnabled = enabled;
        if (_settingsAdsClient is not null) _settingsAdsClient.IsEnabled = enabled;
        if (_settingsAdsLeftSlot is not null) _settingsAdsLeftSlot.IsEnabled = enabled;
        if (_settingsAdsRightSlot is not null) _settingsAdsRightSlot.IsEnabled = enabled;
        if (_settingsAdsSaveButton is not null) _settingsAdsSaveButton.IsEnabled = enabled;
        if (_settingsAdsRefreshButton is not null) _settingsAdsRefreshButton.IsEnabled = enabled;
    }

    private static string ExtractAdsenseClient(string text)
    {
        var match = Regex.Match(text ?? "", @"ca-pub-\d{10,24}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToLowerInvariant() : "";
    }

    private static string ExtractAdsenseSlot(string text)
    {
        var match = Regex.Match(text ?? "", "data-ad-slot\\s*=\\s*[\\\"'](?<slot>\\d{4,20})[\\\"']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["slot"].Value : "";
    }
}
