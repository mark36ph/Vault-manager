using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteCommentModerationInitialized;
    private DispatcherTimer? _websiteCommentModerationTimer;
    private DataGrid? _websiteCommentModerationGrid;
    private ComboBox? _websiteCommentModerationFilter;
    private TextBox? _websiteCommentModerationSearch;
    private TextBlock? _websiteCommentModerationStatus;
    private Button? _websiteCommentHideButton;
    private Button? _websiteCommentRestoreButton;
    private Button? _websiteCommentDismissReportsButton;
    private Button? _websiteCommentDeleteButton;
    private Button? _websiteCommentOpenQuizButton;

    public void InitializeWebsiteCommentModerationPage()
    {
        if (_websiteCommentModerationInitialized) return;
        _websiteCommentModerationInitialized = true;

        _websiteCommentModerationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _websiteCommentModerationTimer.Tick += (_, _) => EnsureWebsiteCommentModerationPage();
        _websiteCommentModerationTimer.Start();
        Closed += (_, _) => _websiteCommentModerationTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteCommentModerationPage));
    }

    private void EnsureWebsiteCommentModerationPage()
    {
        if (_settingsContentHost is null ||
            !_settingsNavButtons.TryGetValue("about", out var aboutButton) ||
            aboutButton.Parent is not Panel sidebar)
        {
            return;
        }

        if (!_settingsPages.ContainsKey("comments"))
        {
            _settingsPages["comments"] = BuildWebsiteCommentModerationPage();
            AddSettingsNav(sidebar, "comments", "Comment moderation");
            if (_settingsNavButtons.TryGetValue("comments", out var commentsButton))
                commentsButton.Click += async (_, _) => await RefreshWebsiteCommentModerationAsync(false);
        }

        if (_settingsNavButtons.TryGetValue("comments", out var commentButton))
        {
            var targetIndex = sidebar.Children.IndexOf(aboutButton);
            if (_settingsNavButtons.TryGetValue("website", out var websiteButton))
            {
                var websiteIndex = sidebar.Children.IndexOf(websiteButton);
                if (websiteIndex >= 0) targetIndex = websiteIndex + 1;
            }
            var currentIndex = sidebar.Children.IndexOf(commentButton);
            if (currentIndex != targetIndex && currentIndex >= 0)
            {
                sidebar.Children.Remove(commentButton);
                targetIndex = Math.Min(targetIndex, sidebar.Children.Count);
                sidebar.Children.Insert(Math.Max(0, targetIndex), commentButton);
            }
            _websiteCommentModerationTimer?.Stop();
        }
    }

    private FrameworkElement BuildWebsiteCommentModerationPage()
    {
        var page = SettingsPageStack(
            "Comment moderation",
            "Review comments from every Factburst quiz, deal with player reports, and hide or remove content without opening each quiz individually.");

        var section = SettingsSection("Website comments");
        page.Children.Add(section);
        var stack = (StackPanel)section.Child;
        stack.Children.Add(new TextBlock
        {
            Text = "Reported comments are shown first by default. Hiding removes a comment from normal visitors while preserving the thread; Delete safely removes its text without breaking replies.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 10),
        });

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _websiteCommentModerationFilter = new ComboBox
        {
            Width = 128,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 6),
        };
        _websiteCommentModerationFilter.Items.Add(new ComboBoxItem { Content = "Reported", Tag = "reported" });
        _websiteCommentModerationFilter.Items.Add(new ComboBoxItem { Content = "Visible", Tag = "active" });
        _websiteCommentModerationFilter.Items.Add(new ComboBoxItem { Content = "Hidden", Tag = "hidden" });
        _websiteCommentModerationFilter.Items.Add(new ComboBoxItem { Content = "All", Tag = "all" });
        _websiteCommentModerationFilter.SelectedIndex = 0;
        _websiteCommentModerationFilter.SelectionChanged += async (_, _) => await RefreshWebsiteCommentModerationAsync(false);
        toolbar.Children.Add(_websiteCommentModerationFilter);

        _websiteCommentModerationSearch = new TextBox
        {
            Width = 260,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 6),
            ToolTip = "Search comment text, username or quiz",
        };
        _websiteCommentModerationSearch.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await RefreshWebsiteCommentModerationAsync(false);
        };
        toolbar.Children.Add(_websiteCommentModerationSearch);

        var searchButton = new Button { Content = "Search", MinWidth = 84, Height = 34, Margin = new Thickness(0, 0, 8, 6) };
        searchButton.Click += async (_, _) => await RefreshWebsiteCommentModerationAsync(false);
        toolbar.Children.Add(searchButton);
        var refreshButton = new Button { Content = "Refresh", MinWidth = 84, Height = 34, Margin = new Thickness(0, 0, 0, 6) };
        refreshButton.Click += async (_, _) => await RefreshWebsiteCommentModerationAsync(true);
        toolbar.Children.Add(refreshButton);
        stack.Children.Add(toolbar);

        _websiteCommentModerationGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            MinHeight = 360,
            MaxHeight = 560,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Reports",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.Reports)),
            Width = new DataGridLength(64),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.Status)),
            Width = new DataGridLength(78),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "User",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.Username)),
            Width = new DataGridLength(120),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.QuizTitle)),
            Width = new DataGridLength(180),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Comment",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.Body)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Report reason",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.ReportReasons)),
            Width = new DataGridLength(115),
        });
        _websiteCommentModerationGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Posted",
            Binding = new Binding(nameof(FactburstWebsiteModerationComment.CreatedAt)),
            Width = new DataGridLength(145),
        });
        _websiteCommentModerationGrid.SelectionChanged += (_, _) => UpdateWebsiteCommentModerationButtons();
        stack.Children.Add(_websiteCommentModerationGrid);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _websiteCommentHideButton = new Button { Content = "Hide", MinWidth = 84, MinHeight = 34, Margin = new Thickness(0, 0, 8, 6), IsEnabled = false };
        _websiteCommentRestoreButton = new Button { Content = "Restore", MinWidth = 84, MinHeight = 34, Margin = new Thickness(0, 0, 8, 6), IsEnabled = false };
        _websiteCommentDismissReportsButton = new Button { Content = "Dismiss reports", MinWidth = 118, MinHeight = 34, Margin = new Thickness(0, 0, 8, 6), IsEnabled = false };
        _websiteCommentDeleteButton = new Button { Content = "Delete", MinWidth = 84, MinHeight = 34, Margin = new Thickness(0, 0, 8, 6), IsEnabled = false };
        _websiteCommentOpenQuizButton = new Button { Content = "Open quiz", MinWidth = 92, MinHeight = 34, Margin = new Thickness(0, 0, 0, 6), IsEnabled = false };
        _websiteCommentHideButton.Click += async (_, _) => await ApplySelectedWebsiteCommentActionAsync("hide");
        _websiteCommentRestoreButton.Click += async (_, _) => await ApplySelectedWebsiteCommentActionAsync("restore");
        _websiteCommentDismissReportsButton.Click += async (_, _) => await ApplySelectedWebsiteCommentActionAsync("dismiss_reports");
        _websiteCommentDeleteButton.Click += async (_, _) => await DeleteSelectedWebsiteCommentAsync();
        _websiteCommentOpenQuizButton.Click += (_, _) => OpenSelectedWebsiteCommentQuiz();
        actions.Children.Add(_websiteCommentHideButton);
        actions.Children.Add(_websiteCommentRestoreButton);
        actions.Children.Add(_websiteCommentDismissReportsButton);
        actions.Children.Add(_websiteCommentDeleteButton);
        actions.Children.Add(_websiteCommentOpenQuizButton);
        stack.Children.Add(actions);

        _websiteCommentModerationStatus = new TextBlock
        {
            Text = "Comment moderation status will appear here.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        stack.Children.Add(_websiteCommentModerationStatus);

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => _ = RefreshWebsiteCommentModerationAsync(false)));
        return SettingsScrollable(page);
    }

    private async Task RefreshWebsiteCommentModerationAsync(bool showErrors)
    {
        if (_websiteCommentModerationGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            _websiteCommentModerationGrid.ItemsSource = Array.Empty<FactburstWebsiteModerationComment>();
            if (_websiteCommentModerationStatus is not null)
                _websiteCommentModerationStatus.Text = "Configure the Link Tracker API key in Website settings to moderate comments.";
            UpdateWebsiteCommentModerationButtons();
            return;
        }

        var status = (_websiteCommentModerationFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "reported";
        var search = _websiteCommentModerationSearch?.Text ?? "";
        try
        {
            using var client = new FactburstWebsiteCommentsAdminClient();
            var result = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey, status, search);
            _websiteCommentModerationGrid.ItemsSource = result.Comments;
            if (_websiteCommentModerationStatus is not null)
            {
                _websiteCommentModerationStatus.Text =
                    $"{result.Summary.Reported:N0} reported • {result.Summary.Active:N0} visible • {result.Summary.Hidden:N0} hidden • {result.Comments.Count:N0} shown";
            }
            UpdateWebsiteCommentModerationButtons();
        }
        catch (Exception error)
        {
            if (_websiteCommentModerationStatus is not null) _websiteCommentModerationStatus.Text = error.Message;
            if (showErrors)
                MessageBox.Show(this, error.Message, "Comment Moderation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateWebsiteCommentModerationButtons()
    {
        var selected = _websiteCommentModerationGrid?.SelectedItem as FactburstWebsiteModerationComment;
        var hasSelection = selected is not null;
        var hidden = string.Equals(selected?.Status, "hidden", StringComparison.OrdinalIgnoreCase);
        if (_websiteCommentHideButton is not null) _websiteCommentHideButton.IsEnabled = hasSelection && !hidden;
        if (_websiteCommentRestoreButton is not null) _websiteCommentRestoreButton.IsEnabled = hasSelection && hidden;
        if (_websiteCommentDismissReportsButton is not null) _websiteCommentDismissReportsButton.IsEnabled = selected?.Reports > 0;
        if (_websiteCommentDeleteButton is not null) _websiteCommentDeleteButton.IsEnabled = hasSelection;
        if (_websiteCommentOpenQuizButton is not null) _websiteCommentOpenQuizButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(selected?.QuizSlug);
    }

    private async Task ApplySelectedWebsiteCommentActionAsync(string action)
    {
        if (_websiteCommentModerationGrid?.SelectedItem is not FactburstWebsiteModerationComment selected) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;

        try
        {
            using var client = new FactburstWebsiteCommentsAdminClient();
            await client.ApplyActionAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id, action);
            await RefreshWebsiteCommentModerationAsync(false);
        }
        catch (Exception error)
        {
            if (_websiteCommentModerationStatus is not null) _websiteCommentModerationStatus.Text = error.Message;
            MessageBox.Show(this, error.Message, "Comment Moderation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedWebsiteCommentAsync()
    {
        if (_websiteCommentModerationGrid?.SelectedItem is not FactburstWebsiteModerationComment selected) return;
        var confirmation = MessageBox.Show(
            this,
            $"Delete this comment from {selected.Username}?\n\n{selected.Body}",
            "Delete Website Comment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        await ApplySelectedWebsiteCommentActionAsync("delete");
    }

    private void OpenSelectedWebsiteCommentQuiz()
    {
        if (_websiteCommentModerationGrid?.SelectedItem is not FactburstWebsiteModerationComment selected ||
            string.IsNullOrWhiteSpace(selected.QuizSlug)) return;
        try
        {
            var url = $"https://factburstquiz.com/quiz.html?slug={Uri.EscapeDataString(selected.QuizSlug)}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Open Quiz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
