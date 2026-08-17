using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _navigationSectionsApplied;

    private void ApplyNavigationSections()
    {
        if (_navigationSectionsApplied || Content is not DependencyObject root)
            return;

        var dashboard = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "0", StringComparison.Ordinal));
        var projects = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "1", StringComparison.Ordinal));
        var production = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "2", StringComparison.Ordinal));
        var media = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "3", StringComparison.Ordinal));
        var assetReview = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "4", StringComparison.Ordinal));
        var settings = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "5", StringComparison.Ordinal));
        var quizzes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizTabIndex.ToString(), StringComparison.Ordinal));
        var questions = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizQuestionBankTabIndex.ToString(), StringComparison.Ordinal));
        var quizHistory = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizHistoryTabIndex.ToString(), StringComparison.Ordinal));

        if (dashboard?.Parent is not StackPanel navigation ||
            projects is null || production is null || media is null ||
            settings is null || quizzes is null || questions is null || quizHistory is null)
        {
            return;
        }

        _navigationSectionsApplied = true;

        navigation.Children.Clear();
        navigation.Children.Add(dashboard);
        navigation.Children.Add(projects);
        navigation.Children.Add(production);
        navigation.Children.Add(media);

        navigation.Children.Add(NavigationSpacer());
        navigation.Children.Add(quizzes);
        navigation.Children.Add(questions);
        navigation.Children.Add(quizHistory);

        navigation.Children.Add(NavigationSpacer());
        navigation.Children.Add(settings);

        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    private static Border NavigationSpacer() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
        Margin = new Thickness(12, 13, 12, 12),
    };
}
