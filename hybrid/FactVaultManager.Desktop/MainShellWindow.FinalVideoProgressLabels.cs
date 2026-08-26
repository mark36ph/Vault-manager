using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool FinalVideoProgressLabelsRegistered = RegisterFinalVideoProgressLabels();

    private static bool RegisterFinalVideoProgressLabels()
    {
        EventManager.RegisterClassHandler(
            typeof(QuizResolveProgressWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FinalVideoProgressWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void FinalVideoProgressWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not QuizResolveProgressWindow window)
            return;

        window.Title = "Render Final Video";
        RewriteFinalVideoProgressLabels(window);

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        timer.Tick += (_, _) =>
        {
            if (!window.IsLoaded)
            {
                timer.Stop();
                return;
            }
            RewriteFinalVideoProgressLabels(window);
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void RewriteFinalVideoProgressLabels(DependencyObject root)
    {
        foreach (var text in FinalVideoProgressDescendants<TextBlock>(root))
        {
            text.Text = text.Text switch
            {
                "Creating Resolve quiz" => "Rendering final quiz video",
                "Preparing quiz export…" => "Preparing final video…",
                "Rendering and packaging…" => "Rendering final video…",
                "Finalizing Resolve package…" => "Finishing final video…",
                "Quiz export complete" => "Final video complete",
                _ => text.Text,
            };
        }
    }

    private static IEnumerable<T> FinalVideoProgressDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var nested in FinalVideoProgressDescendants<T>(child))
                yield return nested;
        }
    }
}
