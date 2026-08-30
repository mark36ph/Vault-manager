using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool AutopilotMasterUiCopyRegistered = RegisterAutopilotMasterUiCopy();

    private static bool RegisterAutopilotMasterUiCopy()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(AutopilotMasterTextBlock_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void AutopilotMasterTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || Window.GetWindow(text) is not MainShellWindow)
            return;

        if (string.Equals(text.Text, "Generate + Fill Schedule", StringComparison.Ordinal))
        {
            text.Text = "Automatic channel production";
            return;
        }

        if (string.Equals(
                text.Text,
                "Autopilot chooses the category mix, renders the quizzes, schedules releases, prepares the website and tracking, creates promos and supervises post-release tasks.",
                StringComparison.Ordinal))
        {
            text.Text = "Turn Autopilot ON once. While the app is open it keeps the rolling quiz schedule full, uses performance signals to choose what to make next, prepares publishing and promotion, and supervises releases automatically.";
        }
    }
}
