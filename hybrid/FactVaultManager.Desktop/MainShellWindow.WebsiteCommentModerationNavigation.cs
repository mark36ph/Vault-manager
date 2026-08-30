using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteCommentModerationNavigationInitialized;
    private int _websiteCommentModerationTabIndex = -1;
    private DispatcherTimer? _websiteCommentModerationNavigationTimer;

    public void InitializeWebsiteCommentModerationNavigation()
    {
        if (_websiteCommentModerationNavigationInitialized) return;
        _websiteCommentModerationNavigationInitialized = true;

        _websiteCommentModerationNavigationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _websiteCommentModerationNavigationTimer.Tick += (_, _) => EnsureWebsiteCommentModerationNavigation();
        _websiteCommentModerationNavigationTimer.Start();
        Closed += (_, _) => _websiteCommentModerationNavigationTimer?.Stop();

        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            if (MainTabs.SelectedIndex == _websiteCommentModerationTabIndex)
                await RefreshWebsiteCommentModerationAsync(false);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteCommentModerationNavigation));
    }

    private void EnsureWebsiteCommentModerationNavigation()
    {
        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null) return;

        if (_websiteCommentModerationTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildWebsiteCommentModerationPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _websiteCommentModerationTabIndex = MainTabs.Items.Count - 1;
        }

        // Users is deliberately the anchor so the website-management order is always:
        // Website -> Users -> Comments.
        if (!_autopilotNavButtons.TryGetValue("Users", out var usersButton)) return;

        if (!_autopilotNavButtons.TryGetValue("Comments", out var commentsButton))
        {
            commentsButton = new Button
            {
                Content = "☵   Comments",
                Tag = AutopilotFirstNavTag + ":Comments",
            };
            if (FindResource("NavButtonStyle") is Style navStyle)
                commentsButton.Style = navStyle;
            commentsButton.Click += (_, _) => NavigateWebsiteCommentModeration();
            _autopilotNavButtons["Comments"] = commentsButton;
        }

        var usersIndex = _autopilotNavContainer.Children.IndexOf(usersButton);
        var currentIndex = _autopilotNavContainer.Children.IndexOf(commentsButton);
        if (usersIndex < 0) return;
        if (currentIndex != usersIndex + 1)
        {
            if (currentIndex >= 0)
                _autopilotNavContainer.Children.Remove(commentsButton);
            usersIndex = _autopilotNavContainer.Children.IndexOf(usersButton);
            _autopilotNavContainer.Children.Insert(
                Math.Clamp(usersIndex + 1, 0, _autopilotNavContainer.Children.Count),
                commentsButton);
        }

        var finalUsersIndex = _autopilotNavContainer.Children.IndexOf(usersButton);
        var finalCommentsIndex = _autopilotNavContainer.Children.IndexOf(commentsButton);
        if (finalUsersIndex >= 0 && finalCommentsIndex == finalUsersIndex + 1)
            _websiteCommentModerationNavigationTimer?.Stop();
    }

    private void NavigateWebsiteCommentModeration()
    {
        EnsureWebsiteCommentModerationNavigation();
        if (_websiteCommentModerationTabIndex < 0) return;
        MainTabs.SelectedIndex = _websiteCommentModerationTabIndex;
        SelectAutopilotNav("Comments");
        _ = RefreshWebsiteCommentModerationAsync(false);
    }
}
