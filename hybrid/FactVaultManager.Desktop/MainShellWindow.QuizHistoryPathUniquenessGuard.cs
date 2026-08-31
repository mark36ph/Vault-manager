using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryPathUniquenessGuardRegistered = RegisterQuizHistoryPathUniquenessGuard();

    private static bool RegisterQuizHistoryPathUniquenessGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowQuizHistoryPathUniqueness_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowQuizHistoryPathUniqueness_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
            return;

        try
        {
            window._data.EnsureQuizHistoryProjectFolderUniquenessGuard();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Could not enable Quiz History path uniqueness guard: {error.Message}");
        }
    }
}
