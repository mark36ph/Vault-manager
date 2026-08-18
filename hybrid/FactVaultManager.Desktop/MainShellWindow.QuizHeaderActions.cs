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

    public void InitializeQuizHeaderActionsForApp()
    {
        if (_quizHeaderActionsInitialized)
            return;

        _quizHeaderActionsInitialized = true;
        Loaded += (_, _) =>
        {
            InitializeQuizHeaderButtons();
            UpdateQuizHeaderButtons();
        };
        MainTabs.SelectionChanged += (_, _) => UpdateQuizHeaderButtons();
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
        _quizHeaderResolveButton.Visibility = selected == _quizTabIndex
            ? Visibility.Visible
            : Visibility.Collapsed;
        _quizHeaderPublishButton.Visibility = selected == _quizHistoryTabIndex
            ? Visibility.Visible
            : Visibility.Collapsed;
        _quizHeaderReopenButton.Visibility = selected == _quizHistoryTabIndex
            ? Visibility.Visible
            : Visibility.Collapsed;

        var hasExport = !string.IsNullOrWhiteSpace(_lastQuizResolveExportPath) &&
                        File.Exists(_lastQuizResolveExportPath);
        _quizHeaderResolveButton.Content = hasExport ? "Open in Resolve" : "Create Resolve Quiz";
        _quizHeaderResolveButton.ToolTip = hasExport
            ? "Open DaVinci Resolve and show the latest quiz FCPXML package."
            : "Go to the quiz Export step to create a Resolve package.";
    }

    private void OpenLatestQuizInResolve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var fcpxml = (_lastQuizResolveExportPath ?? "").Trim();
            if (fcpxml.Length == 0 || !File.Exists(fcpxml))
            {
                SelectQuizWorkspacePage("export");
                if (_quizPageStatusText is not null)
                    _quizPageStatusText.Text = "Configure the quiz export, then click Create Resolve Quiz.";
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
