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
            ToolTip = "Inspect for an existing user database only when you request it.",
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
            _databaseRecoveryCheckStatus.Text = "Checking for an existing database…";

        try
        {
            await Task.Run(() =>
            {
                var appDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FactVaultManager");

                // Allow a previous migration marker to be retried, but never create a DB.
                InstalledDataMigrationMarkerRepair.ClearIfStale(appDataRoot);

                // This migration only copies an existing user database from a known
                // location. It does not manufacture a replacement database.
                if (InstalledDataMigrationGuard.ShouldRun(appDataRoot))
                    InstalledDataMigration.Run();

                // Also inspect the dedicated recovery sources on explicit user request.
                InstalledLibraryRecoveryV2.Run();
                InstalledQuestionLibraryRecoveryV3.Run();
            });

            if (_databaseRecoveryCheckStatus is not null)
            {
                var databasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FactVaultManager",
                    "data",
                    "factvault.db");
                _databaseRecoveryCheckStatus.Text = File.Exists(databasePath)
                    ? "An existing database is now available. Restart Factburst Quiz Manager to reload it."
                    : "No existing user database was found in the recovery sources. No new database was created.";
            }
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
