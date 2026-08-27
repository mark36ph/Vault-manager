using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static bool _youtubeUploadPackageUiRegistered;

    private sealed record YouTubeUploadPackageChoice(
        string Key,
        string Label,
        string TitleFileName,
        string ThumbnailFileName);

    private void InitializeYouTubeUploadPackageUi()
    {
        if (_youtubeUploadPackageUiRegistered)
            return;

        _youtubeUploadPackageUiRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(YouTubeUploadDialog_Loaded),
            handledEventsToo: true);
    }

    private static void YouTubeUploadDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window dialog ||
            dialog.Owner is not MainShellWindow owner ||
            !string.Equals(dialog.Title, "Upload Quiz Video", StringComparison.Ordinal))
            return;

        owner.AttachYouTubeUploadPackageUi(dialog);
    }

    private void AttachYouTubeUploadPackageUi(Window dialog)
    {
        const string marker = "Factburst.YouTubeUploadPackageUi";
        if (dialog.Resources.Contains(marker))
            return;
        dialog.Resources[marker] = true;

        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history ||
            string.Equals(history.VideoType, "Short", StringComparison.Ordinal) ||
            !QuizYouTubePackaging.Exists(history.ProjectFolder) ||
            dialog.Content is not Grid root)
            return;

        var titleBox = FindUploadTextBoxAfterLabel(root, "Upload title");
        var thumbnailPath = FindUploadTextBoxAfterLabel(root, "Thumbnail / Reel cover");
        var heading = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 0);
        if (titleBox is null || thumbnailPath is null || heading is null)
            return;

        var choices = new[]
        {
            new YouTubeUploadPackageChoice("A", "A — Score challenge (recommended)", "YouTube Title A.txt", "Thumbnail A - Score.png"),
            new YouTubeUploadPackageChoice("B", "B — Expert challenge", "YouTube Title B.txt", "Thumbnail B - Experts.png"),
            new YouTubeUploadPackageChoice("C", "C — Category / search", "YouTube Title C.txt", "Thumbnail C - Category.png"),
        };

        var packageBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 221, 235)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 12, 0, 0),
        };
        var packageGrid = new Grid();
        packageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        packageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        packageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        packageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        packageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "YouTube package",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        packageGrid.Children.Add(label);

        var selector = new ComboBox
        {
            MinWidth = 290,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var choice in choices)
            selector.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice });
        Grid.SetColumn(selector, 1);
        packageGrid.Children.Add(selector);

        var openFolder = new Button
        {
            Content = "Open A/B Folder",
            MinWidth = 116,
            MinHeight = 34,
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = "Open the project folder containing all three title and thumbnail candidates.",
        };
        StyleQuizHistoryButton(openFolder, Color.FromRgb(0, 204, 255));
        openFolder.Click += (_, _) => OpenFolder(history.ProjectFolder);
        Grid.SetColumn(openFolder, 2);
        packageGrid.Children.Add(openFolder);

        var hint = new TextBlock
        {
            Text = "Choosing A, B or C automatically loads its matching title and thumbnail. After upload, the app will hand you straight to the official YouTube Studio A/B-test setup.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
        };
        Grid.SetRow(hint, 1);
        Grid.SetColumnSpan(hint, 3);
        packageGrid.Children.Add(hint);
        packageBorder.Child = packageGrid;
        heading.Children.Add(packageBorder);

        void ApplySelectedPackage()
        {
            if (selector.SelectedItem is not ComboBoxItem { Tag: YouTubeUploadPackageChoice choice })
                return;

            var titlePath = Path.Combine(history.ProjectFolder, choice.TitleFileName);
            var thumbnailFile = Path.Combine(history.ProjectFolder, choice.ThumbnailFileName);
            if (File.Exists(titlePath))
                titleBox.Text = File.ReadAllText(titlePath).Trim();
            if (File.Exists(thumbnailFile))
                thumbnailPath.Text = thumbnailFile;
        }

        selector.SelectionChanged += (_, _) => ApplySelectedPackage();
        selector.SelectedIndex = 0;
        ApplySelectedPackage();

        dialog.Closed += (_, _) =>
        {
            if (dialog.DialogResult != true ||
                selector.SelectedItem is not ComboBoxItem { Tag: YouTubeUploadPackageChoice choice })
                return;

            try
            {
                File.WriteAllText(
                    Path.Combine(history.ProjectFolder, "YouTube Initial Package.txt"),
                    $"{choice.Key} — {choice.Label}{Environment.NewLine}");
            }
            catch
            {
                // Packaging selection tracking is helpful but must never turn a successful upload into a failure.
            }

            Dispatcher.BeginInvoke(new Action(() => ShowYouTubeAbTestReady(history, choice)));
        };
    }

    private void ShowYouTubeAbTestReady(QuizHistorySummary history, YouTubeUploadPackageChoice choice)
    {
        var window = new Window
        {
            Title = "YouTube A/B Test Ready",
            Owner = this,
            Width = 650,
            Height = 330,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = $"Uploaded with Package {choice.Key}",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        };
        root.Children.Add(heading);

        var body = new TextBlock
        {
            Text = "Packages A, B and C are ready. YouTube requires the official title/thumbnail experiment to be started in YouTube Studio. Open Studio below, choose Title and thumbnail A/B testing, then add the other two prepared combinations.\n\nIf the video is private or scheduled, start the test after it becomes unlisted or public.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            Margin = new Thickness(0, 16, 0, 16),
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var copyTitles = new Button { Content = "Copy A/B/C Titles", MinWidth = 125, MinHeight = 36 };
        copyTitles.Click += (_, _) =>
        {
            var titlesPath = Path.Combine(history.ProjectFolder, QuizYouTubePackaging.TitlesFileName);
            if (File.Exists(titlesPath))
                Clipboard.SetText(File.ReadAllText(titlesPath));
        };
        buttons.Children.Add(copyTitles);

        var folder = new Button { Content = "Open A/B Folder", MinWidth = 120, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        folder.Click += (_, _) => OpenFolder(history.ProjectFolder);
        buttons.Children.Add(folder);

        var studio = new Button { Content = "Open YouTube Studio", MinWidth = 145, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(studio, Color.FromRgb(255, 190, 0));
        studio.Click += (_, _) => OpenYouTubeStudioForHistory(history);
        buttons.Children.Add(studio);

        var done = new Button { Content = "Done", MinWidth = 78, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        done.Click += (_, _) => window.Close();
        buttons.Children.Add(done);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        window.Content = root;
        window.Show();
    }

    private void OpenYouTubeStudioForHistory(QuizHistorySummary history)
    {
        var refreshed = _quizHistoryGrid?.Items
            .OfType<QuizHistorySummary>()
            .FirstOrDefault(item => item.Id == history.Id);
        var videoUrl = refreshed?.YouTubeUrl ?? history.YouTubeUrl;
        var videoId = ExtractYouTubeVideoId(videoUrl);
        var studioUrl = videoId.Length > 0
            ? $"https://studio.youtube.com/video/{videoId}/edit"
            : "https://studio.youtube.com/";
        Process.Start(new ProcessStartInfo(studioUrl) { UseShellExecute = true });
    }

    private static string ExtractYouTubeVideoId(string? value)
    {
        var text = (value ?? "").Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return "";

        if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], "v", StringComparison.OrdinalIgnoreCase));
            if (query is { Length: 2 })
                return Uri.UnescapeDataString(query[1]);

            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && (segments[0] is "shorts" or "embed"))
                return segments[1];
        }

        return "";
    }

    private static TextBox? FindUploadTextBoxAfterLabel(DependencyObject root, string labelPrefix)
    {
        if (root is Panel panel)
        {
            for (var index = 0; index < panel.Children.Count; index++)
            {
                if (panel.Children[index] is TextBlock label &&
                    label.Text.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    for (var next = index + 1; next < panel.Children.Count; next++)
                        if (panel.Children[next] is TextBox textBox)
                            return textBox;
                }

                if (FindUploadTextBoxAfterLabel(panel.Children[index], labelPrefix) is { } nested)
                    return nested;
            }
        }
        else if (root is Border { Child: DependencyObject child })
        {
            return FindUploadTextBoxAfterLabel(child, labelPrefix);
        }
        else if (root is ContentControl { Content: DependencyObject content })
        {
            return FindUploadTextBoxAfterLabel(content, labelPrefix);
        }

        return null;
    }

    private static void OpenFolder(string? folder)
    {
        var path = (folder ?? "").Trim();
        if (path.Length == 0 || !Directory.Exists(path))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
