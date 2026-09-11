using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _uploadManagerPageInitialized;
    private int _uploadManagerTabIndex = -1;
    private DataGrid? _uploadManagerGrid;
    private TextBlock? _uploadManagerNeedsUploadText;
    private TextBlock? _uploadManagerScheduledText;
    private TextBlock? _uploadManagerCommentReadyText;
    private TextBlock? _uploadManagerCompleteText;
    private bool _uploadManagerRefreshRunning;

    private void InitializeUploadManagerPage()
    {
        if (_uploadManagerPageInitialized || MainTabs is null) return;
        _uploadManagerPageInitialized = true;
        var tab = new TabItem { Content = BuildUploadManagerPage() };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle) tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _uploadManagerTabIndex = MainTabs.Items.Count - 1;
        AddUploadManagerNavigationButton(_uploadManagerTabIndex);
        RefreshUploadManager();
        // The manual social sync button depends on the Upload Manager being created.
        // Add it here rather than only during the window Loaded event, because the
        // Upload Manager is intentionally initialized later during deferred startup.
        Dispatcher.BeginInvoke(AddFactburstManualSyncButton);
    }

    private FrameworkElement BuildUploadManagerPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Upload Manager",
                    FontFamily = new FontFamily("Segoe UI Variable Display"),
                    FontSize = 28,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                },
                new TextBlock
                {
                    Text = "Upload completed quizzes, track schedules, and post first comments when publication is ready.",
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                    Margin = new Thickness(0, 3, 0, 0),
                },
            },
        });
        var refresh = new Button { Content = "Refresh", MinWidth = 92, MinHeight = 34 };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += (_, _) => RefreshUploadManager();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var needsUpload = BuildQuizHistoryStatCard("Need uploading", Color.FromRgb(0, 204, 255));
        _uploadManagerNeedsUploadText = needsUpload.Value;
        needsUpload.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(needsUpload.Card);
        var scheduled = BuildQuizHistoryStatCard("Scheduled", Color.FromRgb(255, 202, 45));
        _uploadManagerScheduledText = scheduled.Value;
        scheduled.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(scheduled.Card, 1);
        stats.Children.Add(scheduled.Card);
        var comments = BuildQuizHistoryStatCard("Comments ready", Color.FromRgb(204, 70, 255));
        _uploadManagerCommentReadyText = comments.Value;
        comments.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(comments.Card, 2);
        stats.Children.Add(comments.Card);
        var complete = BuildQuizHistoryStatCard("Upload complete", Color.FromRgb(70, 235, 115));
        _uploadManagerCompleteText = complete.Value;
        complete.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(complete.Card, 3);
        stats.Children.Add(complete.Card);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _uploadManagerGrid = BuildUploadManagerGrid();
        Grid.SetRow(_uploadManagerGrid, 2);
        root.Children.Add(_uploadManagerGrid);

        var footer = new TextBlock
        {
            Text = "Website publishing status is synced automatically. Use Sync Now to force an immediate social status refresh.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }
}
