using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryPerformanceHandlerRegistered = RegisterQuizHistoryPerformanceHandler();

    private static bool RegisterQuizHistoryPerformanceHandler()
    {
        // Configure DataGrids at load time, before any page can assign an
        // ItemsSource. This ensures virtualization is active before WPF measures rows.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowQuizHistoryPerformance_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowQuizHistoryPerformance_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
            ConfigureQuizHistoryGridPerformance(grid);
    }

    private static void ConfigureQuizHistoryGridPerformance(DataGrid grid)
    {
        // Quiz History can contain thousands of records. Configure virtualization
        // before ItemsSource is assigned so WPF realizes only visible rows.
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
        grid.RowHeight = 42;
    }
}
