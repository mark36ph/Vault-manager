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
            Window.GetWindow(button) is not MainShellWindow window ||
            window._updates.IsInstalled)
        {
            return;
        }

        e.Handled = true;
        var answer = MessageBox.Show(
            window,
            "This copy is running from the development/source folder rather than the Windows installer.\n\n" +
            "Install the current signed Factburst Quiz Manager release now?\n\n" +
            "After this one-time install, the Updates button will download and apply future versions automatically.",
            "Install Factburst Quiz Manager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            button.IsEnabled = false;
            window.HeaderStatusText.Text = "Downloading Factburst Quiz Manager installer...";
            await window._updates.BootstrapInstallAsync(percent => window.Dispatcher.Invoke(() =>
                window.HeaderStatusText.Text = $"Downloading installer... {percent}%"));

            window.HeaderStatusText.Text = "Installer started. Closing development copy...";
            Application.Current?.Shutdown();
        }
        catch (Exception error)
        {
            button.IsEnabled = true;
            window.HeaderStatusText.Text = $"Install failed: {error.Message}";
            MessageBox.Show(
                window,
                error.Message,
                "Install Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
