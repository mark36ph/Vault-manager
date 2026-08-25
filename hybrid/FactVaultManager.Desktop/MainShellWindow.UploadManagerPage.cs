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

        var actions = new StackPanel
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

    private void RefreshUploadManager()
    {
        if (!_uploadManagerPageInitialized || _uploadManagerGrid is null) return;
        var history = _data.GetQuizHistory();
        _uploadManagerGrid.ItemsSource = history;
        _uploadManagerNeedsUploadText!.Text = history.Count(item =>
            SocialUploadQueuePlanner.RemainingDestinations(item) != SocialUploadDestination.None).ToString("N0");
        _uploadManagerScheduledText!.Text = history.Count(item => item.YouTubeIsScheduled || item.FacebookIsScheduled).ToString("N0");
        _uploadManagerCommentReadyText!.Text = history.Count(item => item.FirstCommentDisplay == "Ready to post").ToString("N0");
        _uploadManagerCompleteText!.Text = history.Count(item =>
            SocialUploadQueuePlanner.RemainingDestinations(item) == SocialUploadDestination.None).ToString("N0");
    }

    private DataGridTemplateColumn BuildUploadPlatformLinkColumn(
        string header,
        string displayProperty,
        string urlProperty,
        string linkAvailableProperty,
        double width)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetBinding(ContentControl.ContentProperty, new Binding(displayProperty));
        button.SetBinding(FrameworkElement.TagProperty, new Binding(urlProperty));
        button.SetBinding(UIElement.IsEnabledProperty, new Binding(linkAvailableProperty));
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255))));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        buttonStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        var notApplicableText = new Trigger { Property = ContentControl.ContentProperty, Value = "N/A" };
        notApplicableText.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(120, 24, 24))));
        buttonStyle.Triggers.Add(notApplicableText);
        button.SetValue(FrameworkElement.StyleProperty, buttonStyle);
        button.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
        button.SetValue(FrameworkElement.ToolTipProperty, "Open this video on its platform");
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(UploadPlatformLink_Click));
        var cellStyle = new Style(typeof(DataGridCell), _uploadManagerGrid?.CellStyle);
        var notApplicableCell = new DataTrigger { Binding = new Binding(displayProperty), Value = "N/A" };
        notApplicableCell.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 218, 218))));
        cellStyle.Triggers.Add(notApplicableCell);
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Triggers.Add(selectedCell);
        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = new DataTemplate { VisualTree = button },
            CellStyle = cellStyle,
            Width = new DataGridLength(width),
        };
    }

    private void UploadPlatformLink_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string value } ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        eventArgs.Handled = true;
    }

    private void AddUploadManagerNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root) return;
        var notes = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizNotesTabIndex.ToString(), StringComparison.Ordinal));
        if (notes?.Parent is not StackPanel navigation) return;
        var button = new Button { Content = "⇧   Upload Manager", Tag = tabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle) button.Style = navStyle;
        button.Click += Navigate_Click;
        navigation.Children.Insert(Math.Min(navigation.Children.Count, navigation.Children.IndexOf(notes) + 1), button);
    }
}
