using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record WebsiteUserAdminRow(
    int Id,
    string Username,
    string Email,
    string Verified,
    string Status,
    int Quizzes,
    int Attempts,
    string Score,
    string Accuracy,
    string Joined,
    string LastLogin,
    string LastPlayed,
    FactburstWebsiteUserSummary Source);

public sealed record WebsiteUserQuizRow(
    string Title,
    string Best,
    string Accuracy,
    int Attempts,
    string FirstPlayed,
    string LastPlayed,
    string Slug);

public partial class MainShellWindow
{
    private bool _websiteUsersInitialized;
    private int _websiteUsersTabIndex = -1;
    private DispatcherTimer? _websiteUsersGuardTimer;
    private DataGrid? _websiteUsersGrid;
    private DataGrid? _websiteUserQuizGrid;
    private TextBox? _websiteUserSearchBox;
    private TextBlock? _websiteUsersRegisteredText;
    private TextBlock? _websiteUsersVerifiedText;
    private TextBlock? _websiteUsersSuspendedText;
    private TextBlock? _websiteUsersAttemptsText;
    private TextBlock? _websiteUsersStatusText;
    private TextBlock? _websiteUserDetailTitle;
    private Button? _websiteUserSuspendButton;
    private Button? _websiteUserReinstateButton;
    private Button? _websiteUserDeleteButton;

