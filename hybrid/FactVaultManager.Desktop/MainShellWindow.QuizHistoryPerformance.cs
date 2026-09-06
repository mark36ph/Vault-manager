using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryPerformanceHandlerRegistered = RegisterQuizHistoryPerformanceHandler();

    private static bool RegisterQuizHistoryPerformanceHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowQuizHistoryPerformance_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowQuizHistoryPerformance_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
            window.ConfigureQuizHistoryGridPerformance();
    }

    private void ConfigureQuizHistoryGridPerformance()
    {
        var grid = _quizHistoryGrid;
        if (grid is null)
            return;

        // Quiz History can contain thousands of records. Make virtualization explicit
        // so changing tabs does not create and measure every row at once.
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
    }
}
