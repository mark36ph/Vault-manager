using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryPerformanceHandlerRegistered = RegisterQuizHistoryPerformanceHandler();

    private static bool RegisterQuizHistoryPerformanceHandler()
    {
        // Configure the DataGrid at Initialized time, before Quiz History assigns its
        // ItemsSource. Waiting for MainShellWindow.Loaded was too late: WPF could
        // create/measure a large number of rows before virtualization was enabled.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.InitializedEvent,
            new RoutedEventHandler(MainShellWindowQuizHistoryPerformance_Initialized),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowQuizHistoryPerformance_Initialized(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid && grid.Columns.Count >= 10)
            ConfigureQuizHistoryGridPerformance(grid);
    }

    private static void ConfigureQuizHistoryGridPerformance(DataGrid grid)
    {
        // Quiz History can contain hundreds or thousands of records. Enable row and
        // column virtualization before ItemsSource is assigned so the first layout
        // only realizes the rows that are actually visible.
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
        grid.RowHeight = 42;
    }
}
