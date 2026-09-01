namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _projectsWorkflowInitialized;

    private void ApplyProjectsFilter()
    {
        if (ProjectsGrid is null)
            return;
        ProjectsGrid.ItemsSource = _projects;
    }

    private void ApplyProjectProductionMetadata(DesktopProject project)
    {
        // The retired generic project-production editor no longer owns these fields.
        // Keep existing values intact until the hidden legacy project tab is removed.
    }

    private string CurrentProjectOnScreenText() => _editingProject?.OnScreenText ?? "";

    private string CurrentProjectVisualPlan() => _editingProject?.VisualPlan ?? "";
}
