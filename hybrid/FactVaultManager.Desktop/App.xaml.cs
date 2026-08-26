namespace FactVaultManager.Desktop;

public partial class App : System.Windows.Application
{
    private bool _thumbnailRegenerationActionsInitialized;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_thumbnailRegenerationActionsInitialized || MainWindow is not MainShellWindow shell)
            return;

        _thumbnailRegenerationActionsInitialized = shell.InitializeUploadManagerThumbnailRegenerationActions();
        if (_thumbnailRegenerationActionsInitialized)
            return;

        shell.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!_thumbnailRegenerationActionsInitialized)
                    _thumbnailRegenerationActionsInitialized = shell.InitializeUploadManagerThumbnailRegenerationActions();
            }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
