using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeOpenverseAssetProvider : NativeAssetProviderBase, INativeAssetProvider, IDisposable
{
    private const string FecesSearchGroup = "(droppings | feces | faeces | poop | scat)";

    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public string Name => "openverse";

    public NativeOpenverseAssetProvider(HttpClient? client = null)
    {
        _client = client ?? CreateClient(TimeSpan.FromSeconds(45));
        _ownsClient = client is null;
    }

    public async Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(
        string query,
        string kind,
        int limit,
        CancellationToken cancellationToken = default)
    {
        query = Required(query, "query");
        if (!kind.Equals("image", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<NativeAssetCandidate>();

        var searchQuery = ExpandSearchVocabulary(query);
        if (searchQuery.Length > 200)
            searchQuery = query[..Math.Min(query.Length, 200)];

        var pageSize = Math.Clamp(limit, 1, 20);
        var queryString = QueryString(new Dictionary<string, string>
        {
            ["q"] = searchQuery,
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["license"] = "cc0,pdm",
            ["mature"] = "false",
            ["extension"] = "jpg,jpeg,png,webp",
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.openverse.org/v1/images/?{queryString}");
        using var document = await GetJsonAsync(_client, request, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<NativeAssetCandidate>();

        var results = new List<NativeAssetCandidate>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (item.TryGetProperty("mature", out var mature) && mature.ValueKind == JsonValueKind.True)
                continue;

            var license = ReadString(item, "license").Trim().ToLowerInvariant();
            if (license is not ("cc0" or "pdm"))
                continue;

            var mediaUrl = ReadString(item, "url");
            if (string.IsNullOrWhiteSpace(mediaUrl))
                continue;

            var title = ReadString(item, "title");
            var tags = ReadTags(item);
            var candidateText = string.Join(
                ", ",
                new[] { title, tags }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var sourcePage = ReadString(item, "foreign_landing_url");
            if (string.IsNullOrWhiteSpace(sourcePage))
                sourcePage = ReadString(item, "detail_url");

            if (!CandidateIsRelevant(query, $"{candidateText} {sourcePage}"))
                continue;

            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) id = mediaUrl;
            if (string.IsNullOrWhiteSpace(candidateText)) candidateText = sourcePage;

            var version = ReadString(item, "license_version").Trim();
            var licenseName = license == "cc0"
                ? string.Join(" ", new[] { "CC0", version }.Where(value => value.Length > 0))
                : string.Join(" ", new[] { "Public Domain Mark", version }.Where(value => value.Length > 0));
            var credit = ReadString(item, "attribution");
            if (string.IsNullOrWhiteSpace(credit))
                credit = ReadString(item, "creator");

            results.Add(new NativeAssetCandidate(
                Name,
                id,
                mediaUrl,
                "image",
                candidateText,
                ReadInt(item, "width"),
                ReadInt(item, "height"),
                0,
                Math.Max(0, pageSize - results.Count),
                credit,
                licenseName,
                sourcePage));
        }

        return results;
    }

    private static string ExpandSearchVocabulary(string query) =>
        Regex.Replace(
            query,
            @"\b(?:droppings|feces|faeces|poop|scat)\b",
            FecesSearchGroup,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ReadTags(JsonElement item)
    {
        if (!item.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return "";

        return string.Join(
            ", ",
            tags.EnumerateArray()
                .Where(tag => tag.ValueKind == JsonValueKind.Object)
                .Select(tag => ReadString(tag, "name").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
