using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record WebsiteUserFriendAdminRow(
    string Username,
    string Relationship,
    string AccountStatus,
    string Since);

public partial class MainShellWindow
{
    private bool _websiteUsersFriendsPanelInitialized;
    private DispatcherTimer? _websiteUsersFriendsPanelTimer;
    private DataGrid? _websiteUserFriendsGrid;
    private TextBlock? _websiteUserFriendsTitle;

    public void InitializeWebsiteUsersFriendsPanel()
    {
        if (_websiteUsersFriendsPanelInitialized) return;
        _websiteUsersFriendsPanelInitialized = true;
        _websiteUsersFriendsPanelTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _websiteUsersFriendsPanelTimer.Tick += (_, _) => EnsureWebsiteUsersFriendsPanel();
        _websiteUsersFriendsPanelTimer.Start();
        Closed += (_, _) => _websiteUsersFriendsPanelTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteUsersFriendsPanel));
    }

    private void EnsureWebsiteUsersFriendsPanel()
    {
        if (_websiteUserFriendsGrid is not null)
        {
            _websiteUsersFriendsPanelTimer?.Stop();
            return;
        }
        if (_websiteUserQuizGrid?.Parent is not Border quizCard || quizCard.Parent is not Grid root)
            return;

        root.Children.Remove(quizCard);
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.9, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        Grid.SetRow(split, 4);

        quizCard.Margin = new Thickness(0, 0, 7, 0);
        Grid.SetColumn(quizCard, 0);
        split.Children.Add(quizCard);

        var social = new Grid { Margin = new Thickness(7, 0, 0, 0) };
        social.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        social.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _websiteUserFriendsTitle = new TextBlock
        {
            Text = "Friends & requests",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(3, 0, 0, 7),
        };
        social.Children.Add(_websiteUserFriendsTitle);

        _websiteUserFriendsGrid = BuildWebsiteUsersGrid();
        _websiteUserFriendsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "User",
            Binding = new Binding(nameof(WebsiteUserFriendAdminRow.Username)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteUserFriendsGrid.Columns.Add(UserTextColumn("Relationship", nameof(WebsiteUserFriendAdminRow.Relationship), 105));
        _websiteUserFriendsGrid.Columns.Add(UserTextColumn("Status", nameof(WebsiteUserFriendAdminRow.AccountStatus), 78));
        _websiteUserFriendsGrid.Columns.Add(UserTextColumn("Since", nameof(WebsiteUserFriendAdminRow.Since), 125));
        var card = WebsiteUsersCard(_websiteUserFriendsGrid);
        Grid.SetRow(card, 1);
        social.Children.Add(card);
        Grid.SetColumn(social, 1);
        split.Children.Add(social);

        root.Children.Add(split);
        if (_websiteUsersGrid is not null)
            _websiteUsersGrid.SelectionChanged += async (_, _) => await LoadSelectedWebsiteUserFriendsAsync(false);
        _ = LoadSelectedWebsiteUserFriendsAsync(false);
        _websiteUsersFriendsPanelTimer?.Stop();
    }

    private async Task LoadSelectedWebsiteUserFriendsAsync(bool showErrors)
    {
        if (_websiteUserFriendsGrid is null) return;
        if (_websiteUsersGrid?.SelectedItem is not WebsiteUserAdminRow selected)
        {
            _websiteUserFriendsGrid.ItemsSource = null;
            if (_websiteUserFriendsTitle is not null) _websiteUserFriendsTitle.Text = "Friends & requests";
            return;
        }

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;
        try
        {
            if (_websiteUserFriendsTitle is not null) _websiteUserFriendsTitle.Text = $"{selected.Username} • loading friends…";
            using var client = new FactburstWebsiteUserFriendsClient();
            var result = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id);
            var rows = new List<WebsiteUserFriendAdminRow>();
            rows.AddRange(result.Friends.Select(friend => FriendRow(friend, "Friend")));
            rows.AddRange(result.Incoming.Select(friend => FriendRow(friend, "Incoming")));
            rows.AddRange(result.Outgoing.Select(friend => FriendRow(friend, "Sent")));
            _websiteUserFriendsGrid.ItemsSource = rows;
            if (_websiteUserFriendsTitle is not null)
            {
                var pending = result.Incoming.Count + result.Outgoing.Count;
                _websiteUserFriendsTitle.Text = $"{selected.Username} • {result.Friends.Count:N0} friend{(result.Friends.Count == 1 ? "" : "s")} • {pending:N0} pending";
            }
        }
        catch (Exception error)
        {
            _websiteUserFriendsGrid.ItemsSource = null;
            if (_websiteUserFriendsTitle is not null) _websiteUserFriendsTitle.Text = $"{selected.Username} • friendships unavailable";
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static WebsiteUserFriendAdminRow FriendRow(FactburstWebsiteUserFriend friend, string relationship) => new(
        friend.Username,
        relationship,
        string.Equals(friend.UserStatus, "suspended", StringComparison.OrdinalIgnoreCase) ? "Suspended" : "Active",
        UserAdminDate(string.IsNullOrWhiteSpace(friend.RespondedAt) ? friend.CreatedAt : friend.RespondedAt));
}
