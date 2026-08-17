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
    private ComboBox? _quizVoiceComboBox;
    private CheckBox? _quizCountdownTickCheckBox;
    private CheckBox? _quizAnswerRevealSfxCheckBox;
    private CheckBox? _quizBackgroundMusicCheckBox;
    private TextBox? _quizBackgroundMusicPathTextBox;

    private void InitializeQuizExportWorkflow()
    {
        if (_quizExportWorkflowInitialized || _quizDraftGrid?.Parent is not Grid draft)
            return;

        _quizExportWorkflowInitialized = true;
        var exportPanel = AddQuizBuilderSectionCard(draft);

        var layout = new Grid();
        for (var row = 0; row < 7; row++)
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

        audio.Children.Add(new TextBlock
        {
            Text = "Voice",
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        _quizVoiceComboBox = new ComboBox
        {
            Width = 112,
            MinHeight = 30,
            ItemsSource = QuizVoiceCatalog.BuiltInVoices,
            SelectedItem = "alloy",
            IsEnabled = false,
            ToolTip = "OpenAI voice used for quiz narration.",
            Margin = new Thickness(0, 0, 18, 0),
        };
        audio.Children.Add(_quizVoiceComboBox);

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
            if (_quizVoiceComboBox is not null)
                _quizVoiceComboBox.IsEnabled = true;
        };
        _quizNarrationCheckBox.Unchecked += (_, _) =>
        {
            if (_quizNarrateAnswersCheckBox is not null)
                _quizNarrateAnswersCheckBox.IsEnabled = false;
            if (_quizVoiceComboBox is not null)
                _quizVoiceComboBox.IsEnabled = false;
        };

        var soundEffects = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(soundEffects, 5);
        layout.Children.Add(soundEffects);
        soundEffects.Children.Add(new TextBlock
        {
            Text = "Sound effects",
            FontWeight = FontWeights.SemiBold,
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        });

        _quizCountdownTickCheckBox = new CheckBox
        {
            Content = "Countdown ticks",
            IsChecked = true,
            IsEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Add a short locally generated tick at 3, 2, and 1. No API call is used.",
            Margin = new Thickness(0, 0, 18, 0),
        };
        soundEffects.Children.Add(_quizCountdownTickCheckBox);

        _quizAnswerRevealSfxCheckBox = new CheckBox
        {
            Content = "Correct-answer chime",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Add a short locally generated chime when the correct answer appears. No API call is used.",
        };
        soundEffects.Children.Add(_quizAnswerRevealSfxCheckBox);
        _quizCountdownCheckBox.Checked += (_, _) =>
        {
            if (_quizCountdownTickCheckBox is not null)
                _quizCountdownTickCheckBox.IsEnabled = true;
        };
        _quizCountdownCheckBox.Unchecked += (_, _) =>
        {
            if (_quizCountdownTickCheckBox is not null)
                _quizCountdownTickCheckBox.IsEnabled = false;
        };

        var music = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(music, 6);
        layout.Children.Add(music);

        music.Children.Add(new TextBlock
        {
            Text = "Background music",
            FontWeight = FontWeights.SemiBold,
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        });

        _quizBackgroundMusicCheckBox = new CheckBox
        {
            Content = "Use music",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Loop the selected track at low volume and automatically duck it while narration is speaking.",
        };
        Grid.SetColumn(_quizBackgroundMusicCheckBox, 2);
        music.Children.Add(_quizBackgroundMusicCheckBox);

        _quizBackgroundMusicPathTextBox = new TextBox
        {
            IsReadOnly = true,
            ToolTip = "MP3, WAV, M4A, AAC, FLAC, OGG, or OPUS background music file.",
        };
        Grid.SetColumn(_quizBackgroundMusicPathTextBox, 4);
        music.Children.Add(_quizBackgroundMusicPathTextBox);

        var browseMusic = new Button { Content = "Browse", Margin = new Thickness(0) };
        browseMusic.Click += BrowseQuizBackgroundMusic_Click;
        Grid.SetColumn(browseMusic, 6);
        music.Children.Add(browseMusic);

        var clearMusic = new Button { Content = "Clear", Margin = new Thickness(0) };
        clearMusic.Click += (_, _) =>
        {
            _quizBackgroundMusicPathTextBox?.Clear();
            if (_quizBackgroundMusicCheckBox is not null)
                _quizBackgroundMusicCheckBox.IsChecked = false;
        };
        Grid.SetColumn(clearMusic, 8);
        music.Children.Add(clearMusic);
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

    private void BrowseQuizBackgroundMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select quiz background music",
            Filter = "Audio files (*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus)|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path = QuizMusicFile.Validate(dialog.FileName);
            if (_quizBackgroundMusicPathTextBox is not null)
                _quizBackgroundMusicPathTextBox.Text = path;
            if (_quizBackgroundMusicCheckBox is not null)
                _quizBackgroundMusicCheckBox.IsChecked = true;
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz background music selected: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Background Music", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var selectedVoice = QuizVoiceCatalog.Validate(Convert.ToString(_quizVoiceComboBox?.SelectedItem) ?? "alloy");
            var countdownTicks = showCountdown && _quizCountdownTickCheckBox?.IsChecked == true;
            var answerRevealSfx = _quizAnswerRevealSfxCheckBox?.IsChecked == true;
            var useBackgroundMusic = _quizBackgroundMusicCheckBox?.IsChecked == true;
            var exportQuestions = shuffleAnswers
                ? QuizAnswerShuffler.Shuffle(_quizDraftQuestions)
                : _quizDraftQuestions.ToList();

            var options = new QuizVideoBuildOptions(
                title,
                QuestionSeconds: seconds,
                AnswerSeconds: 3,
                Vertical: vertical,
                FrameRate: settings.FrameRate > 0 ? settings.FrameRate : 30,
                QuizLogoPath: logoPath,
                ShowCountdown: showCountdown,
                AnimateAnswerReveal: animateReveal);

            var quizFolder = ProjectPathSecurity.CombineContained(settings.ProjectsFolder, "Quizzes", title);
            var voiceFolder = ProjectPathSecurity.CombineContained(settings.ProjectsFolder, "Quizzes", title, "Voice");
            var audioFolder = ProjectPathSecurity.CombineContained(settings.ProjectsFolder, "Quizzes", title, "Audio");
            Directory.CreateDirectory(quizFolder);

            IReadOnlyDictionary<int, QuizNarrationAsset> narrationByQuestion = new Dictionary<int, QuizNarrationAsset>();
            if (narrate)
            {
                var credentials = NativeProviderCredentials.FromSettings(settings);
                var apiKey = credentials.Get("openai");
                Directory.CreateDirectory(voiceFolder);

                using var speech = new NativeQuizSpeechProvider(apiKey, voice: selectedVoice);
                var media = new NativeFfmpegTimelineService();
                var generated = new Dictionary<int, QuizNarrationAsset>();
                for (var index = 0; index < exportQuestions.Count; index++)
                {
                    var question = exportQuestions[index];
                    if (_quizPageStatusText is not null)
                        _quizPageStatusText.Text = $"Generating quiz narration {index + 1}/{exportQuestions.Count} with {selectedVoice}...";
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

            QuizAudioCue? countdownTick = null;
            if (countdownTicks)
                countdownTick = QuizAudioCueFactory.EnsureCountdownTick(audioFolder);

            QuizAudioCue? revealChime = null;
            if (answerRevealSfx)
                revealChime = QuizAudioCueFactory.EnsureAnswerReveal(audioFolder);

            QuizPreparedBackgroundMusic? backgroundMusic = null;
            var narrationSeconds = narrationByQuestion.Values.Sum(asset => asset.Duration);
            if (useBackgroundMusic)
            {
                var sourceMusic = QuizMusicFile.Validate(_quizBackgroundMusicPathTextBox?.Text);
                var windows = QuizAudioTimelinePlanner.BuildNarrationWindows(
                    exportQuestions,
                    options,
                    narrationByQuestion);
                var totalDuration = options.EstimatedDuration(exportQuestions.Count, narrationSeconds);
                if (_quizPageStatusText is not null)
                    _quizPageStatusText.Text = windows.Count > 0
                        ? "Preparing background music with narration ducking..."
                        : "Preparing background music...";
                backgroundMusic = await new NativeQuizBackgroundMusicRenderer().RenderAsync(
                    sourceMusic,
                    audioFolder,
                    totalDuration,
                    windows);
            }

            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Rendering quiz cards and creating Resolve export...";

            var result = new NativeQuizVideoBuilder().BuildAndExport(
                exportQuestions,
                options,
                settings.ProjectsFolder,
                narrationByQuestion);
            result = QuizAudioTimelineAugmenter.ApplyAndReExport(
                result,
                exportQuestions,
                options,
                new QuizAudioAssets(
                    countdownTick,
                    revealChime,
                    backgroundMusic,
                    narrate ? selectedVoice : ""));

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
                var duration = result.Timeline.Duration;
                var brandingStatus = logoPath.Length == 0 ? "no quiz logo" : $"logo: {System.IO.Path.GetFileName(logoPath)}";
                var answerStatus = shuffleAnswers ? "answers shuffled" : "answer order unchanged";
                var presentationStatus = $"{(showCountdown ? "countdown on" : "countdown off")} • {(animateReveal ? "reveal pulse on" : "reveal pulse off")}";
                var narrationStatus = narrate
                    ? $"voice {selectedVoice} ({narrationSeconds:0.0}s{(narrateAnswers ? ", choices read" : "")})"
                    : "narration off";
                var sfxStatus = $"{(countdownTicks ? "ticks on" : "ticks off")}, {(answerRevealSfx ? "correct chime on" : "correct chime off")}";
                var musicStatus = backgroundMusic is null
                    ? "music off"
                    : backgroundMusic.DuckedForNarration ? "music on, ducking on" : "music on";
                _quizDraftStatusText.Text = $"Resolve quiz ready • {_quizDraftQuestions.Count} questions • {seconds} sec answer time • {answerStatus} • {presentationStatus} • {narrationStatus} • {sfxStatus} • {musicStatus} • {brandingStatus} • saved to Quiz History • approx {TimeSpan.FromSeconds(duration):m\\:ss}.";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Resolve quiz export created and added to Quiz History";

            MessageBox.Show(
                this,
                $"Quiz export created.\n\nFCPXML:\n{result.ResolveExport.FcpXml.Path}\n\nAnswer positions: {(shuffleAnswers ? "Shuffled for this export" : "Original bank order")}\nCountdown: {(showCountdown ? "3-2-1 enabled" : "Off")}\nCountdown ticks: {(countdownTicks ? "Enabled" : "Off")}\nAnswer reveal pulse: {(animateReveal ? "Enabled" : "Off")}\nCorrect-answer chime: {(answerRevealSfx ? "Enabled" : "Off")}\nOpenAI narration: {(narrate ? (narrateAnswers ? $"{selectedVoice} • question + answer choices" : $"{selectedVoice} • question only") : "Off")}\nBackground music: {(backgroundMusic is null ? "Off" : backgroundMusic.DuckedForNarration ? "Enabled • narration ducking on" : "Enabled")}\nQuiz logo: {(logoPath.Length == 0 ? "None" : System.IO.Path.GetFileName(logoPath))}\nQuiz History: recorded\nValidated media files: {result.ResolveExport.ValidatedMedia.Count}",
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
