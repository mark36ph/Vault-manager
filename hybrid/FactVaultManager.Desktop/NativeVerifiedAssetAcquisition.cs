using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeVerifiedAssetAcquisitionEngine
{
    private static readonly Dictionary<string, int> QualityScore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weak"] = 0,
        ["acceptable"] = 3,
        ["preferred"] = 6,
    };

    private static readonly Dictionary<string, int> StyleScore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["decorative"] = -10,
        ["representational"] = 1,
        ["literal"] = 2,
    };

    private const int SubjectUncertainPenalty = 4;
    private const int MinQualityScan = 5;

    private readonly NativeAssetAcquisitionEngine _engine;
    private readonly INativeAssetVerifier _verifier;

    public Action<string, int, int, string>? Progress { get; set; }
    public string LastSelectedQuality { get; private set; } = "preferred";
    public string LastSelectedStyle { get; private set; } = "literal";
    public bool LastSelectedSubjectUncertain { get; private set; }

    public NativeVerifiedAssetAcquisitionEngine(
        NativeAssetAcquisitionEngine engine,
        INativeAssetVerifier verifier)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async Task<NativeAcquiredAsset> AcquireAsync(
        string query,
        string destinationFolder,
        string kind = "image",
        int limit = 20,
        double? targetRatio = null,
        int attempts = 3,
        ISet<string>? excluded = null,
        CancellationToken cancellationToken = default,
        string requiredSubject = "")
    {
        if (attempts < 1)
            throw new ArgumentException("attempts must be at least 1", nameof(attempts));

        query = (query ?? "").Trim();
        if (query.Length == 0)
            throw new ArgumentException("query is required", nameof(query));
        requiredSubject = (requiredSubject ?? "").Trim();

        if (!kind.Equals("image", StringComparison.OrdinalIgnoreCase))
            return await _engine.AcquireAsync(query, destinationFolder, kind, limit, targetRatio, attempts, excluded, cancellationToken);

        var blocked = excluded is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        var checkedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        NativeAcquiredAsset? decorativeFallback = null;
        NativeAcquiredAsset? uncertainFallback = null;
        var fallbackQueries = new List<string> { query };
        fallbackQueries.AddRange(FallbackSearchQueries(query));
        var verificationQuery = BuildVerificationQuery(query, requiredSubject);

        foreach (var searchQuery in fallbackQueries)
        {
            var scanLimit = Math.Max(attempts, MinQualityScan);
            NativeAcquiredAsset? best = null;
            NativeAssetVerificationResult? bestDecision = null;
            var bestScore = int.MinValue;

            for (var index = 0; index < scanLimit; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NativeAcquiredAsset asset;
                try
                {
                    asset = await _engine.AcquireAsync(
                        searchQuery,
                        destinationFolder,
                        kind,
                        limit,
                        targetRatio,
                        attempts: 1,
                        excluded: blocked,
                        cancellationToken);
                }
                catch (Exception error)
                {
                    failures.Add(error.Message);
                    break;
                }

                var key = CandidateKey(asset.Candidate);
                if (!checkedItems.Add(key) || !checkedItems.Add(asset.Candidate.Url))
                {
                    Discard(asset);
                    break;
                }
                blocked.Add(key);
                blocked.Add(asset.Candidate.Url);

                Report("verify", index + 1, scanLimit, $"Checking visual relevance: {asset.Candidate.Title}");
                NativeAssetVerificationResult decision;
                try
                {
                    decision = await _verifier.VerifyAsync(verificationQuery, asset, cancellationToken);
                }
                catch (Exception error)
                {
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: visual verification failed: {error.Message}");
                    Discard(asset);
                    continue;
                }

                if (!decision.Accepted)
                {
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {decision.Decision}");
                    Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({decision.Decision}); trying another asset");
                    Discard(asset);
                    continue;
                }

                var score = VisualScore(decision);
                if (best is null || score > bestScore)
                {
                    if (best is not null) Discard(best);
                    best = asset;
                    bestDecision = decision;
                    bestScore = score;
                    Report("verify", index + 1, scanLimit,
                        $"Best visual so far: {decision.Quality}/{decision.Style} ({score}{(decision.SubjectUncertain ? ", subject uncertain" : "")})");
                }
                else
                {
                    Discard(asset);
                }
            }

            if (best is null || bestDecision is null)
                continue;

            if (bestDecision.Style.Equals("decorative", StringComparison.OrdinalIgnoreCase))
            {
                if (decorativeFallback is null)
                {
                    decorativeFallback = best;
                    Report("verify", 1, 1, "Decorative visual retained only as last resort; searching for factual imagery");
                }
                else Discard(best);
                continue;
            }

            if (bestDecision.SubjectUncertain)
            {
                if (uncertainFallback is null)
                {
                    uncertainFallback = best;
                    Report("verify", 1, 1, "Subject-uncertain visual retained as fallback; searching for a safer factual match");
                }
                else Discard(best);
                continue;
            }

            if (uncertainFallback is not null) Discard(uncertainFallback);
            if (decorativeFallback is not null) Discard(decorativeFallback);
            Remember(bestDecision);
            return best;
        }

        if (uncertainFallback is not null)
        {
            if (decorativeFallback is not null) Discard(decorativeFallback);
            LastSelectedQuality = "acceptable";
            LastSelectedStyle = "literal";
            LastSelectedSubjectUncertain = true;
            Report("verify", 1, 1, "No certain factual visual found; using subject-uncertain factual fallback");
            return uncertainFallback;
        }

        if (decorativeFallback is not null)
        {
            LastSelectedQuality = "acceptable";
            LastSelectedStyle = "decorative";
            LastSelectedSubjectUncertain = false;
            Report("verify", 1, 1, "No literal or representational visual passed; using decorative fallback as last resort");
            return decorativeFallback;
        }

        throw new NativeAssetAcquisitionException(
            failures.Count > 0
                ? "no visually relevant asset passed verification: " + string.Join("; ", failures)
                : $"no {kind} assets found for: {query}");
    }

    public async Task<IReadOnlyList<NativeAcquiredAsset>> AcquireManyAsync(
        IEnumerable<string> queries,
        string destinationFolder,
        string kind = "image",
        int limit = 20,
        double? targetRatio = null,
        int attempts = 3,
        bool unique = true,
        CancellationToken cancellationToken = default,
        string requiredSubject = "")
    {
        var items = queries.Select(value => (value ?? "").Trim()).Where(value => value.Length > 0).ToArray();
        var results = new List<NativeAcquiredAsset>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < items.Length; index++)
        {
            Report("acquire", index + 1, items.Length, items[index]);
            var result = await AcquireAsync(
                items[index], destinationFolder, kind, limit, targetRatio, attempts,
                unique ? used : null, cancellationToken, requiredSubject);
            results.Add(result);
            if (unique)
            {
                used.Add(CandidateKey(result.Candidate));
                used.Add(result.Candidate.Url);
            }
        }
        return results;
    }

    private static int VisualScore(NativeAssetVerificationResult decision)
    {
        var quality = QualityScore.GetValueOrDefault(decision.Quality, 6);
        var style = StyleScore.GetValueOrDefault(decision.Style, 2);
        return quality + style - (decision.SubjectUncertain ? SubjectUncertainPenalty : 0);
    }

    private void Remember(NativeAssetVerificationResult decision)
    {
        LastSelectedQuality = QualityScore.ContainsKey(decision.Quality) ? decision.Quality : "preferred";
        LastSelectedStyle = StyleScore.ContainsKey(decision.Style) ? decision.Style : "literal";
        LastSelectedSubjectUncertain = decision.SubjectUncertain;
    }

    private static void Discard(NativeAcquiredAsset asset)
    {
        if (asset.Reused) return;
        try { File.Delete(asset.Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string CandidateKey(NativeAssetCandidate candidate) =>
        $"{candidate.Provider}:{(string.IsNullOrWhiteSpace(candidate.Id) ? candidate.Url : candidate.Id)}";

    public static string BuildVerificationQuery(string query, string requiredSubject)
    {
        var cleanQuery = Regex.Replace((query ?? "").Trim(), @"\s+", " ");
        var subjectMatch = Regex.Match(requiredSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*");
        if (!subjectMatch.Success)
            return cleanQuery;

        var subject = subjectMatch.Value.ToLowerInvariant();
        var subjectPattern = new Regex(
            $@"\b{Regex.Escape(subject)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var remainder = subjectPattern.Replace(cleanQuery, "", 1);
        remainder = Regex.Replace(remainder, @"\s+", " ").Trim();
        return string.Join(" ", new[] { "subject", subject, remainder }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IReadOnlyList<string> FallbackSearchQueries(string query)
    {
        var original = query.Trim();
        var words = Regex.Matches(original, "[A-Za-z0-9][A-Za-z0-9'’-]*").Select(match => match.Value).ToList();
        var variants = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { original };

        void Add(string value)
        {
            var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length > 0 && seen.Add(normalized)) variants.Add(normalized);
        }

        Add(string.Join(" ", words));
        foreach (var length in new[] { 12, 9, 6, 4 })
            if (words.Count > length) Add(string.Join(" ", words.Take(length)));
        if (words.Count > 2)
        {
            Add(string.Join(" ", words.Take(3)));
            Add(string.Join(" ", words.Take(2)));
        }
        return variants;
    }

    private void Report(string stage, int current, int total, string message) =>
        Progress?.Invoke(stage, current, total, message);
}
