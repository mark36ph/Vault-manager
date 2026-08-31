using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool WebsiteSeoPublishingAutoRegistered = RegisterWebsiteSeoPublishing();
    private bool _websiteSeoPublishingInitialized;
    private bool _websiteSeoSelectionHooked;
    private DispatcherTimer? _websiteSeoPublishingTimer;
    private Button? _websiteSeoButton;

    private static bool RegisterWebsiteSeoPublishing()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WebsiteSeoPublishingWindow_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void WebsiteSeoPublishingWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MainShellWindow window)
            window.InitializeWebsiteSeoPublishingControls();
    }

    public void InitializeWebsiteSeoPublishingControls()
    {
        if (_websiteSeoPublishingInitialized) return;
        _websiteSeoPublishingInitialized = true;

        _websiteSeoPublishingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _websiteSeoPublishingTimer.Tick += (_, _) => EnsureWebsiteSeoPublishingControls();
        _websiteSeoPublishingTimer.Start();
        Closed += (_, _) => _websiteSeoPublishingTimer?.Stop();

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteSeoPublishingControls));
    }

    private void EnsureWebsiteSeoPublishingControls()
    {
        if (_websiteSeoButton?.Parent is not null)
        {
            HookWebsiteSeoSelection();
            _websiteSeoPublishingTimer?.Stop();
            return;
        }

        if (_websiteResyncButton?.Parent is not Grid footer) return;

        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _websiteSeoButton = new Button
        {
            Content = "Edit SEO",
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false,
            ToolTip = "Edit the selected quiz search and social metadata. The published slug stays locked so existing links and analytics remain intact.",
        };
        _websiteSeoButton.Click += async (_, _) => await EditSelectedWebsiteSeoAsync();
        Grid.SetColumn(_websiteSeoButton, footer.ColumnDefinitions.Count - 1);
        footer.Children.Add(_websiteSeoButton);

        HookWebsiteSeoSelection();
        UpdateWebsiteSeoButtonState();
        _websiteSeoPublishingTimer?.Stop();
    }

    private void HookWebsiteSeoSelection()
    {
        if (_websiteSeoSelectionHooked || _websiteManagerGrid is null) return;
        _websiteSeoSelectionHooked = true;
        _websiteManagerGrid.SelectionChanged += (_, _) => UpdateWebsiteSeoButtonState();
        UpdateWebsiteSeoButtonState();
    }

    private void UpdateWebsiteSeoButtonState()
    {
        if (_websiteSeoButton is not null)
            _websiteSeoButton.IsEnabled = _websiteManagerGrid?.SelectedItem is WebsiteManagerQuizRow;
    }

    private async Task EditSelectedWebsiteSeoAsync()
    {
        if (_websiteManagerGrid?.SelectedItem is not WebsiteManagerQuizRow row) return;
        const string title = "Website Quiz SEO";

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_websiteSeoButton is not null) _websiteSeoButton.IsEnabled = false;
            if (_websiteStatusText is not null) _websiteStatusText.Text = $"Loading SEO for {row.Title}…";

            using var client = new FactburstWebsiteSeoAdminClient();
            var quizzes = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey);
            var selected = quizzes.FirstOrDefault(quiz =>
                string.Equals(quiz.Slug, row.Slug, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
                throw new InvalidOperationException("The selected website quiz could not be found in Cloudflare.");

            var dialog = new WebsiteQuizSeoDialog(selected, quizzes)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true || dialog.ResultValues is null)
            {
                if (_websiteStatusText is not null) _websiteStatusText.Text = "SEO editing cancelled.";
                return;
            }

            if (_websiteStatusText is not null) _websiteStatusText.Text = $"Saving SEO for {row.Title}…";
            await client.UpdateAsync(tracker.BaseUrl, tracker.ApiKey, selected.Slug, dialog.ResultValues);
            if (_websiteStatusText is not null)
                _websiteStatusText.Text = $"SEO saved for {row.Title}. Search and social metadata will use the new copy immediately; social-image caches may take a few minutes to refresh.";
            await RefreshWebsiteManagerAsync(false);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            if (_websiteStatusText is not null) _websiteStatusText.Text = "SEO update failed: " + error.Message;
        }
        finally
        {
            UpdateWebsiteSeoButtonState();
        }
    }
}

