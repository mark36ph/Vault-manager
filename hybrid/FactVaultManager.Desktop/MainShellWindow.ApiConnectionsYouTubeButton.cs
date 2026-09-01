using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public void FinalizeApiConnectionsYouTubeButton()
    {
        if (!_apiConnectionsSettingsInitialized || _settingsYouTubeConnectButton?.Parent is not Grid parent)
            return;

        // The original YouTube settings page attached its Connect handler with an anonymous
        // delegate. Once that control is re-parented there is no safe way to remove only that
        // delegate, so replace it with a fresh button and one explicit async handler.
        var oldButton = _settingsYouTubeConnectButton;
        parent.Children.Remove(oldButton);

        var connect = new Button
        {
            Content = "Connect Google account",
            MinWidth = 154,
        };
        connect.Click += async (_, _) => await ConnectYouTubeAsync();
        Grid.SetColumn(connect, 1);
        parent.Children.Add(connect);
        _settingsYouTubeConnectButton = connect;
    }
}
