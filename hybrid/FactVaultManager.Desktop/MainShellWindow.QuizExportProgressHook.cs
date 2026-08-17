using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly bool _quizResolveProgressHookInitialized = InitializeQuizResolveProgressHook();
    private static int _quizResolveProgressHookRegistered;

    private static bool InitializeQuizResolveProgressHook()
    {
        if (Interlocked.Exchange(ref _quizResolveProgressHookRegistered, 1) != 0)
            return true;

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(QuizResolveExportButton_Clicked),
            handledEventsToo: true);
        return true;
    }

    private static void QuizResolveExportButton_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(Convert.ToString(button.Content)?.Trim(), "Create Resolve Quiz", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Window.GetWindow(button) is MainShellWindow owner)
            QuizResolveProgressCoordinator.Begin(owner);
    }
}
