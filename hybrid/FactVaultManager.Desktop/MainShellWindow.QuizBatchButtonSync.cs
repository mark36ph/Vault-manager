using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizBatchButtonSyncStarted;
    private bool _quizBatchButtonExportNavHooked;

    private void InitializeQuizBatchButtonSync()
    {
        if (_quizBatchButtonSyncStarted)
            return;

        _quizBatchButtonSyncStarted = true;
        var attempts = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };

        void ApplyAndHook()
        {
            EnsureQuizBatchRenderButton();

            if (!_quizBatchButtonExportNavHooked &&
                _quizWorkspaceNavButtons.TryGetValue("export", out var exportNav))
            {
                exportNav.Click += (_, _) => Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(() => EnsureQuizBatchRenderButton()));
                _quizBatchButtonExportNavHooked = true;
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyAndHook));

        timer.Tick += (_, _) =>
        {
            attempts++;
            ApplyAndHook();
            if ((QuizBatchRenderButtonIsVisible() && _quizBatchButtonExportNavHooked) || attempts >= 40)
                timer.Stop();
        };
        timer.Start();
    }

    private bool EnsureQuizBatchRenderButton()
    {
        if (_quizFormatComboBox?.Parent is not Grid controls)
            return false;

        var existingBatchButton = FindQuizBatchButton(controls);
        if (existingBatchButton is not null)
        {
            QuizBatchModeButton_Loaded(existingBatchButton, new RoutedEventArgs(FrameworkElement.LoadedEvent));
            return true;
        }

        var renderButton = controls.Children
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), NativeQuizFinalRenderButtonText, StringComparison.Ordinal) ||
                string.Equals(button.Content?.ToString(), LegacyQuizExportButtonText, StringComparison.Ordinal));
        if (renderButton is null || Window.GetWindow(renderButton) != this)
            return false;

        QuizBatchRenderTarget_Loaded(renderButton, new RoutedEventArgs(FrameworkElement.LoadedEvent));

        var batchButton = FindQuizBatchButton(controls);
        if (batchButton is null)
            return false;

        QuizBatchModeButton_Loaded(batchButton, new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return true;
    }

    private bool QuizBatchRenderButtonIsVisible()
    {
        if (_quizFormatComboBox?.Parent is not Grid controls)
            return false;
        var button = FindQuizBatchButton(controls);
        return button is not null && button.IsVisible;
    }

    private static Button? FindQuizBatchButton(Grid controls)
    {
        foreach (var child in controls.Children)
        {
            if (child is Button direct &&
                string.Equals(direct.Content?.ToString(), "Batch Render...", StringComparison.Ordinal))
            {
                return direct;
            }

            if (child is Panel panel)
            {
                var nested = panel.Children
                    .OfType<Button>()
                    .FirstOrDefault(button =>
                        string.Equals(button.Content?.ToString(), "Batch Render...", StringComparison.Ordinal));
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }
}
