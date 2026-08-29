using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteAdministrationInitialized;
    private DispatcherTimer? _websiteAdministrationTimer;
    private ComboBox? _websiteUserRoleComboBox;
    private Button? _websiteUserRoleButton;
    private Button? _websiteUserFriendsButton;
    private Button? _websiteMaintenanceButton;

    public void InitializeWebsiteAdministrationEnhancements()
    {
        if (_websiteAdministrationInitialized) return;
        _websiteAdministrationInitialized = true;
        InitializeWebsiteUsersPage();
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
        if (_websiteUserRoleButton is not null && _websiteMaintenanceButton is not null)
        {
            _websiteAdministrationTimer?.Stop();
            return;
        }
        if (_websiteUserDetailTitle?.Parent is not Grid detailHeader ||
            _websiteUserSearchBox?.Parent is not StackPanel headerActions ||
            _websiteUsersGrid is null)
            return;

        if (_websiteMaintenanceButton is null)
        {
            _websiteMaintenanceButton = new Button
            {
                Content = "Maintenance: …",
                MinWidth = 126,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Turn website maintenance mode on or off",
            };
            _websiteMaintenanceButton.Click += async (_, _) => await ShowWebsiteMaintenanceDialogAsync();
            headerActions.Children.Add(_websiteMaintenanceButton);
            _ = RefreshWebsiteMaintenanceStateAsync(false);
        }

        if (_websiteUserRoleButton is null)
        {
            if (detailHeader.ColumnDefinitions.Count == 1)
                detailHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var adminActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _websiteUserRoleComboBox = new ComboBox
            {
                MinWidth = 116,
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Website user group",
                IsEnabled = false,
            };
            _websiteUserRoleComboBox.Items.Add(new ComboBoxItem { Content = "User", Tag = "user" });
            _websiteUserRoleComboBox.Items.Add(new ComboBoxItem { Content = "Moderator", Tag = "moderator" });
            _websiteUserRoleComboBox.Items.Add(new ComboBoxItem { Content = "Admin", Tag = "admin" });
            _websiteUserRoleComboBox.SelectedIndex = 0;

            _websiteUserRoleButton = new Button
            {
                Content = "Set group",
                MinWidth = 92,
                MinHeight = 34,
                Margin = new Thickness(7, 0, 0, 0),
                IsEnabled = false,
            };
            _websiteUserRoleButton.Click += async (_, _) => await ApplySelectedWebsiteUserRoleAsync();

            _websiteUserFriendsButton = new Button
            {
                Content = "View friends",
                MinWidth = 102,
                MinHeight = 34,
                Margin = new Thickness(7, 0, 0, 0),
                IsEnabled = false,
            };
            _websiteUserFriendsButton.Click += async (_, _) => await ShowSelectedWebsiteUserFriendsAsync();

            adminActions.Children.Add(_websiteUserRoleComboBox);
            adminActions.Children.Add(_websiteUserRoleButton);
            adminActions.Children.Add(_websiteUserFriendsButton);
            Grid.SetColumn(adminActions, 1);
            detailHeader.Children.Add(adminActions);

            _websiteUsersGrid.SelectionChanged += async (_, _) => await LoadSelectedWebsiteUserAccessAsync(false);
            _ = LoadSelectedWebsiteUserAccessAsync(false);
        }

        _websiteAdministrationTimer?.Stop();
    }

    private async Task LoadSelectedWebsiteUserAccessAsync(bool showErrors)
    {
        var selected = _websiteUsersGrid?.SelectedItem as WebsiteUserAdminRow;
        var hasSelection = selected is not null;
        if (_websiteUserRoleComboBox is not null) _websiteUserRoleComboBox.IsEnabled = hasSelection;
        if (_websiteUserRoleButton is not null) _websiteUserRoleButton.IsEnabled = hasSelection;
        if (_websiteUserFriendsButton is not null) _websiteUserFriendsButton.IsEnabled = hasSelection;
        if (selected is null) return;

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;
        try
        {
            using var client = new FactburstWebsiteAccessAdminClient();
            var access = await client.GetUserAccessAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            SelectWebsiteRole(access.Role);
        }
        catch (Exception error)
        {
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ApplySelectedWebsiteUserRoleAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        var role = (_websiteUserRoleComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString()?.Trim().ToLowerInvariant() ?? "user";
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Add the tracker API key in Settings first.", "Website Users", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var label = role switch { "admin" => "Admin", "moderator" => "Moderator", _ => "User" };
        var confirmation = MessageBox.Show(
            this,
            $"Change {selected.Username} to the {label} group?\n\nAdmins can bypass maintenance mode. Moderators and admins can manage website comments.",
            "Change Website User Group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            if (_websiteUserRoleButton is not null) _websiteUserRoleButton.IsEnabled = false;
            using var client = new FactburstWebsiteAccessAdminClient();
            var updated = await client.SetUserRoleAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id, role);
            SelectWebsiteRole(updated.Role);
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = $"{selected.Username} is now in the {label} group.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_websiteUserRoleButton is not null) _websiteUserRoleButton.IsEnabled = true;
        }
    }

    private void SelectWebsiteRole(string role)
    {
        if (_websiteUserRoleComboBox is null) return;
        var normalized = (role ?? "user").Trim().ToLowerInvariant();
        foreach (var item in _websiteUserRoleComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                _websiteUserRoleComboBox.SelectedItem = item;
                return;
            }
        }
        _websiteUserRoleComboBox.SelectedIndex = 0;
    }

    private async Task ShowSelectedWebsiteUserFriendsAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Add the tracker API key in Settings first.", "Website Users", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var client = new FactburstWebsiteUserFriendsClient();
            var result = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            var rows = new List<WebsiteUserFriendAdminRow>();
            rows.AddRange(result.Friends.Select(friend => FriendRow(friend, "Friend")));
            rows.AddRange(result.Incoming.Select(friend => FriendRow(friend, "Incoming")));
            rows.AddRange(result.Outgoing.Select(friend => FriendRow(friend, "Sent")));

            var grid = BuildWebsiteUsersGrid();
            grid.MinHeight = 260;
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "User",
                Binding = new Binding(nameof(WebsiteUserFriendAdminRow.Username)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
            grid.Columns.Add(UserTextColumn("Relationship", nameof(WebsiteUserFriendAdminRow.Relationship), 110));
            grid.Columns.Add(UserTextColumn("Status", nameof(WebsiteUserFriendAdminRow.AccountStatus), 92));
            grid.Columns.Add(UserTextColumn("Since", nameof(WebsiteUserFriendAdminRow.Since), 145));
            grid.ItemsSource = rows;

            var root = new DockPanel { Margin = new Thickness(18) };
            var summary = new TextBlock
            {
                Text = $"{selected.Username} • {result.Friends.Count:N0} friend{(result.Friends.Count == 1 ? "" : "s")} • {result.Incoming.Count + result.Outgoing.Count:N0} pending request{(result.Incoming.Count + result.Outgoing.Count == 1 ? "" : "s")}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12),
            };
            DockPanel.SetDock(summary, Dock.Top);
            root.Children.Add(summary);
            root.Children.Add(WebsiteUsersCard(grid));

            var window = new Window
            {
                Owner = this,
                Title = $"{selected.Username} — Friends",
                Width = 720,
                Height = 470,
                MinWidth = 600,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = root,
            };
            window.ShowDialog();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        var title = new TextBlock
        {
            Text = "Website maintenance mode",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(title);

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
                if (_websiteUsersStatusText is not null)
                    _websiteUsersStatusText.Text = enabled.IsChecked == true ? "Website maintenance mode is ON." : "Website maintenance mode is off.";
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
