namespace FactVaultManager.Desktop.Tests;

public sealed class NativeProductionProviderWorkflowTests
{
    [Fact]
    public void CheckReadiness_AllowsFreeProvidersWithoutStockProviderKeys()
    {
        var settings = new AppSettingsModel
        {
            OpenAiKey = "test-openai-key",
        };

        var readiness = NativeProductionProviderWorkflow.CheckReadiness(
            settings,
            usePexels: false,
            usePixabay: false);

        Assert.True(readiness.Ready);
        Assert.Contains(readiness.Lines, line => line.Contains("Openverse", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.Lines, line => line.Contains("Wikimedia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(readiness.Lines, line => line.Contains("Select at least one media provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Save_AlwaysPreservesFreeProviders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var saved = NativeProductionProviderWorkflow.Save(
                folder,
                new AppSettingsModel { OpenAiKey = "test-openai-key" },
                usePexels: false,
                usePixabay: false,
                useVoice: true,
                assetKind: "image");

            Assert.Equal(new[] { "openverse", "wikimedia" }, saved.AssetProviders);

            var loaded = NativeProductionProviderWorkflow.Load(folder);
            Assert.Equal(new[] { "openverse", "wikimedia" }, loaded.AssetProviders);
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
