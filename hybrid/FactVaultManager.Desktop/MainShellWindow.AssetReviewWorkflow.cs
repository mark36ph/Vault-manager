using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _assetReviewWorkflowInitialized;
    private TextBox? _assetReviewSearchBox;
    private TextBlock? _assetReviewCountText;
    private TextBlock? _assetReviewNameText;
    private TextBlock? _assetReviewKindText;
    private TextBlock? _assetReviewSceneText;
    private TextBlock? _assetReviewDetailText;
    private TextBlock? _assetReviewPathText;
    private ComboBox? _assetReviewTypeFilter;
    private Button? _assetReviewOpenFileButton;
    private Button? _assetReviewOpenFolderButton;
    private IReadOnlyList<AssetReviewItem> _assetReviewItems = Array.Empty<AssetReviewItem>();

    private void InitializeAssetReviewWorkflow()
    {
        if (_assetReviewWorkflowInitialized || MainTabs.Items.Count < 5 || MainTabs.Items[4] is not TabItem tab)
        {
            return;
        }

        _assetReviewWorkflowInitialized = true;
        Detach(AssetProjectComboBox);
        Detach(AssetReviewGrid);

        AssetReviewGrid.SelectionChanged += (_, _) => UpdateSelectedAssetReviewDetails();
        AssetReviewGrid.AutoGenerateColumns = false;
        AssetReviewGrid.IsReadOnly = true;
        AssetReviewGrid.Columns.Clear();
        AssetReviewGrid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new System.Windows.Data.Binding(nameof(AssetReviewItem.Kind)), Width = 90 });
        AssetReviewGrid.Columns.Add(new DataGridTextColumn { Header = "Asset", Binding = new System.Windows.Data.Binding(nameof(AssetReviewItem.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        AssetReviewGrid.Columns.Add(new DataGridTextColumn { Header = "Scene / context", Binding = new System.Windows.Data.Binding(nameof(AssetReviewItem.Scene)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        AssetReviewGrid.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new System.Windows.Data.Binding(nameof(AssetReviewItem.Detail)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });

        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "Asset Review",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Review production assets, scene context, verification notes and file locations.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 3, 0, 14),
        });
        root.Children.Add(header);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _assetReviewSearchBox = new TextBox { ToolTip = "Search asset name, scene, detail or path", Margin = new Thickness(0, 0, 8, 0) };
        _assetReviewSearchBox.TextChanged += (_, _) => ApplyAssetReviewFilter();
        toolbar.Children.Add(_assetReviewSearchBox);

        AssetProjectComboBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(AssetProjectComboBox, 1);
        toolbar.Children.Add(AssetProjectComboBox);

        _assetReviewTypeFilter = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
        _assetReviewTypeFilter.Items.Add("All types");
        _assetReviewTypeFilter.Items.Add("Images");
        _assetReviewTypeFilter.Items.Add("Videos");
        _assetReviewTypeFilter.SelectedIndex = 0;
        _assetReviewTypeFilter.SelectionChanged += (_, _) => ApplyAssetReviewFilter();
        Grid.SetColumn(_assetReviewTypeFilter, 2);
        toolbar.Children.Add(_assetReviewTypeFilter);

        _assetReviewCountText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0),
        };
        Grid.SetColumn(_assetReviewCountText, 3);
        toolbar.Children.Add(_assetReviewCountText);

        var refresh = new Button { Content = "Refresh", Margin = new Thickness(0) };
        refresh.Click += (_, _) => RefreshAssetReviewWorkspace();
        Grid.SetColumn(refresh, 4);
        toolbar.Children.Add(refresh);

        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        AssetReviewGrid.Margin = new Thickness(0);
        body.Children.Add(AssetReviewGrid);

        var detailsBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
        };
        Grid.SetColumn(detailsBorder, 2);
        body.Children.Add(detailsBorder);

        var details = new StackPanel();
        detailsBorder.Child = details;
        details.Children.Add(new TextBlock { Text = "Review details", FontSize = 16, FontWeight = FontWeights.SemiBold });

        _assetReviewNameText = new TextBlock { Text = "Select an asset", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2), TextWrapping = TextWrapping.Wrap };
        _assetReviewKindText = MutedDetail();
        _assetReviewSceneText = DetailBlock("Scene / context");
        _assetReviewDetailText = DetailBlock("Verification detail");
        _assetReviewPathText = DetailBlock("File path");
        details.Children.Add(_assetReviewNameText);
        details.Children.Add(_assetReviewKindText);
        details.Children.Add(_assetReviewSceneText);
        details.Children.Add(_assetReviewDetailText);
        details.Children.Add(_assetReviewPathText);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        _assetReviewOpenFileButton = new Button { Content = "Open file", IsEnabled = false };
        _assetReviewOpenFolderButton = new Button { Content = "Open folder", IsEnabled = false, Margin = new Thickness(0) };
        _assetReviewOpenFileButton.Click += (_, _) => OpenSelectedAssetReviewFile();
        _assetReviewOpenFolderButton.Click += (_, _) => OpenSelectedAssetReviewFolder();
        actions.Children.Add(_assetReviewOpenFileButton);
        actions.Children.Add(_assetReviewOpenFolderButton);
        details.Children.Add(actions);

        Grid.SetRow(body, 2);
        root.Children.Add(body);
        tab.Content = root;

        RefreshAssetReviewWorkspace();
    }

    private static TextBlock MutedDetail() => new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock DetailBlock(string label) => new()
    {
        Text = label,
        Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
        Margin = new Thickness(0, 14, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    private void RefreshAssetReviewWorkspace()
    {
        try
        {
            var selectedProject = AssetProjectComboBox.SelectedItem as DesktopProject;
            _assetReviewItems = _data.GetAssetReview(selectedProject);
            ApplyAssetReviewFilter();
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Asset review error: {error.Message}";
        }
    }

    private void ApplyAssetReviewFilter()
    {
        var search = (_assetReviewSearchBox?.Text ?? "").Trim();
        var type = _assetReviewTypeFilter?.SelectedItem?.ToString() ?? "All types";

        var filtered = _assetReviewItems.Where(item =>
        {
            var matchesSearch = string.IsNullOrWhiteSpace(search) ||
                                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                item.Scene.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                item.Detail.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                item.Path.Contains(search, StringComparison.OrdinalIgnoreCase);
            var kind = item.Kind.ToLowerInvariant();
            var matchesType = type == "All types" ||
                              (type == "Images" && IsImageKind(kind, item.Path)) ||
                              (type == "Videos" && IsVideoKind(kind, item.Path));
            return matchesSearch && matchesType;
        }).ToList();

        AssetReviewGrid.ItemsSource = filtered;
        _assetReviewCountText!.Text = filtered.Count == 1 ? "1 item" : $"{filtered.Count} items";
        if (filtered.Count > 0)
        {
            AssetReviewGrid.SelectedIndex = 0;
        }
        else
        {
            ClearAssetReviewDetails();
        }
    }

    private static bool IsImageKind(string kind, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return kind.Contains("image") || extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif" or ".avif" or ".heic";
    }

    private static bool IsVideoKind(string kind, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return kind.Contains("video") || extension is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".m4v";
    }

    private void UpdateSelectedAssetReviewDetails()
    {
        if (AssetReviewGrid.SelectedItem is not AssetReviewItem item)
        {
            ClearAssetReviewDetails();
            return;
        }

        _assetReviewNameText!.Text = item.Name;
        _assetReviewKindText!.Text = item.Kind;
        _assetReviewSceneText!.Text = $"Scene / context\n{(string.IsNullOrWhiteSpace(item.Scene) ? "No scene context recorded." : item.Scene)}";
        _assetReviewDetailText!.Text = $"Verification detail\n{(string.IsNullOrWhiteSpace(item.Detail) ? "No additional verification detail." : item.Detail)}";
        _assetReviewPathText!.Text = $"File path\n{item.Path}";
        var exists = File.Exists(item.Path);
        _assetReviewOpenFileButton!.IsEnabled = exists;
        _assetReviewOpenFolderButton!.IsEnabled = exists && Directory.Exists(Path.GetDirectoryName(item.Path));
    }

    private void ClearAssetReviewDetails()
    {
        if (_assetReviewNameText is null)
        {
            return;
        }
        _assetReviewNameText.Text = "Select an asset";
        _assetReviewKindText!.Text = "";
        _assetReviewSceneText!.Text = "Scene / context";
        _assetReviewDetailText!.Text = "Verification detail";
        _assetReviewPathText!.Text = "File path";
        _assetReviewOpenFileButton!.IsEnabled = false;
        _assetReviewOpenFolderButton!.IsEnabled = false;
    }

    private void OpenSelectedAssetReviewFile()
    {
        if (AssetReviewGrid.SelectedItem is not AssetReviewItem item || !File.Exists(item.Path))
        {
            return;
        }
        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
    }

    private void OpenSelectedAssetReviewFolder()
    {
        if (AssetReviewGrid.SelectedItem is not AssetReviewItem item)
        {
            return;
        }
        var folder = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }
}
