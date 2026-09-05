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

            RunStartupRecovery("data migration", () =>
            {
                if (InstalledDataMigrationGuard.ShouldRun(appDataRoot))
                    InstalledDataMigration.Run();
            });
            RunStartupRecovery("library recovery", InstalledLibraryRecoveryV2.Run);
            RunStartupRecovery("question library recovery", InstalledQuestionLibraryRecoveryV3.Run);
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
            mainWindow.InitializeScheduledReleaseReadinessForApp();
            mainWindow.InitializeScheduledPromoBatchForApp();
            mainWindow.InitializePromoRelatedVideoChecklistForApp();
            application.Run(mainWindow);
        }
        catch (Exception error)
        {
            ShowFatalError("FactVaultManager could not start.", error);
        }
    }

    private static void RunStartupRecovery(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            // Recovery is deliberately best-effort: a damaged optional backup or legacy
            // data file must never prevent the desktop application from opening. The
            // exception is still recorded so the underlying recovery issue can be fixed.
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
