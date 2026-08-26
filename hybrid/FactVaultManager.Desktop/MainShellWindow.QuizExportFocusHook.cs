using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizExportFocusHookRegistered = RegisterQuizExportFocusHook();

    private static bool RegisterQuizExportFocusHook()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(QuizResolveExportFocusButton_Click),
            handledEventsToo: true);
        return true;
    }

    private static void QuizResolveExportFocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !IsQuizFinalRenderActionButton(button) ||
            Window.GetWindow(button) is not MainShellWindow window)
            return;

        var sawProgress = QuizResolveProgressCoordinator.IsActive;
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        var checks = 0;
        timer.Tick += (_, _) =>
        {
            checks++;
            sawProgress |= QuizResolveProgressCoordinator.IsActive;
            if ((!sawProgress && checks < 8) || QuizResolveProgressCoordinator.IsActive)
                return;

            timer.Stop();
            if (!window.IsLoaded || !window.IsVisible)
                return;

            window.Activate();
            window.Focus();
        };
        timer.Start();
    }
}
