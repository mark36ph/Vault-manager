using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // The compact Autopilot navigation can be displayed before the deferred legacy
    // navigation buttons have finished being wired. Intercept its Library button at
    // the Button class-handler stage so the old handler cannot show a false "still
    // starting" message before we initialise the page.
    private static readonly bool AutopilotLegacyNavigationFixRegistered = RegisterAutopilotLegacyNavigationFix();

    private static bool RegisterAutopilotLegacyNavigationFix()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(HandleAutopilotLegacyNavigationClick));
        return true;
    }

    private static void HandleAutopilotLegacyNavigationClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button ||
            button.Tag?.ToString() is not string tag ||
            !tag.StartsWith("autopilot-first-nav:", StringComparison.Ordinal))
            return;

        if (button.Tag?.ToString() is not "autopilot-first-nav:Library")
            return;

        var window = FindParent<MainShellWindow>(button);
        if (window is null || window.MainTabs is null)
            return;

        // Library is the Quiz History page. Initialise it on demand if the deferred
        // startup phase has not reached it yet, then navigate directly to its tab.
        if (window._quizHistoryTabIndex < 0)
            window.InitializeQuizHistoryPage();

        if (window._quizHistoryTabIndex < 0 || window._quizHistoryTabIndex >= window.MainTabs.Items.Count)
            return;

        e.Handled = true;
        window.MainTabs.SelectedIndex = window._quizHistoryTabIndex;
        window.ApplyNavigationSelection(window._quizHistoryTabIndex);
        window.SelectAutopilotNav("Library");
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = current is FrameworkElement element ? element.Parent : null;
        }

        return null;
    }
}
