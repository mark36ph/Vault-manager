using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool ReleaseActionButtonVisualsRegistered = RegisterReleaseActionButtonVisuals();

    private static bool RegisterReleaseActionButtonVisuals()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ReleaseActionGrid_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void ReleaseActionGrid_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not DataGrid grid || Window.GetWindow(grid) is not MainShellWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => window.EnsureVisibleReleaseActionButtons(grid)));
    }

    private void EnsureVisibleReleaseActionButtons(DataGrid grid)
    {
        var actionColumn = grid.Columns.FirstOrDefault(column =>
            string.Equals(Convert.ToString(column.Header), "Next thing to fix", StringComparison.Ordinal) ||
            string.Equals(Convert.ToString(column.Header), "Next action", StringComparison.Ordinal));
        if (actionColumn is null)
            return;

        if (actionColumn is DataGridTemplateColumn &&
            string.Equals(Convert.ToString(actionColumn.Header), "Next action", StringComparison.Ordinal))
            return;

        var index = grid.Columns.IndexOf(actionColumn);
        grid.Columns.RemoveAt(index);
        grid.Columns.Insert(index, BuildVisibleScheduledReadinessActionColumn());
    }

    private DataGridTemplateColumn BuildVisibleScheduledReadinessActionColumn()
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ScheduledReleaseReadinessRow.NextAction)));
        button.SetBinding(FrameworkElement.TagProperty, new Binding());
        button.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
        button.SetValue(FrameworkElement.ToolTipProperty, "Open the workflow for this release task");
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 2, 5, 2));
        button.SetValue(FrameworkElement.MinHeightProperty, 26.0);
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(ScheduledReadinessFixRow_Click));

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(39, 74, 155))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 2, 10, 2)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var ready = new Trigger { Property = ContentControl.ContentProperty, Value = "Ready for release" };
        ready.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(28, 112, 72))));
        ready.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(70, 235, 115))));
        style.Triggers.Add(ready);

        button.SetValue(FrameworkElement.StyleProperty, style);

        return new DataGridTemplateColumn
        {
            Header = "Next action",
            CellTemplate = new DataTemplate { VisualTree = button },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 220,
        };
    }
}
