using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteAdsSettingsInitialized;
    private DispatcherTimer? _websiteAdsSettingsTimer;

    public void InitializeWebsiteAdsSettings()
    {
        if (_websiteAdsSettingsInitialized) return;
        _websiteAdsSettingsInitialized = true;
        _websiteAdsSettingsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _websiteAdsSettingsTimer.Tick += (_, _) => EnsureWebsiteAdsSettingsButton();
        _websiteAdsSettingsTimer.Start();
        Closed += (_, _) => _websiteAdsSettingsTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteAdsSettingsButton));
    }

    private void EnsureWebsiteAdsSettingsButton()
    {
        if (_websiteSyncAllButton?.Parent is not StackPanel actions) return;
        if (actions.Children.OfType<Button>().Any(button => string.Equals(Convert.ToString(button.Content), "Ads settings", StringComparison.Ordinal)))
        {
            _websiteAdsSettingsTimer?.Stop();
            return;
        }

        var button = new Button
        {
            Content = "Ads settings",
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Configure optional non-intrusive Google AdSense side ads. Ads remain disabled until you turn them on.",
        };
        button.Click += async (_, _) => await OpenWebsiteAdsSettingsAsync();
        var websiteSettings = actions.Children.OfType<Button>()
            .FirstOrDefault(item => string.Equals(Convert.ToString(item.Content), "Website settings", StringComparison.Ordinal));
        var index = websiteSettings is null ? actions.Children.Count : actions.Children.IndexOf(websiteSettings);
        actions.Children.Insert(Math.Clamp(index, 0, actions.Children.Count), button);
        _websiteAdsSettingsTimer?.Stop();
    }

    private async Task OpenWebsiteAdsSettingsAsync()
    {
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", "Website Ads", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_websiteStatusText is not null) _websiteStatusText.Text = "Loading website ad settings…";
            using var client = new FactburstWebsiteAdsAdminClient();
            var current = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey);
            var dialog = new WebsiteAdsSettingsDialog(current) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                if (_websiteStatusText is not null) _websiteStatusText.Text = current.Enabled ? "Side ads are enabled." : "Side ads are disabled.";
                return;
            }

            if (_websiteStatusText is not null) _websiteStatusText.Text = "Saving website ad settings…";
            var saved = await client.SaveAsync(tracker.BaseUrl, tracker.ApiKey, dialog.Settings);
            if (_websiteStatusText is not null)
                _websiteStatusText.Text = saved.Enabled
                    ? "Google side ads enabled for wide-screen quiz pages. No popup, overlay or mobile ads are used."
                    : "Google side ads disabled. The quiz website will not load the AdSense script.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Ads", MessageBoxButton.OK, MessageBoxImage.Error);
            if (_websiteStatusText is not null) _websiteStatusText.Text = "Website ad settings could not be updated.";
        }
    }
}

internal sealed class WebsiteAdsSettingsDialog : Window
{
    private readonly CheckBox _enabled;
    private readonly TextBox _client;
    private readonly TextBox _leftSlot;
    private readonly TextBox _rightSlot;

    public WebsiteAdsSettingsDialog(FactburstWebsiteAdsSettings current)
    {
        Title = "Website Ads";
        Width = 560;
        Height = 500;
        MinWidth = 500;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31));
        FontFamily = new FontFamily("Segoe UI Variable Text");
        FontSize = 13;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock
        {
            Text = "Optional Google side ads",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Ads are disabled by default. When enabled, Factburst only shows simple side-rail display ads on wide quiz pages. No popups, overlays, anchors or mobile ad rails are added.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(heading);

        var form = new StackPanel();
        _enabled = new CheckBox
        {
            Content = "Enable Google AdSense side ads",
            IsChecked = current.Enabled,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        };
        form.Children.Add(_enabled);
        _client = AddField(form, "AdSense publisher ID", current.Client, "Example: ca-pub-1234567890123456");
        _leftSlot = AddField(form, "Left side ad slot", current.LeftSlot, "Digits from the AdSense display ad unit");
        _rightSlot = AddField(form, "Right side ad slot", current.RightSlot, "Optional second display ad unit slot");
        form.Children.Add(new TextBlock
        {
            Text = "You can configure the IDs now and leave Enable unchecked. The website will not request Google ads until you enable this setting.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        Grid.SetRow(form, 1);
        root.Children.Add(form);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 36, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", MinWidth = 90, Height = 36, IsDefault = true };
        save.Click += (_, _) => { DialogResult = true; };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Content = root;
    }

    public FactburstWebsiteAdsSettings Settings => new(
        _enabled.IsChecked == true,
        _client.Text.Trim(),
        _leftSlot.Text.Trim(),
        _rightSlot.Text.Trim());

    private static TextBox AddField(Panel parent, string label, string value, string hint)
    {
        parent.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        var box = new TextBox
        {
            Text = value,
            Height = 36,
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 0, 3),
        };
        parent.Children.Add(box);
        parent.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 13),
        });
        return box;
    }
}
