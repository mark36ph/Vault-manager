using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteUserProvisioningInitialized;
    private bool _websiteUserProvisioningSelectionHooked;
    private DispatcherTimer? _websiteUserProvisioningTimer;
    private Button? _websiteUserCreateAccountButton;
    private Button? _websiteUserActivateProfileButton;

    public void InitializeWebsiteUserProvisioningControls()
    {
        if (_websiteUserProvisioningInitialized) return;
        _websiteUserProvisioningInitialized = true;

        _websiteUserProvisioningTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _websiteUserProvisioningTimer.Tick += (_, _) => EnsureWebsiteUserProvisioningControls();
        _websiteUserProvisioningTimer.Start();
        Closed += (_, _) => _websiteUserProvisioningTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteUserProvisioningControls));
    }

    private void EnsureWebsiteUserProvisioningControls()
    {
        EnsureWebsiteUsersPage();

        if (_websiteUserCreateAccountButton is null && _websiteUserSearchBox?.Parent is StackPanel actions)
        {
            var create = new Button
            {
                Content = "Create account",
                MinWidth = 108,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Create a website account, including reserved Factburst/admin usernames",
            };
            create.Click += async (_, _) => await CreateWebsiteUserFromAdminAsync();
            var insertAt = Math.Max(0, actions.Children.Count - 1);
            actions.Children.Insert(insertAt, create);
            _websiteUserCreateAccountButton = create;
        }

        if (_websiteUserActivateProfileButton is null && _websiteUsersStatusText?.Parent is Grid footer)
        {
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var activate = new Button
            {
                Content = "Activate profile",
                MinWidth = 112,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = false,
                ToolTip = "Mark the selected user's email as verified without requiring the verification link",
            };
            activate.Click += async (_, _) => await ActivateSelectedWebsiteUserProfileAsync();
            Grid.SetColumn(activate, footer.ColumnDefinitions.Count - 1);
            footer.Children.Add(activate);
            _websiteUserActivateProfileButton = activate;
        }

        if (!_websiteUserProvisioningSelectionHooked && _websiteUsersGrid is not null)
        {
            _websiteUsersGrid.SelectionChanged += (_, _) => UpdateWebsiteUserProvisioningState();
            _websiteUserProvisioningSelectionHooked = true;
        }

        UpdateWebsiteUserProvisioningState();
        if (_websiteUserCreateAccountButton is not null && _websiteUserActivateProfileButton is not null)
            _websiteUserProvisioningTimer?.Stop();
    }

    private async Task CreateWebsiteUserFromAdminAsync()
    {
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Open Website settings and add the tracker API key first.", "Create Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WebsiteUserCreateDialog(this);
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (_websiteUserCreateAccountButton is not null) _websiteUserCreateAccountButton.IsEnabled = false;
            if (_websiteUserActivateProfileButton is not null) _websiteUserActivateProfileButton.IsEnabled = false;
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = $"Creating {dialog.Username}…";

            using var client = new FactburstWebsiteUserProvisioningClient();
            var created = await client.CreateUserAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                dialog.Username,
                dialog.Email,
                dialog.Password,
                dialog.ActivateImmediately);

            if (_websiteUserSearchBox is not null) _websiteUserSearchBox.Text = "";
            await RefreshWebsiteUsersAsync(false, created.UserId);
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = created.EmailVerified
                    ? $"Created and activated {created.Username}. Reserved usernames are allowed for admin-created accounts."
                    : $"Created {created.Username}. Email verification is still required.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Website Account", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_websiteUserCreateAccountButton is not null) _websiteUserCreateAccountButton.IsEnabled = true;
            UpdateWebsiteUserProvisioningState();
        }
    }

    private async Task ActivateSelectedWebsiteUserProfileAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        if (selected.Source.EmailVerified) return;

        if (MessageBox.Show(
                this,
                $"Activate '{selected.Username}' without waiting for email verification?\n\nThis marks the email as verified immediately. If the account is suspended it will also be returned to active status.",
                "Activate Website Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;

        try
        {
            if (_websiteUserActivateProfileButton is not null) _websiteUserActivateProfileButton.IsEnabled = false;
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = $"Activating {selected.Username}…";
            using var client = new FactburstWebsiteUserProvisioningClient();
            await client.ActivateUserAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            await RefreshWebsiteUsersAsync(false, selected.Id);
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = $"{selected.Username} activated. Their email is treated as verified and the profile is active.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Activate Website Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateWebsiteUserProvisioningState();
        }
    }

    private void UpdateWebsiteUserProvisioningState()
    {
        if (_websiteUserActivateProfileButton is null) return;
        _websiteUserActivateProfileButton.IsEnabled =
            _websiteUsersGrid?.SelectedItem is WebsiteUserAdminRow selected && !selected.Source.EmailVerified;
    }
}
