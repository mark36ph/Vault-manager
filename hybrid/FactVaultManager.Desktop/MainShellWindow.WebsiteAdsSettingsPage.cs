using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteAdsSettingsPageInitialized;
    private bool _websiteAdsSettingsPageLoadHooked;

    public void InitializeWebsiteAdsSettingsPage()
    {
        if (_websiteAdsSettingsPageInitialized) return;

        if (!IsLoaded)
        {
            if (_websiteAdsSettingsPageLoadHooked) return;
            _websiteAdsSettingsPageLoadHooked = true;
            Loaded += (_, _) =>
            {
                _websiteAdsSettingsPageLoadHooked = false;
                InitializeWebsiteAdsSettingsPage();
            };
            return;
        }

        var settingsTabs = FindWebsiteAdsSettingsTabControl();
        if (settingsTabs is null) return;

        if (settingsTabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(Convert.ToString(item.Header), "Website Ads", StringComparison.Ordinal)))
        {
            _websiteAdsSettingsPageInitialized = true;
            return;
        }

        var tab = new TabItem
        {
            Header = "Website Ads",
            Content = BuildWebsiteAdsSettingsPage(),
        };
        if (TryFindResource("SectionTabStyle") is Style tabStyle)
            tab.Style = tabStyle;

        var saveTab = settingsTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(Convert.ToString(item.Header), "Save", StringComparison.Ordinal));
        var insertIndex = saveTab is null ? settingsTabs.Items.Count : settingsTabs.Items.IndexOf(saveTab);
        settingsTabs.Items.Insert(Math.Max(0, insertIndex), tab);
        _websiteAdsSettingsPageInitialized = true;
    }

    private TabControl? FindWebsiteAdsSettingsTabControl()
    {
        if (MainTabs.Items.Count < 6 || MainTabs.Items[5] is not TabItem settingsPage)
            return null;
        return FindWebsiteAdsDescendant<TabControl>(settingsPage.Content);
    }

    private static T? FindWebsiteAdsDescendant<T>(object? node) where T : DependencyObject
    {
        if (node is T match) return match;
        if (node is not DependencyObject dependency) return null;

        foreach (var child in LogicalTreeHelper.GetChildren(dependency))
        {
            var found = FindWebsiteAdsDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private FrameworkElement BuildWebsiteAdsSettingsPage()
    {
        var form = new StackPanel
        {
            Margin = new Thickness(4, 16, 4, 20),
            MaxWidth = 760,
        };
        form.Children.Add(new TextBlock
        {
            Text = "Website Ads",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        form.Children.Add(new TextBlock
        {
            Text = "Turn optional Google AdSense side ads on or off and enter your publisher/ad-unit details for the Factburst Quiz website.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 16),
        });

        var configure = new Button
        {
            Content = "Configure AdSense",
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Open the Website Ads controls to toggle ads and enter your AdSense publisher ID and ad slots.",
        };
        if (TryFindResource("PrimaryButton") is Style primaryStyle)
            configure.Style = primaryStyle;
        configure.Click += async (_, _) => await OpenWebsiteAdsSettingsAsync();
        form.Children.Add(configure);

        form.Children.Add(new TextBlock
        {
            Text = "Ads remain disabled unless you explicitly enable them. The current website layout only uses desktop side-rail ads; it does not add popup, overlay or mobile ad rails.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        });
        return form;
    }
}