    public void InitializeWebsiteUsersPage()
    {
        if (_websiteUsersInitialized) return;
        _websiteUsersInitialized = true;

        _websiteUsersGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(550),
        };
        _websiteUsersGuardTimer.Tick += (_, _) => EnsureWebsiteUsersPage();
        _websiteUsersGuardTimer.Start();
        Closed += (_, _) => _websiteUsersGuardTimer?.Stop();

        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            if (MainTabs.SelectedIndex == _websiteUsersTabIndex)
                await RefreshWebsiteUsersAsync(false);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteUsersPage));
    }

    private void EnsureWebsiteUsersPage()
    {
        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null) return;

        if (_websiteUsersTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildWebsiteUsersPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _websiteUsersTabIndex = MainTabs.Items.Count - 1;
        }

        if (_autopilotNavButtons.ContainsKey("Users"))
        {
            _websiteUsersGuardTimer?.Stop();
            return;
        }

        var button = new Button
        {
            Content = "♙   Users",
            Tag = AutopilotFirstNavTag + ":Users",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        button.Click += (_, _) => NavigateWebsiteUsers();

        var website = _autopilotNavButtons.TryGetValue("Website", out var websiteButton) ? websiteButton : null;
        var advanced = _autopilotNavButtons.TryGetValue("Advanced", out var advancedButton) ? advancedButton : null;
        var index = website is not null
            ? _autopilotNavContainer.Children.IndexOf(website) + 1
            : advanced is not null
                ? _autopilotNavContainer.Children.IndexOf(advanced)
                : _autopilotNavContainer.Children.Count;
        _autopilotNavContainer.Children.Insert(Math.Clamp(index, 0, _autopilotNavContainer.Children.Count), button);
        _autopilotNavButtons["Users"] = button;
        _websiteUsersGuardTimer?.Stop();
    }

    private void NavigateWebsiteUsers()
    {
        EnsureWebsiteUsersPage();
        if (_websiteUsersTabIndex < 0) return;
        MainTabs.SelectedIndex = _websiteUsersTabIndex;
        SelectAutopilotNav("Users");
        _ = RefreshWebsiteUsersAsync(false);
    }

    private FrameworkElement BuildWebsiteUsersPage()
    {
        var root = new Grid { Margin = new Thickness(26, 22, 26, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.9, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Website users",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Review registered Factburst players, quiz activity and account status. Suspend or delete accounts without exposing admin controls on the public website.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 20, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _websiteUserSearchBox = new TextBox
        {
            Width = 230,
            Height = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Search username or email",
        };
        _websiteUserSearchBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter) return;
            await RefreshWebsiteUsersAsync(true);
            eventArgs.Handled = true;
        };
        var refresh = new Button { Content = "Refresh", MinWidth = 92, MinHeight = 36 };
        refresh.Click += async (_, _) => await RefreshWebsiteUsersAsync(true);
        actions.Children.Add(_websiteUserSearchBox);
        actions.Children.Add(refresh);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddWebsiteUserStat(stats, 0, "Registered", out _websiteUsersRegisteredText);
        AddWebsiteUserStat(stats, 1, "Verified", out _websiteUsersVerifiedText);
        AddWebsiteUserStat(stats, 2, "Suspended", out _websiteUsersSuspendedText);
        AddWebsiteUserStat(stats, 3, "Attempts", out _websiteUsersAttemptsText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _websiteUsersGrid = BuildWebsiteUsersGrid();
        _websiteUsersGrid.Columns.Add(UserTextColumn("Username", nameof(WebsiteUserAdminRow.Username), 150));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Email", nameof(WebsiteUserAdminRow.Email), 210));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Verified", nameof(WebsiteUserAdminRow.Verified), 80));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Status", nameof(WebsiteUserAdminRow.Status), 90));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Quizzes", nameof(WebsiteUserAdminRow.Quizzes), 72));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Attempts", nameof(WebsiteUserAdminRow.Attempts), 72));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Score", nameof(WebsiteUserAdminRow.Score), 90));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Accuracy", nameof(WebsiteUserAdminRow.Accuracy), 82));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Joined", nameof(WebsiteUserAdminRow.Joined), 145));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Last login", nameof(WebsiteUserAdminRow.LastLogin), 145));
        _websiteUsersGrid.Columns.Add(UserTextColumn("Last played", nameof(WebsiteUserAdminRow.LastPlayed), 145));
        _websiteUsersGrid.SelectionChanged += async (_, _) => await LoadSelectedWebsiteUserAsync(false);
        var usersCard = WebsiteUsersCard(_websiteUsersGrid);
        Grid.SetRow(usersCard, 2);
        root.Children.Add(usersCard);

        var detailHeader = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        detailHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _websiteUserDetailTitle = new TextBlock
        {
            Text = "Select a user to view quiz activity",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        detailHeader.Children.Add(_websiteUserDetailTitle);
        Grid.SetRow(detailHeader, 3);
        root.Children.Add(detailHeader);

        _websiteUserQuizGrid = BuildWebsiteUsersGrid();
        _websiteUserQuizGrid.IsHitTestVisible = true;
        _websiteUserQuizGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(WebsiteUserQuizRow.Title)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("Best", nameof(WebsiteUserQuizRow.Best), 80));
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("Accuracy", nameof(WebsiteUserQuizRow.Accuracy), 82));
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("Attempts", nameof(WebsiteUserQuizRow.Attempts), 72));
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("First played", nameof(WebsiteUserQuizRow.FirstPlayed), 155));
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("Last played", nameof(WebsiteUserQuizRow.LastPlayed), 155));
        _websiteUserQuizGrid.Columns.Add(UserTextColumn("Slug", nameof(WebsiteUserQuizRow.Slug), 170));
        var quizzesCard = WebsiteUsersCard(_websiteUserQuizGrid);
        Grid.SetRow(quizzesCard, 4);
        root.Children.Add(quizzesCard);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _websiteUsersStatusText = new TextBlock
        {
            Text = "Website user status will appear here.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        footer.Children.Add(_websiteUsersStatusText);

        _websiteUserSuspendButton = new Button { Content = "Suspend", MinWidth = 92, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _websiteUserSuspendButton.Click += async (_, _) => await SuspendSelectedWebsiteUserAsync();
        _websiteUserReinstateButton = new Button { Content = "Reinstate", MinWidth = 92, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _websiteUserReinstateButton.Click += async (_, _) => await ReinstateSelectedWebsiteUserAsync();
        _websiteUserDeleteButton = new Button { Content = "Delete account", MinWidth = 112, MinHeight = 36, IsEnabled = false };
        _websiteUserDeleteButton.Click += async (_, _) => await DeleteSelectedWebsiteUserAsync();
        Grid.SetColumn(_websiteUserSuspendButton, 1);
        Grid.SetColumn(_websiteUserReinstateButton, 2);
        Grid.SetColumn(_websiteUserDeleteButton, 3);
        footer.Children.Add(_websiteUserSuspendButton);
        footer.Children.Add(_websiteUserReinstateButton);
        footer.Children.Add(_websiteUserDeleteButton);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);

        return root;
    }

    private static DataGrid BuildWebsiteUsersGrid() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        BorderThickness = new Thickness(0),
        Background = Brushes.White,
        RowBackground = Brushes.White,
        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
        HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)),
    };

    private static Border WebsiteUsersCard(UIElement child) => new()
    {
        Child = child,
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(1),
    };

    private static DataGridTextColumn UserTextColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width),
    };

    private static void AddWebsiteUserStat(Grid parent, int column, string label, out TextBlock value)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        });
        value = new TextBlock
        {
            Text = "—",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 5, 0, 0),
        };
        stack.Children.Add(value);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private async Task RefreshWebsiteUsersAsync(bool showErrors, int selectUserId = 0)
    {
        if (_websiteUsersGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            SetWebsiteUsersUnavailable("Link Tracker is not configured. Open Website settings to add the Cloudflare tracker key.");
            return;
        }

        try
        {
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = "Loading registered website users…";
            using var client = new FactburstWebsiteUserAdminClient();
            var result = await client.FetchUsersAsync(tracker.BaseUrl, tracker.ApiKey, _websiteUserSearchBox?.Text);
            var rows = result.Users
                .Select(user => new WebsiteUserAdminRow(
                    user.Id,
                    user.Username,
                    user.Email,
                    user.EmailVerified ? "Yes" : "No",
                    string.Equals(user.Status, "suspended", StringComparison.OrdinalIgnoreCase) ? "Suspended" : "Active",
                    user.QuizzesCompleted,
                    user.Attempts,
                    $"{user.TotalScore:N0}/{user.TotalPossible:N0}",
                    $"{user.Percentage:N0}%",
                    UserAdminDate(user.CreatedAt),
                    UserAdminDate(user.LastLoginAt),
                    UserAdminDate(user.LastPlayedAt),
                    user))
                .ToList();

            _websiteUsersGrid.ItemsSource = rows;
            if (_websiteUsersRegisteredText is not null) _websiteUsersRegisteredText.Text = result.Summary.Total.ToString("N0");
            if (_websiteUsersVerifiedText is not null) _websiteUsersVerifiedText.Text = result.Summary.Verified.ToString("N0");
            if (_websiteUsersSuspendedText is not null) _websiteUsersSuspendedText.Text = result.Summary.Suspended.ToString("N0");
            if (_websiteUsersAttemptsText is not null) _websiteUsersAttemptsText.Text = result.Users.Sum(user => user.Attempts).ToString("N0");
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = $"{result.Summary.Total:N0} registered • {result.Summary.Verified:N0} verified • {result.Summary.Suspended:N0} suspended";

            if (selectUserId > 0)
                _websiteUsersGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == selectUserId);
            else if (_websiteUsersGrid.SelectedItem is null && rows.Count > 0)
                _websiteUsersGrid.SelectedIndex = 0;
            else
                UpdateWebsiteUserActionButtons();
        }
        catch (Exception error)
        {
            SetWebsiteUsersUnavailable(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadSelectedWebsiteUserAsync(bool showErrors)
    {
        UpdateWebsiteUserActionButtons();
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected)
        {
            if (_websiteUserQuizGrid is not null) _websiteUserQuizGrid.ItemsSource = null;
            if (_websiteUserDetailTitle is not null) _websiteUserDetailTitle.Text = "Select a user to view quiz activity";
            return;
        }

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;
        try
        {
            if (_websiteUserDetailTitle is not null) _websiteUserDetailTitle.Text = $"{selected.Username} • loading quiz activity…";
            using var client = new FactburstWebsiteUserAdminClient();
            var detail = await client.FetchUserAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            var quizRows = detail.Quizzes.Select(quiz => new WebsiteUserQuizRow(
                quiz.Title,
                $"{quiz.BestScore:N0}/{quiz.Total:N0}",
                $"{quiz.Percentage:N0}%",
                quiz.Attempts,
                UserAdminDate(quiz.FirstCompletedAt),
                UserAdminDate(quiz.LastCompletedAt),
                quiz.Slug)).ToList();
            if (_websiteUserQuizGrid is not null) _websiteUserQuizGrid.ItemsSource = quizRows;
            if (_websiteUserDetailTitle is not null)
                _websiteUserDetailTitle.Text = $"{detail.User.Username} • {detail.User.QuizzesCompleted:N0} quizzes • {detail.User.Attempts:N0} attempts • best total {detail.User.TotalScore:N0}/{detail.User.TotalPossible:N0}";
        }
        catch (Exception error)
        {
            if (_websiteUserQuizGrid is not null) _websiteUserQuizGrid.ItemsSource = null;
            if (_websiteUserDetailTitle is not null) _websiteUserDetailTitle.Text = $"{selected.Username} • activity unavailable";
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SuspendSelectedWebsiteUserAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        if (string.Equals(selected.Source.Status, "suspended", StringComparison.OrdinalIgnoreCase)) return;
        if (MessageBox.Show(
                this,
                $"Suspend '{selected.Username}'?\n\nThey will be logged out, blocked from playing or logging in, and removed from all leaderboards until reinstated. Their quiz history will be preserved.",
                "Suspend Website User",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SetSelectedWebsiteUserStatusAsync(selected, "suspended");
    }

    private async Task ReinstateSelectedWebsiteUserAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        if (!string.Equals(selected.Source.Status, "suspended", StringComparison.OrdinalIgnoreCase)) return;
        if (MessageBox.Show(
                this,
                $"Reinstate '{selected.Username}'?\n\nThey will be allowed to log in and their stored scores will appear on leaderboards again.",
                "Reinstate Website User",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await SetSelectedWebsiteUserStatusAsync(selected, "active");
    }

    private async Task SetSelectedWebsiteUserStatusAsync(WebsiteUserAdminRow selected, string status)
    {
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;
        try
        {
            SetWebsiteUserButtonsEnabled(false, false, false);
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = status == "suspended" ? $"Suspending {selected.Username}…" : $"Reinstating {selected.Username}…";
            using var client = new FactburstWebsiteUserAdminClient();
            await client.SetStatusAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id, status);
            await RefreshWebsiteUsersAsync(false, selected.Id);
            if (_websiteUsersStatusText is not null)
                _websiteUsersStatusText.Text = status == "suspended"
                    ? $"{selected.Username} suspended. Their sessions were revoked and leaderboard scores are hidden."
                    : $"{selected.Username} reinstated. Their stored scores are eligible for leaderboards again.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateWebsiteUserActionButtons();
        }
    }

    private async Task DeleteSelectedWebsiteUserAsync()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected) return;
        if (MessageBox.Show(
                this,
                $"Permanently delete '{selected.Username}'?\n\nThis removes their account, active sessions, verification tokens and all stored quiz scores. Their leaderboard entries will disappear immediately.\n\nThis cannot be undone.",
                "Delete Website User",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;
        try
        {
            SetWebsiteUserButtonsEnabled(false, false, false);
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = $"Deleting {selected.Username}…";
            using var client = new FactburstWebsiteUserAdminClient();
            await client.DeleteUserAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            await RefreshWebsiteUsersAsync(false);
            if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = $"Deleted {selected.Username} and their stored quiz scores.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateWebsiteUserActionButtons();
        }
    }

    private void SetWebsiteUsersUnavailable(string message)
    {
        if (_websiteUsersRegisteredText is not null) _websiteUsersRegisteredText.Text = "—";
        if (_websiteUsersVerifiedText is not null) _websiteUsersVerifiedText.Text = "—";
        if (_websiteUsersSuspendedText is not null) _websiteUsersSuspendedText.Text = "—";
        if (_websiteUsersAttemptsText is not null) _websiteUsersAttemptsText.Text = "—";
        if (_websiteUsersStatusText is not null) _websiteUsersStatusText.Text = message;
        if (_websiteUsersGrid is not null) _websiteUsersGrid.ItemsSource = null;
        if (_websiteUserQuizGrid is not null) _websiteUserQuizGrid.ItemsSource = null;
        if (_websiteUserDetailTitle is not null) _websiteUserDetailTitle.Text = "Website user activity unavailable";
        SetWebsiteUserButtonsEnabled(false, false, false);
    }

    private void UpdateWebsiteUserActionButtons()
    {
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected)
        {
            SetWebsiteUserButtonsEnabled(false, false, false);
            return;
        }

        var suspended = string.Equals(selected.Source.Status, "suspended", StringComparison.OrdinalIgnoreCase);
        SetWebsiteUserButtonsEnabled(!suspended, suspended, true);
    }

    private void SetWebsiteUserButtonsEnabled(bool suspend, bool reinstate, bool delete)
    {
        if (_websiteUserSuspendButton is not null) _websiteUserSuspendButton.IsEnabled = suspend;
        if (_websiteUserReinstateButton is not null) _websiteUserReinstateButton.IsEnabled = reinstate;
        if (_websiteUserDeleteButton is not null) _websiteUserDeleteButton.IsEnabled = delete;
    }

    private static string UserAdminDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return value.Trim();
        return parsed.LocalDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
