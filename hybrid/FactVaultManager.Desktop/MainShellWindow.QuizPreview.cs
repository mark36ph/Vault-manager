using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private ComboBox? _quizThemeComboBox;
    private ComboBox? _quizLogoPositionComboBox;
    private Slider? _quizLogoScaleSlider;
    private TextBlock? _quizLogoScaleText;
    private ComboBox? _quizPreviewCardComboBox;
    private ComboBox? _quizPreviewQuestionComboBox;
    private Image? _quizPreviewImage;
    private Grid? _quizPreviewSurface;
    private Border? _quizSafeAreaOverlay;
    private CheckBox? _quizSafeAreaCheckBox;
    private TextBlock? _quizPreflightText;
    private ComboBox? _quizPresetComboBox;
    private TextBox? _quizPresetNameTextBox;
    private bool _quizPreviewEventsHooked;

    private FrameworkElement BuildQuizPreviewPanel()
    {
        var root = new StackPanel();

        var presetsCard = QuizCard(new Thickness(14, 12, 14, 14));
        presetsCard.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(presetsCard);
        var presets = new StackPanel();
        presetsCard.Child = presets;
        presets.Children.Add(new TextBlock
        {
            Text = "Saved presets",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });
        presets.Children.Add(new TextBlock
        {
            Text = "Save and recall the Builder, presentation, branding and audio choices you use repeatedly.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        var presetRow = new Grid();
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presets.Children.Add(presetRow);

        _quizPresetComboBox = new ComboBox { MinHeight = 34, ToolTip = "Saved quiz presets" };
        presetRow.Children.Add(_quizPresetComboBox);
        _quizPresetNameTextBox = new TextBox
        {
            Text = "My Quiz Preset",
            MinHeight = 34,
            ToolTip = "Name used when saving the current quiz settings as a preset",
        };
        Grid.SetColumn(_quizPresetNameTextBox, 2);
        presetRow.Children.Add(_quizPresetNameTextBox);

        var savePreset = new Button { Content = "Save current", MinHeight = 34, Padding = new Thickness(12, 0, 12, 0) };
        savePreset.Click += SaveQuizPreset_Click;
        Grid.SetColumn(savePreset, 4);
        presetRow.Children.Add(savePreset);
        var loadPreset = new Button { Content = "Load", MinHeight = 34, Padding = new Thickness(12, 0, 12, 0) };
        loadPreset.Click += LoadQuizPreset_Click;
        Grid.SetColumn(loadPreset, 6);
        presetRow.Children.Add(loadPreset);
        var deletePreset = new Button { Content = "Delete", MinHeight = 34, Padding = new Thickness(12, 0, 12, 0) };
        deletePreset.Click += DeleteQuizPreset_Click;
        Grid.SetColumn(deletePreset, 8);
        presetRow.Children.Add(deletePreset);

        var appearanceCard = QuizCard(new Thickness(14, 12, 14, 14));
        appearanceCard.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(appearanceCard);
        var appearance = new StackPanel();
        appearanceCard.Child = appearance;
        appearance.Children.Add(new TextBlock
        {
            Text = "Appearance",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });
        appearance.Children.Add(new TextBlock
        {
            Text = "Themes and logo controls are applied to the generated PNG cards that Resolve uses.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        var appearanceRow = new Grid();
        appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        appearance.Children.Add(appearanceRow);

        _quizThemeComboBox = new ComboBox
        {
            ItemsSource = QuizVisualThemeCatalog.DisplayNames,
            SelectedItem = "Dark",
            MinHeight = 34,
        };
        appearanceRow.Children.Add(QuizPreviewLabeledControl("THEME", _quizThemeComboBox));

        _quizLogoPositionComboBox = new ComboBox
        {
            ItemsSource = QuizLogoPositionCatalog.Positions,
            SelectedItem = "Bottom right",
            MinHeight = 34,
        };
        var positionField = QuizPreviewLabeledControl("LOGO POSITION", _quizLogoPositionComboBox);
        Grid.SetColumn(positionField, 2);
        appearanceRow.Children.Add(positionField);

        var scaleStack = new StackPanel();
        var scaleHeader = new Grid();
        scaleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scaleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scaleHeader.Children.Add(new TextBlock
        {
            Text = "LOGO SIZE",
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });
        _quizLogoScaleText = new TextBlock
        {
            Text = "100%",
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetColumn(_quizLogoScaleText, 1);
        scaleHeader.Children.Add(_quizLogoScaleText);
        scaleStack.Children.Add(scaleHeader);
        _quizLogoScaleSlider = new Slider
        {
            Minimum = 0.5,
            Maximum = 2.0,
            Value = 1.0,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 7, 0, 0),
        };
        scaleStack.Children.Add(_quizLogoScaleSlider);
        Grid.SetColumn(scaleStack, 4);
        appearanceRow.Children.Add(scaleStack);

        var previewCard = QuizCard(new Thickness(14, 12, 14, 14));
        root.Children.Add(previewCard);
        var previewLayout = new Grid();
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewCard.Child = previewLayout;

        var previewHeader = new Grid();
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previewHeading = new StackPanel();
        previewHeading.Children.Add(new TextBlock
        {
            Text = "Card preview",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });
        previewHeading.Children.Add(new TextBlock
        {
            Text = "Preview the same card design that will be written into the Resolve package.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
        });
        previewHeader.Children.Add(previewHeading);
        _quizSafeAreaCheckBox = new CheckBox
        {
            Content = "Show safe area",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_quizSafeAreaCheckBox, 1);
        previewHeader.Children.Add(_quizSafeAreaCheckBox);
        previewLayout.Children.Add(previewHeader);

        var previewControls = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        previewControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        previewControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        previewControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        previewControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(previewControls, 1);
        previewLayout.Children.Add(previewControls);

        _quizPreviewCardComboBox = new ComboBox
        {
            ItemsSource = new[] { "Intro", "Question", "Countdown", "Answer reveal", "Explanation", "Outro" },
            SelectedIndex = 1,
            MinHeight = 34,
        };
        previewControls.Children.Add(QuizPreviewLabeledControl("CARD", _quizPreviewCardComboBox));
        _quizPreviewQuestionComboBox = new ComboBox
        {
            MinHeight = 34,
            DisplayMemberPath = nameof(QuizPreviewQuestionChoice.Display),
        };
        var questionField = QuizPreviewLabeledControl("DRAFT QUESTION", _quizPreviewQuestionComboBox);
        Grid.SetColumn(questionField, 2);
        previewControls.Children.Add(questionField);
        var refreshPreview = new Button
        {
            Content = "Refresh preview",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        refreshPreview.Click += (_, _) => RefreshQuizPreview();
        Grid.SetColumn(refreshPreview, 4);
        previewControls.Children.Add(refreshPreview);

        var previewAndPreflight = new Grid();
        previewAndPreflight.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewAndPreflight.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        previewAndPreflight.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        Grid.SetRow(previewAndPreflight, 2);
        previewLayout.Children.Add(previewAndPreflight);

        var previewFrame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8),
            MinHeight = 420,
        };
        previewAndPreflight.Children.Add(previewFrame);
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = 620,
        };
        previewFrame.Child = viewbox;
        _quizPreviewSurface = new Grid { Width = 1920, Height = 1080 };
        viewbox.Child = _quizPreviewSurface;
        _quizPreviewImage = new Image { Stretch = Stretch.Fill };
        _quizPreviewSurface.Children.Add(_quizPreviewImage);
        _quizSafeAreaOverlay = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 251, 146, 60)),
            BorderThickness = new Thickness(5),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _quizPreviewSurface.Children.Add(_quizSafeAreaOverlay);

        var preflightCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
        };
        Grid.SetColumn(preflightCard, 2);
        previewAndPreflight.Children.Add(preflightCard);
        var preflightStack = new StackPanel();
        preflightCard.Child = preflightStack;
        preflightStack.Children.Add(new TextBlock
        {
            Text = "Preflight",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });
        preflightStack.Children.Add(new TextBlock
        {
            Text = "Checks the current draft for text that is likely to wrap tightly or overflow.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });
        _quizPreflightText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            TextWrapping = TextWrapping.Wrap,
        };
        preflightStack.Children.Add(_quizPreflightText);
        var runPreflight = new Button
        {
            Content = "Run preflight",
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 0, 12, 0),
            MinHeight = 32,
        };
        runPreflight.Click += (_, _) => RefreshQuizPreview();
        preflightStack.Children.Add(runPreflight);

        HookQuizPreviewEvents();
        RefreshQuizPresetChoices();
        RefreshQuizPreview();
        return root;
    }

    private void HookQuizPreviewEvents()
    {
        if (_quizPreviewEventsHooked)
            return;
        _quizPreviewEventsHooked = true;

        if (_quizThemeComboBox is not null)
            _quizThemeComboBox.SelectionChanged += (_, _) => RefreshQuizPreview();
        if (_quizLogoPositionComboBox is not null)
            _quizLogoPositionComboBox.SelectionChanged += (_, _) => RefreshQuizPreview();
        if (_quizLogoScaleSlider is not null)
            _quizLogoScaleSlider.ValueChanged += (_, _) => RefreshQuizPreview();
        if (_quizPreviewCardComboBox is not null)
            _quizPreviewCardComboBox.SelectionChanged += (_, _) => RefreshQuizPreview();
        if (_quizPreviewQuestionComboBox is not null)
            _quizPreviewQuestionComboBox.SelectionChanged += (_, _) => RefreshQuizPreview();
        if (_quizSafeAreaCheckBox is not null)
            _quizSafeAreaCheckBox.Checked += (_, _) => UpdateQuizSafeAreaOverlay();
        if (_quizSafeAreaCheckBox is not null)
            _quizSafeAreaCheckBox.Unchecked += (_, _) => UpdateQuizSafeAreaOverlay();
        if (_quizFormatComboBox is not null)
            _quizFormatComboBox.SelectionChanged += (_, _) => RefreshQuizPreview();
        if (_quizTitleTextBox is not null)
            _quizTitleTextBox.TextChanged += (_, _) => RefreshQuizPreview();
        if (_quizSecondsPerQuestionTextBox is not null)
            _quizSecondsPerQuestionTextBox.TextChanged += (_, _) => RefreshQuizPreview();
        if (_quizLogoPathTextBox is not null)
            _quizLogoPathTextBox.TextChanged += (_, _) => RefreshQuizPreview();
        if (_quizCountdownCheckBox is not null)
        {
            _quizCountdownCheckBox.Checked += (_, _) => RefreshQuizPreview();
            _quizCountdownCheckBox.Unchecked += (_, _) => RefreshQuizPreview();
        }
        if (_quizRevealAnimationCheckBox is not null)
        {
            _quizRevealAnimationCheckBox.Checked += (_, _) => RefreshQuizPreview();
            _quizRevealAnimationCheckBox.Unchecked += (_, _) => RefreshQuizPreview();
        }
    }

    private void RefreshQuizPreview()
    {
        if (_quizPreviewImage is null || _quizPreviewSurface is null)
            return;

        try
        {
            RefreshQuizPreviewQuestionChoices();
            var question = SelectedQuizPreviewQuestion() ?? QuizPreviewSampleQuestion();
            var seconds = ParseQuizPreviewInteger(_quizSecondsPerQuestionTextBox?.Text, 8, 2, 60);
            var vertical = _quizFormatComboBox?.SelectedIndex == 1;
            var title = (_quizTitleTextBox?.Text ?? "").Trim();
            if (title.Length == 0)
                title = "Quiz Preview";
            try
            {
                title = ProjectPathSecurity.ValidateSegment(title, "Quiz title");
            }
            catch
            {
                title = "Quiz Preview";
            }

            var logoPath = (_quizLogoPathTextBox?.Text ?? "").Trim();
            if (logoPath.Length > 0 && !File.Exists(logoPath))
                logoPath = "";
            var options = new QuizVideoBuildOptions(
                title,
                QuestionSeconds: seconds,
                AnswerSeconds: 3,
                Vertical: vertical,
                FrameRate: 30,
                QuizLogoPath: logoPath,
                ShowCountdown: _quizCountdownCheckBox?.IsChecked != false,
                AnimateAnswerReveal: _quizRevealAnimationCheckBox?.IsChecked != false);
            var visual = CurrentQuizVisualSettings();
            var kind = SelectedQuizPreviewCardKind();
            var selectedIndex = _quizDraftQuestions.FindIndex(item => item.Id == question.Id);
            var number = selectedIndex >= 0 ? selectedIndex + 1 : 1;
            var total = Math.Max(1, _quizDraftQuestions.Count);

            _quizPreviewImage.Source = new QuizThemedCardRenderer().RenderPreviewBitmap(
                question,
                options,
                visual,
                kind,
                number,
                total);
            _quizPreviewSurface.Width = options.Width;
            _quizPreviewSurface.Height = options.Height;
            if (_quizLogoScaleText is not null)
                _quizLogoScaleText.Text = $"{visual.LogoScale * 100:0}%";
            UpdateQuizSafeAreaOverlay();
            UpdateQuizPreflight(options);
        }
        catch (Exception error)
        {
            if (_quizPreflightText is not null)
                _quizPreflightText.Text = $"Preview unavailable: {error.Message}";
        }
    }

    private void UpdateQuizPreflight(QuizVideoBuildOptions previewOptions)
    {
        if (_quizPreflightText is null)
            return;

        var title = (_quizTitleTextBox?.Text ?? "").Trim();
        var options = previewOptions with { Title = title.Length == 0 ? "Quiz Preview" : title };
        var issues = QuizPreflight.Analyze(_quizDraftQuestions, options);
        var lines = new List<string> { QuizPreflight.Summary(issues) };
        foreach (var issue in issues.Take(6))
            lines.Add($"• {issue.Message}");
        if (issues.Count > 6)
            lines.Add($"• …and {issues.Count - 6} more.");
        _quizPreflightText.Text = string.Join(Environment.NewLine, lines);
        _quizPreflightText.Foreground = issues.Any(issue => issue.Severity == QuizPreflightSeverity.Error)
            ? new SolidColorBrush(Color.FromRgb(180, 35, 24))
            : issues.Count > 0
                ? new SolidColorBrush(Color.FromRgb(181, 71, 8))
                : new SolidColorBrush(Color.FromRgb(21, 128, 61));
    }

    private void UpdateQuizSafeAreaOverlay()
    {
        if (_quizSafeAreaOverlay is null || _quizPreviewSurface is null)
            return;
        var enabled = _quizSafeAreaCheckBox?.IsChecked != false;
        _quizSafeAreaOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
            return;

        var vertical = _quizFormatComboBox?.SelectedIndex == 1;
        var width = _quizPreviewSurface.Width;
        var height = _quizPreviewSurface.Height;
        _quizSafeAreaOverlay.Margin = vertical
            ? new Thickness(width * 0.08, height * 0.08, width * 0.08, height * 0.15)
            : new Thickness(width * 0.05, height * 0.06, width * 0.05, height * 0.06);
    }

    private QuizVisualRenderSettings CurrentQuizVisualSettings() =>
        new QuizVisualRenderSettings(
                QuizVisualThemeCatalog.Normalize(Convert.ToString(_quizThemeComboBox?.SelectedItem) ?? "dark"),
                QuizLogoPositionCatalog.Normalize(Convert.ToString(_quizLogoPositionComboBox?.SelectedItem) ?? "Bottom right"),
                _quizLogoScaleSlider?.Value ?? 1.0)
            .Normalize();

    private void RefreshQuizPreviewQuestionChoices()
    {
        if (_quizPreviewQuestionComboBox is null)
            return;

        var selectedId = (_quizPreviewQuestionComboBox.SelectedItem as QuizPreviewQuestionChoice)?.QuestionId;
        var currentIds = _quizPreviewQuestionComboBox.ItemsSource is IEnumerable<QuizPreviewQuestionChoice> current
            ? current.Select(item => item.QuestionId).ToArray()
            : [];
        var draftIds = _quizDraftQuestions.Select(question => question.Id).ToArray();
        if (currentIds.SequenceEqual(draftIds))
            return;

        var choices = _quizDraftQuestions
            .Select((question, index) => new QuizPreviewQuestionChoice(
                question.Id,
                $"{index + 1}. #{question.Id} — {question.Question}"))
            .ToList();
        _quizPreviewQuestionComboBox.ItemsSource = choices;
        _quizPreviewQuestionComboBox.IsEnabled = choices.Count > 0;
        if (choices.Count > 0)
        {
            _quizPreviewQuestionComboBox.SelectedItem = choices.FirstOrDefault(item => item.QuestionId == selectedId) ?? choices[0];
        }
    }

    private QuizQuestion? SelectedQuizPreviewQuestion()
    {
        var id = (_quizPreviewQuestionComboBox?.SelectedItem as QuizPreviewQuestionChoice)?.QuestionId;
        return id is int questionId
            ? _quizDraftQuestions.FirstOrDefault(question => question.Id == questionId)
            : _quizDraftQuestions.FirstOrDefault();
    }

    private QuizPreviewCardKind SelectedQuizPreviewCardKind() =>
        (_quizPreviewCardComboBox?.SelectedItem?.ToString() ?? "Question") switch
        {
            "Intro" => QuizPreviewCardKind.Intro,
            "Countdown" => QuizPreviewCardKind.Countdown,
            "Answer reveal" => QuizPreviewCardKind.AnswerReveal,
            "Explanation" => QuizPreviewCardKind.Explanation,
            "Outro" => QuizPreviewCardKind.Outro,
            _ => QuizPreviewCardKind.Question,
        };

    private void RefreshQuizPresetChoices(string? selectedName = null)
    {
        if (_quizPresetComboBox is null)
            return;
        var presets = _data.LoadQuizPresets();
        _quizPresetComboBox.ItemsSource = presets;
        _quizPresetComboBox.DisplayMemberPath = nameof(QuizPreset.Name);
        _quizPresetComboBox.SelectedItem = presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? presets.FirstOrDefault();
    }

    private void SaveQuizPreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = (_quizPresetNameTextBox?.Text ?? "").Trim();
            var preset = CaptureQuizPreset(name).Normalize();
            var existing = _data.LoadQuizPresets().FirstOrDefault(item =>
                string.Equals(item.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var overwrite = MessageBox.Show(
                    this,
                    $"Replace the saved preset '{preset.Name}' with the current quiz settings?",
                    "Save Quiz Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                    return;
            }
            _data.UpsertQuizPreset(preset);
            RefreshQuizPresetChoices(preset.Name);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz preset saved: {preset.Name}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Save Quiz Preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadQuizPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_quizPresetComboBox?.SelectedItem is not QuizPreset preset)
            return;
        try
        {
            ApplyQuizPreset(preset.Normalize());
            if (_quizPresetNameTextBox is not null)
                _quizPresetNameTextBox.Text = preset.Name;
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz preset loaded: {preset.Name}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Load Quiz Preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteQuizPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_quizPresetComboBox?.SelectedItem is not QuizPreset preset)
            return;
        var answer = MessageBox.Show(
            this,
            $"Delete the quiz preset '{preset.Name}'?",
            "Delete Quiz Preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        _data.DeleteQuizPreset(preset.Name);
        RefreshQuizPresetChoices();
        if (_quizPageStatusText is not null)
            _quizPageStatusText.Text = $"Quiz preset deleted: {preset.Name}";
    }

    private QuizPreset CaptureQuizPreset(string name) => new()
    {
        Name = name,
        QuestionCount = ParseQuizPreviewInteger(_quizQuestionCountTextBox?.Text, 10, 1, 100),
        QuestionSeconds = ParseQuizPreviewInteger(_quizSecondsPerQuestionTextBox?.Text, 8, 2, 60),
        Category = SelectedQuizCategory(),
        Difficulty = SelectedQuizDifficulty(),
        PreferLeastUsed = _quizPreferLeastUsedCheckBox?.IsChecked == true,
        AvoidRecent = _quizAvoidRecentCheckBox?.IsChecked == true,
        RecentQuizCount = ParseQuizPreviewInteger(_quizRecentQuizCountTextBox?.Text, 5, 1, 50),
        Format = _quizFormatComboBox?.SelectedIndex == 1 ? "vertical" : "landscape",
        ThemeKey = CurrentQuizVisualSettings().ThemeKey,
        LogoPath = (_quizLogoPathTextBox?.Text ?? "").Trim(),
        LogoPosition = CurrentQuizVisualSettings().LogoPosition,
        LogoScale = CurrentQuizVisualSettings().LogoScale,
        ShuffleAnswers = _quizShuffleAnswersCheckBox?.IsChecked == true,
        ShowCountdown = _quizCountdownCheckBox?.IsChecked != false,
        AnimateAnswerReveal = _quizRevealAnimationCheckBox?.IsChecked != false,
        Narrate = _quizNarrationCheckBox?.IsChecked == true,
        NarrateAnswers = _quizNarrateAnswersCheckBox?.IsChecked == true,
        Voice = Convert.ToString(_quizVoiceComboBox?.SelectedItem) ?? "alloy",
        CountdownTicks = _quizCountdownTickCheckBox?.IsChecked == true,
        AnswerRevealSfx = _quizAnswerRevealSfxCheckBox?.IsChecked == true,
        UseBackgroundMusic = _quizBackgroundMusicCheckBox?.IsChecked == true,
        BackgroundMusicPath = (_quizBackgroundMusicPathTextBox?.Text ?? "").Trim(),
    };

    private void ApplyQuizPreset(QuizPreset preset)
    {
        if (_quizQuestionCountTextBox is not null)
            _quizQuestionCountTextBox.Text = preset.QuestionCount.ToString();
        if (_quizSecondsPerQuestionTextBox is not null)
            _quizSecondsPerQuestionTextBox.Text = preset.QuestionSeconds.ToString();
        SelectQuizComboValue(_quizCategoryComboBox, preset.Category, "All categories");
        SelectQuizComboValue(_quizDifficultyComboBox, preset.Difficulty, "All difficulties");
        if (_quizPreferLeastUsedCheckBox is not null)
            _quizPreferLeastUsedCheckBox.IsChecked = preset.PreferLeastUsed;
        if (_quizAvoidRecentCheckBox is not null)
            _quizAvoidRecentCheckBox.IsChecked = preset.AvoidRecent;
        if (_quizRecentQuizCountTextBox is not null)
            _quizRecentQuizCountTextBox.Text = preset.RecentQuizCount.ToString();
        if (_quizFormatComboBox is not null)
            _quizFormatComboBox.SelectedIndex = preset.Format == "vertical" ? 1 : 0;

        var theme = QuizVisualThemeCatalog.Resolve(preset.ThemeKey);
        if (_quizThemeComboBox is not null)
            _quizThemeComboBox.SelectedItem = theme.DisplayName;
        if (_quizLogoPositionComboBox is not null)
            _quizLogoPositionComboBox.SelectedItem = QuizLogoPositionCatalog.Normalize(preset.LogoPosition);
        if (_quizLogoScaleSlider is not null)
            _quizLogoScaleSlider.Value = preset.LogoScale;
        if (_quizLogoPathTextBox is not null)
        {
            _quizLogoPathTextBox.Text = File.Exists(preset.LogoPath) ? preset.LogoPath : "";
            _data.SaveQuizLogoPath(_quizLogoPathTextBox.Text);
        }
        if (_quizShuffleAnswersCheckBox is not null)
            _quizShuffleAnswersCheckBox.IsChecked = preset.ShuffleAnswers;
        if (_quizCountdownCheckBox is not null)
            _quizCountdownCheckBox.IsChecked = preset.ShowCountdown;
        if (_quizRevealAnimationCheckBox is not null)
            _quizRevealAnimationCheckBox.IsChecked = preset.AnimateAnswerReveal;
        if (_quizNarrationCheckBox is not null)
            _quizNarrationCheckBox.IsChecked = preset.Narrate;
        if (_quizNarrateAnswersCheckBox is not null)
            _quizNarrateAnswersCheckBox.IsChecked = preset.NarrateAnswers;
        SelectQuizComboValue(_quizVoiceComboBox, preset.Voice, "alloy");
        if (_quizCountdownTickCheckBox is not null)
            _quizCountdownTickCheckBox.IsChecked = preset.CountdownTicks;
        if (_quizAnswerRevealSfxCheckBox is not null)
            _quizAnswerRevealSfxCheckBox.IsChecked = preset.AnswerRevealSfx;
        if (_quizBackgroundMusicPathTextBox is not null)
            _quizBackgroundMusicPathTextBox.Text = File.Exists(preset.BackgroundMusicPath) ? preset.BackgroundMusicPath : "";
        if (_quizBackgroundMusicCheckBox is not null)
            _quizBackgroundMusicCheckBox.IsChecked = preset.UseBackgroundMusic && File.Exists(preset.BackgroundMusicPath);

        RefreshQuizPreview();
    }

    private static void SelectQuizComboValue(ComboBox? combo, string value, string fallback)
    {
        if (combo is null)
            return;
        foreach (var item in combo.Items)
        {
            if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        foreach (var item in combo.Items)
        {
            if (string.Equals(item?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static int ParseQuizPreviewInteger(string? value, int fallback, int minimum, int maximum) =>
        int.TryParse((value ?? "").Trim(), out var parsed) && parsed >= minimum && parsed <= maximum
            ? parsed
            : fallback;

    private static StackPanel QuizPreviewLabeledControl(string label, FrameworkElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });
        control.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(control);
        return stack;
    }

    private static QuizQuestion QuizPreviewSampleQuestion() => new(
        0,
        "Which is the largest planet in our Solar System?",
        "Earth",
        "Mars",
        "Jupiter",
        "Saturn",
        2,
        "Jupiter is the largest planet in the Solar System.",
        "Space",
        "easy",
        "Preview",
        0,
        true);

    private sealed record QuizPreviewQuestionChoice(int QuestionId, string Display);
}
