using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool SocialAgentLoadedHookRegistered = RegisterSocialAgentLoadedHook();
    private bool _socialAgentSettingsInjected;
    private PasswordBox? _socialAgentApiKeyBox;
    private TextBox? _socialAgentBaseUrlBox;
    private TextBlock? _socialAgentStatusText;

    private static bool RegisterSocialAgentLoadedHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindow_LoadedForSocialAgent));
        return true;
    }

    private static void MainShellWindow_LoadedForSocialAgent(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window) return;
        window.InitializeSocialAgent();
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.InjectSocialAgentSettings));
    }

    private void InjectSocialAgentSettings()
    {
        if (_socialAgentSettingsInjected || !_settingsPages.TryGetValue("facebook", out var page))
            return;

        if (page is not ScrollViewer scroll || scroll.Content is not StackPanel stack)
            return;

        _socialAgentSettingsInjected = true;
        var settings = SocialAgentSettingsStore.Load(_data.SettingsPath);

        var section = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 224, 234)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        var content = new StackPanel();
        section.Child = content;
        content.Children.Add(new TextBlock
        {
            Text = "Social Agent",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "The desktop app polls the website command queue and executes moderation/reply actions with the credentials stored on this PC. The API key is encrypted locally.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10),
        });

        content.Children.Add(new TextBlock { Text = "Website base URL" });
        _socialAgentBaseUrlBox = new TextBox
        {
            Text = settings.BaseUrl,
            Margin = new Thickness(0, 4, 0, 8),
        };
        content.Children.Add(_socialAgentBaseUrlBox);

        content.Children.Add(new TextBlock { Text = "Social Agent API key" });
        _socialAgentApiKeyBox = new PasswordBox
        {
            Margin = new Thickness(0, 4, 0, 8),
            Password = settings.ApiKey,
        };
        content.Children.Add(_socialAgentApiKeyBox);

        var save = new Button
        {
            Content = "Save Social Agent settings",
            Width = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 6),
        };
        save.Click += (_, _) => SaveSocialAgentSettings();
        content.Children.Add(save);

        _socialAgentStatusText = new TextBlock
        {
            Text = settings.IsConfigured ? "Configured — agent will poll every 30 seconds." : "Not configured.",
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(_socialAgentStatusText);
        stack.Children.Add(section);
    }

    private void SaveSocialAgentSettings()
    {
        try
        {
            SocialAgentSettingsStore.Save(
                _data.SettingsPath,
                _socialAgentBaseUrlBox?.Text ?? SocialAgentSettingsStore.DefaultBaseUrl,
                _socialAgentApiKeyBox?.Password ?? "");
            if (_socialAgentStatusText is not null)
                _socialAgentStatusText.Text = "Saved securely. The desktop Social Agent is active.";
        }
        catch (Exception error)
        {
            if (_socialAgentStatusText is not null)
                _socialAgentStatusText.Text = error.Message;
        }
    }
}
