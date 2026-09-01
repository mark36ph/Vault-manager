using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _startupSafeUiCleanupInitialized;
    private bool _startupSafeUiCleanupQueued;

    public void InitializeStartupSafeUiCleanup()
    {
        if (_startupSafeUiCleanupInitialized)
            return;

        _startupSafeUiCleanupInitialized = true;

        // BuildInfo invokes this from the window Loaded handler. Do not run a repeating timer while
        // the shell is still settling: one coalesced idle pass is enough for the initial cleanup.
        QueueStartupSafeUiCleanup(DispatcherPriority.ApplicationIdle);

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) || !IsLoaded)
                return;

            QueueStartupSafeUiCleanup(DispatcherPriority.ContextIdle);
        };
    }

    private void QueueStartupSafeUiCleanup(DispatcherPriority priority)
    {
        if (_startupSafeUiCleanupQueued)
            return;

        _startupSafeUiCleanupQueued = true;
        Dispatcher.BeginInvoke(
            priority,
            new Action(() =>
            {
                _startupSafeUiCleanupQueued = false;
                if (!IsLoaded)
                    return;

                ApplyDailyUiCleanup();
            }));
    }
}
