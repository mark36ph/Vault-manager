using System.Text;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace FactVaultManager.Desktop;

public static class Program
{
    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FactVaultManager",
        "logs",
        "startup-crash.log");

    private static readonly List<(string Name, Action Action)> DeferredStartupRecovery = new();

    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            WriteCrashLog("AppDomain unhandled exception", exception);
        };

        try
        {
            VelopackApp.Build().Run();
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FactVaultManager");

            // A stale completion marker must never prevent recovery of an existing
            // user database. This only removes the marker when the installed database
            // is missing, empty or unreadable; it never creates a database.
            RunStartupRecovery("stale data migration marker repair", () =>
                InstalledDataMigrationMarkerRepair.ClearIfStale(appDataRoot));

            // An application update must NEVER manufacture a replacement database.
            // If the installed database is missing, the migration path may only copy
            // an existing user database from a known legacy location. If no source is
            // found, the application remains without a database until the user chooses
            // the explicit recovery action in Settings.
            RunStartupRecovery("data migration", () =>
            {
                if (InstalledDataMigrationGuard.ShouldRun(appDataRoot))
                    InstalledDataMigration.Run();
            });

            // Database/library recovery is deliberately not a startup operation.
            // It can perform filesystem discovery and must only run when the user
            // explicitly requests a recovery check from Settings.

            RunStartupRecovery("project consolidation", InstalledProjectConsolidation.Run);
            RunStartupRecovery("credential recovery", InstalledCredentialRecovery.Run);
            RunStartupRecovery("deep credential recovery", InstalledCredentialDeepRecovery.Run);
            RunStartupRecovery("credential backup recovery", InstalledCredentialBackupRecovery.Run);
            RunStartupRecovery("configuration backup recovery", InstalledConfigurationBackupRecovery.Run);
            RunStartupRecovery("tracker settings recovery", InstalledTrackerSettingsRecovery.Run);
            RunStartupRecovery("renamed tracker settings recovery", InstalledRenamedTrackerSettingsRecovery.Run);
            RunStartupRecovery("YouTube OAuth client recovery", InstalledYouTubeOAuthClientIdRecovery.Run);
            RunStartupRecovery("YouTube account identity recovery", InstalledYouTubeAccountIdentityRecovery.Run);

            var application = new Application();
            AppInteractionPolish.Initialize();
            application.DispatcherUnhandledException += Application_DispatcherUnhandledException;
            var mainWindow = new MainShellWindow();
            mainWindow.PrepareFactburstFirstPaint();
            mainWindow.InitializeQuizHeaderActionsForApp();
            mainWindow.InitializeQuizWorkspaceNavigationForApp();
            mainWindow.InitializeFactburstTrackerForApp();
            mainWindow.InitializeScheduledPromoBatchForApp();
            mainWindow.InitializePromoRelatedVideoChecklistForApp();
            mainWindow.InitializeDailyQuizAutopilotForApp();

            if (DeferredStartupRecovery.Count > 0)
            {
                mainWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => _ = RunDeferredStartupRecoveryAsync()));
            }

            application.Run(mainWindow);
        }
        catch (Exception error)
        {
            ShowFatalError("FactVaultManager could not start.", error);
        }
    }

    private static void RunStartupRecovery(string name, Action action)
    {
        if (string.Equals(name, "data migration", StringComparison.Ordinal) ||
            string.Equals(name, "stale data migration marker repair", StringComparison.Ordinal))
        {
            RunRecoveryNow(name, action);
            return;
        }

        DeferredStartupRecovery.Add((name, action));
    }

    private static async Task RunDeferredStartupRecoveryAsync()
    {
        foreach (var recovery in DeferredStartupRecovery)
        {
            await Task.Run(() => RunRecoveryNow(recovery.Name, recovery.Action));
        }

        DeferredStartupRecovery.Clear();
    }

    private static void RunRecoveryNow(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            WriteCrashLog($"Startup recovery warning: {name}", error);
        }
    }

    private static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (ProjectsFolderConfigurationGuard.IsMissingProjectsFolderException(e.Exception))
        {
            WriteCrashLog("WPF dispatcher Projects Folder configuration exception (handled)", e.Exception);
            e.Handled = true;

            if (sender is Application application && application.MainWindow is MainShellWindow shell)
            {
                shell.ShowProjectsFolderConfigurationRequired();
            }
            else
            {
                try
                {
                    MessageBox.Show(
                        "Set the Projects Folder in Settings before using project-based features.",
                        "Projects Folder Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch
                {
                }
            }

            return;
        }

        if (e.Exception is FileNotFoundException databaseError &&
            string.Equals(databaseError.Message, "FactVault database was not found.", StringComparison.Ordinal))
        {
            WriteCrashLog("WPF dispatcher database-missing exception (handled)", e.Exception);
            e.Handled = true;

            try
            {
                var owner = sender is Application application ? application.MainWindow : null;
                MessageBox.Show(
                    owner,
                    "The FactVault database is not currently available.\n\nNo replacement database will be created automatically. Open Settings → Project Integrity → Check database recovery to search for an existing user database.",
                    "Database Recovery Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
            }

            return;
        }

        WriteCrashLog("WPF dispatcher unhandled exception", e.Exception);

        try
        {
            MessageBox.Show(
                $"FactVaultManager encountered an unexpected error.\n\n{e.Exception.Message}\n\nCrash log:\n{CrashLogPath}",
                "FactVaultManager Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }

        e.Handled = true;
        if (sender is Application applicationToShutdown)
            applicationToShutdown.Shutdown(-1);
    }

    private static void ShowFatalError(string heading, Exception error)
    {
        WriteCrashLog("Fatal startup exception", error);
        try
        {
            MessageBox.Show(
                $"{heading}\n\n{error.Message}\n\nCrash log:\n{CrashLogPath}",
                "FactVaultManager Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static void WriteCrashLog(string context, Exception? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var text = new StringBuilder()
                .AppendLine(new string('=', 80))
                .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .AppendLine(context)
                .AppendLine($"Process: {Environment.ProcessPath}")
                .AppendLine($"Working directory: {Environment.CurrentDirectory}")
                .AppendLine()
                .AppendLine(error?.ToString() ?? "No exception object was available.")
                .AppendLine()
                .ToString();
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
        }
    }
}
