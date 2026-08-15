using System.Net;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeOpenverseAssetProviderTests
{
    [Fact]
    public async Task Search_ExpandsFecesVocabularyAndUsesPublicDomainFilter()
    {
        const string response = """
        {
          "results": [
            {
              "id": "cc0-wombat",
              "title": "Wombat Faeces",
              "foreign_landing_url": "https://commons.wikimedia.org/wiki/File:Wombat_Faeces.jpg",
              "url": "https://upload.wikimedia.org/wombat.jpg",
              "creator": "Example Creator",
              "license": "cc0",
              "license_version": "1.0",
              "attribution": "Wombat Faeces by Example Creator is marked with CC0 1.0.",
              "mature": false,
              "width": 1495,
              "height": 1211,
              "tags": [
                { "name": "wombat" },
                { "name": "faeces" }
              ]
            },
            {
              "id": "cc-by-wombat",
              "title": "Wombat Faeces",
              "foreign_landing_url": "https://example.invalid/by",
              "url": "https://example.invalid/by.jpg",
              "creator": "Other Creator",
              "license": "by",
              "license_version": "4.0",
              "mature": false,
              "width": 1000,
              "height": 1000,
              "tags": [{ "name": "wombat" }]
            }
          ]
        }
        """;

        var handler = new StubHandler(response);
        using var client = new HttpClient(handler);
        using var provider = new NativeOpenverseAssetProvider(client);

        var results = await provider.SearchAsync("wombat droppings", "image", 20);

        var result = Assert.Single(results);
        Assert.Equal("openverse", result.Provider);
        Assert.Equal("cc0-wombat", result.Id);
        Assert.Equal("CC0 1.0", result.License);
        Assert.Contains("wombat", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("faeces", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("license=cc0%2Cpdm", handler.LastRequestUri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mature=false", handler.LastRequestUri.Query, StringComparison.OrdinalIgnoreCase);
        var decodedQuery = Uri.UnescapeDataString(handler.LastRequestUri.Query);
        Assert.Contains("(droppings | feces | faeces | poop | scat)", decodedQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyDefaultSettings_GainOpenverseAutomatically()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, NativeProviderSettingsStore.FileName),
                """
                {
                  "asset_providers": ["pexels", "pixabay"]
                }
                """);

            var settings = new NativeProviderSettingsStore(folder).Load();

            Assert.Equal(new[] { "pexels", "pixabay", "openverse" }, settings.AssetProviders);
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;

        public StubHandler(string response) => _response = response;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            });
        }
    }
}
