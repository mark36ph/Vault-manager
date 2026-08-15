namespace FactVaultManager.Desktop;

public sealed class ProductionLogFileStore
{
    private readonly string _root;

    public ProductionLogFileStore(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        _root = Path.Combine(runtimeRoot, "logs", "production");
    }

    public string Start(int projectId, string title)
    {
        Directory.CreateDirectory(_root);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var safeTitle = SafeFileName(title);
        var path = Path.Combine(_root, $"{stamp}-{projectId}-{safeTitle}.log");
        File.WriteAllText(
            path,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  Production log started{Environment.NewLine}");
        return path;
    }

    public void Append(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.IsNullOrEmpty(text))
            return;
        File.AppendAllText(path, text);
    }

    public void Finish(string path)
    {
        Append(
            path,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  Production log closed{Environment.NewLine}");
    }

    internal static string SafeFileName(string title)
    {
        var source = string.IsNullOrWhiteSpace(title) ? "project" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(source.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "project";
        return safe.Length <= 80 ? safe : safe[..80];
    }
}
