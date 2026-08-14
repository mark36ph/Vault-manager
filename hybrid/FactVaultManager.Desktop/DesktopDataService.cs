using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    private readonly string _runtimeRoot;
    private readonly string _dataRoot;
    private readonly string _databasePath;
    private readonly string _settingsPath;

    public DesktopDataService()
    {
        _runtimeRoot = LocateRuntimeRoot();
        _dataRoot = File.Exists(Path.Combine(AppContext.BaseDirectory, "FactVaultWorker.exe"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FactVaultManager")
            : _runtimeRoot;
        _databasePath = Path.Combine(_dataRoot, "data", "factvault.db");
        _settingsPath = Path.Combine(_dataRoot, "data", "settings.json");
    }

    public string RuntimeRoot => _runtimeRoot;
    public string DatabasePath => _databasePath;
    public string SettingsPath => _settingsPath;

    public IReadOnlyList<DesktopProject> GetProjects()
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, category, status, folder, created,
                   COALESCE(script, ''), COALESCE(description, ''),
                   COALESCE(pinned_comment, ''), COALESCE(notes, ''),
                   COALESCE(tags, ''), COALESCE(sources, ''), COALESCE(pinned, 0)
            FROM projects
            ORDER BY pinned DESC, id DESC
            """;

        using var reader = command.ExecuteReader();
        var results = new List<DesktopProject>();
        while (reader.Read())
        {
            results.Add(new DesktopProject(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
                reader.GetInt32(12) != 0));
        }
        return results;
    }

    public DashboardSummary GetDashboardSummary()
    {
        var projects = GetProjects();
        return new DashboardSummary(
            projects.Count,
            projects.Count(project => project.Status == "In Progress"),
            projects.Count(project => project.Status == "Completed"),
            projects.Count(project => project.Status == "Scheduled"),
            projects.Count(project => project.Status == "Published"));
    }

    public DesktopProject CreateProject(string title, string category, string status)
    {
        title = title.Trim();
        category = category.Trim();
        status = status.Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Project title is required.");
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.");
        if (string.IsNullOrWhiteSpace(status)) status = "In Progress";

        var root = GetProjectsRoot();
        var folder = Path.Combine(root, status, title);
        if (Directory.Exists(folder)) throw new IOException($"Project folder already exists: {folder}");

        var createdFolders = new[]
        {
            folder,
            Path.Combine(folder, "Assets", "Images"), Path.Combine(folder, "Assets", "Videos"),
            Path.Combine(folder, "Assets", "Music"), Path.Combine(folder, "Assets", "SFX"),
            Path.Combine(folder, "Assets", "Overlays"), Path.Combine(folder, "Assets", "Thumbnails"),
            Path.Combine(folder, "Export"), Path.Combine(folder, "Voice")
        };

        foreach (var path in createdFolders) Directory.CreateDirectory(path);
        try
        {
            EnsureDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            command.CommandText = """
                INSERT INTO projects(title, category, status, folder, created, script, description, pinned_comment, notes, updated)
                VALUES($title, $category, $status, $folder, $created, '', '', '', '', $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$category", category);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$folder", Path.GetRelativePath(root, folder));
            command.Parameters.AddWithValue("$created", created);
            var id = Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L));
            return GetProjects().First(project => project.Id == id);
        }
        catch
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            throw;
        }
    }

    public void SaveProject(DesktopProject project)
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE projects
            SET category=$category, script=$script, description=$description,
                pinned_comment=$pinnedComment, notes=$notes, tags=$tags, sources=$sources,
                pinned=$pinned, updated=$updated
            WHERE id=$id
            """;
        command.Parameters.AddWithValue("$category", project.Category);
        command.Parameters.AddWithValue("$script", project.Script);
        command.Parameters.AddWithValue("$description", project.Description);
        command.Parameters.AddWithValue("$pinnedComment", project.PinnedComment);
        command.Parameters.AddWithValue("$notes", project.Notes);
        command.Parameters.AddWithValue("$tags", project.Tags);
        command.Parameters.AddWithValue("$sources", project.Sources);
        command.Parameters.AddWithValue("$pinned", project.Pinned ? 1 : 0);
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", project.Id);
        command.ExecuteNonQuery();
    }

    public DesktopProject ChangeStatus(DesktopProject project, string newStatus)
    {
        newStatus = newStatus.Trim();
        if (newStatus == project.Status) return project;
        var root = GetProjectsRoot();
        var oldFolder = ResolveProjectFolder(project);
        var newFolder = Path.Combine(root, newStatus, project.Title);
        if (!Directory.Exists(oldFolder)) throw new DirectoryNotFoundException(oldFolder);
        if (Directory.Exists(newFolder)) throw new IOException($"Destination already exists: {newFolder}");

        Directory.CreateDirectory(Path.GetDirectoryName(newFolder)!);
        Directory.Move(oldFolder, newFolder);
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE projects SET status=$status, folder=$folder, updated=$updated WHERE id=$id";
            command.Parameters.AddWithValue("$status", newStatus);
            command.Parameters.AddWithValue("$folder", Path.GetRelativePath(root, newFolder));
            command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", project.Id);
            command.ExecuteNonQuery();
        }
        catch
        {
            if (Directory.Exists(newFolder) && !Directory.Exists(oldFolder)) Directory.Move(newFolder, oldFolder);
            throw;
        }
        return GetProjects().First(item => item.Id == project.Id);
    }

    public void DeleteProject(DesktopProject project, bool deleteFolder)
    {
        var folder = ResolveProjectFolder(project);
        string? staged = null;
        if (deleteFolder && Directory.Exists(folder))
        {
            staged = folder + ".delete-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(folder, staged);
        }
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM projects WHERE id=$id";
            command.Parameters.AddWithValue("$id", project.Id);
            command.ExecuteNonQuery();
        }
        catch
        {
            if (staged is not null && Directory.Exists(staged) && !Directory.Exists(folder)) Directory.Move(staged, folder);
            throw;
        }
        if (staged is not null && Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
    }

    public string ResolveProjectFolder(DesktopProject project)
    {
        if (Path.IsPathRooted(project.Folder)) return project.Folder;
        return Path.Combine(GetProjectsRoot(), project.Folder);
    }

    public IReadOnlyList<MediaItem> GetMedia(DesktopProject? project)
    {
        if (project is null) return Array.Empty<MediaItem>();
        var folder = ResolveProjectFolder(project);
        var roots = new[]
        {
            ("Image", Path.Combine(folder, "Assets", "Images")),
            ("Video", Path.Combine(folder, "Assets", "Videos")),
            ("Thumbnail", Path.Combine(folder, "Assets", "Thumbnails")),
            ("Export", Path.Combine(folder, "Export"))
        };
        var items = new List<MediaItem>();
        foreach (var (kind, root) in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                items.Add(new MediaItem(kind, info.Name, path, info.Length, info.LastWriteTime));
            }
        }
        return items.OrderByDescending(item => item.Modified).ToList();
    }

    public IReadOnlyList<AssetReviewItem> GetAssetReview(DesktopProject? project)
    {
        if (project is null) return Array.Empty<AssetReviewItem>();
        var folder = ResolveProjectFolder(project);
        var timelinePath = Path.Combine(folder, "timeline.json");
        var media = GetMedia(project);
        if (!File.Exists(timelinePath))
        {
            return media.Select(item => new AssetReviewItem(item.Kind, item.Name, item.Path, "Available media", "")).ToList();
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(timelinePath));
            var found = new List<AssetReviewItem>();
            CollectAssetPaths(root, found);
            if (found.Count > 0) return found;
        }
        catch (JsonException)
        {
        }
        return media.Select(item => new AssetReviewItem(item.Kind, item.Name, item.Path, "Timeline present", "Scene mapping unavailable")).ToList();
    }

    public AppSettingsModel LoadSettings()
    {
        if (!File.Exists(_settingsPath)) return new AppSettingsModel();
        var node = JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject();
        return new AppSettingsModel
        {
            ProjectsFolder = node["general"]?["projects_folder"]?.GetValue<string>() ?? "",
            Theme = node["general"]?["theme"]?.GetValue<string>() ?? "dark",
            OpenAiKey = node["ai"]?["api_key"]?.GetValue<string>() ?? "",
            OpenAiModel = node["ai"]?["model"]?.GetValue<string>() ?? "",
            PexelsKey = node["images"]?["pexels_api_key"]?.GetValue<string>() ?? "",
            PixabayKey = node["images"]?["pixabay_api_key"]?.GetValue<string>() ?? "",
            ResolvePath = node["resolve"]?["application_path"]?.GetValue<string>() ?? "",
            TimelineWidth = node["resolve"]?["timeline_width"]?.GetValue<int>() ?? 1080,
            TimelineHeight = node["resolve"]?["timeline_height"]?.GetValue<int>() ?? 1920,
            FrameRate = node["resolve"]?["frame_rate"]?.GetValue<double>() ?? 30,
            CheckUpdates = node["general"]?["check_updates"]?.GetValue<bool>() ?? true,
        };
    }

    public void SaveSettings(AppSettingsModel settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var node = File.Exists(_settingsPath)
            ? JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var general = node["general"] as JsonObject ?? new JsonObject();
        var ai = node["ai"] as JsonObject ?? new JsonObject();
        var images = node["images"] as JsonObject ?? new JsonObject();
        var resolve = node["resolve"] as JsonObject ?? new JsonObject();
        node["general"] = general; node["ai"] = ai; node["images"] = images; node["resolve"] = resolve;
        general["projects_folder"] = settings.ProjectsFolder;
        general["theme"] = settings.Theme;
        general["check_updates"] = settings.CheckUpdates;
        ai["api_key"] = settings.OpenAiKey;
        ai["model"] = settings.OpenAiModel;
        images["pexels_api_key"] = settings.PexelsKey;
        images["pixabay_api_key"] = settings.PixabayKey;
        resolve["application_path"] = settings.ResolvePath;
        resolve["timeline_width"] = settings.TimelineWidth;
        resolve["timeline_height"] = settings.TimelineHeight;
        resolve["frame_rate"] = settings.FrameRate;
        File.WriteAllText(_settingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private string GetProjectsRoot()
    {
        var root = LoadSettings().ProjectsFolder.Trim();
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Set the Projects Folder in Settings first.");
        return Path.GetFullPath(root);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private void EnsureDatabase()
    {
        if (!File.Exists(_databasePath)) throw new FileNotFoundException("FactVault database was not found.", _databasePath);
    }

    private static string LocateRuntimeRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "database.py")) || File.Exists(Path.Combine(directory.FullName, "FactVaultWorker.exe")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return AppContext.BaseDirectory;
    }

    private static void CollectAssetPaths(JsonNode? node, List<AssetReviewItem> items)
    {
        if (node is JsonObject obj)
        {
            string scene = obj["scene"]?.ToString() ?? obj["text"]?.ToString() ?? obj["caption"]?.ToString() ?? "";
            foreach (var pair in obj)
            {
                if (pair.Value is JsonValue value && pair.Key.Contains("path", StringComparison.OrdinalIgnoreCase))
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text) && (text.Contains('\\') || text.Contains('/')))
                    {
                        items.Add(new AssetReviewItem(Path.GetExtension(text).TrimStart('.').ToUpperInvariant(), Path.GetFileName(text), text, scene, pair.Key));
                    }
                }
                CollectAssetPaths(pair.Value, items);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) CollectAssetPaths(child, items);
        }
    }
}

public sealed record DesktopProject(int Id, string Title, string Category, string Status, string Folder, string Created,
    string Script, string Description, string PinnedComment, string Notes, string Tags, string Sources, bool Pinned)
{
    public string DisplayName => $"{Title}  •  {Status}";
}

public sealed record DashboardSummary(int Total, int InProgress, int Completed, int Scheduled, int Published);
public sealed record MediaItem(string Kind, string Name, string Path, long Bytes, DateTime Modified)
{
    public string Size => Bytes < 1024 * 1024 ? $"{Bytes / 1024.0:0.0} KB" : $"{Bytes / 1024.0 / 1024.0:0.0} MB";
}
public sealed record AssetReviewItem(string Kind, string Name, string Path, string Scene, string Detail);

public sealed class AppSettingsModel
{
    public string ProjectsFolder { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public string OpenAiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = "";
    public string PexelsKey { get; set; } = "";
    public string PixabayKey { get; set; } = "";
    public string ResolvePath { get; set; } = "";
    public int TimelineWidth { get; set; } = 1080;
    public int TimelineHeight { get; set; } = 1920;
    public double FrameRate { get; set; } = 30;
    public bool CheckUpdates { get; set; } = true;
}
