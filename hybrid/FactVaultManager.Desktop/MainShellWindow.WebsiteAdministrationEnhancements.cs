using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteAdministrationInitialized;
    private DispatcherTimer? _websiteAdministrationTimer;
    private Button? _websiteMaintenanceButton;

    public void InitializeWebsiteAdministrationEnhancements()
    {
        if (_websiteAdministrationInitialized) return;
        _websiteAdministrationInitialized = true;
        _websiteAdministrationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _websiteAdministrationTimer.Tick += (_, _) => EnsureWebsiteAdministrationControls();
        _websiteAdministrationTimer.Start();
        Closed += (_, _) => _websiteAdministrationTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteAdministrationControls));
    }

    private void EnsureWebsiteAdministrationControls()
    {
        if (_websiteMaintenanceButton is not null)
        {
            _websiteAdministrationTimer?.Stop();
            return;
        }

        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null)
            return;

        var website = _autopilotNavButtons.TryGetValue("Website", out var websiteButton) ? websiteButton : null;
        if (website?.Parent is not Panel parent)
            return;

        _websiteMaintenanceButton = new Button
        {
            Content = "Maintenance: …",
            MinWidth = 126,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Turn website maintenance mode on or off",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            _websiteMaintenanceButton.Style = navStyle;
        _websiteMaintenanceButton.Click += async (_, _) => await ShowWebsiteMaintenanceDialogAsync();
        var index = parent.Children.IndexOf(website);
        parent.Children.Insert(Math.Clamp(index + 1, 0, parent.Children.Count), _websiteMaintenanceButton);
        _ = RefreshWebsiteMaintenanceStateAsync(false);
        _websiteAdministrationTimer?.Stop();
    }

    private async Task RefreshWebsiteMaintenanceStateAsync(bool showErrors)
    {
        if (_websiteMaintenanceButton is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            _websiteMaintenanceButton.Content = "Maintenance: unavailable";
            _websiteMaintenanceButton.IsEnabled = false;
            return;
        }
        try
        {
            using var client = new FactburstWebsiteAccessAdminClient();
            var settings = await client.GetMaintenanceAsync(tracker.BaseUrl, tracker.ApiKey);
            _websiteMaintenanceButton.Content = settings.Enabled ? "Maintenance: ON" : "Maintenance: Off";
            _websiteMaintenanceButton.IsEnabled = true;
        }
        catch (Exception error)
        {
            _websiteMaintenanceButton.Content = "Maintenance: error";
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ShowWebsiteMaintenanceDialogAsync()
    {
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Add the tracker API key in Settings first.", "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FactburstWebsiteMaintenanceSettings current;
        try
        {
            using var client = new FactburstWebsiteAccessAdminClient();
            current = await client.GetMaintenanceAsync(tracker.BaseUrl, tracker.ApiKey);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Website maintenance mode",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var enabled = new CheckBox
        {
            Content = "Put Factburst Quiz into maintenance mode",
            IsChecked = current.Enabled,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 14),
        };
        Grid.SetRow(enabled, 1);
        root.Children.Add(enabled);

        var messagePanel = new StackPanel();
        messagePanel.Children.Add(new TextBlock
        {
            Text = "Message shown to visitors",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        var message = new TextBox
        {
            Text = current.Message,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 105,
            MaxLength = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
        };
        messagePanel.Children.Add(message);
        messagePanel.Children.Add(new TextBlock
        {
            Text = "Visitors and normal users will only see this maintenance notice. Admin users can still enter the site and will see a maintenance banner across the top.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        Grid.SetRow(messagePanel, 2);
        root.Children.Add(messagePanel);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save", MinWidth = 90, MinHeight = 36, IsDefault = true };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Website Maintenance",
            Width = 610,
            Height = 430,
            MinWidth = 540,
            MinHeight = 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            try
            {
                save.IsEnabled = false;
                using var client = new FactburstWebsiteAccessAdminClient();
                await client.SetMaintenanceAsync(tracker.BaseUrl, tracker.ApiKey, enabled.IsChecked == true, message.Text);
                dialog.Close();
                await RefreshWebsiteMaintenanceStateAsync(false);
            }
            catch (Exception error)
            {
                save.IsEnabled = true;
                MessageBox.Show(dialog, error.Message, "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        dialog.ShowDialog();
        await RefreshWebsiteMaintenanceStateAsync(false);
    }
}
