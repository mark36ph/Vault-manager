namespace FactVaultManager.Desktop.Tests;

public sealed class ProductionLogFileStoreTests
{
    [Fact]
    public void StartAppendFinish_WritesRecoverableProjectLog()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new ProductionLogFileStore(root);
            var path = store.Start(42, "Octopuses: Have / Three Hearts");
            store.Append(path, "19:40:00  Finding visuals" + Environment.NewLine);
            store.Finish(path);

            Assert.True(File.Exists(path));
            Assert.StartsWith(Path.Combine(root, "logs", "production"), path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(':', Path.GetFileName(path));
            Assert.DoesNotContain('/', Path.GetFileName(path));

            var text = File.ReadAllText(path);
            Assert.Contains("Production log started", text, StringComparison.Ordinal);
            Assert.Contains("Finding visuals", text, StringComparison.Ordinal);
            Assert.Contains("Production log closed", text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
