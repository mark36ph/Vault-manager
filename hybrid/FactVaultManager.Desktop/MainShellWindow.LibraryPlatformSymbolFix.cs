using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public static class LibraryPlatformSymbolPlanner
{
    public static string Symbol(string? status)
    {
        var value = (status ?? "").Trim();
        return value switch
        {
            "Posted" or "Uploaded" or "Public" => "✓",
            "Waiting" or "—" or "" => "—",
            _ => "✕",
        };
    }
}

public partial class MainShellWindow
{
    private bool _libraryPlatformSymbolFixInitialized;
    private int _libraryPlatformSymbolFixAttempts;
    private DispatcherTimer? _libraryPlatformSymbolFixRetryTimer;

    public void InitializeLibraryPlatformSymbolFix()
    {
        if (_libraryPlatformSymbolFixInitialized)
            return;

        _libraryPlatformSymbolFixInitialized = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyLibraryPlatformSymbolFix));
    }

    private void ApplyLibraryPlatformSymbolFix()
    {
        if (_quizHistoryGrid is null)
        {
            RetryLibraryPlatformSymbolFix();
            return;
        }

        if (!RebindLibrarySocialSymbolColumn("FB", LibraryReleasePlatformStatusField.Facebook) |
            !RebindLibrarySocialSymbolColumn("IG", LibraryReleasePlatformStatusField.Instagram))
        {
            RetryLibraryPlatformSymbolFix();
            return;
        }

        SetLibraryColumnWidth("Stage", 128);
        SetLibraryColumnWidth("Next action", 180);
    }

    private bool RebindLibrarySocialSymbolColumn(string header, LibraryReleasePlatformStatusField field)
    {
        if (_quizHistoryGrid?.Columns.FirstOrDefault(column =>
                string.Equals(column.Header?.ToString(), header, StringComparison.Ordinal)) is not DataGridTextColumn column)
        {
            return false;
        }

        column.Binding = new Binding
        {
            Converter = new LibraryReleasePlatformSymbolConverter(this, field),
        };
        column.Width = new DataGridLength(52);
        column.CanUserSort = false;
        column.ElementStyle = BuildLibraryPlatformSymbolStyle(field);
        return true;
    }

    private Style BuildLibraryPlatformSymbolStyle(LibraryReleasePlatformStatusField field)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 17d));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding
        {
            Converter = new LibraryReleasePlatformTooltipConverter(this, field),
        }));
        return style;
    }

    private void RetryLibraryPlatformSymbolFix()
    {
        if (++_libraryPlatformSymbolFixAttempts >= 50)
            return;

        _libraryPlatformSymbolFixRetryTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _libraryPlatformSymbolFixRetryTimer.Tick -= LibraryPlatformSymbolFixRetryTimer_Tick;
        _libraryPlatformSymbolFixRetryTimer.Tick += LibraryPlatformSymbolFixRetryTimer_Tick;
        _libraryPlatformSymbolFixRetryTimer.Start();
    }

    private void LibraryPlatformSymbolFixRetryTimer_Tick(object? sender, EventArgs e)
    {
        _libraryPlatformSymbolFixRetryTimer?.Stop();
        ApplyLibraryPlatformSymbolFix();
    }

    private string ResolveLibraryReleasePlatformStatusText(
        QuizHistorySummary history,
        LibraryReleasePlatformStatusField field)
    {
        var status = ResolveLibraryReleasePlatformStatus(history);
        return field switch
        {
            LibraryReleasePlatformStatusField.Facebook => status.Facebook,
            LibraryReleasePlatformStatusField.Instagram => status.Instagram,
            _ => status.YouTube,
        };
    }

    private sealed class LibraryReleasePlatformSymbolConverter(
        MainShellWindow owner,
        LibraryReleasePlatformStatusField field) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QuizHistorySummary history)
                return "—";
            return LibraryPlatformSymbolPlanner.Symbol(
                owner.ResolveLibraryReleasePlatformStatusText(history, field));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    private sealed class LibraryReleasePlatformTooltipConverter(
        MainShellWindow owner,
        LibraryReleasePlatformStatusField field) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QuizHistorySummary history)
                return "No publication state";
            var platform = field == LibraryReleasePlatformStatusField.Facebook ? "Facebook" : "Instagram";
            return platform + ": " + owner.ResolveLibraryReleasePlatformStatusText(history, field);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
