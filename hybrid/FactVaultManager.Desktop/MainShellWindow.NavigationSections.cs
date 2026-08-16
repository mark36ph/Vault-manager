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
            projects is null || production is null || media is null || assetReview is null ||
            settings is null || quizzes is null || questions is null || quizHistory is null)
        {
            return;
        }

        _navigationSectionsApplied = true;
        var known = new HashSet<Button>
        {
            dashboard, projects, production, media, assetReview, settings, quizzes, questions, quizHistory,
        };
        var extraButtons = navigation.Children
            .OfType<Button>()
            .Where(button => !known.Contains(button))
            .ToArray();

        navigation.Children.Clear();

        AddNavigationSectionLabel(navigation, "OVERVIEW");
        navigation.Children.Add(dashboard);

        navigation.Children.Add(NavigationDivider());
        AddNavigationSectionLabel(navigation, "FACTS");
        navigation.Children.Add(projects);
        navigation.Children.Add(production);
        navigation.Children.Add(media);
        navigation.Children.Add(assetReview);

        navigation.Children.Add(NavigationDivider());
        AddNavigationSectionLabel(navigation, "QUIZZES");
        navigation.Children.Add(quizzes);
        navigation.Children.Add(questions);
        navigation.Children.Add(quizHistory);

        if (extraButtons.Length > 0)
        {
            navigation.Children.Add(NavigationDivider());
            AddNavigationSectionLabel(navigation, "OTHER");
            foreach (var extra in extraButtons)
                navigation.Children.Add(extra);
        }

        navigation.Children.Add(NavigationDivider());
        AddNavigationSectionLabel(navigation, "SYSTEM");
        navigation.Children.Add(settings);

        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    private static void AddNavigationSectionLabel(Panel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(119, 119, 119)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 4, 0, 7),
        });
    }

    private static Border NavigationDivider() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
        Margin = new Thickness(12, 10, 12, 8),
    };
}
