using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizExportWorkflowInitialized;
    private TextBox? _quizTitleTextBox;
    private ComboBox? _quizFormatComboBox;
    private TextBox? _quizLogoPathTextBox;

    private void InitializeQuizExportWorkflow()
    {
        if (_quizExportWorkflowInitialized || _quizDraftGrid?.Parent is not Grid draft)
            return;

        _quizExportWorkflowInitialized = true;
        var exportRow = draft.RowDefinitions.Count;
        draft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var exportPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(exportPanel, exportRow);
        draft.Children.Add(exportPanel);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        exportPanel.Child = layout;

        layout.Children.Add(new TextBlock
        {
            Text = "Resolve export",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });

        var controls = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(controls, 1);
        layout.Children.Add(controls);

        _quizTitleTextBox = new TextBox
        {
            Text = "General Knowledge Quiz",
            ToolTip = "Quiz title and Resolve project name",
        };
        controls.Children.Add(_quizTitleTextBox);

        _quizFormatComboBox = new ComboBox { MinHeight = 34 };
        _quizFormatComboBox.Items.Add("YouTube 16:9 (1920x1080)");
        _quizFormatComboBox.Items.Add("Shorts 9:16 (1080x1920)");
        _quizFormatComboBox.SelectedIndex = 0;
        Grid.SetColumn(_quizFormatComboBox, 2);
        controls.Children.Add(_quizFormatComboBox);

        var exportButton = new Button
        {
            Content = "Create Resolve Quiz",
            Height = 34,
            Padding = new Thickness(13, 0, 13, 0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0),
        };
        exportButton.Click += ExportQuizToResolve_Click;
        Grid.SetColumn(exportButton, 4);
        controls.Children.Add(exportButton);

        var branding = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        branding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(branding, 2);
        layout.Children.Add(branding);

        branding.Children.Add(new TextBlock
        {
            Text = "Quiz logo",
            FontWeight = FontWeights.SemiBold,
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        });

        _quizLogoPathTextBox = new TextBox
        {
            Text = _data.LoadQuizLogoPath(),
            IsReadOnly = true,
            ToolTip = "Dedicated logo used on quiz cards. Quiz exports never fall back to the Facts logo.",
        };
        Grid.SetColumn(_quizLogoPathTextBox, 2);
        branding.Children.Add(_quizLogoPathTextBox);

        var browseLogo = new Button { Content = "Browse", Margin = new Thickness(0) };
        browseLogo.Click += BrowseQuizLogo_Click;
        Grid.SetColumn(browseLogo, 4);
        branding.Children.Add(browseLogo);

        var clearLogo = new Button { Content = "Clear", Margin = new Thickness(0) };
        clearLogo.Click += (_, _) =>
        {
            if (_quizLogoPathTextBox is null)
                return;
            _quizLogoPathTextBox.Clear();
            _data.SaveQuizLogoPath("");
        };
        Grid.SetColumn(clearLogo, 6);
        branding.Children.Add(clearLogo);
    }

    private void BrowseQuizLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select quiz logo",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path = QuizBranding.ValidateLogoPath(dialog.FileName);
            _data.SaveQuizLogoPath(path);
            if (_quizLogoPathTextBox is not null)
                _quizLogoPathTextBox.Text = path;
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz logo selected: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Logo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportQuizToResolve_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Pick random questions first.");
            if (_quizTitleTextBox is null || _quizFormatComboBox is null || _quizSecondsPerQuestionTextBox is null)
                throw new InvalidOperationException("Quiz export controls are not ready.");
            if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
                throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");

            var title = ProjectPathSecurity.ValidateSegment(_quizTitleTextBox.Text, "Quiz title");
            var vertical = _quizFormatComboBox.SelectedIndex == 1;
            var settings = _data.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.ProjectsFolder))
                throw new InvalidOperationException("Set the Projects Folder in Settings before creating a quiz video.");

            var logoPath = (_quizLogoPathTextBox?.Text ?? "").Trim();
            if (logoPath.Length > 0)
            {
                logoPath = QuizBranding.ValidateLogoPath(logoPath);
                _data.SaveQuizLogoPath(logoPath);
            }

            var shuffleAnswers = _quizShuffleAnswersCheckBox?.IsChecked == true;
            var exportQuestions = shuffleAnswers
                ? QuizAnswerShuffler.Shuffle(_quizDraftQuestions)
                : _quizDraftQuestions.ToList();

            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Rendering quiz cards and creating Resolve export...";

            var options = new QuizVideoBuildOptions(
                title,
                QuestionSeconds: seconds,
                AnswerSeconds: 3,
                Vertical: vertical,
                FrameRate: settings.FrameRate > 0 ? settings.FrameRate : 30,
                QuizLogoPath: logoPath);
            var result = new NativeQuizVideoBuilder().BuildAndExport(
                exportQuestions,
                options,
                settings.ProjectsFolder);

            _data.RecordQuizExport(
                title,
                _quizDraftQuestions,
                vertical,
                seconds,
                shuffleAnswers,
                result.ProjectFolder);
            RefreshQuizBank();
            RefreshQuizDraftUsageCounts();
            RefreshQuizHistory();

            if (_quizDraftStatusText is not null)
            {
                var duration = options.EstimatedDuration(_quizDraftQuestions.Count);
                var brandingStatus = logoPath.Length == 0 ? "no quiz logo" : $"logo: {System.IO.Path.GetFileName(logoPath)}";
                var answerStatus = shuffleAnswers ? "answers shuffled" : "answer order unchanged";
                _quizDraftStatusText.Text = $"Resolve quiz ready • {_quizDraftQuestions.Count} questions • {seconds} sec/question • {answerStatus} • {brandingStatus} • saved to Quiz History • approx {TimeSpan.FromSeconds(duration):m\\:ss}.";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Resolve quiz export created and added to Quiz History";

            MessageBox.Show(
                this,
                $"Quiz export created.\n\nFCPXML:\n{result.ResolveExport.FcpXml.Path}\n\nAnswer positions: {(shuffleAnswers ? "Shuffled for this export" : "Original bank order")}\nQuiz logo: {(logoPath.Length == 0 ? "None" : System.IO.Path.GetFileName(logoPath))}\nQuiz History: recorded\nValidated media files: {result.ResolveExport.ValidatedMedia.Count}",
                "Quiz Resolve Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz export failed: {error.Message}";
            MessageBox.Show(this, error.Message, "Quiz Resolve Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
