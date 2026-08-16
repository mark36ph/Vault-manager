using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public static class NativeVisualSelectionPolicy
{
    private static readonly HashSet<string> ScientificIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anatomy", "anatomical", "gill", "gills", "heart", "hearts", "organ", "organs",
        "blood", "hemocyanin", "circulatory", "circulation", "internal", "biological", "biology",
    };

    private static readonly HashSet<string> LivingSceneWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "swimming", "swim", "crawling", "crawl", "moving", "move", "feeding", "feed",
        "eating", "eat", "foraging", "forage", "resting", "rest", "underwater", "seafloor",
        "wildlife", "habitat",
    };

    private static readonly HashSet<string> PreparedFoodWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fried", "cooked", "grilled", "roasted", "meal", "dish", "restaurant", "food",
        "seafood", "platter", "market", "stall",
    };

    private static readonly HashSet<string> DeadDisplayWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dead", "dried", "drying", "carcass", "preserved", "taxidermy",
    };

    private static readonly string[] SyntheticPhrases =
    {
        "3d print", "3d printed", "3d printing", "printed model", "plastic model", "thingiverse",
    };

    private static readonly string[] BlockedSourceHosts =
    {
        "thingiverse.com",
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

    public static string BlockedSourceReason(NativeAssetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        foreach (var value in new[] { candidate.SourcePage, candidate.Url })
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                continue;

            foreach (var blockedHost in BlockedSourceHosts)
            {
                if (uri.Host.Equals(blockedHost, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + blockedHost, StringComparison.OrdinalIgnoreCase))
                    return blockedHost;
            }
        }

        return Words(CandidateText(candidate)).Contains("thingiverse")
            ? "thingiverse.com"
            : "";
    }

    public static string SceneContradiction(string query, NativeAssetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var blockedSource = BlockedSourceReason(candidate);
        if (blockedSource.Length > 0)
            return $"blocked source '{blockedSource}'";

        var queryWords = Words(query);
        if (PreparedFoodWords.Any(queryWords.Contains) || DeadDisplayWords.Any(queryWords.Contains))
            return "";

        var scientificIntent = ScientificIntentWords.Any(queryWords.Contains);
        var livingIntent = LivingSceneWords.Any(queryWords.Contains);
        if (!scientificIntent && !livingIntent)
            return "";

        var candidateWords = Words(CandidateText(candidate));
        var preparedConflict = PreparedFoodWords.FirstOrDefault(candidateWords.Contains);
        if (preparedConflict is not null)
        {
            var intent = scientificIntent ? "scientific scene intent" : "living-subject scene intent";
            return $"prepared-food imagery conflicts with {intent} ('{preparedConflict}')";
        }

        var deadConflict = DeadDisplayWords.FirstOrDefault(candidateWords.Contains);
        if (deadConflict is not null)
        {
            var intent = scientificIntent ? "scientific scene intent" : "living-subject scene intent";
            return $"dead/preserved imagery conflicts with {intent} ('{deadConflict}')";
        }

        return "";
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
        string.Join(" ", new[] { candidate.Title, candidate.Credit, candidate.SourcePage, candidate.Url }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static HashSet<string> Words(string value) =>
        Regex.Matches(value ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}
