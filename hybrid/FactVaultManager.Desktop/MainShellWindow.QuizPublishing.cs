using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private ComboBox? _quizSeriesComboBox;
    private TextBox? _quizEpisodeTextBox;
    private TextBox? _quizYouTubeTitleTextBox;
    private TextBox? _quizYouTubeDescriptionTextBox;
    private TextBox? _quizHashtagsTextBox;
    private TextBox? _quizPinnedCommentTextBox;
    private TextBox? _quizThumbnailHeadlineTextBox;
    private TextBox? _quizThumbnailSubtitleTextBox;
    private Image? _quizThumbnailPreviewImage;
    private TextBlock? _quizPublishChecklistText;
    private TextBlock? _quizPublishingStatusText;
    private bool _quizThumbnailPreviewCurrent;
    private string _quizAutoSeriesName = "General Knowledge Quiz";
    private string _quizAutoThumbnailSubtitle = "GENERAL KNOWLEDGE QUIZ";
    private string _lastQuizExportFolder = "";
    private string _lastQuizThumbnailPath = "";
    private string _lastQuizResolveExportPath = "";
    private int _lastQuizHistoryId;

    private FrameworkElement BuildQuizPublishingPanel()
    {
        var root = new StackPanel();

        var seriesCard = QuizCard(new Thickness(14, 12, 14, 14));
        seriesCard.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(seriesCard);
        var seriesStack = new StackPanel();
        seriesCard.Child = seriesStack;
        seriesStack.Children.Add(new TextBlock
        {
            Text = "Series and episode",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        seriesStack.Children.Add(new TextBlock
        {
            Text = "Type a new series name or reuse one from Quiz History. Episode numbering is suggested from successful exports.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        var seriesRow = new Grid();
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        seriesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seriesStack.Children.Add(seriesRow);

        var seriesField = new StackPanel();
        seriesField.Children.Add(PublishingLabel("SERIES"));
        _quizSeriesComboBox = new ComboBox
        {
            IsEditable = true,
            MinHeight = 34,
            Text = "General Knowledge Quiz",
        };
        seriesField.Children.Add(_quizSeriesComboBox);
        seriesRow.Children.Add(seriesField);

        var episodeField = new StackPanel();
        episodeField.Children.Add(PublishingLabel("EPISODE"));
        _quizEpisodeTextBox = new TextBox
        {
            Text = "1",
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        episodeField.Children.Add(_quizEpisodeTextBox);
        Grid.SetColumn(episodeField, 2);
        seriesRow.Children.Add(episodeField);

        var nextEpisode = new Button
        {
            Content = "Use next episode",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        nextEpisode.Click += (_, _) =>
        {
            SuggestNextQuizEpisode();
            InvalidateQuizThumbnailPreview();
        };
        Grid.SetColumn(nextEpisode, 4);
        seriesRow.Children.Add(nextEpisode);

        var generate = new Button
        {
            Content = "Generate metadata",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
        };
        generate.Click += (_, _) => GenerateQuizPublishingMetadataFromDraft();
        Grid.SetColumn(generate, 6);
        seriesRow.Children.Add(generate);

        var metadataCard = QuizCard(new Thickness(14, 12, 14, 14));
        metadataCard.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(metadataCard);
        var metadataStack = new StackPanel();
        metadataCard.Child = metadataStack;
        metadataStack.Children.Add(new TextBlock
        {
            Text = "YouTube metadata",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        metadataStack.Children.Add(new TextBlock
        {
            Text = "Generate a starting point, then edit anything you want before export. These fields are saved with the Resolve project and Quiz History.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        metadataStack.Children.Add(PublishingLabel("YOUTUBE TITLE"));
        _quizYouTubeTitleTextBox = new TextBox { MinHeight = 34 };
        metadataStack.Children.Add(_quizYouTubeTitleTextBox);

        metadataStack.Children.Add(PublishingLabel("DESCRIPTION"));
        _quizYouTubeDescriptionTextBox = PublishingMultilineBox(150);
        metadataStack.Children.Add(_quizYouTubeDescriptionTextBox);

        metadataStack.Children.Add(PublishingLabel("HASHTAGS"));
        _quizHashtagsTextBox = new TextBox { MinHeight = 34 };
        metadataStack.Children.Add(_quizHashtagsTextBox);

        metadataStack.Children.Add(PublishingLabel("PINNED COMMENT"));
        _quizPinnedCommentTextBox = PublishingMultilineBox(78);
        metadataStack.Children.Add(_quizPinnedCommentTextBox);

        var thumbnailCard = QuizCard(new Thickness(14, 12, 14, 14));
        thumbnailCard.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(thumbnailCard);
        var thumbnailStack = new StackPanel();
        thumbnailCard.Child = thumbnailStack;
        thumbnailStack.Children.Add(new TextBlock
        {
            Text = "YouTube thumbnail",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        thumbnailStack.Children.Add(new TextBlock
        {
            Text = "Create a bold 1280×720 FactBurst thumbnail using the current quiz theme and quiz logo. Edit the copy here; the final PNG is saved with the Resolve export.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        var thumbnailFields = new Grid();
        thumbnailFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        thumbnailFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        thumbnailFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        thumbnailStack.Children.Add(thumbnailFields);

        var headlineField = new StackPanel();
        headlineField.Children.Add(PublishingLabel("HEADLINE"));
        _quizThumbnailHeadlineTextBox = new TextBox
        {
            MinHeight = 34,
            MaxLength = QuizThumbnailSettings.MaxHeadlineLength,
            ToolTip = "Large text shown on the thumbnail",
        };
        headlineField.Children.Add(_quizThumbnailHeadlineTextBox);
        thumbnailFields.Children.Add(headlineField);

        var subtitleField = new StackPanel();
        subtitleField.Children.Add(PublishingLabel("SUBTITLE"));
        _quizThumbnailSubtitleTextBox = new TextBox
        {
            MinHeight = 34,
            MaxLength = QuizThumbnailSettings.MaxSubtitleLength,
            ToolTip = "Category or quiz name shown below the thumbnail headline",
        };
        subtitleField.Children.Add(_quizThumbnailSubtitleTextBox);
        Grid.SetColumn(subtitleField, 2);
        thumbnailFields.Children.Add(subtitleField);

        var thumbnailActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 10),
        };
        var suggestedThumbnail = new Button
        {
            Content = "Use suggested text",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
        };
        suggestedThumbnail.Click += (_, _) => ApplySuggestedQuizThumbnailText();
        thumbnailActions.Children.Add(suggestedThumbnail);
        var refreshThumbnail = new Button
        {
            Content = "Refresh thumbnail preview",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0),
        };
        refreshThumbnail.Click += (_, _) => GenerateQuizThumbnailPreview();
        thumbnailActions.Children.Add(refreshThumbnail);
        thumbnailStack.Children.Add(thumbnailActions);

        var thumbnailPreviewBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _quizThumbnailPreviewImage = new Image
        {
            Width = 640,
            Height = 360,
            Stretch = Stretch.Uniform,
        };
        thumbnailPreviewBorder.Child = _quizThumbnailPreviewImage;
        thumbnailStack.Children.Add(thumbnailPreviewBorder);

        var checklistCard = QuizCard(new Thickness(14, 12, 14, 14));
        root.Children.Add(checklistCard);
        var checklistStack = new StackPanel();
        checklistCard.Child = checklistStack;
        checklistStack.Children.Add(new TextBlock
        {
            Text = "Publishing checklist",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        checklistStack.Children.Add(new TextBlock
        {
            Text = "A quick readiness check for the current quiz. Resolve export and Quiz History become ready only after a successful export.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });
        _quizPublishChecklistText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
        };
        checklistStack.Children.Add(_quizPublishChecklistText);

        _quizPublishingStatusText = new TextBlock
        {
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        checklistStack.Children.Add(_quizPublishingStatusText);

        HookQuizPublishingChangeEvents();
        RefreshQuizPublishingSeries();
        SyncQuizCategorySeriesName();
        SuggestNextQuizEpisode();
        UpdateQuizPublishingChecklist();
        return root;
    }

    private static TextBlock PublishingLabel(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        Margin = new Thickness(0, 8, 0, 4),
    };

    private static TextBox PublishingMultilineBox(double height) => new()
    {
        Height = height,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    private void HookQuizPublishingChangeEvents()
    {
        foreach (var textBox in new[]
                 {
                     _quizEpisodeTextBox,
                     _quizYouTubeTitleTextBox,
                     _quizYouTubeDescriptionTextBox,
                     _quizHashtagsTextBox,
                     _quizPinnedCommentTextBox,
                 })
        {
            if (textBox is not null)
                textBox.TextChanged += (_, _) =>
                {
                    InvalidateQuizThumbnailPreview();
                    InvalidateQuizPublishingExportCompletion();
                    UpdateQuizPublishingChecklist();
                };
        }

        foreach (var textBox in new[] { _quizThumbnailHeadlineTextBox, _quizThumbnailSubtitleTextBox })
        {
            if (textBox is not null)
                textBox.TextChanged += (_, _) =>
                {
                    _quizThumbnailPreviewCurrent = false;
                    InvalidateQuizPublishingExportCompletion();
                    UpdateQuizPublishingChecklist();
                };
        }
    }

    private void RefreshQuizPublishingPage()
    {
        RefreshQuizPublishingSeries();
        SyncQuizCategorySeriesName();
        _quizThumbnailPreviewCurrent = false;
        if (_quizDraftQuestions.Count == 0)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = "Build a quiz draft first, then generate publishing metadata and a thumbnail.";
            UpdateQuizPublishingChecklist();
            return;
        }

        if (string.IsNullOrWhiteSpace(_quizYouTubeTitleTextBox?.Text))
            GenerateQuizPublishingMetadataFromDraft(showErrors: false);
        else
            GenerateQuizThumbnailPreview(showErrors: false);
        UpdateQuizPublishingChecklist();
    }

    private void RefreshQuizPublishingSeries()
    {
        if (_quizSeriesComboBox is null)
            return;

        var current = _quizSeriesComboBox.Text.Trim();
        try
        {
            var names = _data.GetQuizSeriesNames();
            _quizSeriesComboBox.ItemsSource = null;
            _quizSeriesComboBox.ItemsSource = names;
            _quizSeriesComboBox.Text = current.Length > 0
                ? current
                : names.FirstOrDefault() ?? "General Knowledge Quiz";
        }
        catch (Exception error)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Series history: {error.Message}";
        }
    }

    private void SyncQuizCategorySeriesName()
    {
        var suggested = QuizPublishMetadataGenerator.SuggestSeriesName(SelectedQuizCategory());
        var previousAuto = _quizAutoSeriesName;
        var currentSeries = (_quizSeriesComboBox?.Text ?? "").Trim();
        if (_quizSeriesComboBox is not null &&
            (currentSeries.Length == 0 ||
             string.Equals(currentSeries, previousAuto, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(currentSeries, "General Knowledge Quiz", StringComparison.OrdinalIgnoreCase)))
        {
            _quizSeriesComboBox.Text = suggested;
        }

        if (_quizTitleTextBox is not null)
        {
            var currentTitle = _quizTitleTextBox.Text.Trim();
            if (currentTitle.Length == 0 ||
                string.Equals(currentTitle, previousAuto, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentTitle, "General Knowledge Quiz", StringComparison.OrdinalIgnoreCase) ||
                currentTitle.StartsWith(previousAuto + " #", StringComparison.OrdinalIgnoreCase))
            {
                _quizTitleTextBox.Text = suggested;
            }
        }

        _quizAutoSeriesName = suggested;
    }

    private void SuggestNextQuizEpisode()
    {
        if (_quizSeriesComboBox is null || _quizEpisodeTextBox is null)
            return;

        try
        {
            var series = QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox.Text);
            _quizSeriesComboBox.Text = series;
            _quizEpisodeTextBox.Text = _data.GetNextQuizSeriesEpisode(series).ToString();
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Next episode for {series}: #{int.Parse(_quizEpisodeTextBox.Text):000}.";
        }
        catch (Exception error)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = error.Message;
        }
        UpdateQuizPublishingChecklist();
    }

    private void GenerateQuizPublishingMetadataFromDraft(bool showErrors = true)
    {
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Build a quiz draft before generating publishing metadata.");
            SyncQuizCategorySeriesName();
            var series = QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox?.Text);
            if (!int.TryParse((_quizEpisodeTextBox?.Text ?? "").Trim(), out var episode))
                throw new ArgumentException("Episode number must be a whole number from 1 to 9999.");
            var vertical = _quizFormatComboBox?.SelectedIndex == 1;
            var metadata = QuizPublishMetadataGenerator.Generate(series, episode, _quizDraftQuestions, vertical);
            var previousResolveTitle = (_quizTitleTextBox?.Text ?? "").Trim();
            ApplyQuizPublishingMetadata(metadata);
            if (_quizTitleTextBox is not null &&
                (previousResolveTitle.Length == 0 ||
                 string.Equals(previousResolveTitle, "General Knowledge Quiz", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(previousResolveTitle, series, StringComparison.OrdinalIgnoreCase)))
            {
                _quizTitleTextBox.Text = $"{metadata.SeriesName} {metadata.EpisodeLabel}";
            }
            EnsureQuizThumbnailDefaults(metadata);
            GenerateQuizThumbnailPreview(showErrors: false);
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Generated metadata and thumbnail preview for {metadata.SeriesName} {metadata.EpisodeLabel}. You can edit every field before export.";
        }
        catch (Exception error)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = error.Message;
            if (showErrors)
                MessageBox.Show(this, error.Message, "Quiz Publishing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateQuizPublishingChecklist();
    }

    private QuizPublishMetadata CurrentQuizPublishMetadata(
        IReadOnlyList<QuizQuestion> questions,
        bool vertical)
    {
        var currentSeries = QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox?.Text);
        var series = QuizPublishMetadataGenerator.SuggestSeriesNameForQuestions(questions);
        var seriesChanged = !string.Equals(currentSeries, series, StringComparison.OrdinalIgnoreCase);
        var episodeText = (_quizEpisodeTextBox?.Text ?? "").Trim();
        var episode = !seriesChanged && int.TryParse(episodeText, out var parsed)
            ? parsed
            : _data.GetNextQuizSeriesEpisode(series);
        var titleMatchesSeries = QuizPublishMetadataGenerator.TitleMatchesSeries(
            _quizYouTubeTitleTextBox?.Text,
            series);

        if (seriesChanged ||
            !titleMatchesSeries ||
            string.IsNullOrWhiteSpace(_quizYouTubeDescriptionTextBox?.Text) ||
            string.IsNullOrWhiteSpace(_quizHashtagsTextBox?.Text) ||
            string.IsNullOrWhiteSpace(_quizPinnedCommentTextBox?.Text))
        {
            var generated = QuizPublishMetadataGenerator.Generate(series, episode, questions, vertical);
            ApplyQuizPublishingMetadata(generated);
            if (_quizTitleTextBox is not null &&
                !QuizPublishMetadataGenerator.TitleMatchesSeries(_quizTitleTextBox.Text, series))
            {
                _quizTitleTextBox.Text = $"{generated.SeriesName} {generated.EpisodeLabel}";
            }
            _quizAutoSeriesName = series;
            return generated;
        }

        return QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
            series,
            episode,
            _quizYouTubeTitleTextBox!.Text,
            _quizYouTubeDescriptionTextBox!.Text,
            _quizHashtagsTextBox!.Text,
            _quizPinnedCommentTextBox!.Text));
    }

    private QuizThumbnailSettings CurrentQuizThumbnailSettings(QuizPublishMetadata metadata)
    {
        EnsureQuizThumbnailDefaults(metadata);
        var suggested = QuizThumbnailDefaults.Create(metadata, _quizDraftQuestions.Count);
        return new QuizThumbnailSettings(
            string.IsNullOrWhiteSpace(_quizThumbnailHeadlineTextBox?.Text)
                ? suggested.Headline
                : _quizThumbnailHeadlineTextBox.Text,
            string.IsNullOrWhiteSpace(_quizThumbnailSubtitleTextBox?.Text)
                ? suggested.Subtitle
                : _quizThumbnailSubtitleTextBox.Text).Normalize();
    }

    private void EnsureQuizThumbnailDefaults(QuizPublishMetadata metadata)
    {
        if (_quizDraftQuestions.Count == 0)
            return;
        var suggested = QuizThumbnailDefaults.Create(metadata, _quizDraftQuestions.Count);
        if (_quizThumbnailHeadlineTextBox is not null && string.IsNullOrWhiteSpace(_quizThumbnailHeadlineTextBox.Text))
            _quizThumbnailHeadlineTextBox.Text = suggested.Headline;
        if (_quizThumbnailSubtitleTextBox is not null &&
            QuizThumbnailDefaults.ShouldReplaceSubtitle(
                _quizThumbnailSubtitleTextBox.Text,
                _quizAutoThumbnailSubtitle))
        {
            _quizThumbnailSubtitleTextBox.Text = suggested.Subtitle;
        }
        _quizAutoThumbnailSubtitle = suggested.Subtitle;
    }

    private void ApplySuggestedQuizThumbnailText()
    {
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Build a quiz draft first.");
            var metadata = CurrentQuizPublishMetadata(_quizDraftQuestions, _quizFormatComboBox?.SelectedIndex == 1);
            var suggested = QuizThumbnailDefaults.Create(metadata, _quizDraftQuestions.Count);
            if (_quizThumbnailHeadlineTextBox is not null)
                _quizThumbnailHeadlineTextBox.Text = suggested.Headline;
            if (_quizThumbnailSubtitleTextBox is not null)
                _quizThumbnailSubtitleTextBox.Text = suggested.Subtitle;
            _quizAutoThumbnailSubtitle = suggested.Subtitle;
            GenerateQuizThumbnailPreview();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Thumbnail", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GenerateQuizThumbnailPreview(bool showErrors = true)
    {
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Build a quiz draft before generating a thumbnail.");
            var vertical = _quizFormatComboBox?.SelectedIndex == 1;
            var metadata = CurrentQuizPublishMetadata(_quizDraftQuestions, vertical);
            var thumbnail = CurrentQuizThumbnailSettings(metadata);
            var visual = CurrentQuizVisualSettings();
            var logoPath = (_quizLogoPathTextBox?.Text ?? "").Trim();
            var bitmap = new QuizThumbnailRenderer().RenderPreview(
                metadata,
                _quizDraftQuestions,
                thumbnail,
                visual,
                logoPath,
                vertical);
            if (_quizThumbnailPreviewImage is not null)
                _quizThumbnailPreviewImage.Source = bitmap;
            _quizThumbnailPreviewCurrent = true;
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = vertical
                    ? "Thumbnail preview ready • 1080×1920 Shorts PNG will be written during a successful Resolve export."
                    : "Thumbnail preview ready • 1280×720 PNG will be written during a successful Resolve export.";
        }
        catch (Exception error)
        {
            _quizThumbnailPreviewCurrent = false;
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Thumbnail: {error.Message}";
            if (showErrors)
                MessageBox.Show(this, error.Message, "Quiz Thumbnail", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateQuizPublishingChecklist();
    }

    private void InvalidateQuizThumbnailPreview()
    {
        _quizThumbnailPreviewCurrent = false;
    }

    private void InvalidateQuizPublishingExportCompletion()
    {
        _lastQuizExportFolder = "";
        _lastQuizThumbnailPath = "";
        _lastQuizResolveExportPath = "";
        _lastQuizHistoryId = 0;
    }

    private void MarkQuizPublishingExportComplete(
        string projectFolder,
        string thumbnailPath,
        string resolveExportPath,
        int historyId)
    {
        _lastQuizExportFolder = (projectFolder ?? "").Trim();
        _lastQuizThumbnailPath = (thumbnailPath ?? "").Trim();
        _lastQuizResolveExportPath = (resolveExportPath ?? "").Trim();
        _lastQuizHistoryId = Math.Max(0, historyId);
        _quizThumbnailPreviewCurrent = _lastQuizThumbnailPath.Length > 0 && File.Exists(_lastQuizThumbnailPath);
        UpdateQuizPublishingChecklist();
    }

    private void UpdateQuizPublishingChecklist()
    {
        if (_quizPublishChecklistText is null)
            return;

        var youtubeTitleReady = PublishingTextReady(
            _quizYouTubeTitleTextBox?.Text,
            QuizPublishMetadataGenerator.MaxTitleLength);
        var descriptionReady = PublishingTextReady(
            _quizYouTubeDescriptionTextBox?.Text,
            QuizPublishMetadataGenerator.MaxDescriptionLength);
        var hashtagsReady = TryValidateCurrentQuizHashtags();
        var pinnedCommentReady = PublishingTextReady(
            _quizPinnedCommentTextBox?.Text,
            QuizPublishMetadataGenerator.MaxPinnedCommentLength);
        var resolveExportReady = _lastQuizResolveExportPath.Length > 0 && File.Exists(_lastQuizResolveExportPath);
        var historyRecorded = TryCheckLastQuizHistoryEntry();

        var items = QuizPublishChecklist.Evaluate(
            _quizDraftQuestions.Count,
            youtubeTitleReady,
            descriptionReady,
            hashtagsReady,
            pinnedCommentReady,
            _quizThumbnailPreviewCurrent,
            resolveExportReady,
            historyRecorded);
        _quizPublishChecklistText.Text = QuizPublishChecklist.Format(items);
    }

    private static bool PublishingTextReady(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        return text.Length > 0 && text.Length <= maxLength;
    }

    private bool TryValidateCurrentQuizHashtags()
    {
        try
        {
            QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
                "Checklist",
                1,
                "Checklist title",
                "Checklist description",
                _quizHashtagsTextBox?.Text ?? "",
                "Checklist pinned comment"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryCheckLastQuizHistoryEntry()
    {
        if (_lastQuizHistoryId <= 0 || _lastQuizExportFolder.Length == 0)
            return false;

        try
        {
            return _data.GetQuizHistory(500).Any(history =>
                history.Id == _lastQuizHistoryId &&
                string.Equals(
                    Path.GetFullPath(history.ProjectFolder),
                    Path.GetFullPath(_lastQuizExportFolder),
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private bool TryValidateCurrentQuizPublishingMetadata()
    {
        try
        {
            if (_quizDraftQuestions.Count == 0 ||
                !int.TryParse((_quizEpisodeTextBox?.Text ?? "").Trim(), out var episode))
            {
                return false;
            }
            QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
                QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox?.Text),
                episode,
                _quizYouTubeTitleTextBox?.Text ?? "",
                _quizYouTubeDescriptionTextBox?.Text ?? "",
                _quizHashtagsTextBox?.Text ?? "",
                _quizPinnedCommentTextBox?.Text ?? ""));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryCheckCurrentQuizPreflight()
    {
        try
        {
            if (_quizDraftQuestions.Count == 0 ||
                !int.TryParse((_quizSecondsPerQuestionTextBox?.Text ?? "").Trim(), out var seconds) ||
                seconds is < 2 or > 60)
            {
                return false;
            }
            var title = ProjectPathSecurity.ValidateSegment(_quizTitleTextBox?.Text ?? "", "Quiz title");
            var options = new QuizVideoBuildOptions(
                title,
                QuestionSeconds: seconds,
                AnswerSeconds: 3,
                Vertical: _quizFormatComboBox?.SelectedIndex == 1,
                FrameRate: 30,
                QuizLogoPath: (_quizLogoPathTextBox?.Text ?? "").Trim(),
                ShowCountdown: _quizCountdownCheckBox?.IsChecked != false,
                AnimateAnswerReveal: _quizRevealAnimationCheckBox?.IsChecked != false);
            return QuizPreflight.Analyze(_quizDraftQuestions, options)
                .All(issue => issue.Severity != QuizPreflightSeverity.Error);
        }
        catch
        {
            return false;
        }
    }

    private bool TryCheckQuizExportSettings()
    {
        try
        {
            ProjectPathSecurity.ValidateSegment(_quizTitleTextBox?.Text ?? "", "Quiz title");
            var settings = _data.LoadSettings();
            return !string.IsNullOrWhiteSpace(settings.ProjectsFolder);
        }
        catch
        {
            return false;
        }
    }

    private void ApplyQuizPublishingMetadata(QuizPublishMetadata metadata)
    {
        if (_quizSeriesComboBox is not null)
            _quizSeriesComboBox.Text = metadata.SeriesName;
        if (_quizEpisodeTextBox is not null)
            _quizEpisodeTextBox.Text = metadata.EpisodeNumber.ToString();
        if (_quizYouTubeTitleTextBox is not null)
            _quizYouTubeTitleTextBox.Text = metadata.YouTubeTitle;
        if (_quizYouTubeDescriptionTextBox is not null)
            _quizYouTubeDescriptionTextBox.Text = metadata.Description;
        if (_quizHashtagsTextBox is not null)
            _quizHashtagsTextBox.Text = metadata.Hashtags;
        if (_quizPinnedCommentTextBox is not null)
            _quizPinnedCommentTextBox.Text = metadata.PinnedComment;
    }
}
