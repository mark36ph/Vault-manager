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

        var settings = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "5", StringComparison.Ordinal));
        var quizzes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizTabIndex.ToString(), StringComparison.Ordinal));
        var quizHistory = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizHistoryTabIndex.ToString(), StringComparison.Ordinal));
        var youtubeAnalytics = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _youtubeAnalyticsTabIndex.ToString(), StringComparison.Ordinal));

        // Only keep the active production, Library and analytics destinations
        // in the legacy navigation. Question Bank, Upload Manager, Facebook and
        // Instagram are retired from the current Factburst shell.
        if (settings is null || quizzes is null || quizHistory is null || youtubeAnalytics is null ||
            quizzes.Parent is not StackPanel navigation)
        {
            return;
        }

        _navigationSectionsApplied = true;

        navigation.Children.Clear();
        navigation.Children.Add(quizzes);
        navigation.Children.Add(quizHistory);
        navigation.Children.Add(NavigationSpacer());
        navigation.Children.Add(youtubeAnalytics);
        navigation.Children.Add(NavigationSpacer());
        navigation.Children.Add(settings);

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
