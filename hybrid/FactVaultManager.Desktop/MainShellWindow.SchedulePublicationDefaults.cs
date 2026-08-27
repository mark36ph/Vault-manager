using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public static class QuizScheduleDatePlanner
{
    public static DateTime FindNextOpenDate(
        IEnumerable<QuizHistorySummary> histories,
        DateTime startDate,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(histories);

        var occupiedDates = histories
            .SelectMany(history => new[]
            {
                ParseFutureSchedule(history.YouTubeScheduledFor, now),
                ParseFutureSchedule(history.FacebookScheduledFor, now),
            })
            .Where(schedule => schedule.HasValue)
            .Select(schedule => schedule!.Value.LocalDateTime.Date)
            .ToHashSet();

        var candidate = startDate.Date;
        while (occupiedDates.Contains(candidate))
            candidate = candidate.AddDays(1);

        return candidate;
    }

    private static DateTimeOffset? ParseFutureSchedule(string? value, DateTimeOffset now) =>
        DateTimeOffset.TryParse(
            (value ?? "").Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var scheduled) && scheduled > now
            ? scheduled
            : null;
}

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
        if (sender is not Window window || window.Owner is not MainShellWindow owner)
            return;

        var scheduleTime = FindScheduleTimeTextBox(window);
        if (scheduleTime is null)
            return;

        // Only replace the previous built-in default. Never overwrite a value that has
        // already been changed by another workflow or by future persisted preferences.
        if (string.Equals(scheduleTime.Text, PreviousDefaultScheduleTime, StringComparison.Ordinal))
            scheduleTime.Text = DefaultScheduleTime;

        var scheduleDate = FindScheduleDatePicker(scheduleTime);
        var builtInStartDate = DateTime.Today.AddDays(1);
        if (scheduleDate?.SelectedDate?.Date == builtInStartDate)
        {
            scheduleDate.SelectedDate = QuizScheduleDatePlanner.FindNextOpenDate(
                owner._data.GetQuizHistory(),
                builtInStartDate,
                DateTimeOffset.Now);
        }
    }

    private static DatePicker? FindScheduleDatePicker(DependencyObject scheduleTime)
    {
        var parent = VisualTreeHelper.GetParent(scheduleTime);
        if (parent is null)
            return null;

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualTreeHelper.GetChild(parent, index) is DatePicker datePicker)
                return datePicker;
        }

        return null;
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
