using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly DatabaseBackupService _databaseBackupService = new();
    private DispatcherTimer? _databaseBackupTimer;
    private DispatcherTimer? _quizDatabaseProtectionTimer;
    private DispatcherTimer? _databaseProtectionUiTimer;
    private TextBlock? _databaseProtectionStatus;
    private Button? _databaseBackupNowButton;
    private Button? _databaseProtectExistingButton;
    private Button? _databaseRestoreMissingButton;
    private int _databaseBackupBusy;
    private int _quizDatabaseProtectionBusy;
    private bool _databaseProtectionUiInstalled;

    public void InitializeDatabaseBackupAndRecovery()
    {
        if (_databaseBackupTimer is not null) return;

        _databaseProtectionUiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _databaseProtectionUiTimer.Tick += (_, _) => EnsureDatabaseProtectionUi();
        _databaseProtectionUiTimer.Start();

        _databaseBackupTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromHours(1),
        };
        _databaseBackupTimer.Tick += async (_, _) => await RunAutomaticDatabaseBackupAsync();
        _databaseBackupTimer.Start();

        _quizDatabaseProtectionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _quizDatabaseProtectionTimer.Tick += async (_, _) => await ProtectNewQuizProjectsAsync(showStatus: false);
        _quizDatabaseProtectionTimer.Start();

        Closed += (_, _) =>
        {
            _databaseProtectionUiTimer?.Stop();
            _databaseBackupTimer?.Stop();
            _quizDatabaseProtectionTimer?.Stop();
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            EnsureDatabaseProtectionUi();
            _ = RunAutomaticDatabaseBackupAsync();
            _ = ProtectNewQuizProjectsAsync(showStatus: false);
        }));
    }

    private void EnsureDatabaseProtectionUi()
    {
        if (_databaseProtectionUiInstalled)
        {
            _databaseProtectionUiTimer?.Stop();
            return;
        }
        if (!_settingsPages.TryGetValue("integrity", out var page) ||
            page is not ScrollViewer scroll || scroll.Content is not StackPanel stack)
            return;

        var card = SettingsSection("Database protection & recovery");
        var content = (StackPanel)card.Child;
        content.Children.Add(new TextBlock
        {
            Text = "SQLite is the recovery source for quiz projects. The app stores the exact quiz manifest plus a compressed copy of the non-video project files needed to rebuild the quiz. Final MP4/MOV files are derived outputs and are rendered again instead of being duplicated inside the database.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 10),
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Daily database backup: {DatabaseBackupService.DefaultBackupDirectory}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        content.Children.Add(new TextBlock
        {
            Text = "A verified SQLite backup is made automatically once per day while the app is running and Z: is available. You can also create one immediately.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _databaseBackupNowButton = new Button
        {
            Content = "Back up database now",
            MinWidth = 154,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 8, 8),
        };
        _databaseBackupNowButton.Click += async (_, _) => await RunManualDatabaseBackupAsync();
        buttons.Children.Add(_databaseBackupNowButton);

        _databaseProtectExistingButton = new Button
        {
            Content = "Protect existing quizzes",
            MinWidth = 154,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Store recovery snapshots for older Quiz History projects that still have their source files.",
        };
        _databaseProtectExistingButton.Click += async (_, _) => await ProtectNewQuizProjectsAsync(showStatus: true);
        buttons.Children.Add(_databaseProtectExistingButton);

        _databaseRestoreMissingButton = new Button
        {
            Content = "Restore missing quiz files",
            MinWidth = 170,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Recreate missing project source files from the recovery copies stored in SQLite.",
        };
        _databaseRestoreMissingButton.Click += async (_, _) => await RestoreMissingQuizFilesAsync();
        buttons.Children.Add(_databaseRestoreMissingButton);
        content.Children.Add(buttons);

        _databaseProtectionStatus = new TextBlock
        {
            Text = "New quiz projects are protected automatically. Backup status will appear here.",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(_databaseProtectionStatus);

        stack.Children.Add(card);
        _databaseProtectionUiInstalled = true;
        _databaseProtectionUiTimer?.Stop();
    }

    private async Task RunAutomaticDatabaseBackupAsync()
    {
        if (Interlocked.CompareExchange(ref _databaseBackupBusy, 1, 0) != 0)
            return;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var directory = DatabaseBackupService.DefaultBackupDirectory;
            var hasBackup = await Task.Run(() => _databaseBackupService.HasBackupForDate(directory, today));
            if (hasBackup)
            {
                SetDatabaseProtectionStatus($"Database backup is current for {today:dd-MM-yyyy}.");
                return;
            }

            if (!await Task.Run(() => _databaseBackupService.IsTargetAvailable(directory)))
            {
                SetDatabaseProtectionStatus("Automatic database backup is waiting for Z: to become available.");
                return;
            }

            var result = await Task.Run(() => _databaseBackupService.Backup(_data.DatabasePath, directory));
            SetDatabaseProtectionStatus($"Automatic database backup complete: {result.BackupPath}");
        }
        catch (Exception error)
        {
            SetDatabaseProtectionStatus("Automatic database backup could not run: " + error.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _databaseBackupBusy, 0);
        }
    }

    private async Task RunManualDatabaseBackupAsync()
    {
        if (Interlocked.CompareExchange(ref _databaseBackupBusy, 1, 0) != 0)
            return;
        try
        {
            if (_databaseBackupNowButton is not null) _databaseBackupNowButton.IsEnabled = false;
            SetDatabaseProtectionStatus("Backing up the database to Z:…");
            var result = await Task.Run(() => _databaseBackupService.Backup(
                _data.DatabasePath,
                DatabaseBackupService.DefaultBackupDirectory));
            SetDatabaseProtectionStatus($"Database backup complete: {result.BackupPath}");
            MessageBox.Show(this,
                $"Database backup completed and passed SQLite integrity check.\n\n{result.BackupPath}",
                "Database Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            SetDatabaseProtectionStatus("Database backup failed: " + error.Message);
            MessageBox.Show(this, error.Message, "Database Backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_databaseBackupNowButton is not null) _databaseBackupNowButton.IsEnabled = true;
            Interlocked.Exchange(ref _databaseBackupBusy, 0);
        }
    }

    private async Task ProtectNewQuizProjectsAsync(bool showStatus)
    {
        if (Interlocked.CompareExchange(ref _quizDatabaseProtectionBusy, 1, 0) != 0)
            return;
        try
        {
            if (_databaseProtectExistingButton is not null) _databaseProtectExistingButton.IsEnabled = false;
            if (showStatus) SetDatabaseProtectionStatus("Protecting quiz project source files in SQLite…");
            var result = await Task.Run(() => _data.ProtectExistingQuizProjects(2_000));
            if (showStatus || result.Protected > 0)
            {
                SetDatabaseProtectionStatus(
                    $"Quiz database protection: {result.Protected:N0} newly protected • " +
                    $"{result.AlreadyProtected:N0} already protected • {result.Unavailable:N0} unavailable.");
            }
        }
        catch (Exception error)
        {
            SetDatabaseProtectionStatus("Quiz database protection failed: " + error.Message);
            if (showStatus)
                MessageBox.Show(this, error.Message, "Quiz Database Protection", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_databaseProtectExistingButton is not null) _databaseProtectExistingButton.IsEnabled = true;
            Interlocked.Exchange(ref _quizDatabaseProtectionBusy, 0);
        }
    }

    private async Task RestoreMissingQuizFilesAsync()
    {
        try
        {
            if (_databaseRestoreMissingButton is not null) _databaseRestoreMissingButton.IsEnabled = false;
            SetDatabaseProtectionStatus("Restoring missing quiz project files from SQLite…");
            var result = await Task.Run(() => _data.RestoreMissingQuizProjectFiles(2_000));
            SetDatabaseProtectionStatus(
                $"Recovery complete: {result.FilesRestored:N0} file(s) restored across " +
                $"{result.ProjectsRestored:N0} project(s). Existing files were left untouched.");
            MessageBox.Show(this,
                $"Checked {result.ProjectsChecked:N0} protected quiz project(s).\n" +
                $"Restored {result.FilesRestored:N0} missing source file(s) across {result.ProjectsRestored:N0} project(s).\n\n" +
                "Existing files were not overwritten. Final video files are derived outputs; if one is missing, restore the project sources here and render the final video again.",
                "Quiz File Recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            SetDatabaseProtectionStatus("Quiz file recovery failed: " + error.Message);
            MessageBox.Show(this, error.Message, "Quiz File Recovery", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_databaseRestoreMissingButton is not null) _databaseRestoreMissingButton.IsEnabled = true;
        }
    }

    private void SetDatabaseProtectionStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetDatabaseProtectionStatus(text)));
            return;
        }
        if (_databaseProtectionStatus is not null)
            _databaseProtectionStatus.Text = text;
    }
}
