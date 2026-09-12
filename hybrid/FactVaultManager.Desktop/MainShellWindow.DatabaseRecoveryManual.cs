using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool DatabaseRecoveryManualUiRegistered = RegisterDatabaseRecoveryManualUi();
    private bool _databaseRecoveryManualUiAdded;
    private Button? _databaseRecoveryCheckButton;
    private TextBlock? _databaseRecoveryCheckStatus;

    private static bool RegisterDatabaseRecoveryManualUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowDatabaseRecovery_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowDatabaseRecovery_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(window.EnsureDatabaseRecoveryManualUi));
        }
    }

    private void EnsureDatabaseRecoveryManualUi()
    {
        if (_databaseRecoveryManualUiAdded || !_settingsPages.TryGetValue("integrity", out var page))
            return;
        if (page is not ScrollViewer scroll || scroll.Content is not StackPanel stack)
            return;

        var card = stack.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Child is StackPanel panel &&
                panel.Children.OfType<TextBlock>().Any(text =>
                    string.Equals(text.Text, "Integrity report", StringComparison.OrdinalIgnoreCase)));
        if (card?.Child is not StackPanel content)
            return;

        var heading = new TextBlock
        {
            Text = "Database recovery",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 14, 0, 4),
        };
        content.Children.Add(heading);
        content.Children.Add(new TextBlock
        {
            Text = "Database recovery checks are manual. Nothing scans the filesystem or attempts database recovery during startup.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _databaseRecoveryCheckButton = new Button
        {
            Content = "Check database recovery",
            MinWidth = 170,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Inspect for a recoverable Library/question database only when you request it.",
        };
        _databaseRecoveryCheckButton.Click += async (_, _) => await RunManualDatabaseRecoveryCheckAsync();
        content.Children.Add(_databaseRecoveryCheckButton);

        _databaseRecoveryCheckStatus = new TextBlock
        {
            Text = "No database recovery check has been run.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        content.Children.Add(_databaseRecoveryCheckStatus);
        _databaseRecoveryManualUiAdded = true;
    }

    private async Task RunManualDatabaseRecoveryCheckAsync()
    {
        if (_databaseRecoveryCheckButton is not null)
            _databaseRecoveryCheckButton.IsEnabled = false;
        if (_databaseRecoveryCheckStatus is not null)
            _databaseRecoveryCheckStatus.Text = "Checking database recovery sources…";

        try
        {
            await Task.Run(() =>
            {
                InstalledLibraryRecoveryV2.Run();
                InstalledQuestionLibraryRecoveryV3.Run();
            });

            if (_databaseRecoveryCheckStatus is not null)
                _databaseRecoveryCheckStatus.Text = "Database recovery check completed. Review the integrity report and recovery diagnostics if a problem was found.";
        }
        catch (Exception error)
        {
            if (_databaseRecoveryCheckStatus is not null)
                _databaseRecoveryCheckStatus.Text = "Database recovery check failed: " + error.Message;
            MessageBox.Show(this, error.Message, "Database Recovery Check", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_databaseRecoveryCheckButton is not null)
                _databaseRecoveryCheckButton.IsEnabled = true;
        }
    }
}
