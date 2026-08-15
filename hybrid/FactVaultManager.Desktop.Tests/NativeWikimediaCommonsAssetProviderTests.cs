using System.Net;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeWikimediaCommonsAssetProviderTests
{
    [Fact]
    public async Task Search_ReturnsPublicDomainEvidenceAndRejectsAttributionLicense()
    {
        const string response = """
        {
          "query": {
            "pages": [
              {
                "pageid": 123,
                "ns": 6,
                "title": "File:Wombat Faeces.jpg",
                "index": 1,
                "imageinfo": [
                  {
                    "url": "https://upload.wikimedia.org/wikipedia/commons/a/aa/Wombat_Faeces.jpg",
                    "descriptionurl": "https://commons.wikimedia.org/wiki/File:Wombat_Faeces.jpg",
                    "width": 1495,
                    "height": 1211,
                    "mime": "image/jpeg",
                    "extmetadata": {
                      "LicenseShortName": { "value": "CC0 1.0" },
                      "UsageTerms": { "value": "Creative Commons CC0 1.0" },
                      "Artist": { "value": "<b>Example Creator</b>" },
                      "ImageDescription": { "value": "<p>Characteristic wombat scat photographed on the ground.</p>" },
                      "Categories": { "value": "Wombat feces" }
                    }
                  }
                ]
              },
              {
                "pageid": 456,
                "ns": 6,
                "title": "File:Other wombat.jpg",
                "index": 2,
                "imageinfo": [
                  {
                    "url": "https://upload.wikimedia.org/wikipedia/commons/b/bb/Other_Wombat.jpg",
                    "descriptionurl": "https://commons.wikimedia.org/wiki/File:Other_wombat.jpg",
                    "width": 1200,
                    "height": 800,
                    "mime": "image/jpeg",
                    "extmetadata": {
                      "LicenseShortName": { "value": "CC BY 4.0" },
                      "UsageTerms": { "value": "Creative Commons Attribution 4.0" },
                      "Artist": { "value": "Other Creator" },
                      "ImageDescription": { "value": "Wombat in grass" }
                    }
                  }
                ]
              }
            ]
          }
        }
        """;

        var handler = new StubHandler(response);
        using var client = new HttpClient(handler);
        using var provider = new NativeWikimediaCommonsAssetProvider(client);

        var results = await provider.SearchAsync(
            "wombat droppings ground wildlife Australia",
            "image",
            20);

        var result = Assert.Single(results);
        Assert.Equal("wikimedia", result.Provider);
        Assert.Equal("123", result.Id);
        Assert.Equal("CC0 1.0", result.License);
        Assert.Equal("Example Creator", result.Credit);
        Assert.Contains("Wombat Faeces", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("droppings", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scat", result.Title, StringComparison.OrdinalIgnoreCase);

        Assert.NotEmpty(handler.RequestUris);
        var firstQuery = Uri.UnescapeDataString(handler.RequestUris[0].Query);
        Assert.Contains("generator=search", firstQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gsrnamespace=6", firstQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gsrsearch=wombat faeces", firstQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("iiprop=url|size|mime|extmetadata", firstQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DroppingsSearch_UsesArchiveVocabularyInsteadOfStockQualifiers()
    {
        var queries = NativeWikimediaCommonsAssetProvider.BuildSearchQueries(
            "wombat droppings rock ground Australia wildlife");

        Assert.Equal("wombat faeces", queries[0]);
        Assert.Contains("wombat feces", queries);
        Assert.Contains("wombat scat", queries);
        Assert.DoesNotContain(queries, query => query.Contains("ground", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, query => query.Contains("wildlife", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, query => query.Contains("Australia", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviousThreeProviderDefault_GainsWikimediaAutomatically()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, NativeProviderSettingsStore.FileName),
                """
                {
                  "asset_providers": ["pexels", "pixabay", "openverse"]
                }
                """);

            var settings = new NativeProviderSettingsStore(folder).Load();

            Assert.Equal(
                new[] { "pexels", "pixabay", "openverse", "wikimedia" },
                settings.AssetProviders);
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

        public List<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
                RequestUris.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            });
        }
    }
}
