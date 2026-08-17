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
    private CheckBox? _quizCountdownCheckBox;
    private CheckBox? _quizRevealAnimationCheckBox;
    private CheckBox? _quizNarrationCheckBox;
    private CheckBox? _quizNarrateAnswersCheckBox;

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

        var presentation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(presentation, 3);
        layout.Children.Add(presentation);

        presentation.Children.Add(new TextBlock
        {
            Text = "Presentation",
            FontWeight = FontWeights.SemiBold,
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        });

        _quizCountdownCheckBox = new CheckBox
        {
            Content = "3-2-1 countdown",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Split the final three seconds of each question into visible countdown cards in Resolve.",
            Margin = new Thickness(0, 0, 18, 0),
        };
        presentation.Children.Add(_quizCountdownCheckBox);

        _quizRevealAnimationCheckBox = new CheckBox
        {
            Content = "Answer reveal pulse",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Show a short highlighted CORRECT reveal before the explanation card.",
        };
        presentation.Children.Add(_quizRevealAnimationCheckBox);

        var audio = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(audio, 4);
        layout.Children.Add(audio);
        audio.Children.Add(new TextBlock
        {
            Text = "Audio",
            FontWeight = FontWeights.SemiBold,
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        });

        _quizNarrationCheckBox = new CheckBox
        {
            Content = "OpenAI narration",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Use the OpenAI API key saved in Settings to narrate each question. Narration is cached in the quiz Voice folder.",
            Margin = new Thickness(0, 0, 18, 0),
        };
        audio.Children.Add(_quizNarrationCheckBox);

        _quizNarrateAnswersCheckBox = new CheckBox
        {
            Content = "Read A/B/C/D choices",
            IsChecked = true,
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "When narration is enabled, read all four answer choices after the question.",
        };
        audio.Children.Add(_quizNarrateAnswersCheckBox);
        _quizNarrationCheckBox.Checked += (_, _) =>
        {
            if (_quizNarrateAnswersCheckBox is not null)
                _quizNarrateAnswersCheckBox.IsEnabled = true;
        };
        _quizNarrationCheckBox.Unchecked += (_, _) =>
        {
            if (_quizNarrateAnswersCheckBox is not null)
                _quizNarrateAnswersCheckBox.IsEnabled = false;
        };
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

    private async void ExportQuizToResolve_Click(object sender, RoutedEventArgs e)
    {
        var exportButton = sender as Button;
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Pick random questions first.");
            if (_quizTitleTextBox is null || _quizFormatComboBox is null || _quizSecondsPerQuestionTextBox is null)
                throw new InvalidOperationException("Quiz export controls are not ready.");
            if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
                throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");

            if (exportButton is not null)
                exportButton.IsEnabled = false;

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
            var showCountdown = _quizCountdownCheckBox?.IsChecked != false;
            var animateReveal = _quizRevealAnimationCheckBox?.IsChecked != false;
            var narrate = _quizNarrationCheckBox?.IsChecked == true;
            var narrateAnswers = narrate && _quizNarrateAnswersCheckBox?.IsChecked == true;
            var exportQuestions = shuffleAnswers
                ? QuizAnswerShuffler.Shuffle(_quizDraftQuestions)
                : _quizDraftQuestions.ToList();

            IReadOnlyDictionary<int, QuizNarrationAsset> narrationByQuestion = new Dictionary<int, QuizNarrationAsset>();
            if (narrate)
            {
                var credentials = NativeProviderCredentials.FromSettings(settings);
                var apiKey = credentials.Get("openai");
                var quizFolder = ProjectPathSecurity.CombineContained(settings.ProjectsFolder, "Quizzes", title);
                var voiceFolder = ProjectPathSecurity.CombineContained(settings.ProjectsFolder, "Quizzes", title, "Voice");
                Directory.CreateDirectory(quizFolder);
                Directory.CreateDirectory(voiceFolder);

                using var speech = new NativeQuizSpeechProvider(apiKey);
                var media = new NativeFfmpegTimelineService();
                var generated = new Dictionary<int, QuizNarrationAsset>();
                for (var index = 0; index < exportQuestions.Count; index++)
                {
                    var question = exportQuestions[index];
                    if (_quizPageStatusText is not null)
                        _quizPageStatusText.Text = $"Generating quiz narration {index + 1}/{exportQuestions.Count}...";
                    var path = await speech.GenerateQuestionAsync(
                        question,
                        index + 1,
                        narrateAnswers,
                        voiceFolder);
                    var duration = await media.MediaDurationAsync(path);
                    generated[question.Id] = new QuizNarrationAsset(question.Id, path, duration);
                }
                narrationByQuestion = generated;
            }

            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Rendering quiz cards and creating Resolve export...";

            var options = new QuizVideoBuildOptions(
                title,
                QuestionSeconds: seconds,
                AnswerSeconds: 3,
                Vertical: vertical,
                FrameRate: settings.FrameRate > 0 ? settings.FrameRate : 30,
                QuizLogoPath: logoPath,
                ShowCountdown: showCountdown,
                AnimateAnswerReveal: animateReveal);
            var result = new NativeQuizVideoBuilder().BuildAndExport(
                exportQuestions,
                options,
                settings.ProjectsFolder,
                narrationByQuestion);

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

            var narrationSeconds = narrationByQuestion.Values.Sum(asset => asset.Duration);
            if (_quizDraftStatusText is not null)
            {
                var duration = result.Timeline.Duration;
                var brandingStatus = logoPath.Length == 0 ? "no quiz logo" : $"logo: {System.IO.Path.GetFileName(logoPath)}";
                var answerStatus = shuffleAnswers ? "answers shuffled" : "answer order unchanged";
                var presentationStatus = $"{(showCountdown ? "countdown on" : "countdown off")} • {(animateReveal ? "reveal pulse on" : "reveal pulse off")}";
                var audioStatus = narrate
                    ? $"OpenAI narration on ({narrationSeconds:0.0}s{(narrateAnswers ? ", choices read" : "")})"
                    : "narration off";
                _quizDraftStatusText.Text = $"Resolve quiz ready • {_quizDraftQuestions.Count} questions • {seconds} sec answer time • {answerStatus} • {presentationStatus} • {audioStatus} • {brandingStatus} • saved to Quiz History • approx {TimeSpan.FromSeconds(duration):m\\:ss}.";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Resolve quiz export created and added to Quiz History";

            MessageBox.Show(
                this,
                $"Quiz export created.\n\nFCPXML:\n{result.ResolveExport.FcpXml.Path}\n\nAnswer positions: {(shuffleAnswers ? "Shuffled for this export" : "Original bank order")}\nCountdown: {(showCountdown ? "3-2-1 enabled" : "Off")}\nAnswer reveal pulse: {(animateReveal ? "Enabled" : "Off")}\nOpenAI narration: {(narrate ? (narrateAnswers ? "Question + answer choices" : "Question only") : "Off")}\nQuiz logo: {(logoPath.Length == 0 ? "None" : System.IO.Path.GetFileName(logoPath))}\nQuiz History: recorded\nValidated media files: {result.ResolveExport.ValidatedMedia.Count}",
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
        finally
        {
            if (exportButton is not null)
                exportButton.IsEnabled = true;
        }
    }
}
