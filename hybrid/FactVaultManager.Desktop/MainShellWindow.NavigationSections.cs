using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _navigationSectionsApplied;
    private bool _quizHomeSelected;

    private void ApplyNavigationSections()
    {
        if (_navigationSectionsApplied || Content is not DependencyObject root)
            return;

        var quizzes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizTabIndex.ToString(), StringComparison.Ordinal));

        // The legacy navigation panel is the bootstrap source for the modern
        // Factburst sidebar. Do not clear or rebuild it here: doing so removes
        // the Dashboard/Quizzes anchors that the deferred sidebar activation
        // uses to locate the panel and prevents Website, Users, SEO, Analytics,
        // Comments and History navigation from ever being added.
        if (quizzes is null)
            return;

        _navigationSectionsApplied = true;

        if (!_quizHomeSelected)
        {
            _quizHomeSelected = true;
            MainTabs.SelectedIndex = _quizTabIndex;
        }

        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    private static Border NavigationSpacer() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
        Margin = new Thickness(12, 13, 12, 12),
    };
}
