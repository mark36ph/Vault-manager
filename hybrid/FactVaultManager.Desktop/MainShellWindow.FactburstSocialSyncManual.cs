using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _factburstManualSyncButtonAdded;

    private void AddFactburstManualSyncButton()
    {
        if (_factburstManualSyncButtonAdded || MainTabs is null || _uploadManagerTabIndex < 0 || _uploadManagerTabIndex >= MainTabs.Items.Count)
            return;

        if (MainTabs.Items[_uploadManagerTabIndex] is not TabItem tab)
            return;

        var refresh = FindButton(tab.Content as DependencyObject, "Refresh");
        if (refresh?.Parent is not Grid header)
            return;

        var sync = new Button
        {
            Content = "Sync Now",
            MinWidth = 92,
            MinHeight = 34,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Immediately sync YouTube, Facebook and Instagram publication status to FactBurst Quiz.",
        };
        StyleQuizHistoryButton(sync, Color.FromRgb(70, 235, 115));
        sync.Click += async (_, _) =>
        {
            sync.IsEnabled = false;
            var oldContent = sync.Content;
            sync.Content = "Syncing…";
            try
            {
                await SyncFactburstSocialStateAsync();
                RefreshUploadManager();
                sync.Content = "Synced ✓";
            }
            catch
            {
                sync.Content = "Sync failed";
            }
            finally
            {
                await Task.Delay(1500);
                sync.Content = oldContent;
                sync.IsEnabled = true;
            }
        };

        var refreshColumn = Grid.GetColumn(refresh);
        Grid.SetColumn(sync, refreshColumn);
        header.Children.Add(sync);
        _factburstManualSyncButtonAdded = true;
    }

    private static Button? FindButton(DependencyObject? root, string content)
    {
        if (root is null) return null;
        if (root is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindButton(VisualTreeHelper.GetChild(root, index), content);
            if (found is not null) return found;
        }
        return null;
    }

    private void InitializeFactburstManualSyncButton()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(AddFactburstManualSyncButton);
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(AddFactburstManualSyncButton);
    }
}
