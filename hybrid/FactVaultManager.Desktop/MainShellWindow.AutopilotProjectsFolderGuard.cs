using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool AutopilotProjectsFolderGuardRegistered = RegisterAutopilotProjectsFolderGuard();

    private static bool RegisterAutopilotProjectsFolderGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(AutopilotProjectsFolderButton_Click),
            handledEventsToo: true);
        return true;
    }

    private static void AutopilotProjectsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Tag?.ToString(), AutopilotFirstNavTag + ":Autopilot", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        var availability = window.GetProjectsFolderAvailability();
        if (availability.Ready)
            return;

        // The Autopilot home page reads release/project state from the configured project root.
        // Block the normal button handler before it changes tabs when that root is unavailable.
        e.Handled = true;
        window.ShowProjectsFolderConfigurationRequired(availability.Message);
    }

    internal ProjectsFolderAvailability GetProjectsFolderAvailability()
    {
        try
        {
            return ProjectsFolderConfigurationGuard.Check(_data.LoadSettings().ProjectsFolder);
        }
        catch (Exception error)
        {
            return new ProjectsFolderAvailability(
                false,
                "",
                "Projects Folder settings could not be read: " + error.Message);
        }
    }

    internal void ShowProjectsFolderConfigurationRequired(string? message = null)
    {
        var availability = GetProjectsFolderAvailability();
        var detail = string.IsNullOrWhiteSpace(message) ? availability.Message : message.Trim();
        if (string.IsNullOrWhiteSpace(detail))
            detail = "Set the Projects Folder in Settings before using Autopilot.";

        HeaderStatusText.Text = "Autopilot needs a valid Projects Folder";

        // Route after the current WPF event has unwound. This also makes the global dispatcher
        // fallback safe when it handles a legacy page callback that threw during navigation.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                try
                {
                    NavigateLegacy("Settings", "Settings");
                }
                catch
                {
                    // Settings remaining reachable is preferable to turning a configuration
                    // problem into another dispatcher exception.
                }
            }));

        try
        {
            MessageBox.Show(
                this,
                detail + "\n\nOpen Settings → General and choose the folder that contains your Factburst quiz project folders.",
                "Projects Folder Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
        }
    }
}
