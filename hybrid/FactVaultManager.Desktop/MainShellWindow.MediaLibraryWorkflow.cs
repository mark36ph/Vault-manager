using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private TextBox? _mediaSearchBox;
    private TextBlock? _mediaResultCount;
    private Image? _mediaPreviewImage;
    private TextBlock? _mediaPreviewMessage;
    private TextBlock? _mediaPreviewName;
    private TextBlock? _mediaPreviewMeta;
    private TextBlock? _mediaPreviewPath;
    private Button? _mediaOpenFileButton;
    private Button? _mediaOpenFolderButton;
    private readonly Dictionary<string, Button> _mediaFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MediaItem> _mediaItems = Array.Empty<MediaItem>();
    private string _mediaTypeFilter = "All";
    private bool _mediaLibraryWorkflowInitialized;

    private void InitializeMediaLibraryWorkflow()
    {
        if (_mediaLibraryWorkflowInitialized || MainTabs.Items.Count < 4 || MainTabs.Items[3] is not TabItem mediaPage)
        {
            return;
        }

        _mediaLibraryWorkflowInitialized = true;

        MediaProjectComboBox.SelectionChanged -= MediaProjectComboBox_SelectionChanged;
        MediaProjectComboBox.SelectionChanged += MediaProjectComboBox_WorkflowSelectionChanged;
        MediaGrid.SelectionChanged += MediaGrid_WorkflowSelectionChanged;

        Detach(MediaProjectComboBox);
        Detach(MediaGrid);

        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Media Library",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Browse project media and assets gathered by Production.",
            Foreground = MediaMutedBrush(),
            Margin = new Thickness(0, 3, 0, 14),
        });
        root.Children.Add(heading);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(toolbar, 1);

        _mediaSearchBox = new TextBox
        {
            ToolTip = "Search media by file name or type",
            Margin = new Thickness(0, 0, 8, 0),
        };
        _mediaSearchBox.TextChanged += (_, _) => ApplyMediaFilter();
        toolbar.Children.Add(_mediaSearchBox);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var label in new[] { "All", "Images", "Videos" })
        {
            var button = new Button
            {
                Content = label,
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 4, 0),
                BorderThickness = new Thickness(0),
            };
            button.Click += MediaTypeFilter_Click;
            _mediaFilterButtons[label] = button;
            filters.Children.Add(button);
        }
        Grid.SetColumn(filters, 1);
        toolbar.Children.Add(filters);

        MediaProjectComboBox.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(MediaProjectComboBox, 2);
        toolbar.Children.Add(MediaProjectComboBox);

        var refresh = new Button
        {
            Content = "Refresh",
            Margin = new Thickness(8, 0, 0, 0),
        };
        refresh.Click += (_, _) => RefreshMediaWorkflow();
        Grid.SetColumn(refresh, 3);
        toolbar.Children.Add(refresh);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetRow(body, 2);

        var listPanel = new Grid();
        listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var listHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHeader.Children.Add(new TextBlock
        {
            Text = "Media",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _mediaResultCount = new TextBlock
        {
            Text = "0 items",
            Foreground = MediaMutedBrush(),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_mediaResultCount, 1);
        listHeader.Children.Add(_mediaResultCount);
        listPanel.Children.Add(listHeader);

        MediaGrid.AutoGenerateColumns = false;
        MediaGrid.IsReadOnly = true;
        MediaGrid.Margin = new Thickness(0);
        Grid.SetRow(MediaGrid, 1);
        listPanel.Children.Add(MediaGrid);
        body.Children.Add(listPanel);

        var previewCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
        };
        Grid.SetColumn(previewCard, 2);

        var preview = new Grid();
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.Children.Add(new TextBlock
        {
            Text = "Preview",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });

        var previewSurface = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 12, 0, 12),
            MinHeight = 300,
        };
        var previewGrid = new Grid();
        _mediaPreviewImage = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12),
            Visibility = Visibility.Collapsed,
        };
        _mediaPreviewMessage = new TextBlock
        {
            Text = "Select an image or video.",
            Foreground = MediaMutedBrush(),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
        };
        previewGrid.Children.Add(_mediaPreviewImage);
        previewGrid.Children.Add(_mediaPreviewMessage);
        previewSurface.Child = previewGrid;
        Grid.SetRow(previewSurface, 1);
        preview.Children.Add(previewSurface);

        var details = new StackPanel();
        _mediaPreviewName = new TextBlock { Text = "No asset selected", FontWeight = FontWeights.SemiBold, FontSize = 15 };
        _mediaPreviewMeta = new TextBlock { Foreground = MediaMutedBrush(), Margin = new Thickness(0, 4, 0, 0) };
        _mediaPreviewPath = new TextBlock
        {
            Foreground = MediaMutedBrush(),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10),
        };
        details.Children.Add(_mediaPreviewName);
        details.Children.Add(_mediaPreviewMeta);
        details.Children.Add(_mediaPreviewPath);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _mediaOpenFileButton = new Button { Content = "Open file", IsEnabled = false };
        _mediaOpenFileButton.Click += (_, _) => OpenSelectedMediaFile();
        _mediaOpenFolderButton = new Button { Content = "Open folder", IsEnabled = false };
        _mediaOpenFolderButton.Click += (_, _) => OpenSelectedMediaFolder();
        actions.Children.Add(_mediaOpenFileButton);
        actions.Children.Add(_mediaOpenFolderButton);
        details.Children.Add(actions);
        Grid.SetRow(details, 2);
        preview.Children.Add(details);

        previewCard.Child = preview;
        body.Children.Add(previewCard);
        root.Children.Add(body);

        mediaPage.Content = root;
        UpdateMediaFilterStyles();
        RefreshMediaWorkflow();
    }

    private async void MediaProjectComboBox_WorkflowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Dispatcher.BeginInvoke(RefreshMediaWorkflow);
    }

    private void MediaTypeFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }
        _mediaTypeFilter = button.Content?.ToString() ?? "All";
        UpdateMediaFilterStyles();
        ApplyMediaFilter();
    }

    private void UpdateMediaFilterStyles()
    {
        foreach (var pair in _mediaFilterButtons)
        {
            var selected = string.Equals(pair.Key, _mediaTypeFilter, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected ? new SolidColorBrush(Color.FromRgb(234, 242, 255)) : Brushes.Transparent;
            pair.Value.Foreground = selected ? new SolidColorBrush(Color.FromRgb(23, 92, 211)) : new SolidColorBrush(Color.FromRgb(71, 84, 103));
            pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void RefreshMediaWorkflow()
    {
        try
        {
            _mediaItems = _data.GetMedia(MediaProjectComboBox.SelectedItem as DesktopProject);
            ApplyMediaFilter();
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Media error: {error.Message}";
        }
    }

    private void ApplyMediaFilter()
    {
        var search = (_mediaSearchBox?.Text ?? "").Trim();
        var selectedPath = (MediaGrid.SelectedItem as MediaItem)?.Path;
        var filtered = _mediaItems
            .Where(item =>
                (string.IsNullOrWhiteSpace(search) ||
                 item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 item.Kind.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 item.Path.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (_mediaTypeFilter == "All" ||
                 (_mediaTypeFilter == "Images" && IsImageMedia(item)) ||
                 (_mediaTypeFilter == "Videos" && IsVideoMedia(item))))
            .OrderByDescending(item => item.Modified)
            .ToList();

        MediaGrid.ItemsSource = filtered;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            MediaGrid.SelectedItem = filtered.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        }
        if (MediaGrid.SelectedItem is null && filtered.Count > 0)
        {
            MediaGrid.SelectedIndex = 0;
        }

        if (_mediaResultCount is not null)
        {
            _mediaResultCount.Text = filtered.Count == 1 ? "1 item" : $"{filtered.Count} items";
        }
        UpdateMediaPreview();
    }

    private void MediaGrid_WorkflowSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMediaPreview();

    private void UpdateMediaPreview()
    {
        var item = MediaGrid.SelectedItem as MediaItem;
        if (_mediaPreviewImage is null || _mediaPreviewMessage is null || _mediaPreviewName is null ||
            _mediaPreviewMeta is null || _mediaPreviewPath is null || _mediaOpenFileButton is null || _mediaOpenFolderButton is null)
        {
            return;
        }

        _mediaPreviewImage.Source = null;
        _mediaPreviewImage.Visibility = Visibility.Collapsed;
        _mediaPreviewMessage.Visibility = Visibility.Visible;

        if (item is null)
        {
            _mediaPreviewMessage.Text = "Select an image or video.";
            _mediaPreviewName.Text = "No asset selected";
            _mediaPreviewMeta.Text = "";
            _mediaPreviewPath.Text = "";
            _mediaOpenFileButton.IsEnabled = false;
            _mediaOpenFolderButton.IsEnabled = false;
            return;
        }

        _mediaPreviewName.Text = item.Name;
        _mediaPreviewMeta.Text = $"{item.Kind}  •  {item.Size}  •  {item.Modified:g}";
        _mediaPreviewPath.Text = item.Path;
        _mediaOpenFileButton.IsEnabled = File.Exists(item.Path);
        _mediaOpenFolderButton.IsEnabled = Directory.Exists(Path.GetDirectoryName(item.Path));

        if (IsImageMedia(item) && File.Exists(item.Path))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(item.Path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                _mediaPreviewImage.Source = image;
                _mediaPreviewImage.Visibility = Visibility.Visible;
                _mediaPreviewMessage.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                _mediaPreviewMessage.Text = "This image could not be previewed, but it can still be opened externally.";
                return;
            }
        }

        _mediaPreviewMessage.Text = IsVideoMedia(item)
            ? "Video selected. Use Open file to play it in your default Windows video app."
            : "Preview is not available for this file type.";
    }

    private void OpenSelectedMediaFile()
    {
        if (MediaGrid.SelectedItem is not MediaItem item || !File.Exists(item.Path))
        {
            return;
        }
        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
    }

    private void OpenSelectedMediaFolder()
    {
        if (MediaGrid.SelectedItem is not MediaItem item)
        {
            return;
        }
        var folder = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }

    private static bool IsImageMedia(MediaItem item)
    {
        var extension = Path.GetExtension(item.Path);
        return item.Kind.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoMedia(MediaItem item)
    {
        var extension = Path.GetExtension(item.Path);
        return item.Kind.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
    }

    private static Brush MediaMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));
}
