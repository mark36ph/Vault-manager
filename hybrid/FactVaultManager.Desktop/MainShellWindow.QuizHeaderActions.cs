using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private Button? _quizHeaderProductionButton;
    private Button? _quizHeaderResolveButton;
    private Button? _quizHeaderPublishButton;
    private Button? _quizHeaderReopenButton;
    private bool _quizHeaderActionsInitialized;
    private bool _finalVideoRenderLabelsApplied;

    public void InitializeQuizHeaderActionsForApp()
    {
        if (_quizHeaderActionsInitialized)
            return;

        _quizHeaderActionsInitialized = true;
        Loaded += (_, _) =>
        {
            InitializeQuizHeaderButtons();
            UpdateQuizHeaderButtons();
            ApplyQuizFinalRenderLabels();
        };
        MainTabs.SelectionChanged += (_, _) =>
        {
            UpdateQuizHeaderButtons();
        };
    }

    private void InitializeQuizHeaderButtons()
    {
        if (_quizHeaderResolveButton is not null || Content is not DependencyObject root)
            return;

        _quizHeaderProductionButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "▷  Production",
                StringComparison.Ordinal));

        if (_quizHeaderProductionButton?.Parent is not StackPanel headerActions)
            return;

        _quizHeaderResolveButton = new Button
        {
            Content = "Open in Resolve",
            Height = 34,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            ToolTip = "Open DaVinci Resolve and the latest quiz FCPXML package.",
        };
        _quizHeaderResolveButton.Click += OpenLatestQuizInResolve_Click;
        headerActions.Children.Add(_quizHeaderResolveButton);

        _quizHeaderPublishButton = new Button
        {
            Content = "Open Publish",
            Height = 34,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            ToolTip = "Load the selected Quiz History entry and open its Publish step.",
        };
        _quizHeaderPublishButton.Click += (_, _) => ReopenSelectedQuizHistoryInBuilder("publish");
        headerActions.Children.Add(_quizHeaderPublishButton);

        _quizHeaderReopenButton = new Button
        {
            Content = "Reopen in Quiz Builder",
            Height = 34,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(0),
            Visibility = Visibility.Collapsed,
            ToolTip = "Load the selected Quiz History entry back into the quiz workflow.",
        };
        _quizHeaderReopenButton.Click += (_, _) => ReopenSelectedQuizHistoryInBuilder();
        headerActions.Children.Add(_quizHeaderReopenButton);
    }

    private void UpdateQuizHeaderButtons()
    {
        InitializeQuizHeaderButtons();
        if (_quizHeaderProductionButton is null || _quizHeaderResolveButton is null ||
            _quizHeaderPublishButton is null || _quizHeaderReopenButton is null)
        {
            return;
        }

        var selected = MainTabs.SelectedIndex;
        var quizContext = selected == _quizTabIndex ||
                          selected == _quizQuestionBankTabIndex ||
                          selected == _quizHistoryTabIndex;

        _quizHeaderProductionButton.Visibility = quizContext
            ? Visibility.Collapsed
            : Visibility.Visible;

        _quizHeaderResolveButton.Visibility = Visibility.Collapsed;
        _quizHeaderPublishButton.Visibility = Visibility.Collapsed;
        _quizHeaderReopenButton.Visibility = Visibility.Collapsed;

        var hasExport = !string.IsNullOrWhiteSpace(_lastQuizResolveExportPath) &&
                        File.Exists(_lastQuizResolveExportPath);
        _quizHeaderResolveButton.Content = hasExport ? "Open in Resolve" : "Render Final Video";
        _quizHeaderResolveButton.ToolTip = hasExport
            ? "Open DaVinci Resolve and show the latest quiz FCPXML package."
            : "Go to Create > Finish > Manual settings & render to render the finished MP4.";
    }

    private void ApplyQuizFinalRenderLabels()
    {
        if (_finalVideoRenderLabelsApplied || Content is not DependencyObject root)
            return;

        var foundTarget = false;

        foreach (var button in FindVisualChildren<Button>(root))
        {
            if (!string.Equals(
                    Convert.ToString(button.Content)?.Trim(),
                    "Create Resolve Quiz",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            button.Content = "Render Final Video";
            button.ToolTip = "Create the finished YouTube-ready MP4 directly in Content Vault Manager. A Resolve/FCPXML package is also kept for optional advanced editing.";
            foundTarget = true;
        }

        foreach (var text in FindVisualChildren<TextBlock>(root))
        {
            if (string.Equals(text.Text, "Resolve export", StringComparison.OrdinalIgnoreCase))
            {
                text.Text = "Final video";
                foundTarget = true;
            }
            else if (string.Equals(
                         text.Text,
                         "Configure Resolve format, quiz branding, presentation, narration, sound effects, and background music.",
                         StringComparison.Ordinal))
            {
                text.Text = "Configure final video format, quiz branding, presentation, narration, sound effects, and background music.";
                foundTarget = true;
            }
            else if (string.Equals(
                         text.Text,
                         "Pick random questions from your reusable bank, set the timing, and prepare quiz videos for Resolve.",
                         StringComparison.Ordinal))
            {
                text.Text = "Pick random questions from your reusable bank, set the timing, and create finished quiz videos.";
                foundTarget = true;
            }
        }

        if (foundTarget)
            _finalVideoRenderLabelsApplied = true;
    }

    private void OpenLatestQuizInResolve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var fcpxml = (_lastQuizResolveExportPath ?? "").Trim();
            if (fcpxml.Length == 0 || !File.Exists(fcpxml))
            {
                SelectQuizWorkspacePage("export");
                ApplyQuizFinalRenderLabels();
                if (_quizPageStatusText is not null)
                    _quizPageStatusText.Text = "Open Manual settings & render, then click Render Final Video.";
                return;
            }

            var settings = _data.LoadSettings();
            var resolvePath = (settings.ResolvePath ?? "").Trim();
            if (resolvePath.Length > 0 && File.Exists(resolvePath))
            {
                Process.Start(new ProcessStartInfo(resolvePath)
                {
                    UseShellExecute = true,
                });
            }

            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                $"/select,\"{fcpxml}\"")
            {
                UseShellExecute = true,
            });

            if (resolvePath.Length == 0 || !File.Exists(resolvePath))
            {
                MessageBox.Show(
                    this,
                    "The quiz FCPXML has been highlighted. Set the DaVinci Resolve path in Settings if you also want the app to launch Resolve automatically.",
                    "Quiz Resolve Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (_quizPageStatusText is not null)
            {
                _quizPageStatusText.Text = "Resolve opened • latest quiz FCPXML highlighted for import";
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Open Quiz in Resolve", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
