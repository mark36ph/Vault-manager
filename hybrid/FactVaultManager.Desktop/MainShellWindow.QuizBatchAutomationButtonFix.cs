using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // Anchor the Generate + Schedule action to the permanent final-render button.
    // The earlier implementation listened for Loaded on the dynamically-created
    // Batch Render button, but controls added after the Export view was loaded do
    // not reliably raise the class-handler path we depended on.
    private static readonly bool QuizBatchAutomationStableButtonRegistered =
        RegisterQuizBatchAutomationStableButton();

    private static bool RegisterQuizBatchAutomationStableButton()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizBatchAutomationStableTarget_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizBatchAutomationStableTarget_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button renderButton ||
            (!string.Equals(renderButton.Content?.ToString(), NativeQuizFinalRenderButtonText, StringComparison.Ordinal) &&
             !string.Equals(renderButton.Content?.ToString(), LegacyQuizExportButtonText, StringComparison.Ordinal)) ||
            Window.GetWindow(renderButton) is not MainShellWindow window)
        {
            return;
        }

        // Let the existing Batch Render wrapper finish first, then insert our action
        // into that same Export action row. If the wrapper has not run for any reason,
        // EnsureQuizBatchRenderButton creates it before we continue.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => window.EnsureQuizBatchAutomationStableButton(renderButton)));
    }

    private void EnsureQuizBatchAutomationStableButton(Button renderButton)
    {
        if (renderButton.Parent is not StackPanel)
            EnsureQuizBatchRenderButton();

        if (renderButton.Parent is not StackPanel actions)
            return;

        if (actions.Children.OfType<FrameworkElement>().Any(child =>
                string.Equals(child.Tag?.ToString(), QuizBatchAutomationButtonTag, StringComparison.Ordinal)) ||
            actions.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Generate + Schedule...", StringComparison.Ordinal)))
        {
            return;
        }

        var button = new Button
        {
            Content = "Generate + Schedule...",
            Tag = QuizBatchAutomationButtonTag,
            Height = renderButton.Height > 0 ? renderButton.Height : 34,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Generate a batch of fresh quizzes, schedule each full quiz to YouTube on the next free 09:00 day, then create its promo Short ready for the following day.",
        };
        button.Click += GenerateAndScheduleQuizBatch_Click;

        var batchButton = actions.Children
            .OfType<Button>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Content?.ToString(), "Batch Render...", StringComparison.Ordinal));
        var batchIndex = batchButton is null ? -1 : actions.Children.IndexOf(batchButton);
        var renderIndex = actions.Children.IndexOf(renderButton);
        var insertIndex = batchIndex >= 0
            ? batchIndex + 1
            : renderIndex >= 0
                ? renderIndex
                : actions.Children.Count;

        actions.Children.Insert(insertIndex, button);
    }
}
