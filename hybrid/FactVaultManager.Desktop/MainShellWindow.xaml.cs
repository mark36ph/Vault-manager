using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow : Window
{
    private readonly DesktopDataService _data = new();
    private readonly AppUpdateService _updates = new();

    // The current Settings workflow reparents these controls into its runtime pages.
    // They no longer need a retired XAML settings page to exist first.
    private readonly TextBox ProjectsFolderTextBox = new();
    private readonly CheckBox CheckUpdatesCheckBox = new();
    private readonly PasswordBox OpenAiKeyPasswordBox = new();
    private readonly TextBox OpenAiModelTextBox = new();
    private readonly PasswordBox PexelsKeyPasswordBox = new();
    private readonly PasswordBox PixabayKeyPasswordBox = new();
    private readonly PasswordBox YouTubeApiKeyPasswordBox = new();
    private readonly TextBox ResolvePathTextBox = new();
    private readonly TextBox TimelineWidthTextBox = new();
    private readonly TextBox TimelineHeightTextBox = new();
    private readonly TextBox FrameRateTextBox = new();
    private readonly TextBlock SettingsStatusText = new();

    // Retained for the Settings integrity report. The retired Projects workspace no longer populates it at startup.
    private readonly ObservableCollection<DesktopProject> _projects = [];

    public MainShellWindow()
    {
        InitializeComponent();

        // Installed releases can use a different LocalAppData root from the development
        // checkout. Recover both encrypted credentials and non-secret preferences before
        // the settings workflow reads the destination document.
        InstalledCredentialRecovery.Run();
        InstalledSettingsRecovery.Run();
        LoadBootstrapSettingsInputs();

        Loaded += (_, _) =>
        {
            _data.ResumeQuizFolderCleanupSafely();
            InitializeUploadManagerThumbnailRegenerationActions();
        };
    }

    private void LoadBootstrapSettingsInputs()
    {
        var settings = _data.LoadSettings();
        ProjectsFolderTextBox.Text = settings.ProjectsFolder;
        CheckUpdatesCheckBox.IsChecked = settings.CheckUpdates;
        OpenAiKeyPasswordBox.Password = settings.OpenAiKey;
        OpenAiModelTextBox.Text = settings.OpenAiModel;
        PexelsKeyPasswordBox.Password = settings.PexelsKey;
        PixabayKeyPasswordBox.Password = settings.PixabayKey;
        YouTubeApiKeyPasswordBox.Password = settings.YouTubeApiKey;
        ResolvePathTextBox.Text = settings.ResolvePath;
        TimelineWidthTextBox.Text = settings.TimelineWidth.ToString(CultureInfo.InvariantCulture);
        TimelineHeightTextBox.Text = settings.TimelineHeight.ToString(CultureInfo.InvariantCulture);
        FrameRateTextBox.Text = settings.FrameRate.ToString(CultureInfo.InvariantCulture);
    }

    private string GetBuildVersionLabel() => $"Build {CurrentBuildNumber}";

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HeaderStatusText.Text = "Checking for updates...";

            if (!_updates.IsInstalled)
            {
                const string developmentMessage =
                    "This is the development build, so the in-app updater is not active yet.\n\n" +
                    "Check for Updates will become fully active after you install the first Factburst Quiz Manager release build.";
                HeaderStatusText.Text = "Updates: development build";
                MessageBox.Show(
                    this,
                    developmentMessage,
                    "Factburst Quiz Manager Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var update = await _updates.CheckAsync();
            if (update is null)
            {
                var message = $"Factburst Quiz Manager {_updates.CurrentVersion} is up to date.";
                HeaderStatusText.Text = message;
                MessageBox.Show(
                    this,
                    message,
                    "Factburst Quiz Manager Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            HeaderStatusText.Text = "Update found. Downloading...";
            await _updates.InstallAsync(update, percent => Dispatcher.Invoke(() =>
                HeaderStatusText.Text = $"Downloading update... {percent}%"));
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Update failed: {error.Message}";
            MessageBox.Show(
                this,
                error.Message,
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
