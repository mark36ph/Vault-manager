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
    private TextBlock? _quizPublishingStatusText;

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
        nextEpisode.Click += (_, _) => SuggestNextQuizEpisode();
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

        _quizPublishingStatusText = new TextBlock
        {
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        metadataStack.Children.Add(_quizPublishingStatusText);

        RefreshQuizPublishingSeries();
        SuggestNextQuizEpisode();
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

    private void RefreshQuizPublishingPage()
    {
        RefreshQuizPublishingSeries();
        if (_quizDraftQuestions.Count == 0)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = "Build a quiz draft first, then generate publishing metadata.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_quizYouTubeTitleTextBox?.Text))
            GenerateQuizPublishingMetadataFromDraft(showErrors: false);
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
    }

    private void GenerateQuizPublishingMetadataFromDraft(bool showErrors = true)
    {
        try
        {
            if (_quizDraftQuestions.Count == 0)
                throw new InvalidOperationException("Build a quiz draft before generating publishing metadata.");
            var series = QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox?.Text);
            if (!int.TryParse((_quizEpisodeTextBox?.Text ?? "").Trim(), out var episode))
                throw new ArgumentException("Episode number must be a whole number from 1 to 9999.");
            var vertical = _quizFormatComboBox?.SelectedIndex == 1;
            var metadata = QuizPublishMetadataGenerator.Generate(series, episode, _quizDraftQuestions, vertical);
            ApplyQuizPublishingMetadata(metadata);
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Generated metadata for {metadata.SeriesName} {metadata.EpisodeLabel}. You can edit every field before export.";
        }
        catch (Exception error)
        {
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = error.Message;
            if (showErrors)
                MessageBox.Show(this, error.Message, "Quiz Publishing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private QuizPublishMetadata CurrentQuizPublishMetadata(
        IReadOnlyList<QuizQuestion> questions,
        bool vertical)
    {
        var series = QuizPublishMetadataGenerator.NormalizeSeriesName(_quizSeriesComboBox?.Text);
        var episodeText = (_quizEpisodeTextBox?.Text ?? "").Trim();
        var episode = int.TryParse(episodeText, out var parsed)
            ? parsed
            : _data.GetNextQuizSeriesEpisode(series);

        if (string.IsNullOrWhiteSpace(_quizYouTubeTitleTextBox?.Text) ||
            string.IsNullOrWhiteSpace(_quizYouTubeDescriptionTextBox?.Text) ||
            string.IsNullOrWhiteSpace(_quizHashtagsTextBox?.Text) ||
            string.IsNullOrWhiteSpace(_quizPinnedCommentTextBox?.Text))
        {
            var generated = QuizPublishMetadataGenerator.Generate(series, episode, questions, vertical);
            ApplyQuizPublishingMetadata(generated);
            return generated;
        }

        return QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
            series,
            episode,
            _quizYouTubeTitleTextBox.Text,
            _quizYouTubeDescriptionTextBox.Text,
            _quizHashtagsTextBox.Text,
            _quizPinnedCommentTextBox.Text));
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
