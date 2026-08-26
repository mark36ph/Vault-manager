using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _finalVideoLabelSyncStarted;
    private bool _finalVideoExportNavHooked;
    private bool _finalVideoExportPageHooked;
    private bool _finalVideoLabelApplyQueued;

    private void InitializeFinalVideoLabelSync()
    {
        if (_finalVideoLabelSyncStarted)
            return;

        _finalVideoLabelSyncStarted = true;

        // Export can be opened through the sidebar, the Continue button, or the
        // top-right Render Final Video action. Some of those paths replace the
        // ContentControl content before WPF has materialized its visual children,
        // so applying the labels only once can miss the newly-visible Export card.
        // Keep a lightweight visibility/layout hook so the final-video wording is
        // applied after the page has actually entered the visual tree.
        LayoutUpdated += (_, _) =>
        {
            if (string.Equals(_quizWorkspaceSelectedPage, "export", StringComparison.OrdinalIgnoreCase))
                QueueFinalVideoLabelApply();
        };

        var attempts = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };

        void ApplyAndHook()
        {
            QueueFinalVideoLabelApply();

            if (!_finalVideoExportNavHooked &&
                _quizWorkspaceNavButtons.TryGetValue("export", out var exportNav))
            {
                exportNav.Click += (_, _) => QueueFinalVideoLabelApply();
                _finalVideoExportNavHooked = true;
            }

            if (!_finalVideoExportPageHooked &&
                _quizWorkspacePages.TryGetValue("export", out var exportPage))
            {
                exportPage.IsVisibleChanged += (_, _) =>
                {
                    if (exportPage.IsVisible)
                        QueueFinalVideoLabelApply();
                };
                _finalVideoExportPageHooked = true;
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyAndHook));

        timer.Tick += (_, _) =>
        {
            attempts++;
            ApplyAndHook();
            if ((_finalVideoExportNavHooked && _finalVideoExportPageHooked) || attempts >= 50)
                timer.Stop();
        };
        timer.Start();
    }

    private void QueueFinalVideoLabelApply()
    {
        if (_finalVideoLabelApplyQueued)
            return;

        _finalVideoLabelApplyQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _finalVideoLabelApplyQueued = false;
            ApplyQuizFinalRenderLabels();
        }));
    }
}
