using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _settingsWorkflowInitialized;
    private ContentControl? _settingsContentHost;
    private readonly Dictionary<string, Button> _settingsNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _settingsPages = new(StringComparer.OrdinalIgnoreCase);
    private CheckBox? _settingsStartMaximized;
    private CheckBox? _settingsRememberProject;
    private ComboBox? _settingsTheme;
    private ComboBox? _settingsImageProvider;
    private ComboBox? _settingsOrientation;
    private TextBox? _settingsResolveModulePath;
    private ComboBox? _settingsResolveMode;
    private TextBlock? _settingsIntegrityText;
    private TextBlock? _settingsPageStatus;
    private TextBox? _settingsYouTubeClientId;
    private PasswordBox? _settingsYouTubeClientSecret;
    private TextBlock? _settingsYouTubeConnectionStatus;
    private Button? _settingsYouTubeConnectButton;
    private PasswordBox? _settingsFacebookPageAccessToken;
    private readonly YouTubeOAuthService _youtubeOAuth = new();
    private string _settingsSelectedPage = "general";

    private void InitializeSettingsWorkflow()
    {
        if (_settingsWorkflowInitialized || MainTabs.Items.Count < 6 || MainTabs.Items[5] is not TabItem tab)
        {
            return;
        }

        _settingsWorkflowInitialized = true;

        foreach (var element in new FrameworkElement[]
        {
            ProjectsFolderTextBox, CheckUpdatesCheckBox,
            OpenAiKeyPasswordBox, OpenAiModelTextBox,
            PexelsKeyPasswordBox, PixabayKeyPasswordBox,
            YouTubeApiKeyPasswordBox,
            ResolvePathTextBox, TimelineWidthTextBox, TimelineHeightTextBox, FrameRateTextBox,
            SettingsStatusText,
        })
        {
            Detach(element);
        }

        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "App preferences, project integrity, providers, Resolve export, AI and version information.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 3, 0, 14),
        });
        root.Children.Add(header);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        var sidebar = SettingsCard(new Thickness(8));
        workspace.Children.Add(sidebar);
        var sidebarStack = new StackPanel();
        sidebar.Child = sidebarStack;
        sidebarStack.Children.Add(new TextBlock
        {
            Text = "PREFERENCES",
            Foreground = new SolidColorBrush(Color.FromRgb(152, 162, 179)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 8, 6, 8),
        });

        AddSettingsNav(sidebarStack, "general", "General");
        AddSettingsNav(sidebarStack, "integrity", "Project Integrity");
        AddSettingsNav(sidebarStack, "images", "Images");
        AddSettingsNav(sidebarStack, "resolve", "DaVinci Resolve");
        AddSettingsNav(sidebarStack, "ai", "AI");
        AddSettingsNav(sidebarStack, "youtube", "YouTube");
        AddSettingsNav(sidebarStack, "facebook", "Facebook");
        AddSettingsNav(sidebarStack, "about", "About");

        var contentBorder = SettingsCard(new Thickness(18));
        Grid.SetColumn(contentBorder, 2);
        workspace.Children.Add(contentBorder);
        _settingsContentHost = new ContentControl();
        contentBorder.Child = _settingsContentHost;

        _settingsPages["general"] = BuildGeneralSettingsPage();
        _settingsPages["integrity"] = BuildIntegritySettingsPage();
        _settingsPages["images"] = BuildImagesSettingsPage();
        _settingsPages["resolve"] = BuildResolveSettingsPage();
        _settingsPages["ai"] = BuildAiSettingsPage();
        _settingsPages["youtube"] = BuildYouTubeSettingsPage();
        _settingsPages["facebook"] = BuildFacebookSettingsPage();
        _settingsPages["about"] = BuildAboutSettingsPage();

        tab.Content = root;
        LoadExtendedSettings();
        SelectSettingsPage(_settingsSelectedPage);
    }

    private void AddSettingsNav(Panel parent, string key, string text)
    {
        var button = new Button
        {
            Content = text,
            Tag = key,
            Height = 36,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        };
        button.Click += (_, _) => SelectSettingsPage(key);
        _settingsNavButtons[key] = button;
        parent.Children.Add(button);
    }

    private void SelectSettingsPage(string key)
    {
        if (_settingsContentHost is null || !_settingsPages.TryGetValue(key, out var page))
        {
            return;
        }

        _settingsSelectedPage = key;
        _settingsContentHost.Content = page;
        foreach (var pair in _settingsNavButtons)
        {
            var selected = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected
                ? new SolidColorBrush(Color.FromRgb(234, 241, 255))
                : Brushes.Transparent;
            pair.Value.Foreground = selected
                ? new SolidColorBrush(Color.FromRgb(23, 92, 211))
                : new SolidColorBrush(Color.FromRgb(52, 64, 84));
            pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (key == "integrity")
        {
            RefreshIntegrityReport();
        }
    }

    private FrameworkElement BuildGeneralSettingsPage()
    {
        var page = SettingsPageStack("General", "Choose where projects are stored and how the app behaves at startup.");

        var storage = SettingsSection("Project storage");
        page.Children.Add(storage);
        var storageStack = (StackPanel)storage.Child;
        storageStack.Children.Add(SettingsFieldLabel("Projects folder"));
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ProjectsFolderTextBox.Margin = new Thickness(0, 5, 8, 0);
        folderRow.Children.Add(ProjectsFolderTextBox);
        var browse = new Button { Content = "Browse", Width = 88, Margin = new Thickness(0, 5, 0, 0) };
        browse.Click += (_, _) => BrowseSettingsFolder();
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        storageStack.Children.Add(folderRow);

        var startup = SettingsSection("Startup");
        page.Children.Add(startup);
        var startupStack = (StackPanel)startup.Child;
        _settingsStartMaximized = new CheckBox { Content = "Open maximized", Margin = new Thickness(0, 8, 0, 4) };
        _settingsRememberProject = new CheckBox { Content = "Remember last opened project", Margin = new Thickness(0, 4, 0, 4) };
        CheckUpdatesCheckBox.Content = "Check for updates on startup";
        CheckUpdatesCheckBox.Margin = new Thickness(0, 4, 0, 8);
        startupStack.Children.Add(_settingsStartMaximized);
        startupStack.Children.Add(_settingsRememberProject);
        startupStack.Children.Add(CheckUpdatesCheckBox);

        var appearance = SettingsSection("Appearance");
        page.Children.Add(appearance);
        var appearanceStack = (StackPanel)appearance.Child;
        appearanceStack.Children.Add(SettingsFieldLabel("Theme"));
        _settingsTheme = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        _settingsTheme.Items.Add("Light");
        _settingsTheme.Items.Add("Dark");
        _settingsTheme.Items.Add("System");
        appearanceStack.Children.Add(_settingsTheme);
        appearanceStack.Children.Add(new TextBlock
        {
            Text = "The C# shell currently keeps the Windows light styling; this value is preserved for Python compatibility.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
        });

        page.Children.Add(SettingsFooter("Save changes", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildImagesSettingsPage()
    {
        var page = SettingsPageStack("Images", "Configure image-search providers, API keys, and the default result orientation.");

        var provider = SettingsSection("Search provider");
        page.Children.Add(provider);
        var providerStack = (StackPanel)provider.Child;
        _settingsImageProvider = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        _settingsImageProvider.Items.Add("Pixabay");
        _settingsImageProvider.Items.Add("Pexels");
        providerStack.Children.Add(_settingsImageProvider);

        var keys = SettingsSection("API keys");
        page.Children.Add(keys);
        var keyStack = (StackPanel)keys.Child;
        keyStack.Children.Add(SettingsFieldLabel("Pixabay"));
        PixabayKeyPasswordBox.Margin = new Thickness(0, 5, 0, 10);
        keyStack.Children.Add(PixabayKeyPasswordBox);
        keyStack.Children.Add(SettingsFieldLabel("Pexels"));
        PexelsKeyPasswordBox.Margin = new Thickness(0, 5, 0, 0);
        keyStack.Children.Add(PexelsKeyPasswordBox);

        var defaults = SettingsSection("Defaults");
        page.Children.Add(defaults);
        var defaultsStack = (StackPanel)defaults.Child;
        defaultsStack.Children.Add(SettingsFieldLabel("Orientation"));
        _settingsOrientation = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        _settingsOrientation.Items.Add("vertical");
        _settingsOrientation.Items.Add("horizontal");
        _settingsOrientation.Items.Add("all");
        defaultsStack.Children.Add(_settingsOrientation);

        page.Children.Add(SettingsFooter("Save changes", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildAiSettingsPage()
    {
        var page = SettingsPageStack("AI", "Configure OpenAI for research, scripts, visual prompts, and narration.");

        var provider = SettingsSection("Provider");
        page.Children.Add(provider);
        ((StackPanel)provider.Child).Children.Add(new TextBlock { Text = "OpenAI", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });

        var credentials = SettingsSection("Credentials");
        page.Children.Add(credentials);
        var credentialsStack = (StackPanel)credentials.Child;
        credentialsStack.Children.Add(SettingsFieldLabel("OpenAI API key"));
        OpenAiKeyPasswordBox.Margin = new Thickness(0, 5, 0, 0);
        credentialsStack.Children.Add(OpenAiKeyPasswordBox);

        var model = SettingsSection("Model");
        page.Children.Add(model);
        var modelStack = (StackPanel)model.Child;
        modelStack.Children.Add(SettingsFieldLabel("Text model"));
        OpenAiModelTextBox.Margin = new Thickness(0, 5, 0, 0);
        modelStack.Children.Add(OpenAiModelTextBox);
        modelStack.Children.Add(new TextBlock
        {
            Text = "Used by the production pipeline for text generation tasks.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 7, 0, 0),
        });

        page.Children.Add(SettingsFooter("Save changes", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildResolveSettingsPage()
    {
        var page = SettingsPageStack("DaVinci Resolve", "Configure the Resolve Free export format and optional scripting connection. Normal exports use FCPXML for manual import.");

        var export = SettingsSection("Export settings");
        page.Children.Add(export);
        var exportStack = (StackPanel)export.Child;
        var dimensions = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < 3; i++) dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        AddSettingsNumberField(dimensions, 0, "Width", TimelineWidthTextBox);
        AddSettingsNumberField(dimensions, 1, "Height", TimelineHeightTextBox);
        AddSettingsNumberField(dimensions, 2, "Frame rate", FrameRateTextBox);
        exportStack.Children.Add(dimensions);

        var scripting = SettingsSection("Optional scripting");
        page.Children.Add(scripting);
        var scriptingStack = (StackPanel)scripting.Child;
        scriptingStack.Children.Add(SettingsFieldLabel("Resolve application"));
        var applicationRow = SettingsPathRow(ResolvePathTextBox, BrowseResolveApplication);
        scriptingStack.Children.Add(applicationRow);
        scriptingStack.Children.Add(SettingsFieldLabel("Scripting Modules folder"));
        _settingsResolveModulePath = new TextBox();
        scriptingStack.Children.Add(SettingsPathRow(_settingsResolveModulePath, BrowseResolveModuleFolder));
        scriptingStack.Children.Add(SettingsFieldLabel("Integration mode"));
        _settingsResolveMode = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        _settingsResolveMode.Items.Add("external");
        _settingsResolveMode.Items.Add("internal-script");
        scriptingStack.Children.Add(_settingsResolveMode);

        page.Children.Add(SettingsFooter("Save settings", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildYouTubeSettingsPage()
    {
        var page = SettingsPageStack(
            "YouTube",
            "Configure analytics and securely connect the channel for comments and playlists.");

        var credentials = SettingsSection("YouTube Data API v3");
        page.Children.Add(credentials);
        var credentialsStack = (StackPanel)credentials.Child;
        credentialsStack.Children.Add(SettingsFieldLabel("API key"));
        YouTubeApiKeyPasswordBox.Margin = new Thickness(0, 5, 0, 0);
        credentialsStack.Children.Add(YouTubeApiKeyPasswordBox);
        credentialsStack.Children.Add(new TextBlock
        {
            Text = "Create the key in Google Cloud, enable YouTube Data API v3, then restrict the key to that API.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        var management = SettingsSection("Channel management");
        page.Children.Add(management);
        var managementStack = (StackPanel)management.Child;
        managementStack.Children.Add(new TextBlock
        {
            Text = "Create an OAuth client with application type Desktop app in the same Google Cloud project.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 6, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });
        managementStack.Children.Add(SettingsFieldLabel("OAuth desktop client ID"));
        _settingsYouTubeClientId = new TextBox { Margin = new Thickness(0, 5, 0, 8) };
        managementStack.Children.Add(_settingsYouTubeClientId);
        managementStack.Children.Add(SettingsFieldLabel("OAuth client secret"));
        _settingsYouTubeClientSecret = new PasswordBox { Margin = new Thickness(0, 5, 0, 10) };
        managementStack.Children.Add(_settingsYouTubeClientSecret);

        var connectionRow = new Grid();
        connectionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        connectionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _settingsYouTubeConnectionStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SettingsMutedBrush(),
        };
        connectionRow.Children.Add(_settingsYouTubeConnectionStatus);
        _settingsYouTubeConnectButton = new Button { Content = "Connect Google account", MinWidth = 154, Margin = new Thickness(8, 0, 0, 0) };
        _settingsYouTubeConnectButton.Click += async (_, _) => await ConnectYouTubeAsync();
        Grid.SetColumn(_settingsYouTubeConnectButton, 1);
        connectionRow.Children.Add(_settingsYouTubeConnectButton);
        var disconnect = new Button { Content = "Disconnect", MinWidth = 92, Margin = new Thickness(8, 0, 0, 0) };
        disconnect.Click += async (_, _) => await DisconnectYouTubeAsync();
        Grid.SetColumn(disconnect, 2);
        connectionRow.Children.Add(disconnect);
        managementStack.Children.Add(connectionRow);

        var behaviour = SettingsSection("Automatic updates");
        page.Children.Add(behaviour);
        ((StackPanel)behaviour.Child).Children.Add(new TextBlock
        {
            Text = "Published quizzes with a saved YouTube video link update whenever Quiz History opens or you click Refresh.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        page.Children.Add(SettingsFooter("Save YouTube settings", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildFacebookSettingsPage()
    {
        var page = SettingsPageStack(
            "Facebook",
            "Connect the Factburst Quiz Page so Facebook Reel figures can update automatically.");

        var credentials = SettingsSection("Meta Graph API");
        page.Children.Add(credentials);
        var stack = (StackPanel)credentials.Child;
        stack.Children.Add(SettingsFieldLabel("Page access token"));
        _settingsFacebookPageAccessToken = new PasswordBox { Margin = new Thickness(0, 5, 0, 0) };
        stack.Children.Add(_settingsFacebookPageAccessToken);
        stack.Children.Add(new TextBlock
        {
            Text = "Use a Page access token with permission to read Page engagement and insights. The token is encrypted on this PC.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        var behaviour = SettingsSection("Automatic updates");
        page.Children.Add(behaviour);
        ((StackPanel)behaviour.Child).Children.Add(new TextBlock
        {
            Text = "Link each exported Short to its Facebook Reel in Facebook Manager. Views, reactions, comments and shares then update when you click Refresh from Facebook.",
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        page.Children.Add(SettingsFooter("Save Facebook settings", SaveAllSettings));
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildIntegritySettingsPage()
    {
        var page = SettingsPageStack("Project Integrity", "Check the database and project folders for missing or inconsistent project data.");
        var card = SettingsSection("Integrity report");
        page.Children.Add(card);
        var stack = (StackPanel)card.Child;
        _settingsIntegrityText = new TextBlock
        {
            Text = "Run a scan to check project integrity.",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            LineHeight = 20,
            Margin = new Thickness(0, 6, 0, 10),
        };
        stack.Children.Add(_settingsIntegrityText);
        var scan = new Button { Content = "Run integrity scan", HorizontalAlignment = HorizontalAlignment.Left };
        scan.Click += (_, _) => RefreshIntegrityReport();
        stack.Children.Add(scan);
        return SettingsScrollable(page);
    }

    private FrameworkElement BuildAboutSettingsPage()
    {
        var page = SettingsPageStack("About", "Application information, runtime details, and update status.");
        var version = typeof(MainShellWindow).Assembly.GetName().Version?.ToString() ?? "development";

        var appCard = SettingsSection("FactVaultManager");
        page.Children.Add(appCard);
        var appStack = (StackPanel)appCard.Child;
        appStack.Children.Add(new TextBlock { Text = $"Version {version}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        appStack.Children.Add(new TextBlock { Text = "C# desktop UI • Python production engine", Foreground = SettingsMutedBrush() });

        var details = SettingsSection("Details");
        page.Children.Add(details);
        var detailsStack = (StackPanel)details.Child;
        detailsStack.Children.Add(SettingsAboutRow("Runtime root", _data.RuntimeRoot));
        detailsStack.Children.Add(SettingsAboutRow("Database", _data.DatabasePath));
        detailsStack.Children.Add(SettingsAboutRow("Settings", _data.SettingsPath));

        var updates = SettingsSection("Updates");
        page.Children.Add(updates);
        var updatesStack = (StackPanel)updates.Child;
        updatesStack.Children.Add(new TextBlock { Text = "Check whether a newer installed release is available.", Foreground = SettingsMutedBrush(), Margin = new Thickness(0, 6, 0, 8) });
        var updateButton = new Button { Content = "Check for updates", HorizontalAlignment = HorizontalAlignment.Left };
        updateButton.Click += CheckUpdates_Click;
        updatesStack.Children.Add(updateButton);
        return SettingsScrollable(page);
    }

    private void LoadExtendedSettings()
    {
        try
        {
            var node = LoadSettingsJson();
            _settingsStartMaximized!.IsChecked = ReadBool(node, "general", "start_maximized", true);
            _settingsRememberProject!.IsChecked = ReadBool(node, "general", "remember_last_project", true);
            SelectComboValue(_settingsTheme!, ReadString(node, "general", "theme", "light").TitleCaseInvariant(), "Light");
            SelectComboValue(_settingsImageProvider!, ReadString(node, "images", "provider", "Pixabay"), "Pixabay");
            SelectComboValue(_settingsOrientation!, ReadString(node, "images", "default_orientation", "vertical"), "vertical");
            _settingsResolveModulePath!.Text = ReadString(node, "resolve", "scripting_module_path", "");
            SelectComboValue(_settingsResolveMode!, ReadString(node, "resolve", "integration_mode", "external"), "external");
            var settings = _data.LoadSettings();
            if (_settingsYouTubeClientId is not null) _settingsYouTubeClientId.Text = settings.YouTubeOAuthClientId;
            if (_settingsYouTubeClientSecret is not null) _settingsYouTubeClientSecret.Password = settings.YouTubeOAuthClientSecret;
            if (_settingsFacebookPageAccessToken is not null) _settingsFacebookPageAccessToken.Password = settings.FacebookPageAccessToken;
            SetYouTubeConnectionStatus(settings.YouTubeOAuthRefreshToken.Length > 0 ? "Connected to Google" : "Not connected");
        }
        catch (Exception error)
        {
            SettingsStatusText.Text = error.Message;
        }
    }

    private void SaveAllSettings() => SaveAllSettingsCore(null);

    private bool SaveAllSettingsCore(string? refreshTokenOverride)
    {
        try
        {
            if (!int.TryParse(TimelineWidthTextBox.Text, out var width) || width <= 0)
                throw new ArgumentException("Timeline width must be a positive whole number.");
            if (!int.TryParse(TimelineHeightTextBox.Text, out var height) || height <= 0)
                throw new ArgumentException("Timeline height must be a positive whole number.");
            if (!double.TryParse(FrameRateTextBox.Text, out var frameRate) || frameRate <= 0)
                throw new ArgumentException("Frame rate must be a positive number.");

            var existingSettings = _data.LoadSettings();
            _data.SaveSettings(new AppSettingsModel
            {
                ProjectsFolder = ProjectsFolderTextBox.Text.Trim(),
                Theme = (_settingsTheme?.SelectedItem?.ToString() ?? "Light").ToLowerInvariant(),
                OpenAiKey = OpenAiKeyPasswordBox.Password.Trim(),
                OpenAiModel = string.IsNullOrWhiteSpace(OpenAiModelTextBox.Text) ? "gpt-5-mini" : OpenAiModelTextBox.Text.Trim(),
                PexelsKey = PexelsKeyPasswordBox.Password.Trim(),
                PixabayKey = PixabayKeyPasswordBox.Password.Trim(),
                YouTubeApiKey = YouTubeApiKeyPasswordBox.Password.Trim(),
                YouTubeOAuthClientId = _settingsYouTubeClientId?.Text.Trim() ?? existingSettings.YouTubeOAuthClientId,
                YouTubeOAuthClientSecret = _settingsYouTubeClientSecret?.Password.Trim() ?? existingSettings.YouTubeOAuthClientSecret,
                YouTubeOAuthRefreshToken = refreshTokenOverride ?? existingSettings.YouTubeOAuthRefreshToken,
                FacebookPageAccessToken = _settingsFacebookPageAccessToken?.Password.Trim() ?? existingSettings.FacebookPageAccessToken,
                ResolvePath = ResolvePathTextBox.Text.Trim(),
                TimelineWidth = width,
                TimelineHeight = height,
                FrameRate = frameRate,
                CheckUpdates = CheckUpdatesCheckBox.IsChecked == true,
            });

            var node = LoadSettingsJson();
            var general = EnsureSection(node, "general");
            var images = EnsureSection(node, "images");
            var resolve = EnsureSection(node, "resolve");
            var ai = EnsureSection(node, "ai");
            general["start_maximized"] = _settingsStartMaximized?.IsChecked == true;
            general["remember_last_project"] = _settingsRememberProject?.IsChecked == true;
            images["provider"] = _settingsImageProvider?.SelectedItem?.ToString() ?? "Pixabay";
            images["default_orientation"] = _settingsOrientation?.SelectedItem?.ToString() ?? "vertical";
            resolve["scripting_module_path"] = _settingsResolveModulePath?.Text.Trim() ?? "";
            resolve["integration_mode"] = _settingsResolveMode?.SelectedItem?.ToString() ?? "external";
            ai["provider"] = "OpenAI";
            Directory.CreateDirectory(Path.GetDirectoryName(_data.SettingsPath)!);
            File.WriteAllText(_data.SettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var message = "Settings saved.";
            SettingsStatusText.Text = message;
            if (_settingsPageStatus is not null) _settingsPageStatus.Text = message;
            HeaderStatusText.Text = message;
            return true;
        }
        catch (Exception error)
        {
            SettingsStatusText.Text = error.Message;
            if (_settingsPageStatus is not null) _settingsPageStatus.Text = error.Message;
            return false;
        }
    }

    private async Task ConnectYouTubeAsync()
    {
        if (_settingsYouTubeConnectButton is null) return;
        try
        {
            _settingsYouTubeConnectButton.IsEnabled = false;
            SetYouTubeConnectionStatus("Waiting for Google sign-in...");
            var clientId = _settingsYouTubeClientId?.Text.Trim() ?? "";
            var clientSecret = _settingsYouTubeClientSecret?.Password.Trim() ?? "";
            var tokens = await _youtubeOAuth.AuthorizeAsync(clientId, clientSecret);
            if (!SaveAllSettingsCore(tokens.RefreshToken))
                throw new InvalidOperationException("The YouTube connection could not be saved.");
            SetYouTubeConnectionStatus("Connected to Google");
        }
        catch (Exception error)
        {
            SetYouTubeConnectionStatus("Not connected");
            MessageBox.Show(this, error.Message, "Connect YouTube", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _settingsYouTubeConnectButton.IsEnabled = true;
        }
    }

    private async Task DisconnectYouTubeAsync()
    {
        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthRefreshToken.Length == 0)
        {
            SetYouTubeConnectionStatus("Not connected");
            return;
        }
        if (MessageBox.Show(
                this,
                "Disconnect Factburst Quiz Manager from this Google account?",
                "Disconnect YouTube",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await _youtubeOAuth.RevokeAsync(settings.YouTubeOAuthRefreshToken);
            if (!SaveAllSettingsCore(""))
                throw new InvalidOperationException("The disconnected state could not be saved.");
            SetYouTubeConnectionStatus("Not connected");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Disconnect YouTube", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetYouTubeConnectionStatus(string text)
    {
        if (_settingsYouTubeConnectionStatus is not null)
        {
            _settingsYouTubeConnectionStatus.Text = text;
            _settingsYouTubeConnectionStatus.Foreground = text.StartsWith("Connected", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.FromRgb(25, 140, 75))
                : SettingsMutedBrush();
        }
    }

    private void RefreshIntegrityReport()
    {
        if (_settingsIntegrityText is null) return;
        try
        {
            var lines = new List<string>();
            lines.Add(File.Exists(_data.DatabasePath) ? "✓ Database found" : "✕ Database missing");
            lines.Add(File.Exists(_data.SettingsPath) ? "✓ Settings file found" : "! Settings file not created yet");

            var settings = _data.LoadSettings();
            var root = settings.ProjectsFolder.Trim();
            lines.Add(!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) ? "✓ Projects folder found" : "✕ Projects folder missing or not configured");

            var missing = new List<string>();
            foreach (var project in _projects)
            {
                try
                {
                    var folder = _data.ResolveProjectFolder(project);
                    if (!Directory.Exists(folder)) missing.Add(project.Title);
                }
                catch
                {
                    missing.Add(project.Title);
                }
            }
            lines.Add($"Projects checked: {_projects.Count}");
            lines.Add(missing.Count == 0 ? "✓ All database project folders exist" : $"✕ Missing project folders: {missing.Count}");
            foreach (var title in missing.Take(12)) lines.Add($"  • {title}");
            if (missing.Count > 12) lines.Add($"  • …and {missing.Count - 12} more");

            _settingsIntegrityText.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception error)
        {
            _settingsIntegrityText.Text = $"Integrity scan failed: {error.Message}";
        }
    }

    private bool GetStartMaximizedSetting()
    {
        try { return ReadBool(LoadSettingsJson(), "general", "start_maximized", true); }
        catch { return true; }
    }

    private JsonObject LoadSettingsJson()
    {
        if (!File.Exists(_data.SettingsPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(_data.SettingsPath)) as JsonObject ?? new JsonObject();
    }

    private static JsonObject EnsureSection(JsonObject root, string name)
    {
        if (root[name] is JsonObject section) return section;
        section = new JsonObject();
        root[name] = section;
        return section;
    }

    private static string ReadString(JsonObject root, string section, string key, string fallback) =>
        root[section]?[key]?.GetValue<string>() ?? fallback;

    private static bool ReadBool(JsonObject root, string section, string key, bool fallback) =>
        root[section]?[key]?.GetValue<bool>() ?? fallback;

    private static void SelectComboValue(ComboBox combo, string value, string fallback)
    {
        foreach (var item in combo.Items)
        {
            if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(item => string.Equals(item?.ToString(), fallback, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
    }

    private void BrowseSettingsFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select projects folder" };
        if (dialog.ShowDialog(this) == true) ProjectsFolderTextBox.Text = dialog.FolderName;
    }

    private void BrowseResolveApplication()
    {
        var dialog = new OpenFileDialog { Title = "Select DaVinci Resolve application", Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) ResolvePathTextBox.Text = dialog.FileName;
    }

    private void BrowseResolveModuleFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select Resolve Scripting Modules folder" };
        if (dialog.ShowDialog(this) == true && _settingsResolveModulePath is not null) _settingsResolveModulePath.Text = dialog.FolderName;
    }

    private static Border SettingsCard(Thickness padding) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
    };

    private static StackPanel SettingsPageStack(string title, string subtitle)
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = title, FontFamily = new FontFamily("Segoe UI Variable Display"), FontSize = 23, FontWeight = FontWeights.SemiBold });
        page.Children.Add(new TextBlock { Text = subtitle, Foreground = SettingsMutedBrush(), Margin = new Thickness(0, 3, 0, 16), TextWrapping = TextWrapping.Wrap });
        return page;
    }

    private static ScrollViewer SettingsScrollable(FrameworkElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private static Border SettingsSection(string title)
    {
        var border = SettingsCard(new Thickness(14, 12, 14, 14));
        border.Margin = new Thickness(0, 0, 0, 10);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        border.Child = stack;
        return border;
    }

    private static TextBlock SettingsFieldLabel(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
    private static Brush SettingsMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));

    private FrameworkElement SettingsFooter(string buttonText, Action saveAction)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new TextBlock { Foreground = SettingsMutedBrush(), VerticalAlignment = VerticalAlignment.Center };
        _settingsPageStatus ??= status;
        grid.Children.Add(status);
        var save = new Button { Content = buttonText, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 126, Margin = new Thickness(0) };
        save.Click += (_, _) => saveAction();
        Grid.SetColumn(save, 1);
        grid.Children.Add(save);
        return grid;
    }

    private static void AddSettingsNumberField(Grid grid, int column, string title, TextBox box)
    {
        var stack = new StackPanel { Margin = column < 2 ? new Thickness(0, 0, 8, 0) : new Thickness(0) };
        stack.Children.Add(SettingsFieldLabel(title));
        box.Margin = new Thickness(0, 5, 0, 0);
        stack.Children.Add(box);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private static Grid SettingsPathRow(TextBox box, Action browseAction)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(box);
        var browse = new Button { Content = "Browse", Width = 88, Margin = new Thickness(0) };
        browse.Click += (_, _) => browseAction();
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        return row;
    }

    private static Grid SettingsAboutRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        var valueText = new TextBlock { Text = value, Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }
}

internal static class SettingsStringExtensions
{
    public static string TitleCaseInvariant(this string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
