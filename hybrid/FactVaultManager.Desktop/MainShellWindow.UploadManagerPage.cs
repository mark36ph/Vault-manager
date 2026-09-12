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
        Dispatcher.BeginInvoke(AddFactburstManualSyncButton);
    }

    private void AddUploadManagerNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root) return;
        var notes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Library", StringComparison.OrdinalIgnoreCase));
        if (notes?.Parent is Panel panel)
        {
            var button = new Button { Content = "Upload Manager", Tag = $"autopilot-first-nav:Upload Manager" };
            button.Click += (_, _) =>
            {
                if (_uploadManagerTabIndex < 0) InitializeUploadManagerPage();
                if (_uploadManagerTabIndex >= 0 && MainTabs is not null)
                {
                    MainTabs.SelectedIndex = _uploadManagerTabIndex;
                }
            };
            panel.Children.Add(button);
        }
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

        _uploadManagerGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            MinRowHeight = 44,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(24, 39, 105)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(29, 48, 122)),
        };
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Triggers.Add(selected);
        _uploadManagerGrid.CellStyle = cellStyle;
        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9)));
        _uploadManagerGrid.ColumnHeaderStyle = headerStyle;
        _uploadManagerGrid.Columns.Add(new DataGridTextColumn { Header = "Quiz", Binding = new Binding(nameof(QuizHistorySummary.UploadTitleDisplay)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _uploadManagerGrid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(QuizHistorySummary.VideoType)), Width = new DataGridLength(72) });
        _uploadManagerGrid.Columns.Add(BuildUploadPlatformLinkColumn(
            "YouTube", nameof(QuizHistorySummary.YouTubePublicationDisplay), nameof(QuizHistorySummary.YouTubeUrl),
            nameof(QuizHistorySummary.YouTubePlatformLinkAvailable), 175));
        _uploadManagerGrid.Columns.Add(BuildUploadPlatformLinkColumn(
            "Facebook", nameof(QuizHistorySummary.FacebookPublicationDisplay), nameof(QuizHistorySummary.FacebookUrl),
            nameof(QuizHistorySummary.FacebookPlatformLinkAvailable), 175));
        _uploadManagerGrid.Columns.Add(BuildUploadPlatformLinkColumn(
            "Instagram", nameof(QuizHistorySummary.InstagramPublicationDisplay), nameof(QuizHistorySummary.InstagramUrl),
            nameof(QuizHistorySummary.InstagramPlatformLinkAvailable), 110));
        _uploadManagerGrid.Columns.Add(new DataGridTextColumn { Header = "First comment", Binding = new Binding(nameof(QuizHistorySummary.FirstCommentDisplay)), Width = new DataGridLength(155) });
        _uploadManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Current step",
            Binding = new Binding(nameof(QuizHistorySummary.UploadJournalDisplay)),
            Width = new DataGridLength(250),
        });
        _uploadManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Promo Short",
            Binding = new Binding(nameof(QuizHistorySummary.PromoShortDisplay)),
            Width = new DataGridLength(120),
        });
        var table = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Child = _uploadManagerGrid,
        };
        Grid.SetRow(table, 2);
        root.Children.Add(table);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var commentsButton = new Button { Content = "First Comments", MinWidth = 112 };
        StyleQuizHistoryButton(commentsButton, Color.FromRgb(204, 70, 255));
        commentsButton.Click += (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                ShowQuizPublishingMetadata(history, manageComments: true);
        };
        actions.Children.Add(commentsButton);
        var resetUpload = new Button { Content = "Reset Upload State", MinWidth = 126, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(resetUpload, Color.FromRgb(255, 190, 0));
        resetUpload.Click += (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                ShowResetUploadStateDialog(history);
        };
        actions.Children.Add(resetUpload);
        var retryFailed = new Button { Content = "Retry Failed Step", MinWidth = 126, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(retryFailed, Color.FromRgb(0, 204, 255));
        retryFailed.Click += async (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                await RetryFailedUploadStepsAsync(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Retry Failed Step", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        actions.Children.Add(retryFailed);
        var promoShort = new Button { Content = "Create Promo Short", MinWidth = 126, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(promoShort, Color.FromRgb(204, 70, 255));
        promoShort.Click += (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                ShowQuizPromoShortDialog(history);
            else
                MessageBox.Show(this, "Select a long-form quiz first.", "Create Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        actions.Children.Add(promoShort);
        var uploadPromoShort = new Button { Content = "Upload Promo Short", MinWidth = 132, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(uploadPromoShort, Color.FromRgb(255, 190, 0));
        uploadPromoShort.Click += (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                ShowQuizPromoShortUploadDialog(history);
            else
                MessageBox.Show(this, "Select the long-form quiz first.", "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        actions.Children.Add(uploadPromoShort);
        var upload = new Button { Content = "Upload Selected", MinWidth = 118, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(upload, Color.FromRgb(70, 235, 115));
        upload.Click += (_, _) =>
        {
            if (_uploadManagerGrid.SelectedItem is QuizHistorySummary history)
                ShowQuizUploadDialog(history);
        };
        actions.Children.Add(upload);
        var queue = new Button { Content = "Upload Queue", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(queue, Color.FromRgb(0, 204, 255));
        queue.Click += (_, _) => ShowUploadQueueDialog();
        actions.Children.Add(queue);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.58),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0), new Point(1, 1)),
            Child = root,
        };
    }

    // Existing Upload Manager methods continue below unchanged in the repository.
}