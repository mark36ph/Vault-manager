using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool InstagramBusinessLoginUiRegistered = RegisterInstagramBusinessLoginUi();

    private static bool RegisterInstagramBusinessLoginUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            Button.ClickEvent,
            new RoutedEventHandler(InstagramBusinessLoginButton_Click),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(InstagramBusinessLoginUi_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void InstagramBusinessLoginUi_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                foreach (var button in FindVisualChildren<Button>(window))
                {
                    if (string.Equals(button.Content?.ToString(), "Instagram token setup", StringComparison.Ordinal))
                    {
                        button.Content = "Connect Instagram";
                        button.ToolTip = "Sign in to Instagram Business Login and reconnect the account automatically.";
                    }
                }
            }));
    }

    private static async void InstagramBusinessLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !string.Equals(button.Content?.ToString(), "Connect Instagram", StringComparison.Ordinal))
            return;

        e.Handled = true;
        var window = FindWindow(button);
        if (window is null)
            return;

        try
        {
            button.IsEnabled = false;
            var config = InstagramBusinessLoginConfigurationStore.Load(window._data.SettingsPath);
            if (config.AppId.Length == 0 || config.AppSecret.Length == 0)
            {
                var entered = InstagramBusinessLoginConfigurationDialog.Show(window, config.AppId, config.AppSecret);
                if (entered is null)
                    return;
                config = entered;
                InstagramBusinessLoginConfigurationStore.Save(window._data.SettingsPath, config);
            }

            window.SetInstagramConnectionStatus("Waiting for Instagram sign-in...");
            var result = await InstagramBusinessLoginService.ConnectAsync(config.AppId, config.AppSecret);
            window.SaveInstagramBusinessLoginResult(result);
            window.SetInstagramConnectionStatus(
                result.Username.Length > 0
                    ? $"✓ Connected — @{result.Username}"
                    : $"✓ Connected — {result.UserId}");
            MessageBox.Show(
                window,
                result.Username.Length > 0
                    ? $"Instagram is connected as @{result.Username}. The long-lived access token was saved securely."
                    : "Instagram is connected. The long-lived access token was saved securely.",
                "Connect Instagram",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            window.SetInstagramConnectionStatus("Not connected");
        }
        catch (Exception error)
        {
            window.SetInstagramConnectionStatus("Not connected");
            MessageBox.Show(window, error.Message, "Connect Instagram", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void SaveInstagramBusinessLoginResult(InstagramBusinessLoginResult result)
    {
        var document = _data.LoadSettingsDocument();
        var instagram = document["instagram"] as JsonObject ?? new JsonObject();
        document["instagram"] = instagram;
        instagram["access_token"] = LocalSecretProtector.Protect(result.AccessToken);
        instagram["connected_user_id"] = result.UserId;
        instagram["connected_username"] = result.Username;
        instagram["connected_account_type"] = result.AccountType;
        instagram["access_token_expires_at_utc"] = result.ExpiresInSeconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(result.ExpiresInSeconds).ToString("O")
            : "";
        _data.SaveSettingsDocument(document);
        if (_settingsInstagramAccessToken is not null)
            _settingsInstagramAccessToken.Password = result.AccessToken;
    }

    private void SetInstagramConnectionStatus(string text)
    {
        if (_apiConnectionStatuses.TryGetValue("instagram", out var status))
        {
            status.Text = text;
            status.Foreground = text.StartsWith("✓", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.FromRgb(25, 140, 75))
                : SettingsMutedBrush();
        }
    }

    private static MainShellWindow? FindWindow(DependencyObject child)
    {
        while (child is not null)
        {
            if (child is MainShellWindow window)
                return window;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}

internal sealed record InstagramBusinessLoginConfiguration(string AppId, string AppSecret);

internal static class InstagramBusinessLoginConfigurationStore
{
    public static InstagramBusinessLoginConfiguration Load(string settingsPath)
    {
        try
        {
            var path = GetPath(settingsPath);
            if (!File.Exists(path))
                return new InstagramBusinessLoginConfiguration("", "");
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var appId = root?["app_id"]?.GetValue<string>()?.Trim() ?? "";
            var encryptedSecret = root?["app_secret"]?.GetValue<string>() ?? "";
            var appSecret = encryptedSecret.Length == 0 ? "" : LocalSecretProtector.Unprotect(encryptedSecret).Trim();
            return new InstagramBusinessLoginConfiguration(appId, appSecret);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new InstagramBusinessLoginConfiguration("", "");
        }
    }

    public static void Save(string settingsPath, InstagramBusinessLoginConfiguration configuration)
    {
        var path = GetPath(settingsPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var root = new JsonObject
        {
            ["app_id"] = configuration.AppId.Trim(),
            ["app_secret"] = LocalSecretProtector.Protect(configuration.AppSecret.Trim()),
        };
        File.WriteAllText(path, root.ToJsonString());
    }

    private static string GetPath(string settingsPath) =>
        Path.Combine(Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory, "instagram-oauth.json");
}

internal sealed class InstagramBusinessLoginConfigurationDialog : Window
{
    private readonly TextBox _appId;
    private readonly PasswordBox _appSecret;

    private InstagramBusinessLoginConfigurationDialog(MainShellWindow owner, string appId, string appSecret)
    {
        Owner = owner;
        Title = "Instagram Business Login";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 500;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = "Connect Instagram automatically",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Enter the Instagram App ID and App Secret from your Meta Developer app. They are stored locally, and the App Secret is encrypted.\n\nBefore connecting, add this exact OAuth redirect URI to Instagram Business Login settings:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = InstagramBusinessLoginService.RedirectUri,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        stack.Children.Add(new TextBlock { Text = "Instagram App ID", FontWeight = FontWeights.SemiBold });
        _appId = new TextBox { Text = appId, Margin = new Thickness(0, 4, 0, 10), MinWidth = 440 };
        stack.Children.Add(_appId);

        stack.Children.Add(new TextBlock { Text = "Instagram App Secret", FontWeight = FontWeights.SemiBold });
        _appSecret = new PasswordBox { Margin = new Thickness(0, 4, 0, 14), MinWidth = 440 };
        _appSecret.Password = appSecret;
        stack.Children.Add(_appSecret);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        var connect = new Button { Content = "Continue to Instagram", MinWidth = 150, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        connect.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_appId.Text) || string.IsNullOrWhiteSpace(_appSecret.Password))
            {
                MessageBox.Show(this, "Both the Instagram App ID and App Secret are required.", "Instagram Business Login", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(connect);
        stack.Children.Add(actions);
        Content = stack;
    }

    public static InstagramBusinessLoginConfiguration? Show(MainShellWindow owner, string appId, string appSecret)
    {
        var dialog = new InstagramBusinessLoginConfigurationDialog(owner, appId, appSecret);
        return dialog.ShowDialog() == true
            ? new InstagramBusinessLoginConfiguration(dialog._appId.Text.Trim(), dialog._appSecret.Password.Trim())
            : null;
    }
}
