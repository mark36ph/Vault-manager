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
        CheckUpdatesCheckBox.IsChecked = settings.CheckUpdatesOnLaunch;
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HeaderStatusText.Text = "Checking for C# app updates...";
            var result = await _updates.CheckAsync();

            if (!result.HasUpdate)
            {
                MessageBox.Show(
                    result.Message,
                    $"FactVaultManager {GetBuildVersionLabel()}",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                HeaderStatusText.Text = $"Factburst Quiz Manager • {GetBuildVersionLabel()} • Quizzes • Library • Uploads • Analytics • Website management";
                return;
            }

            var prompt = $"{result.Message}\n\nInstall v{result.Manifest?.LatestVersion} now?";
            if (MessageBox.Show(prompt, "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            HeaderStatusText.Text = $"Updating to v{result.Manifest?.LatestVersion}...";
            var install = await _updates.InstallAsync(result.Manifest!);
            if (!install.Success)
            {
                MessageBox.Show(install.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                HeaderStatusText.Text = "Update failed";
                return;
            }

            MessageBox.Show(install.Message, "Update ready", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
            HeaderStatusText.Text = "Update failed";
        }
    }
}