public sealed class WebsiteQuizSeoDialog : Window
{
    private readonly FactburstWebsiteSeoQuiz _quiz;
    private readonly IReadOnlyList<FactburstWebsiteSeoQuiz> _allQuizzes;
    private readonly FactburstWebsiteSeoValues _suggested;
    private readonly TextBox _seoTitle;
    private readonly TextBox _seoDescription;
    private readonly TextBox _socialTitle;
    private readonly TextBox _socialDescription;
    private readonly TextBlock _seoTitleCount;
    private readonly TextBlock _seoDescriptionCount;
    private readonly TextBlock _socialTitleCount;
    private readonly TextBlock _socialDescriptionCount;
    private readonly TextBlock _searchTitlePreview;
    private readonly TextBlock _searchDescriptionPreview;
    private readonly TextBlock _socialTitlePreview;
    private readonly TextBlock _socialDescriptionPreview;
    private readonly TextBlock _warningText;
    private readonly Button _saveButton;

    public FactburstWebsiteSeoValues? ResultValues { get; private set; }

    public WebsiteQuizSeoDialog(
        FactburstWebsiteSeoQuiz quiz,
        IReadOnlyList<FactburstWebsiteSeoQuiz> allQuizzes)
    {
        _quiz = quiz ?? throw new ArgumentNullException(nameof(quiz));
        _allQuizzes = allQuizzes ?? Array.Empty<FactburstWebsiteSeoQuiz>();
        _suggested = FactburstWebsiteSeoDefaults.Create(quiz);
        var effective = FactburstWebsiteSeoDefaults.Effective(quiz);

        Title = $"SEO • {quiz.Title}";
        Width = 860;
        Height = 820;
        MinWidth = 720;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        scroll.Content = stack;
        root.Children.Add(scroll);

        stack.Children.Add(new TextBlock
        {
            Text = "Search & social publishing",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "These fields control the search-result title/description and the text used by Facebook, X, Discord and other link previews. Suggested values are generated automatically for every website quiz.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        });

        var slugCard = Card();
        var slugStack = new StackPanel();
        slugCard.Child = slugStack;
        slugStack.Children.Add(Label("PUBLISHED URL"));
        var slugBox = new TextBox
        {
            Text = FactburstWebsiteSeoDefaults.CleanQuizUrl(quiz.Slug),
            IsReadOnly = true,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
        };
        slugStack.Children.Add(slugBox);
        slugStack.Children.Add(new TextBlock
        {
            Text = "The slug is generated when the quiz is published and is locked afterwards. Keeping it stable preserves shared links, tracking, analytics, comments and resync history.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        stack.Children.Add(slugCard);

        var fieldsCard = Card(new Thickness(0, 12, 0, 0));
        var fields = new StackPanel();
        fieldsCard.Child = fields;
        fields.Children.Add(new TextBlock
        {
            Text = "Search metadata",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        });

        var seoTitleRow = FieldHeader("SEO TITLE", out _seoTitleCount);
        fields.Children.Add(seoTitleRow);
        _seoTitle = new TextBox { Text = effective.SeoTitle, MinHeight = 34, MaxLength = 120 };
        fields.Children.Add(_seoTitle);

        var seoDescriptionRow = FieldHeader("META DESCRIPTION", out _seoDescriptionCount);
        fields.Children.Add(seoDescriptionRow);
        _seoDescription = Multiline(effective.SeoDescription, 86, 300);
        fields.Children.Add(_seoDescription);

        fields.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 8) });
        fields.Children.Add(new TextBlock
        {
            Text = "Social preview copy",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        });

        var socialTitleRow = FieldHeader("SOCIAL TITLE", out _socialTitleCount);
        fields.Children.Add(socialTitleRow);
        _socialTitle = new TextBox { Text = effective.SocialTitle, MinHeight = 34, MaxLength = 160 };
        fields.Children.Add(_socialTitle);

        var socialDescriptionRow = FieldHeader("SOCIAL DESCRIPTION", out _socialDescriptionCount);
        fields.Children.Add(socialDescriptionRow);
        _socialDescription = Multiline(effective.SocialDescription, 76, 300);
        fields.Children.Add(_socialDescription);
        stack.Children.Add(fieldsCard);

        var previewCard = Card(new Thickness(0, 12, 0, 0));
        var previews = new StackPanel();
        previewCard.Child = previews;
        previews.Children.Add(new TextBlock
        {
            Text = "Search preview",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        });
        previews.Children.Add(new TextBlock
        {
            Text = FactburstWebsiteSeoDefaults.CleanQuizUrl(quiz.Slug),
            Foreground = new SolidColorBrush(Color.FromRgb(32, 128, 71)),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 2),
        });
        _searchTitlePreview = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(26, 13, 171)),
            FontSize = 19,
            TextWrapping = TextWrapping.Wrap,
        };
        previews.Children.Add(_searchTitlePreview);
        _searchDescriptionPreview = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(77, 81, 86)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        previews.Children.Add(_searchDescriptionPreview);

        previews.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 12) });
        previews.Children.Add(new TextBlock
        {
            Text = "Social card preview",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        });
        var socialCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 18, 32)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Margin = new Thickness(0, 8, 0, 8),
            MinHeight = 220,
        };
        var socialStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        socialStack.Children.Add(new TextBlock
        {
            Text = "FACTBURST QUIZ",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 220, 255)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        _socialTitlePreview = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 27,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 10),
        };
        socialStack.Children.Add(_socialTitlePreview);
        _socialDescriptionPreview = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        };
        socialStack.Children.Add(_socialDescriptionPreview);
        socialStack.Children.Add(new TextBlock
        {
            Text = $"{Math.Max(1, quiz.QuestionCount)} QUESTIONS  •  {quiz.Category.ToUpperInvariant()}",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 220, 255)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 0),
        });
        socialCard.Child = socialStack;
        previews.Children.Add(socialCard);
        previews.Children.Add(new TextBlock
        {
            Text = "Generated 1200×630 PNG: " + FactburstWebsiteSeoDefaults.SocialImageUrl(quiz.Slug),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(previewCard);

        _warningText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 12, 2, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
        };
        stack.Children.Add(_warningText);

        var buttons = new Grid
        {
            Background = Brushes.White,
            Margin = new Thickness(0),
        };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var useSuggested = new Button
        {
            Content = "Use suggested",
            MinWidth = 112,
            MinHeight = 36,
            Margin = new Thickness(24, 12, 8, 12),
        };
        useSuggested.Click += (_, _) => ApplySuggested();
        buttons.Children.Add(useSuggested);

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            MinHeight = 36,
            Margin = new Thickness(0, 12, 8, 12),
            IsCancel = true,
        };
        Grid.SetColumn(cancel, 2);
        buttons.Children.Add(cancel);

        _saveButton = new Button
        {
            Content = "Save SEO",
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(0, 12, 24, 12),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
        };
        _saveButton.Click += (_, _) => SaveAndClose();
        Grid.SetColumn(_saveButton, 3);
        buttons.Children.Add(_saveButton);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        foreach (var box in new[] { _seoTitle, _seoDescription, _socialTitle, _socialDescription })
            box.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private void ApplySuggested()
    {
        _seoTitle.Text = _suggested.SeoTitle;
        _seoDescription.Text = _suggested.SeoDescription;
        _socialTitle.Text = _suggested.SocialTitle;
        _socialDescription.Text = _suggested.SocialDescription;
    }

    private void SaveAndClose()
    {
        var values = CurrentValues();
        if (values.SeoTitle.Length == 0 || values.SeoDescription.Length == 0 ||
            values.SocialTitle.Length == 0 || values.SocialDescription.Length == 0)
        {
            MessageBox.Show(this, "SEO title, description and social preview fields cannot be blank.",
                "Website Quiz SEO", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultValues = values;
        DialogResult = true;
    }

    private void UpdatePreview()
    {
        var values = CurrentValues();
        _seoTitleCount.Text = $"{values.SeoTitle.Length}/65 recommended";
        _seoDescriptionCount.Text = $"{values.SeoDescription.Length}/160 recommended";
        _socialTitleCount.Text = $"{values.SocialTitle.Length}/100 recommended";
        _socialDescriptionCount.Text = $"{values.SocialDescription.Length}/200 recommended";

        _searchTitlePreview.Text = values.SeoTitle.Length > 0 ? values.SeoTitle : "SEO title will appear here";
        _searchDescriptionPreview.Text = values.SeoDescription.Length > 0 ? values.SeoDescription : "Meta description will appear here.";
        _socialTitlePreview.Text = values.SocialTitle.Length > 0 ? values.SocialTitle : _quiz.Title;
        _socialDescriptionPreview.Text = values.SocialDescription.Length > 0 ? values.SocialDescription : _quiz.Description;

        var warnings = new List<string>();
        if (values.SeoTitle.Length == 0 || values.SeoDescription.Length == 0 ||
            values.SocialTitle.Length == 0 || values.SocialDescription.Length == 0)
            warnings.Add("Required SEO fields cannot be blank.");
        if (values.SeoTitle.Length > FactburstWebsiteSeoDefaults.RecommendedTitleLength)
            warnings.Add("SEO title is longer than the usual search-result title range and may be truncated.");
        if (values.SeoDescription.Length < 70)
            warnings.Add("SEO description is quite short; aim for roughly 120–160 characters when useful.");
        if (values.SeoDescription.Length > FactburstWebsiteSeoDefaults.RecommendedDescriptionLength)
            warnings.Add("SEO description is longer than the usual search snippet and may be truncated.");
        if (values.SocialTitle.Length > FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength)
            warnings.Add("Social title is long and may wrap heavily on preview cards.");

        var duplicateSeo = _allQuizzes
            .Where(item => !string.Equals(item.Slug, _quiz.Slug, StringComparison.OrdinalIgnoreCase))
            .Select(FactburstWebsiteSeoDefaults.Effective)
            .Any(item => string.Equals(item.SeoTitle.Trim(), values.SeoTitle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicateSeo && values.SeoTitle.Length > 0)
            warnings.Add("Another website quiz already uses this SEO title. Unique titles are better for search results.");

        var duplicateQuizTitle = _allQuizzes.Any(item =>
            !string.Equals(item.Slug, _quiz.Slug, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Title.Trim(), _quiz.Title.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicateQuizTitle)
            warnings.Add("Another website quiz has the same visible quiz title. Consider making this SEO title more specific.");

        var duplicateSlug = _allQuizzes.Count(item =>
            string.Equals(item.Slug, _quiz.Slug, StringComparison.OrdinalIgnoreCase)) > 1;
        if (duplicateSlug)
            warnings.Add("Duplicate slug detected in the website inventory. Resolve this before publishing more copies.");

        _warningText.Text = warnings.Count == 0
            ? "✓ SEO looks ready. The published slug is stable and the metadata is unique in the current website inventory."
            : string.Join("\n", warnings.Select(message => "• " + message));
        _warningText.Foreground = warnings.Count == 0
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : new SolidColorBrush(Color.FromRgb(180, 83, 9));
        _saveButton.IsEnabled = values.SeoTitle.Length > 0 && values.SeoDescription.Length > 0 &&
                                values.SocialTitle.Length > 0 && values.SocialDescription.Length > 0;
    }

    private FactburstWebsiteSeoValues CurrentValues() => new(
        Compact(_seoTitle.Text),
        Compact(_seoDescription.Text),
        Compact(_socialTitle.Text),
        Compact(_socialDescription.Text));

    private static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Border Card(Thickness? margin = null) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 14, 16, 14),
        Margin = margin ?? new Thickness(0),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static Grid FieldHeader(string label, out TextBlock counter)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Label(label));
        counter = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetColumn(counter, 1);
        grid.Children.Add(counter);
        return grid;
    }

    private static TextBox Multiline(string text, double height, int maxLength) => new()
    {
        Text = text,
        Height = height,
        MaxLength = maxLength,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
}
