using System.Windows;

namespace FactVaultManager.Desktop;

public partial class ProductionWindow
{
    private readonly AppUpdateService _updateService = new();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        try
        {
            var message = await _updateService.RunAsync(
                progress => Dispatcher.BeginInvoke(() =>
                {
                    CheckUpdatesButton.Content = $"Updating {progress}%";
                    UpdateStatusText.Text = $"Downloading update: {progress}%";
                })
            );
            UpdateStatusText.Text = $"Updates: {message}";
            AppendLog(message);
        }
        catch (Exception error)
        {
            UpdateStatusText.Text = "Updates: failed";
            AppendLog($"Update failed: {error.Message}");
        }
        finally
        {
            CheckUpdatesButton.Content = "Check for Updates";
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
