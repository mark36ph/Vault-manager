using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool InstalledUpdaterUiRegistered = RegisterInstalledUpdaterUi();

    private static bool RegisterInstalledUpdaterUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(InstalledUpdaterButton_Clicked),
            handledEventsToo: true);
        return true;
    }

    private static async void InstalledUpdaterButton_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(Convert.ToString(button.Content)?.Trim(), "Updates", StringComparison.OrdinalIgnoreCase) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        e.Handled = true;
        var installed = window._updates.IsInstalled;

        try
        {
            button.IsEnabled = false;
            window.HeaderStatusText.Text = "Checking latest Factburst Quiz Manager release...";

            bool started;
            if (installed)
            {
                started = await window._updates.InstallLatestSetupIfNewerAsync(percent => window.Dispatcher.Invoke(() =>
                    window.HeaderStatusText.Text = $"Downloading installer... {percent}%"));

                if (!started)
                {
                    button.IsEnabled = true;
                    var message = $"Factburst Quiz Manager {window._updates.CurrentVersion} is up to date.";
                    window.HeaderStatusText.Text = message;
                    MessageBox.Show(window, message, "Factburst Quiz Manager Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else
            {
                window.HeaderStatusText.Text = "Downloading Factburst Quiz Manager installer...";
                await window._updates.BootstrapInstallAsync(percent => window.Dispatcher.Invoke(() =>
                    window.HeaderStatusText.Text = $"Downloading installer... {percent}%"));
            }

            window.HeaderStatusText.Text = "Update downloaded. Restarting Factburst Quiz Manager...";
            Application.Current?.Shutdown();
        }
        catch (Exception error)
        {
            button.IsEnabled = true;
            window.HeaderStatusText.Text = $"Update failed: {error.Message}";
            MessageBox.Show(
                window,
                error.Message,
                installed ? "Update Failed" : "Install Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
