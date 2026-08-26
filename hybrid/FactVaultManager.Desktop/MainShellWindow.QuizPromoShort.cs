using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ShowQuizPromoShortDialog(QuizHistorySummary history)
    {
        if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Choose a long-form quiz. Promotional Shorts are generated from full videos.",
                "Create Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(history.ProjectFolder) || !Directory.Exists(history.ProjectFolder))
        {
            MessageBox.Show(this, "The quiz project folder could not be found.",
                "Create Promo Short", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new Window
        {
            Title = "Create Promotional Short",
            Owner = this,
            Width = 720,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };
        var root = new Grid { Margin = new Thickness(24) };
        for (var row = 0; row < 5; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock
        {
            Text = "Turn the first Insane question into a Short",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            TextWrapping = TextWrapping.Wrap,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "The saved quiz timeline supplies the exact timestamp. The video is reframed to 9:16 and finished with a Fable call to action.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        });
        root.Children.Add(heading);

        var videoGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        videoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        videoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        videoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        videoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        videoGrid.Children.Add(Label("FINAL LONG-FORM VIDEO"));
        var videoPath = new TextBox
        {
            Text = SocialVideoUploadRules.FindLikelyRenderedVideo(history.ProjectFolder) ?? "",
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        Grid.SetRow(videoPath, 1);
        videoGrid.Children.Add(videoPath);
        var browse = new Button { Content = "Browse...", MinWidth = 92, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(browse, Color.FromRgb(0, 204, 255));
        browse.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Choose the rendered long-form quiz video",
                Filter = "Video files (*.mp4;*.mov;*.m4v)|*.mp4;*.mov;*.m4v|All files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = history.ProjectFolder,
            };
            if (picker.ShowDialog(dialog) == true) videoPath.Text = picker.FileName;
        };
        Grid.SetRow(browse, 1);
        Grid.SetColumn(browse, 1);
        videoGrid.Children.Add(browse);
        Grid.SetRow(videoGrid, 1);
        root.Children.Add(videoGrid);

        var ctaStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        ctaStack.Children.Add(Label("FABLE END-CARD SCRIPT"));
        var cta = new TextBox
        {
            Text = QuizPromoShortScript.DefaultCallToAction,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 82,
            Padding = new Thickness(8),
        };
        ctaStack.Children.Add(cta);
        ctaStack.Children.Add(new TextBlock
        {
            Text = history.YouTubeUrl.Length > 0
                ? "The published full-video URL will be saved in promo-short.json."
                : "After uploading the Short, select the full quiz as its related video in YouTube Studio.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        });
        Grid.SetRow(ctaStack, 2);
        root.Children.Add(ctaStack);

        var status = new TextBlock
        {
            Text = "Ready to generate.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(status, 3);
        root.Children.Add(status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "Close", MinWidth = 84, IsCancel = true };
        var openFolder = new Button { Content = "Open Output Folder", MinWidth = 125, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        var create = new Button { Content = "Create Promo Short", MinWidth = 132, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        StyleQuizHistoryButton(openFolder, Color.FromRgb(255, 190, 0));
        StyleQuizHistoryButton(create, Color.FromRgb(70, 235, 115));
        openFolder.Click += (_, _) =>
        {
            var folder = QuizPromoShortPaths.Folder(history.ProjectFolder);
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        };
        create.Click += async (_, _) =>
        {
            create.IsEnabled = false;
            close.IsEnabled = false;
            browse.IsEnabled = false;
            try
            {
                var settings = _data.LoadSettings();
                var apiKey = NativeProviderCredentials.FromSettings(settings).Get("openai");
                var quizLogoPath = _data.LoadQuizLogoPath();
                var renderer = new QuizPromoShortRenderer();
                var result = await renderer.CreateAsync(
                    videoPath.Text,
                    history.ProjectFolder,
                    history.UploadTitleDisplay,
                    history.YouTubeUrl,
                    cta.Text,
                    apiKey,
                    quizLogoPath,
                    message => status.Text = message);
                status.Text = $"Ready: {Path.GetFileName(result.VideoPath)} • {result.Plan.TotalDuration:0.0} seconds";
                openFolder.IsEnabled = true;
                RefreshUploadManager();
                MessageBox.Show(dialog,
                    $"Promotional Short created from {result.Plan.SceneTitle}.\n\n{result.VideoPath}\n\n" +
                    "When it is uploaded to YouTube, select the full quiz as the related video in YouTube Studio.",
                    "Promo Short Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                status.Text = "Promo Short failed: " + error.Message;
                MessageBox.Show(dialog, error.Message, "Create Promo Short", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                create.IsEnabled = true;
                close.IsEnabled = true;
                browse.IsEnabled = true;
            }
        };
        actions.Children.Add(close);
        actions.Children.Add(openFolder);
        actions.Children.Add(create);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        Margin = new Thickness(0, 0, 0, 5),
    };
}
