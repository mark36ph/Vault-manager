using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizYouTubePackagingMenuSyncStarted;
    private bool _quizYouTubePackagingTabHooked;

    private void InitializeQuizYouTubePackagingMenuSync()
    {
        if (_quizYouTubePackagingMenuSyncStarted)
            return;

        _quizYouTubePackagingMenuSyncStarted = true;
        var attempts = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };

        void ApplyAndHook()
        {
            // Ensure the Upload Manager actions exist before trying to extend Quiz Tools.
            InitializeUploadManagerThumbnailRegenerationActions();

            var toolsButton = FindQuizToolsButton(this);
            if (toolsButton is not null)
                EnsureQuizPackagingMenuItems(toolsButton);

            if (!_quizYouTubePackagingTabHooked)
            {
                MainTabs.SelectionChanged += (_, e) =>
                {
                    if (!ReferenceEquals(e.OriginalSource, MainTabs))
                        return;

                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() =>
                        {
                            InitializeUploadManagerThumbnailRegenerationActions();
                            var button = FindQuizToolsButton(this);
                            if (button is not null)
                                EnsureQuizPackagingMenuItems(button);
                        }));
                };
                _quizYouTubePackagingTabHooked = true;
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyAndHook));

        timer.Tick += (_, _) =>
        {
            attempts++;
            ApplyAndHook();
            if (QuizPackagingMenuIsReady() || attempts >= 40)
                timer.Stop();
        };
        timer.Start();
    }

    private bool QuizPackagingMenuIsReady()
    {
        var toolsButton = FindQuizToolsButton(this);
        if (toolsButton is null || !TryGetUploadManagerPopup(toolsButton, out _, out var panel))
            return false;

        return panel.Children
            .OfType<Button>()
            .Any(item => string.Equals(
                item.Content?.ToString(),
                QuizPackagingTodayMenuText,
                StringComparison.Ordinal));
    }

    private static Button? FindQuizToolsButton(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), "Quiz Tools ▾", StringComparison.Ordinal))
            {
                return button;
            }

            var nested = FindQuizToolsButton(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
