namespace FactVaultManager.Desktop;

public sealed class ProductionProjectCatalog
{
    private readonly DesktopDataService _data;

    public ProductionProjectCatalog(DesktopDataService data)
    {
        _data = data;
    }

    public IReadOnlyList<HybridProject> GetProjects()
    {
        var projects = new List<HybridProject>();
        foreach (var project in _data.GetProjects())
        {
            if (project.Status is not ("In Progress" or "Completed"))
            {
                continue;
            }

            string folder;
            try
            {
                folder = _data.ResolveProjectFolder(project);
            }
            catch
            {
                folder = project.Folder;
            }

            var folderExists = Directory.Exists(folder);
            projects.Add(new HybridProject(
                project.Id,
                project.Title,
                project.Status,
                project.Category,
                folder,
                folderExists,
                folderExists && File.Exists(Path.Combine(folder, "production_checkpoint.json")),
                folderExists && File.Exists(Path.Combine(folder, "timeline.json"))));
        }

        return projects;
    }
}
