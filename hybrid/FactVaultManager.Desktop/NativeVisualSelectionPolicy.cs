using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public static class NativeVisualSelectionPolicy
{
    private static readonly HashSet<string> ScientificIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anatomy", "anatomical", "gill", "gills", "heart", "hearts", "organ", "organs",
        "blood", "hemocyanin", "circulatory", "circulation", "internal", "biological", "biology",
    };

    private static readonly HashSet<string> PreparedFoodWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fried", "cooked", "grilled", "roasted", "meal", "dish", "restaurant", "food",
        "seafood", "platter", "market", "stall",
    };

    private static readonly string[] SyntheticPhrases =
    {
        "3d print", "3d printed", "3d printing", "printed model", "plastic model", "thingiverse",
    };

    public static string SourceFamilyKey(NativeAssetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var source = (candidate.SourcePage ?? "").Trim();
        if (source.Length == 0)
            return "";

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
            return $"family:{candidate.Provider}:{uri.Host.ToLowerInvariant()}{path.ToLowerInvariant()}";
        }

        return $"family:{candidate.Provider}:{Regex.Replace(source.ToLowerInvariant(), @"\s+", " ")}";
    }

    public static string SceneContradiction(string query, NativeAssetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var queryWords = Words(query);
        if (!ScientificIntentWords.Any(queryWords.Contains))
            return "";

        var candidateWords = Words(CandidateText(candidate));
        var conflict = PreparedFoodWords.FirstOrDefault(candidateWords.Contains);
        return conflict is null ? "" : $"prepared-food imagery conflicts with scientific scene intent ('{conflict}')";
    }

    public static string UnrequestedSyntheticRepresentation(string query, NativeAssetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var normalizedQuery = Normalize(query);
        var normalizedCandidate = Normalize(CandidateText(candidate));

        foreach (var phrase in SyntheticPhrases)
        {
            var normalizedPhrase = Normalize(phrase);
            if (normalizedCandidate.Contains(normalizedPhrase, StringComparison.Ordinal) &&
                !normalizedQuery.Contains(normalizedPhrase, StringComparison.Ordinal))
                return normalizedPhrase;
        }
        return "";
    }

    private static string CandidateText(NativeAssetCandidate candidate) =>
        string.Join(" ", new[] { candidate.Title, candidate.Credit, candidate.SourcePage }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static HashSet<string> Words(string value) =>
        Regex.Matches(value ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}
