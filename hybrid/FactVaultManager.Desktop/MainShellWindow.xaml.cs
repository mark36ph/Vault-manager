using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow : Window
{
    private readonly DesktopDataService _data = new();
    private readonly AppUpdateService _updates = new();
    private List<DesktopProject> _projects = new();
    private DesktopProject? _editingProject;

    public MainShellWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        try
        {
            _projects = _data.GetProjects().ToList();
            ProjectsGrid.ItemsSource = null;
            ProjectsGrid.ItemsSource = _projects;
            MediaProjectComboBox.ItemsSource = null;
            MediaProjectComboBox.ItemsSource = _projects;
            AssetProjectComboBox.ItemsSource = null;
            AssetProjectComboBox.ItemsSource = _projects;

            var summary = _data.GetDashboardSummary();
            TotalCountText.Text = summary.Total.ToString();
            InProgressCountText.Text = summary.InProgress.ToString();
            CompletedCountText.Text = summary.Completed.ToString();
            ScheduledCountText.Text = summary.Scheduled.ToString();
            PublishedCountText.Text = summary.Published.ToString();

            if (_projects.Count > 0)
            {
                if (ProjectsGrid.SelectedItem is not DesktopProject) ProjectsGrid.SelectedIndex = 0;
                if (MediaProjectComboBox.SelectedItem is null) MediaProjectComboBox.SelectedIndex = 0;
                if (AssetProjectComboBox.SelectedItem is null) AssetProjectComboBox.SelectedIndex = 0;
            }
            LoadSettings();
            HeaderStatusText.Text = $"C# desktop shell • {_projects.Count} projects • Python production engine";
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Data error: {error.Message}";
        }
    }

    private void RefreshAll_Click(object sender, RoutedEventArgs e) => RefreshAll();
    private void GoProjects_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void GoMedia_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 3;

    private void OpenProduction_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 2;
        ApplyNavigationSelection(2);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HeaderStatusText.Text = "Checking for updates...";

            if (!_updates.IsInstalled)
            {
                const string developmentMessage =
                    "This is the development build, so the in-app updater is not active yet.\n\n" +
                    "Check for Updates will become fully active after you install the first FactVaultManager release build.";
                HeaderStatusText.Text = "Updates: development build";
                MessageBox.Show(
                    this,
                    developmentMessage,
                    "FactVaultManager Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var update = await _updates.CheckAsync();
            if (update is null)
            {
                var message = $"FactVaultManager {_updates.CurrentVersion} is up to date.";
                HeaderStatusText.Text = message;
                MessageBox.Show(
                    this,
                    message,
                    "FactVaultManager Updates",
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

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var title = NewProjectTitleTextBox.Text.Trim();
            var created = _data.CreateProject(title, "Misc", "In Progress");
            NewProjectTitleTextBox.Clear();
            RefreshAll();
            ProjectsGrid.SelectedItem = _projects.FirstOrDefault(project => project.Id == created.Id);
            MainTabs.SelectedIndex = 1;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsGrid.SelectedItem is not DesktopProject project)
        {
            _editingProject = null;
            ProjectEditorTitle.Text = "Select a project";
            ProjectEditorFolderText.Text = "";
            return;
        }

        _editingProject = project;
        ProjectEditorTitle.Text = project.Title;
        try { ProjectEditorFolderText.Text = _data.ResolveProjectFolder(project); }
        catch { ProjectEditorFolderText.Text = project.Folder; }
        ProjectCategoryTextBox.Text = project.Category;
        SelectStatus(project.Status);
        ProjectPinnedCheckBox.IsChecked = project.Pinned;
        ProjectScriptTextBox.Text = project.Script;
        ProjectDescriptionTextBox.Text = project.Description;
        ProjectPinnedCommentTextBox.Text = project.PinnedComment;
        ProjectTagsTextBox.Text = project.Tags;
        ProjectNotesTextBox.Text = project.Notes;
        ProjectSourcesTextBox.Text = project.Sources;
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_editingProject is null) return;
        try
        {
            var updated = _editingProject with
            {
                Category = ProjectCategoryTextBox.Text.Trim(),
                Script = ProjectScriptTextBox.Text,
                Description = ProjectDescriptionTextBox.Text,
                PinnedComment = ProjectPinnedCommentTextBox.Text,
                Tags = ProjectTagsTextBox.Text,
                Notes = ProjectNotesTextBox.Text,
                Sources = ProjectSourcesTextBox.Text,
                Pinned = ProjectPinnedCheckBox.IsChecked == true,
            };
            _data.SaveProject(updated);
            var id = updated.Id;
            RefreshAll();
            ProjectsGrid.SelectedItem = _projects.FirstOrDefault(project => project.Id == id);
            HeaderStatusText.Text = $"Saved {updated.Title}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_editingProject is null) return;
        try
        {
            var newStatus = SelectedStatus();
            var updated = _data.ChangeStatus(_editingProject, newStatus);
            var id = updated.Id;
            RefreshAll();
            ProjectsGrid.SelectedItem = _projects.FirstOrDefault(project => project.Id == id);
            HeaderStatusText.Text = $"Moved {updated.Title} to {updated.Status}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Change Status", MessageBoxButton.OK, MessageBoxImage.Error);
            SelectStatus(_editingProject.Status);
        }
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (_editingProject is null) return;
        var answer = MessageBox.Show(
            this,
            $"Delete '{_editingProject.Title}' and its project folder?\n\nThis cannot be undone.",
            "Delete Project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _data.DeleteProject(_editingProject, deleteFolder: true);
            _editingProject = null;
            RefreshAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectStatus(string status)
    {
        foreach (var item in ProjectStatusComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), status, StringComparison.OrdinalIgnoreCase))
            {
                ProjectStatusComboBox.SelectedItem = item;
                return;
            }
        }
        ProjectStatusComboBox.SelectedIndex = 1;
    }

    private string SelectedStatus() =>
        (ProjectStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "In Progress";

    private void MediaProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshMedia();
    private void RefreshMedia_Click(object sender, RoutedEventArgs e) => RefreshMedia();

    private void RefreshMedia()
    {
        try
        {
            MediaGrid.ItemsSource = _data.GetMedia(MediaProjectComboBox.SelectedItem as DesktopProject);
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Media error: {error.Message}";
        }
    }

    private void AssetProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshAssetReview();
    private void RefreshAssetReview_Click(object sender, RoutedEventArgs e) => RefreshAssetReview();

    private void RefreshAssetReview()
    {
        try
        {
            AssetReviewGrid.ItemsSource = _data.GetAssetReview(AssetProjectComboBox.SelectedItem as DesktopProject);
        }
        catch (Exception error)
        {
            HeaderStatusText.Text = $"Asset review error: {error.Message}";
        }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _data.LoadSettings();
            ProjectsFolderTextBox.Text = settings.ProjectsFolder;
            OpenAiKeyPasswordBox.Password = settings.OpenAiKey;
            OpenAiModelTextBox.Text = settings.OpenAiModel;
            PexelsKeyPasswordBox.Password = settings.PexelsKey;
            PixabayKeyPasswordBox.Password = settings.PixabayKey;
            ResolvePathTextBox.Text = settings.ResolvePath;
            TimelineWidthTextBox.Text = settings.TimelineWidth.ToString();
            TimelineHeightTextBox.Text = settings.TimelineHeight.ToString();
            FrameRateTextBox.Text = settings.FrameRate.ToString("0.###");
            CheckUpdatesCheckBox.IsChecked = settings.CheckUpdates;
        }
        catch (Exception error)
        {
            SettingsStatusText.Text = error.Message;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(TimelineWidthTextBox.Text, out var width) || width <= 0)
                throw new ArgumentException("Timeline width must be a positive whole number.");
            if (!int.TryParse(TimelineHeightTextBox.Text, out var height) || height <= 0)
                throw new ArgumentException("Timeline height must be a positive whole number.");
            if (!double.TryParse(FrameRateTextBox.Text, out var frameRate) || frameRate <= 0)
                throw new ArgumentException("Frame rate must be a positive number.");

            _data.SaveSettings(new AppSettingsModel
            {
                ProjectsFolder = ProjectsFolderTextBox.Text.Trim(),
                OpenAiKey = OpenAiKeyPasswordBox.Password.Trim(),
                OpenAiModel = OpenAiModelTextBox.Text.Trim(),
                PexelsKey = PexelsKeyPasswordBox.Password.Trim(),
                PixabayKey = PixabayKeyPasswordBox.Password.Trim(),
                ResolvePath = ResolvePathTextBox.Text.Trim(),
                TimelineWidth = width,
                TimelineHeight = height,
                FrameRate = frameRate,
                CheckUpdates = CheckUpdatesCheckBox.IsChecked == true,
            });
            SettingsStatusText.Text = "Settings saved.";
            RefreshAll();
        }
        catch (Exception error)
        {
            SettingsStatusText.Text = error.Message;
        }
    }
}
