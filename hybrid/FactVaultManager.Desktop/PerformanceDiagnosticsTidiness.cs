using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

internal static class PerformanceDiagnosticsTidiness
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainShellLoaded));
    }

    private static void OnMainShellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
            return;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };

        var attempts = 0;
        timer.Tick += (_, _) =>
        {
            attempts++;
            if (Tidy(window) || attempts >= 10)
                timer.Stop();
        };
        timer.Start();
    }

    private static bool Tidy(MainShellWindow window)
    {
        var actionLabels = new[]
        {
            "Scan app now",
            "Benchmark navigation",
            "Run full performance profile",
            "Profile next startup",
            "Write report now",
            "Open diagnostics folder",
        };

        var buttons = FindVisualChildren<Button>(window)
            .Where(button => actionLabels.Contains(button.Content?.ToString() ?? string.Empty, StringComparer.Ordinal))
            .ToList();

        if (buttons.Count == 0)
            return false;

        var parent = buttons[0].Parent as Panel;
        if (parent is null || buttons.Any(button => button.Parent != parent))
            return false;

        var actionButtons = buttons
            .OrderBy(button => Array.IndexOf(actionLabels, button.Content?.ToString() ?? string.Empty))
            .ToList();

        var existingWrap = parent.Children.OfType<WrapPanel>().FirstOrDefault(panel =>
            panel.Tag is string tag && tag == "PerformanceDiagnosticsActions");

        if (existingWrap is null)
        {
            existingWrap = new WrapPanel
            {
                Tag = "PerformanceDiagnosticsActions",
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2),
            };

            var firstIndex = parent.Children.IndexOf(actionButtons[0]);
            foreach (var button in actionButtons)
                parent.Children.Remove(button);

            parent.Children.Insert(Math.Max(0, firstIndex), existingWrap);
        }

        foreach (var button in actionButtons)
        {
            if (button.Parent != existingWrap)
            {
                (button.Parent as Panel)?.Children.Remove(button);
                existingWrap.Children.Add(button);
            }

            button.MinWidth = button.Content?.ToString() == "Run full performance profile" ? 225 : 180;
            button.MinHeight = 34;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Padding = new Thickness(12, 5, 12, 5);
        }

        var results = FindVisualChildren<TextBox>(window)
            .FirstOrDefault(textBox => textBox.IsReadOnly &&
                                       textBox.AcceptsReturn &&
                                       string.Equals(textBox.FontFamily?.Source, "Consolas", StringComparison.OrdinalIgnoreCase));

        if (results is not null)
        {
            results.MinHeight = 360;
            results.Margin = new Thickness(0, 4, 0, 0);
            results.Padding = new Thickness(10);
        }

        var status = FindVisualChildren<TextBlock>(window)
            .FirstOrDefault(textBlock =>
                textBlock.Text.StartsWith("Performance diagnostics", StringComparison.OrdinalIgnoreCase) ||
                textBlock.Text.StartsWith("Full profile", StringComparison.OrdinalIgnoreCase) ||
                textBlock.Text.StartsWith("Scan complete", StringComparison.OrdinalIgnoreCase) ||
                textBlock.Text.StartsWith("Navigation benchmark", StringComparison.OrdinalIgnoreCase));

        if (status is not null)
        {
            status.Margin = new Thickness(0, 2, 0, 8);
            status.MaxWidth = 900;
        }

        return true;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
