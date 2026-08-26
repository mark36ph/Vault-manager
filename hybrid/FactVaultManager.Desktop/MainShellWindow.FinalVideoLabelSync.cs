using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _finalVideoLabelSyncStarted;
    private bool _finalVideoExportNavHooked;

    private void InitializeFinalVideoLabelSync()
    {
        if (_finalVideoLabelSyncStarted)
            return;

        _finalVideoLabelSyncStarted = true;

        var attempts = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };

        void ApplyAndHook()
        {
            ApplyQuizFinalRenderLabels();

            if (!_finalVideoExportNavHooked &&
                _quizWorkspaceNavButtons.TryGetValue("export", out var exportNav))
            {
                exportNav.Click += (_, _) => Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(ApplyQuizFinalRenderLabels));
                _finalVideoExportNavHooked = true;
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyAndHook));

        timer.Tick += (_, _) =>
        {
            attempts++;
            ApplyAndHook();
            if (_finalVideoExportNavHooked || attempts >= 30)
                timer.Stop();
        };
        timer.Start();
    }
}
