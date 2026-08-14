using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _projectWorkspaceActionsInitialized;
    private TextBlock? _projectWorkspaceReadiness;

    private void EnsureProjectWorkspaceActions()
    {
        if (_projectWorkspaceActionsInitialized || !_safeProjectEditorInitialized || _safeEditorTitle is null)
            return;

        if (_safeEditorTitle.Parent is not StackPanel identity ||
            identity.Parent is not Grid header ||
            header.Parent is not StackPanel page)
            return;

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var summary = new StackPanel();
        summary.Children.Add(new TextBlock
        {
            Text = "Production readiness",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        _projectWorkspaceReadiness = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        summary.Children.Add(_projectWorkspaceReadiness);
        layout.Children.Add(summary);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var media = WorkspaceActionButton("Media Library");
        media.Click += (_, _) => OpenCurrentProjectMedia();
        actions.Children.Add(media);

        var assets = WorkspaceActionButton("Asset Review");
        assets.Click += (_, _) => OpenCurrentProjectAssetReview();
        actions.Children.Add(assets);

        Grid.SetColumn(actions, 1);
        layout.Children.Add(actions);
        card.Child = layout;

        page.Children.Insert(Math.Min(2, page.Children.Count), card);
        _projectWorkspaceActionsInitialized = true;
        UpdateProjectWorkspaceActions();
    }

    private void UpdateProjectWorkspaceActions()
    {
        if (!_projectWorkspaceActionsInitialized || _projectWorkspaceReadiness is null)
            return;

        var project = CurrentSafeEditorProject();
        if (project is null)
        {
            _projectWorkspaceReadiness.Text = "Select a project to see readiness.";
            return;
        }

        var contentReady = 0;
        if (!string.IsNullOrWhiteSpace(project.Script)) contentReady++;
        if (!string.IsNullOrWhiteSpace(project.OnScreenText)) contentReady++;
        if (!string.IsNullOrWhiteSpace(project.VisualPlan)) contentReady++;

        var hasAssets = false;
        var hasVoice = false;
        var hasResolveExport = false;
        try
        {
            var folder = _data.ResolveProjectFolder(project);
            var assets = Path.Combine(folder, "Assets");
            var voice = Path.Combine(folder, "Voice");
            var export = Path.Combine(folder, "Export");

            hasAssets = Directory.Exists(assets) && Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories).Any();
            hasVoice = Directory.Exists(voice) && Directory.EnumerateFiles(voice, "*", SearchOption.AllDirectories).Any();
            hasResolveExport = Directory.Exists(export) && Directory.EnumerateFiles(export, "*", SearchOption.AllDirectories)
                .Any(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Equals(".fcpxml", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".drp", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            // Readiness remains useful even when the project folder is temporarily unavailable.
        }

        _projectWorkspaceReadiness.Text =
            $"Content {contentReady}/3   •   Assets {(hasAssets ? "✓" : "—")}   •   Voice {(hasVoice ? "✓" : "—")}   •   Resolve export {(hasResolveExport ? "✓" : "—")}";
    }

    private void OpenCurrentProjectMedia()
    {
        var project = CurrentSafeEditorProject();
        if (project is null) return;

        InitializeMediaLibraryWorkflow();
        MainTabs.SelectedIndex = 3;
        ApplyNavigationSelection(3);
        MediaProjectComboBox.SelectedItem = _projects.FirstOrDefault(item => item.Id == project.Id);
    }

    private void OpenCurrentProjectAssetReview()
    {
        var project = CurrentSafeEditorProject();
        if (project is null) return;

        InitializeAssetReviewWorkflow();
        MainTabs.SelectedIndex = 4;
        ApplyNavigationSelection(4);
        AssetProjectComboBox.SelectedItem = _projects.FirstOrDefault(item => item.Id == project.Id);
    }

    private static Button WorkspaceActionButton(string text) => new()
    {
        Content = text,
        Height = 32,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(7, 0, 0, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
        Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
    };
}
