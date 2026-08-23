using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public sealed record InstagramAnalyticsRow(
    string MediaId,
    string Type,
    string Caption,
    string Published,
    long Views,
    long Reach,
    long Likes,
    long Comments,
    long Saved,
    long Shares,
    string Url);

public partial class MainShellWindow
{
    private bool _instagramManagerPageInitialized;
    private bool _instagramManagerPageRefreshing;
    private int _instagramManagerTabIndex = -1;
    private readonly InstagramManagementService _instagramManagement = new();
    private DataGrid? _instagramAnalyticsGrid;
    private TextBlock? _instagramAccountText;
    private TextBlock? _instagramMediaCountText;
    private TextBlock? _instagramViewsText;
    private TextBlock? _instagramLikesText;
    private TextBlock? _instagramCommentsText;
    private TextBlock? _instagramSharesText;
    private TextBlock? _instagramManagerStatus;

    private void InitializeInstagramManagerPage()
    {
        if (_instagramManagerPageInitialized || MainTabs is null) return;
        _instagramManagerPageInitialized = true;
        var tab = new TabItem { Content = BuildInstagramManagerPage() };
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                await RefreshInstagramManagerAsync(false);
        };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle) tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _instagramManagerTabIndex = MainTabs.Items.Count - 1;
        AddInstagramNavigationButton(_instagramManagerTabIndex);
    }

    private FrameworkElement BuildInstagramManagerPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Instagram Manager",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(225, 48, 108), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.65,
            },
        });
        _instagramAccountText = new TextBlock
        {
            Text = "Add the Instagram token in Settings, then refresh.",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        };
        heading.Children.Add(_instagramAccountText);
        header.Children.Add(heading);
        var refresh = new Button { Content = "Refresh from Instagram", MinWidth = 174, MinHeight = 36, VerticalAlignment = VerticalAlignment.Bottom };
        StyleQuizHistoryButton(refresh, Color.FromRgb(225, 48, 108));
        refresh.Click += async (_, _) => await RefreshInstagramManagerAsync(true);
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 5; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var media = BuildQuizHistoryStatCard("Recent media", Color.FromRgb(225, 48, 108));
        _instagramMediaCountText = media.Value;
        media.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(media.Card);
        var views = BuildQuizHistoryStatCard("Views", Color.FromRgb(131, 58, 180));
        _instagramViewsText = views.Value;
        views.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(views.Card, 1);
        stats.Children.Add(views.Card);
        var likes = BuildQuizHistoryStatCard("Likes", Color.FromRgb(253, 29, 29));
        _instagramLikesText = likes.Value;
        likes.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(likes.Card, 2);
        stats.Children.Add(likes.Card);
        var comments = BuildQuizHistoryStatCard("Comments", Color.FromRgb(252, 175, 69));
        _instagramCommentsText = comments.Value;
        comments.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(comments.Card, 3);
        stats.Children.Add(comments.Card);
        var shares = BuildQuizHistoryStatCard("Shares", Color.FromRgb(70, 235, 115));
        _instagramSharesText = shares.Value;
        shares.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(shares.Card, 4);
        stats.Children.Add(shares.Card);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _instagramAnalyticsGrid = BuildInstagramGrid();
        var table = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 48, 108)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Child = _instagramAnalyticsGrid,
        };
        Grid.SetRow(table, 2);
        root.Children.Add(table);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _instagramManagerStatus = new TextBlock
        {
            Text = "Instagram analytics have not been refreshed yet.",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(_instagramManagerStatus);
        var open = new Button { Content = "Open selected post", MinWidth = 142, MinHeight = 36 };
        StyleQuizHistoryButton(open, Color.FromRgb(252, 175, 69));
        open.Click += (_, _) => OpenSelectedInstagramPost();
        Grid.SetColumn(open, 1);
        footer.Children.Add(open);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(64, 34, 118), 0.55),
                    new(Color.FromRgb(193, 53, 132), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private static DataGrid BuildInstagramGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            MinRowHeight = 42,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(38, 24, 80)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(51, 27, 89)),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(InstagramAnalyticsRow.Type)), Width = new DataGridLength(78) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Caption", Binding = new Binding(nameof(InstagramAnalyticsRow.Caption)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Published", Binding = new Binding(nameof(InstagramAnalyticsRow.Published)), Width = new DataGridLength(116) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Views", Binding = new Binding(nameof(InstagramAnalyticsRow.Views)) { StringFormat = "N0" }, Width = new DataGridLength(82) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Reach", Binding = new Binding(nameof(InstagramAnalyticsRow.Reach)) { StringFormat = "N0" }, Width = new DataGridLength(82) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Likes", Binding = new Binding(nameof(InstagramAnalyticsRow.Likes)) { StringFormat = "N0" }, Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Comments", Binding = new Binding(nameof(InstagramAnalyticsRow.Comments)) { StringFormat = "N0" }, Width = new DataGridLength(86) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Saved", Binding = new Binding(nameof(InstagramAnalyticsRow.Saved)) { StringFormat = "N0" }, Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Shares", Binding = new Binding(nameof(InstagramAnalyticsRow.Shares)) { StringFormat = "N0" }, Width = new DataGridLength(76) });
        return grid;
    }

    private async Task RefreshInstagramManagerAsync(bool showErrors)
    {
        if (_instagramManagerPageRefreshing || _instagramAnalyticsGrid is null) return;
        try
        {
            _instagramManagerPageRefreshing = true;
            SetInstagramStatus("Refreshing Instagram media and insights...");
            var result = await _instagramManagement.ListMediaAsync(_data.LoadSettings().InstagramAccessToken);
            var rows = result.Media.Select(item => new InstagramAnalyticsRow(
                    item.MediaId,
                    item.MediaType,
                    item.Caption.Replace('\r', ' ').Replace('\n', ' '),
                    item.PublishedAt?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? "",
                    item.Views,
                    item.Reach,
                    item.Likes,
                    item.Comments,
                    item.Saved,
                    item.Shares,
                    item.Permalink))
                .ToList();
            _instagramAnalyticsGrid.ItemsSource = rows;
            if (_instagramAccountText is not null)
                _instagramAccountText.Text = $"@{result.Username} • {result.AccountType} • {result.MediaCount:N0} total posts";
            if (_instagramMediaCountText is not null) _instagramMediaCountText.Text = rows.Count.ToString("N0");
            if (_instagramViewsText is not null) _instagramViewsText.Text = rows.Sum(item => item.Views).ToString("N0");
            if (_instagramLikesText is not null) _instagramLikesText.Text = rows.Sum(item => item.Likes).ToString("N0");
            if (_instagramCommentsText is not null) _instagramCommentsText.Text = rows.Sum(item => item.Comments).ToString("N0");
            if (_instagramSharesText is not null) _instagramSharesText.Text = rows.Sum(item => item.Shares).ToString("N0");
            SetInstagramStatus($"Updated {rows.Count:N0} recent Instagram post(s).");
        }
        catch (Exception error)
        {
            SetInstagramStatus(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Refresh Instagram", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _instagramManagerPageRefreshing = false;
        }
    }

    private void OpenSelectedInstagramPost()
    {
        if (_instagramAnalyticsGrid?.SelectedItem is not InstagramAnalyticsRow row || row.Url.Length == 0) return;
        Process.Start(new ProcessStartInfo(row.Url) { UseShellExecute = true });
    }

    private void AddInstagramNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root) return;
        var facebook = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _facebookAnalyticsTabIndex.ToString(), StringComparison.Ordinal));
        if (facebook?.Parent is not StackPanel navigation) return;
        var button = new Button { Content = "◎  Instagram Manager", Tag = tabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle) button.Style = navStyle;
        button.Click += Navigate_Click;
        var index = navigation.Children.IndexOf(facebook);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, index + 1), button);
    }

    private void SetInstagramStatus(string text)
    {
        if (_instagramManagerStatus is not null) _instagramManagerStatus.Text = text;
    }
}
