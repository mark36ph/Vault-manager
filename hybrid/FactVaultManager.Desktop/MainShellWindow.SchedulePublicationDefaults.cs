using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduleTimeToolTip = "Local time in 24-hour HH:mm format";
    private const string PreviousDefaultScheduleTime = "18:00";
    private const string DefaultScheduleTime = "09:00";
    private static readonly bool SchedulePublicationDefaultsRegistered = RegisterSchedulePublicationDefaults();

    private static bool RegisterSchedulePublicationDefaults()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplySchedulePublicationDefaults),
            handledEventsToo: true);
        return true;
    }

    private static void ApplySchedulePublicationDefaults(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || window.Owner is not MainShellWindow)
            return;

        var scheduleTime = FindScheduleTimeTextBox(window);
        if (scheduleTime is null)
            return;

        // Only replace the previous built-in default. Never overwrite a value that has
        // already been changed by another workflow or by future persisted preferences.
        if (string.Equals(scheduleTime.Text, PreviousDefaultScheduleTime, StringComparison.Ordinal))
            scheduleTime.Text = DefaultScheduleTime;
    }

    private static TextBox? FindScheduleTimeTextBox(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox textBox &&
                string.Equals(textBox.ToolTip?.ToString(), ScheduleTimeToolTip, StringComparison.Ordinal))
            {
                return textBox;
            }

            var nested = FindScheduleTimeTextBox(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
