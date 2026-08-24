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
        var quizNotes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizNotesTabIndex.ToString(), StringComparison.Ordinal));
        var uploadManager = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _uploadManagerTabIndex.ToString(), StringComparison.Ordinal));
        var youtubeAnalytics = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _youtubeAnalyticsTabIndex.ToString(), StringComparison.Ordinal));
        var facebookAnalytics = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _facebookAnalyticsTabIndex.ToString(), StringComparison.Ordinal));
        var instagramManager = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _instagramManagerTabIndex.ToString(), StringComparison.Ordinal));

        if (dashboard?.Parent is not StackPanel navigation ||
            projects is null || production is null || media is null ||
            settings is null || quizzes is null || questions is null || quizHistory is null || quizNotes is null || uploadManager is null ||
            youtubeAnalytics is null || facebookAnalytics is null || instagramManager is null)
        {
            return;
        }

        _navigationSectionsApplied = true;

        navigation.Children.Clear();
        navigation.Children.Add(quizzes);
        navigation.Children.Add(questions);
        navigation.Children.Add(quizHistory);
        navigation.Children.Add(quizNotes);
        navigation.Children.Add(NavigationSpacer());
        navigation.Children.Add(uploadManager);
        navigation.Children.Add(youtubeAnalytics);
        navigation.Children.Add(facebookAnalytics);
        navigation.Children.Add(instagramManager);

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
