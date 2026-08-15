using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeWikimediaCommonsAssetProvider : NativeAssetProviderBase, INativeAssetProvider, IDisposable
{
    private static readonly HashSet<string> ArchiveStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "close", "up", "wildlife", "photo", "photography", "image", "vertical", "portrait",
        "realistic", "documentary", "dramatic", "nature", "animal",
    };

    private static readonly HashSet<string> FecesWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "droppings", "feces", "faeces", "poop", "scat",
    };

    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public string Name => "wikimedia";

    public NativeWikimediaCommonsAssetProvider(HttpClient? client = null)
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

        var results = new List<NativeAssetCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perSearchLimit = Math.Clamp(limit, 1, 10);

        foreach (var searchQuery in BuildSearchQueries(query))
        {
            var queryString = QueryString(new Dictionary<string, string>
            {
                ["action"] = "query",
                ["format"] = "json",
                ["formatversion"] = "2",
                ["generator"] = "search",
                ["gsrsearch"] = searchQuery,
                ["gsrnamespace"] = "6",
                ["gsrlimit"] = perSearchLimit.ToString(CultureInfo.InvariantCulture),
                ["prop"] = "imageinfo",
                ["iiprop"] = "url|size|mime|extmetadata",
                ["iiextmetadatalanguage"] = "en",
                ["iiextmetadatafilter"] = "LicenseShortName|UsageTerms|Artist|ImageDescription|Categories",
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://commons.wikimedia.org/w/api.php?{queryString}");
            using var document = await GetJsonAsync(_client, request, cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("query", out var queryObject) || queryObject.ValueKind != JsonValueKind.Object ||
                !queryObject.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var page in pages.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object ||
                    !page.TryGetProperty("imageinfo", out var infos) || infos.ValueKind != JsonValueKind.Array ||
                    infos.GetArrayLength() == 0)
                    continue;

                var info = infos[0];
                if (info.ValueKind != JsonValueKind.Object)
                    continue;

                var mime = ReadString(info, "mime").Trim().ToLowerInvariant();
                if (mime is not ("image/jpeg" or "image/png" or "image/webp"))
                    continue;

                var licenseShortName = ReadMetadata(info, "LicenseShortName");
                var usageTerms = ReadMetadata(info, "UsageTerms");
                if (!IsPublicDomainLicense(licenseShortName, usageTerms))
                    continue;

                var mediaUrl = ReadString(info, "url");
                if (string.IsNullOrWhiteSpace(mediaUrl) || !seen.Add(mediaUrl))
                    continue;

                var pageTitle = ReadString(page, "title");
                var title = pageTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
                    ? pageTitle[5..]
                    : pageTitle;
                var description = StripHtml(ReadMetadata(info, "ImageDescription"));
                var categories = StripHtml(ReadMetadata(info, "Categories"));
                var candidateText = string.Join(
                    ", ",
                    new[] { title, description, categories }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                candidateText = AddEvidenceAliases(candidateText);

                var sourcePage = ReadString(info, "descriptionurl");
                if (!CandidateIsRelevant(query, $"{candidateText} {sourcePage}"))
                    continue;

                var id = ReadString(page, "pageid");
                if (string.IsNullOrWhiteSpace(id)) id = pageTitle;
                var credit = StripHtml(ReadMetadata(info, "Artist"));
                var license = !string.IsNullOrWhiteSpace(licenseShortName)
                    ? StripHtml(licenseShortName)
                    : StripHtml(usageTerms);

                results.Add(new NativeAssetCandidate(
                    Name,
                    id,
                    mediaUrl,
                    "image",
                    candidateText,
                    ReadInt(info, "width"),
                    ReadInt(info, "height"),
                    0,
                    Math.Max(1, perSearchLimit - results.Count),
                    credit,
                    license,
                    sourcePage));

                if (results.Count >= limit)
                    return results;
            }
        }

        return results;
    }

    internal static IReadOnlyList<string> BuildSearchQueries(string query)
    {
        var words = Regex.Matches(query ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        if (words.Count == 0)
            return Array.Empty<string>();

        var evidenceIndex = words.FindIndex(word => FecesWords.Contains(word));
        if (evidenceIndex > 0)
        {
            var subject = words[evidenceIndex - 1];
            return new[]
            {
                $"{subject} faeces",
                $"{subject} feces",
                $"{subject} scat",
                $"{subject} droppings",
                $"{subject} poop",
            };
        }

        var meaningful = words
            .Where(word => word.Length >= 3 && !ArchiveStopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (meaningful.Count == 0)
            meaningful = words;

        var queries = new List<string>();
        var concise = string.Join(" ", meaningful.Take(3));
        if (concise.Length > 0)
            queries.Add(concise);
        if (meaningful.Count > 1)
            queries.Add(meaningful[0]);
        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ReadMetadata(JsonElement info, string name)
    {
        if (!info.TryGetProperty("extmetadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Object)
            return "";
        return ReadString(property, "value");
    }

    private static bool IsPublicDomainLicense(string licenseShortName, string usageTerms)
    {
        var text = $"{licenseShortName} {usageTerms}".Trim().ToLowerInvariant();
        return text.Contains("cc0", StringComparison.Ordinal) ||
               text.Contains("public domain", StringComparison.Ordinal) ||
               text.Equals("pdm", StringComparison.Ordinal);
    }

    private static string AddEvidenceAliases(string candidateText)
    {
        if (!FecesWords.Any(word => Regex.IsMatch(
                candidateText ?? "",
                $@"\b{Regex.Escape(word)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            return candidateText;

        return string.Join(", ", new[]
        {
            candidateText,
            "droppings, feces, faeces, poop, scat",
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string StripHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
