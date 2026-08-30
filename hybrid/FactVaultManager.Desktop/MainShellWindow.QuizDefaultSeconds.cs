using System;
using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizDefaultSecondsUiRegistered = RegisterQuizDefaultSecondsUi();

    private static bool RegisterQuizDefaultSecondsUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowQuizDefaultSeconds_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowQuizDefaultSeconds_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
        {
            return;
        }

        window.ContentRendered -= window.QuizDefaultSeconds_ContentRendered;
        window.ContentRendered += window.QuizDefaultSeconds_ContentRendered;
    }

    private void QuizDefaultSeconds_ContentRendered(object? sender, EventArgs e)
    {
        if (_quizSecondsPerQuestionTextBox is not null &&
            string.Equals(_quizSecondsPerQuestionTextBox.Text?.Trim(), "8", StringComparison.Ordinal))
        {
            _quizSecondsPerQuestionTextBox.Text = QuizBuilderModePresets.Full.SecondsPerQuestion.ToString();
            _quizSecondsPerQuestion = QuizBuilderModePresets.Full.SecondsPerQuestion;
        }
    }
}
