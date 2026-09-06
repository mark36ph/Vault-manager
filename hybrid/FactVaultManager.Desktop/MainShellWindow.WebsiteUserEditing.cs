using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool WebsiteUserEditingHandlerRegistered = RegisterWebsiteUserEditingHandler();
    private DispatcherTimer? _websiteUserEditingTimer;
    private Button? _websiteUserEditButton;
    private bool _websiteUserEditingSelectionHooked;

    private static bool RegisterWebsiteUserEditingHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowWebsiteUserEditing_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowWebsiteUserEditing_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
            window.InitializeWebsiteUserEditingControls();
    }

    private void InitializeWebsiteUserEditingControls()
    {
        if (_websiteUserEditingTimer is not null) return;

        _websiteUserEditingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _websiteUserEditingTimer.Tick += (_, _) => EnsureWebsiteUserEditingControls();
        _websiteUserEditingTimer.Start();
        Closed += (_, _) => _websiteUserEditingTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteUserEditingControls));
    }

    private void EnsureWebsiteUserEditingControls()
    {
        if (_websiteUserEditButton is null && _websiteUserDeleteButton?.Parent is Grid footer)
        {
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var edit = new Button
            {
                Content = "Edit account",
                MinWidth = 108,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Change the selected user's username, email address or password",
                IsEnabled = false,
            };
            edit.Click += async (_, _) => await EditSelectedWebsiteUserAsync();
            Grid.SetColumn(edit, footer.ColumnDefinitions.Count - 1);
            footer.Children.Add(edit);
            _websiteUserEditButton = edit;
        }

        if (!_websiteUserEditingSelectionHooked && _websiteUsersGrid is not null)
        {
            _websiteUsersGrid.SelectionChanged += (_, _) => UpdateWebsiteUserEditingState();
            _websiteUserEditingSelectionHooked = true;
        }

        UpdateWebsiteUserEditingState();
        if (_websiteUserEditButton is not null)
            _websiteUserEditingTimer?.Stop();
    }

    private void UpdateWebsiteUserEditingState()
    {
        if (_websiteUserEditButton is null) return;
        _websiteUserEditButton.IsEnabled = _websiteUsersGrid?.SelectedItem is WebsiteUserAdminRow;
    }

    private async Task EditSelectedWebsiteUserAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Open Website settings and add the tracker API key first.", "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WebsiteUserEditDialog(this, selected.Username, selected.Email);
        if (dialog.ShowDialog() != true) return;

        var emailChanged = !string.Equals(selected.Email.Trim(), dialog.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        var passwordChanged = dialog.Password.Length > 0;
        var usernameChanged = !string.Equals(selected.Username.Trim(), dialog.Username.Trim(), StringComparison.Ordinal);

        if (!emailChanged && !passwordChanged && !usernameChanged)
            return;

        if (MessageBox.Show(
                this,
                BuildWebsiteUserEditConfirmation(selected, emailChanged, passwordChanged, usernameChanged),
                "Confirm Account Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            if (_websiteUserEditButton is not null) _websiteUserEditButton.IsEnabled = false;
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = $"Updating {selected.Username}…";

            using var client = new FactburstWebsiteUserEditingClient();
            await client.UpdateUserAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                selected.Id,
                dialog.Username,
                dialog.Email,
                passwordChanged ? dialog.Password : null);

            await RefreshWebsiteUsersAsync(false, selected.Id);
            if (_websiteUsersStatusText is not null)
            {
                var note = emailChanged
                    ? " The new email must be verified before the account can use verified-only features."
                    : passwordChanged
                        ? " All existing website sessions were signed out because the password changed."
                        : "";
                _websiteUsersStatusText.Text = $"{dialog.Username} updated successfully.{note}";
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateWebsiteUserEditingState();
        }
    }

    private static string BuildWebsiteUserEditConfirmation(
        WebsiteUserAdminRow selected,
        bool emailChanged,
        bool passwordChanged,
        bool usernameChanged)
    {
        var changes = new List<string>();
        if (usernameChanged) changes.Add("username");
        if (emailChanged) changes.Add("email address");
        if (passwordChanged) changes.Add("password");

        var text = $"Save changes to '{selected.Username}'?\n\nChanging: {string.Join(", ", changes)}.";
        if (emailChanged)
            text += "\n\nChanging the email will clear its current verification so the new address must be verified.";
        if (passwordChanged)
            text += "\n\nChanging the password will sign the user out of all existing website sessions.";
        return text;
    }
}
