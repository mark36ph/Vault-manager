using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool AutopilotHomeLaunchGuardRegistered = RegisterAutopilotHomeLaunchGuard();

    private static bool RegisterAutopilotHomeLaunchGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(AutopilotHomeLaunchButton_Click),
            handledEventsToo: true);
        return true;
    }

    private static void AutopilotHomeLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            (!string.Equals(button.Content?.ToString(), "Generate + Fill Schedule", StringComparison.Ordinal) &&
             !string.Equals(button.Content?.ToString(), "Fill schedule now", StringComparison.Ordinal)) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        // Prepare the actual permanent Autopilot button before the home-page instance
        // handler searches the visual tree. This keeps the home CTA/manual override
        // reliable even when the Quiz workspace was last left elsewhere.
        window.NavigateLegacy("Quizzes", "Create");
        window.SelectQuizWorkspacePage("export");
    }
}
