using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string LegacyQuizExportButtonText = "Create Resolve Quiz";
    private const string NativeQuizFinalRenderButtonText = "Render Final Video";
    private static readonly bool NativeQuizFinalRenderUiRegistered = RegisterNativeQuizFinalRenderUi();

    private static bool RegisterNativeQuizFinalRenderUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeQuizFinalRenderButton_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(NativeQuizFinalRenderButton_Clicked),
            handledEventsToo: true);
        return true;
    }

    private static bool IsQuizFinalRenderActionButton(Button button)
    {
        var text = Convert.ToString(button.Content)?.Trim();
        return string.Equals(text, LegacyQuizExportButtonText, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, NativeQuizFinalRenderButtonText, StringComparison.OrdinalIgnoreCase);
    }

    private static void NativeQuizFinalRenderButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !IsQuizFinalRenderActionButton(button) ||
            Window.GetWindow(button) is not MainShellWindow window)
            return;

        button.Content = NativeQuizFinalRenderButtonText;
        button.ToolTip = "Create the finished YouTube-ready MP4 directly in Content Vault Manager. A Resolve/FCPXML package is also kept for optional advanced editing.";

        if (button.Parent is Grid controls && controls.Parent is Grid layout)
        {
            var heading = layout.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => string.Equals(text.Text, "Resolve export", StringComparison.OrdinalIgnoreCase));
            if (heading is not null)
                heading.Text = "Final video";
        }

        if (window._quizTitleTextBox is not null)
            window._quizTitleTextBox.ToolTip = "Category title and final video project name";
    }

    private static void NativeQuizFinalRenderButton_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !IsQuizFinalRenderActionButton(button) ||
            Window.GetWindow(button) is not MainShellWindow window)
            return;

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        var checks = 0;
        timer.Tick += (_, _) =>
        {
            checks++;
            if (!button.IsEnabled && checks < 18_000)
                return;

            timer.Stop();
            if (!button.IsEnabled || string.IsNullOrWhiteSpace(window._lastQuizExportFolder))
                return;

            var finalVideo = NativeQuizFinalRenderer.OutputPath(window._lastQuizExportFolder);
            if (!File.Exists(finalVideo) || new FileInfo(finalVideo).Length == 0)
                return;

            if (window._quizPageStatusText is not null)
                window._quizPageStatusText.Text = $"Final MP4 ready: {Path.GetFileName(finalVideo)} • added to Quiz History";
            if (window._quizDraftStatusText is not null)
                window._quizDraftStatusText.Text = $"Final quiz video ready • {Path.GetFileName(finalVideo)}";
            if (window._quizPublishingStatusText is not null)
                window._quizPublishingStatusText.Text = "Upload package ready: final MP4, Thumbnail.png, publishing metadata and optional Resolve/FCPXML backup are in the quiz export folder.";
        };
        timer.Start();
    }
}
