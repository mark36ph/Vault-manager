using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public IReadOnlyList<string> GetCategories()
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM categories ORDER BY name COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var categories = new List<string>();
        while (reader.Read())
        {
            var value = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (!string.IsNullOrWhiteSpace(value)) categories.Add(value);
        }
        return categories.Count > 0 ? categories : new[] { "Misc" };
    }

    public IReadOnlyList<string> GetTemplates()
    {
        var root = ResolveTemplatesRoot();
        if (!Directory.Exists(root)) return new[] { "Standard Fact" };

        var templates = Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return templates.Count > 0 ? templates : new[] { "Standard Fact" };
    }

    public void ApplyTemplate(DesktopProject project, string template)
    {
        var selected = (template ?? "").Trim();
        if (string.IsNullOrWhiteSpace(selected)) return;
        selected = ProjectPathSecurity.ValidateSegment(selected, "Template name");

        var templatesRoot = ResolveTemplatesRoot();
        var source = ProjectPathSecurity.CombineContained(templatesRoot, selected);
        if (!Directory.Exists(source)) return;

        var destination = ResolveProjectFolder(project);
        Directory.CreateDirectory(destination);
        CopyTemplateDirectory(source, destination);
    }

    public DesktopProject CreateFactProject(NewFactData fact)
    {
        var title = ProjectPathSecurity.ValidateSegment(fact.Title, "Project title");
        var category = fact.Category.Trim();
        var desiredStatus = string.IsNullOrWhiteSpace(fact.Status)
            ? "In Progress"
            : ProjectPathSecurity.ValidateSegment(fact.Status, "Project status");
        if (string.IsNullOrWhiteSpace(category)) category = "Misc";
        if (desiredStatus == "Scheduled" && fact.ScheduledFor is null)
            throw new ArgumentException("Choose a date and time for a scheduled project.");

        var createStatus = desiredStatus == "Scheduled" ? "In Progress" : desiredStatus;
        var root = GetProjectsRoot();
        var folder = ProjectPathSecurity.CombineContained(root, createStatus, title);
        if (Directory.Exists(folder)) throw new IOException($"Project folder already exists: {folder}");

        foreach (var path in new[]
        {
            folder,
            Path.Combine(folder, "Assets", "Images"), Path.Combine(folder, "Assets", "Videos"),
            Path.Combine(folder, "Assets", "Music"), Path.Combine(folder, "Assets", "SFX"),
            Path.Combine(folder, "Assets", "Overlays"), Path.Combine(folder, "Assets", "Thumbnails"),
            Path.Combine(folder, "Export"), Path.Combine(folder, "Voice")
        })
        {
            Directory.CreateDirectory(path);
        }

        int id;
        try
        {
            EnsureDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            command.CommandText = """
                INSERT INTO projects(
                    title, category, status, folder, created,
                    script, on_screen_text, visual_plan, description, pinned_comment,
                    notes, tags, sources, scheduled_for, updated)
                VALUES(
                    $title, $category, $status, $folder, $created,
                    $script, $onScreenText, $visualPlan, $description, $pinnedComment,
                    $notes, $tags, $sources, '', $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$category", category);
            command.Parameters.AddWithValue("$status", createStatus);
            command.Parameters.AddWithValue("$folder", Path.GetRelativePath(root, folder));
            command.Parameters.AddWithValue("$created", created);
            command.Parameters.AddWithValue("$script", fact.Script.Trim());
            command.Parameters.AddWithValue("$onScreenText", fact.OnScreenText.Trim());
            command.Parameters.AddWithValue("$visualPlan", fact.VisualPlan.Trim());
            command.Parameters.AddWithValue("$description", fact.Description.Trim());
            command.Parameters.AddWithValue("$pinnedComment", fact.PinnedComment.Trim());
            command.Parameters.AddWithValue("$notes", fact.Notes.Trim());
            command.Parameters.AddWithValue("$tags", fact.Tags.Trim());
            command.Parameters.AddWithValue("$sources", fact.Sources.Trim());
            id = Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L));
        }
        catch
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            throw;
        }

        var project = GetProjects().First(item => item.Id == id);
        if (desiredStatus != "Scheduled") return project;

        var scheduledFolder = ProjectPathSecurity.CombineContained(root, "Scheduled", title);
        if (Directory.Exists(scheduledFolder))
            throw new IOException($"Scheduled project folder already exists: {scheduledFolder}");

        Directory.CreateDirectory(Path.GetDirectoryName(scheduledFolder)!);
        Directory.Move(folder, scheduledFolder);
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE projects
                SET status='Scheduled', folder=$folder, scheduled_for=$scheduledFor, updated=$updated
                WHERE id=$id
                """;
            command.Parameters.AddWithValue("$folder", Path.GetRelativePath(root, scheduledFolder));
            command.Parameters.AddWithValue("$scheduledFor", fact.ScheduledFor!.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
        catch
        {
            if (Directory.Exists(scheduledFolder) && !Directory.Exists(folder)) Directory.Move(scheduledFolder, folder);
            throw;
        }

        return GetProjects().First(item => item.Id == id);
    }

    private static string ResolveTemplatesRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "templates"),
            Path.Combine(AppContext.BaseDirectory, "templates"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "templates")),
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static void CopyTemplateDirectory(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(ProjectPathSecurity.EnsureContained(destination, Path.Combine(destination, relative)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = ProjectPathSecurity.EnsureContained(destination, Path.Combine(destination, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

public sealed record NewFactData(
    string Title,
    string Category,
    string Status,
    DateTime? ScheduledFor,
    string Script,
    string OnScreenText,
    string VisualPlan,
    string Description,
    string PinnedComment,
    string Tags,
    string Notes,
    string Sources,
    string Template = "Standard Fact");
