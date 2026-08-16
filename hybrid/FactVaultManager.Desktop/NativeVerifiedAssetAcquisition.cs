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

    private static readonly HashSet<string> GenericSceneLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "close", "up", "wide", "macro", "dramatic", "documentary", "portrait", "vertical", "realistic",
        "walking", "walk", "standing", "stand", "sitting", "sit", "running", "run", "flying", "fly", "body",
    };

    private static readonly HashSet<string> BehaviorSceneEvidenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "swimming", "swim", "crawling", "crawl", "moving", "move", "feeding", "feed",
        "eating", "eat", "foraging", "forage", "resting", "rest", "underwater",
    };

    private static readonly HashSet<string> MultiWordEvidenceCueWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anatomy", "anatomical", "blood", "circulation", "circulatory", "organ", "organs",
        "protein", "pigment", "molecule", "molecular", "chemical", "chemistry", "compound", "cell", "cells",
        "tissue", "tissues", "bone", "bones", "muscle", "muscles", "vein", "veins", "artery", "arteries",
        "internal", "structure", "structural",
    };

    private static readonly HashSet<string> WeakEvidenceLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "blue", "red", "green", "yellow", "orange", "purple", "pink", "black", "white", "brown",
        "gray", "grey", "gold", "golden", "silver", "bright", "dark", "pale", "light", "vivid", "vibrant",
        "large", "small", "tiny", "giant", "huge", "one", "two", "three", "four", "five", "six", "seven",
        "eight", "nine", "ten",
    };

    private static readonly Dictionary<string, string> SubjectTokenAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["octopi"] = "octopus",
    };

    private static readonly HashSet<string> SyntheticRepresentationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "puppet", "toy", "plush", "plushie", "figurine", "statue", "sculpture",
        "painting", "painted", "illustration", "illustrated", "drawing", "cartoon", "cgi", "render", "rendered",
        "fantasy", "fantastical", "mutant", "hybrid", "monster", "mythical", "mythological",
        "surreal", "surrealist",
    };

    private static readonly string[] SyntheticRepresentationPhrases =
    {
        "ai generated", "ai-generated", "3d render", "3d rendered", "computer generated", "computer-generated",
        "digital art", "concept art", "fantasy art",
    };

    private static readonly HashSet<string> ScientificIllustrationCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "illustration", "illustrated", "drawing", "painting", "painted",
    };

    private static readonly HashSet<string> ScientificReferenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fig", "figure", "plate", "journal", "bulletin", "biodiversity", "smithsonian", "museum",
        "university", "library", "archive", "monograph", "textbook", "encyclopedia", "scientific",
        "zoology", "taxonomy",
    };

    private static readonly HashSet<string> ScientificContentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anatomy", "anatomical", "biology", "biological", "zoology", "taxonomy", "species", "specimen",
        "organ", "organs", "gill", "gills", "heart", "hearts", "blood", "circulation", "circulatory",
        "cell", "cells", "tissue", "tissues", "mollusk", "mollusks", "mollusc", "molluscs",
        "malacology", "malacological", "locomotion", "behavior", "behaviour",
    };

    private static readonly string[] ScientificReferenceDisqualifiers =
    {
        "ai generated", "3d render", "digital art", "concept art", "fantasy", "cartoon", "clipart",
        "comic", "logo", "t shirt", "tshirt", "mockup",
    };

    private static readonly HashSet<string> StableRepresentationHardNegatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "unrequested_statue_or_sculpture", "unrequested_logo_or_symbol", "unrequested_generic_diagram",
    };

    private const int SubjectUncertainPenalty = 4;
    private const int SceneEvidenceBonus = 6;
    private const int MinQualityScan = 5;

    private readonly NativeAssetAcquisitionEngine _engine;
    private readonly INativeAssetVerifier _verifier;
    private readonly Dictionary<string, string> _stableRepresentationFindings = new(StringComparer.OrdinalIgnoreCase);

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
        NativeAcquiredAsset? sceneFallback = null;
        NativeAssetVerificationResult? sceneFallbackDecision = null;
        var sceneFallbackScore = int.MinValue;
        var fallbackQueries = new List<string> { query };
        fallbackQueries.AddRange(FallbackSearchQueries(query));
        var verificationQuery = BuildVerificationQuery(query, requiredSubject);
        var evidenceSubject = SceneEvidenceSubject(query, requiredSubject);
        var evidencePhrase = SceneEvidencePhrase(query, requiredSubject);
        var evidenceLabel = evidencePhrase.Length > 0 ? evidencePhrase : evidenceSubject;
        var evidenceCanReplaceSubject = evidenceSubject.Length > 0 && !BehaviorSceneEvidenceWords.Contains(evidenceSubject);

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
                var sourceFamily = NativeVisualSelectionPolicy.SourceFamilyKey(asset.Candidate);
                if (sourceFamily.Length > 0 && blocked.Contains(sourceFamily))
                {
                    blocked.Add(key);
                    blocked.Add(asset.Candidate.Url);
                    Report("verify", index + 1, scanLimit,
                        "Skipping another visual from an already used source/project family");
                    Discard(asset);
                    continue;
                }

                if (!checkedItems.Add(key) || !checkedItems.Add(asset.Candidate.Url))
                {
                    Discard(asset);
                    break;
                }
                blocked.Add(key);
                blocked.Add(asset.Candidate.Url);

                if (TryGetStableRepresentationFinding(asset.Candidate, out var stableFinding) &&
                    !QueryExplicitlyRequestsStableRepresentation(query, stableFinding))
                {
                    var reason = $"cached stable representation finding '{stableFinding}'";
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {reason}");
                    Report("verify", index + 1, scanLimit,
                        $"Visual relevance rejected ({reason}); trying another asset");
                    Discard(asset);
                    continue;
                }

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

                RememberStableRepresentationFinding(asset.Candidate, decision);
                if (!decision.Accepted)
                {
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {decision.Decision}");
                    Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({decision.Decision}); trying another asset");
                    Discard(asset);
                    continue;
                }

                if (requiredSubject.Length > 0 &&
                    decision.SubjectIdentityMode.Equals("visually_recognizable", StringComparison.OrdinalIgnoreCase) &&
                    !decision.RequestedSubjectVisible &&
                    !evidenceCanReplaceSubject)
                {
                    const string reason = "generic or behavioral scene evidence cannot replace the explicit visual subject";
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {reason}");
                    Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({reason}); trying another asset");
                    Discard(asset);
                    continue;
                }

                if (requiredSubject.Length > 0 &&
                    decision.SubjectIdentityMode.Equals("visually_recognizable", StringComparison.OrdinalIgnoreCase) &&
                    decision.RequestedSubjectVisible &&
                    !CandidateTitleMentionsSubject(asset.Candidate.Title, requiredSubject))
                {
                    if (CandidateTitleIsSpecific(asset.Candidate.Title))
                    {
                        var reason = $"specific stock metadata does not name required subject '{requiredSubject}'";
                        failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {reason}");
                        Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({reason}); trying another asset");
                        Discard(asset);
                        continue;
                    }

                    try
                    {
                        var subjectDecision = await _verifier.VerifyAsync(
                            BuildEvidenceVerificationQuery(query, requiredSubject), asset, cancellationToken);
                        if (!subjectDecision.Accepted || !subjectDecision.RequestedSubjectVisible)
                        {
                            var reason = $"independent subject-only verification did not confirm '{requiredSubject}'";
                            failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {reason}");
                            Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({reason}); trying another asset");
                            Discard(asset);
                            continue;
                        }
                        Report("verify", index + 1, scanLimit,
                            $"Independent subject-only verification confirmed '{requiredSubject}' for vague metadata");
                    }
                    catch (Exception error)
                    {
                        var reason = $"subject-only verification failed for '{requiredSubject}': {error.Message}";
                        failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {reason}");
                        Report("verify", index + 1, scanLimit, $"Visual relevance rejected ({reason}); trying another asset");
                        Discard(asset);
                        continue;
                    }
                }

                if (evidenceSubject.Length > 0 && decision.RequestedSceneEvidenceVisible)
                {
                    if (CandidateTitleIsSpecific(asset.Candidate.Title) &&
                        !CandidateTitleMentionsSubject(asset.Candidate.Title, evidenceSubject))
                    {
                        decision = decision with
                        {
                            RequestedSceneEvidenceVisible = false,
                            Decision = decision.Decision + $"; specific stock metadata does not mention scene evidence '{evidenceLabel}'",
                        };
                        Report("verify", index + 1, scanLimit,
                            $"Specific stock metadata does not mention scene evidence '{evidenceLabel}'; treating asset as subject-only fallback");
                    }
                    else
                    {
                        try
                        {
                            var evidenceDecision = await _verifier.VerifyAsync(
                                BuildEvidenceVerificationQuery(query, requiredSubject, evidenceLabel), asset, cancellationToken);
                            if (!evidenceDecision.Accepted || !evidenceDecision.RequestedSceneEvidenceVisible)
                            {
                                decision = decision with
                                {
                                    RequestedSceneEvidenceVisible = false,
                                    Decision = decision.Decision + $"; scene evidence '{evidenceLabel}' was not independently confirmed",
                                };
                                Report("verify", index + 1, scanLimit,
                                    $"Scene-specific evidence '{evidenceLabel}' was not confirmed; treating asset as subject-only fallback");
                            }
                        }
                        catch (Exception error)
                        {
                            failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: scene-evidence verification failed: {error.Message}");
                            decision = decision with
                            {
                                RequestedSceneEvidenceVisible = false,
                                Decision = decision.Decision + $"; scene evidence '{evidenceLabel}' could not be confirmed",
                            };
                            Report("verify", index + 1, scanLimit,
                                $"Scene-specific evidence '{evidenceLabel}' could not be confirmed; treating asset as subject-only fallback");
                        }
                    }
                }

                var contradiction = NativeVisualSelectionPolicy.SceneContradiction(query, asset.Candidate);
                if (contradiction.Length > 0)
                {
                    failures.Add($"{asset.Candidate.Provider}/{asset.Candidate.Id}: {contradiction}");
                    Report("verify", index + 1, scanLimit,
                        $"Visual relevance rejected ({contradiction}); trying another asset");
                    Discard(asset);
                    continue;
                }

                var syntheticCue = UnrequestedSyntheticRepresentation(query, asset.Candidate.Title);
                if (syntheticCue.Length == 0)
                    syntheticCue = NativeVisualSelectionPolicy.UnrequestedSyntheticRepresentation(query, asset.Candidate);

                var verifiedScientificReference =
                    syntheticCue.Length > 0 &&
                    evidenceSubject.Length > 0 &&
                    decision.RequestedSceneEvidenceVisible &&
                    IsScientificReferenceIllustration(query, asset.Candidate, syntheticCue);
                if (verifiedScientificReference)
                {
                    decision = decision with
                    {
                        Style = "representational",
                        Decision = decision.Decision + "; verified scientific/reference illustration",
                    };
                    Report("verify", index + 1, scanLimit,
                        "Verified scientific/reference illustration retained as representational evidence");
                }
                else if (syntheticCue.Length > 0 && !decision.Style.Equals("decorative", StringComparison.OrdinalIgnoreCase))
                {
                    decision = decision with
                    {
                        Style = "decorative",
                        Decision = decision.Decision + $"; unrequested synthetic representation '{syntheticCue}'",
                    };
                    Report("verify", index + 1, scanLimit,
                        $"Unrequested synthetic/prop visual '{syntheticCue}' downgraded to decorative fallback");
                }

                var score = VisualScore(decision, evidenceSubject.Length > 0);
                if (best is null || score > bestScore)
                {
                    if (best is not null) Discard(best);
                    best = asset;
                    bestDecision = decision;
                    bestScore = score;
                    Report("verify", index + 1, scanLimit,
                        $"Best visual so far: {decision.Quality}/{decision.Style} ({score}{(decision.SubjectUncertain ? ", subject uncertain" : "")}{(evidenceSubject.Length > 0 && decision.RequestedSceneEvidenceVisible ? ", scene evidence confirmed" : "")})");
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

            if (evidenceSubject.Length > 0 &&
                requiredSubject.Length > 0 &&
                bestDecision.RequestedSubjectVisible &&
                !bestDecision.RequestedSceneEvidenceVisible)
            {
                if (sceneFallback is null || bestScore > sceneFallbackScore)
                {
                    if (sceneFallback is not null) Discard(sceneFallback);
                    sceneFallback = best;
                    sceneFallbackDecision = bestDecision;
                    sceneFallbackScore = bestScore;
                    Report("verify", 1, 1,
                        $"Subject-only visual retained as fallback; searching for visible '{evidenceLabel}' evidence");
                }
                else Discard(best);
                continue;
            }

            if (sceneFallback is not null) Discard(sceneFallback);
            if (uncertainFallback is not null) Discard(uncertainFallback);
            if (decorativeFallback is not null) Discard(decorativeFallback);
            Remember(bestDecision);
            return best;
        }

        if (sceneFallback is not null && sceneFallbackDecision is not null)
        {
            if (uncertainFallback is not null) Discard(uncertainFallback);
            if (decorativeFallback is not null) Discard(decorativeFallback);
            Remember(sceneFallbackDecision);
            Report("verify", 1, 1,
                $"No confirmed '{evidenceLabel}' evidence found; using the best subject-only factual fallback");
            return sceneFallback;
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
            if (excluded is not null && excluded.Count > 0)
            {
                Discard(decorativeFallback);
                throw new NativeAssetAcquisitionException(
                    $"no factual unexcluded {kind} assets found for: {query}");
            }

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
            NativeAcquiredAsset result;
            try
            {
                result = await AcquireAsync(
                    items[index], destinationFolder, kind, limit, targetRatio, attempts,
                    unique ? used : null, cancellationToken, requiredSubject);
            }
            catch (NativeAssetAcquisitionException error) when (
                unique && used.Count > 0 && IsUniquenessExhaustion(error))
            {
                var recent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (results.Count > 0)
                {
                    recent.Add(CandidateKey(results[^1].Candidate));
                    recent.Add(results[^1].Candidate.Url);
                    var recentFamily = NativeVisualSelectionPolicy.SourceFamilyKey(results[^1].Candidate);
                    if (recentFamily.Length > 0)
                        recent.Add(recentFamily);
                }

                Report("acquire", index + 1, items.Length,
                    "No fresh unique visual passed; allowing non-adjacent reuse as a fallback");
                try
                {
                    result = await AcquireAsync(
                        items[index], destinationFolder, kind, limit, targetRatio, attempts,
                        recent.Count > 0 ? recent : null, cancellationToken, requiredSubject);
                }
                catch (NativeAssetAcquisitionException)
                {
                    Report("acquire", index + 1, items.Length,
                        "Only previously used visuals remain; allowing last-resort reuse to keep production running");
                    result = await AcquireAsync(
                        items[index], destinationFolder, kind, limit, targetRatio, attempts,
                        null, cancellationToken, requiredSubject);
                }
            }

            results.Add(result);
            if (unique)
            {
                used.Add(CandidateKey(result.Candidate));
                used.Add(result.Candidate.Url);
                var family = NativeVisualSelectionPolicy.SourceFamilyKey(result.Candidate);
                if (family.Length > 0)
                    used.Add(family);
            }
        }
        return results;
    }

    private static int VisualScore(NativeAssetVerificationResult decision, bool sceneEvidenceRequested)
    {
        var quality = QualityScore.GetValueOrDefault(decision.Quality, 6);
        var style = StyleScore.GetValueOrDefault(decision.Style, 2);
        var sceneEvidence = sceneEvidenceRequested && decision.RequestedSceneEvidenceVisible ? SceneEvidenceBonus : 0;
        return quality + style + sceneEvidence - (decision.SubjectUncertain ? SubjectUncertainPenalty : 0);
    }

    private void Remember(NativeAssetVerificationResult decision)
    {
        LastSelectedQuality = QualityScore.ContainsKey(decision.Quality) ? decision.Quality : "preferred";
        LastSelectedStyle = StyleScore.ContainsKey(decision.Style) ? decision.Style : "literal";
        LastSelectedSubjectUncertain = decision.SubjectUncertain;
    }

    private void RememberStableRepresentationFinding(
        NativeAssetCandidate candidate,
        NativeAssetVerificationResult decision)
    {
        if (decision.Accepted || !StableRepresentationHardNegatives.Contains(decision.HardNegative))
            return;

        var key = CandidateKey(candidate);
        _stableRepresentationFindings[key] = decision.HardNegative;
        if (!string.IsNullOrWhiteSpace(candidate.Url))
            _stableRepresentationFindings[candidate.Url] = decision.HardNegative;
    }

    private bool TryGetStableRepresentationFinding(NativeAssetCandidate candidate, out string finding)
    {
        if (_stableRepresentationFindings.TryGetValue(CandidateKey(candidate), out finding!))
            return true;
        if (!string.IsNullOrWhiteSpace(candidate.Url) &&
            _stableRepresentationFindings.TryGetValue(candidate.Url, out finding!))
            return true;
        finding = "";
        return false;
    }

    private static bool QueryExplicitlyRequestsStableRepresentation(string query, string finding)
    {
        var words = Regex.Matches(query ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return finding switch
        {
            "unrequested_statue_or_sculpture" =>
                words.Contains("statue") || words.Contains("sculpture") || words.Contains("figurine") || words.Contains("monument"),
            "unrequested_logo_or_symbol" =>
                words.Contains("logo") || words.Contains("symbol") || words.Contains("emblem") || words.Contains("icon"),
            "unrequested_generic_diagram" =>
                words.Contains("diagram") || words.Contains("schematic") || words.Contains("chart") || words.Contains("infographic"),
            _ => false,
        };
    }

    private static bool IsScientificReferenceIllustration(
        string query,
        NativeAssetCandidate candidate,
        string representationCue)
    {
        if (!ScientificIllustrationCues.Contains(representationCue))
            return false;

        var candidateText = string.Join(" ", new[]
        {
            candidate.Title, candidate.Credit, candidate.SourcePage, candidate.Url,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var normalizedCandidate = Regex.Replace(candidateText.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        if (ScientificReferenceDisqualifiers.Any(phrase =>
                normalizedCandidate.Contains(phrase, StringComparison.Ordinal)))
            return false;

        var candidateWords = Regex.Matches(candidateText, "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ScientificReferenceWords.Any(candidateWords.Contains))
            return true;

        var queryWords = Regex.Matches(query ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scientificQuery = MultiWordEvidenceCueWords.Any(queryWords.Contains) ||
            ScientificContentWords.Any(queryWords.Contains);
        return scientificQuery && ScientificContentWords.Any(candidateWords.Contains);
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

    internal static bool CandidateTitleMentionsSubject(string candidateTitle, string requiredSubject)
    {
        var subjectWords = Regex.Matches(requiredSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        if (subjectWords.Count == 0)
            return true;

        var titleWords = Regex.Matches(candidateTitle ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        return subjectWords.All(subjectWord =>
            titleWords.Any(titleWord => SubjectTokensEquivalent(subjectWord, titleWord)));
    }

    internal static bool SubjectTokensEquivalent(string left, string right) =>
        CanonicalSubjectToken(left).Equals(CanonicalSubjectToken(right), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalSubjectToken(string value)
    {
        var match = Regex.Match(value ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*");
        if (!match.Success)
            return "";

        var token = match.Value.ToLowerInvariant();
        if (SubjectTokenAliases.TryGetValue(token, out var alias))
            return alias;

        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
            return token[..^3] + "y";

        if (token.Length > 4 && token.EndsWith("es", StringComparison.Ordinal))
        {
            var stem = token[..^2];
            if (stem.EndsWith("s", StringComparison.Ordinal) ||
                stem.EndsWith("x", StringComparison.Ordinal) ||
                stem.EndsWith("z", StringComparison.Ordinal) ||
                stem.EndsWith("ch", StringComparison.Ordinal) ||
                stem.EndsWith("sh", StringComparison.Ordinal))
                return stem;
        }

        if (token.Length > 3 &&
            token.EndsWith("s", StringComparison.Ordinal) &&
            !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("us", StringComparison.Ordinal) &&
            !token.EndsWith("is", StringComparison.Ordinal))
            return token[..^1];

        return token;
    }

    private static bool CandidateTitleIsSpecific(string candidateTitle) =>
        Regex.Matches(candidateTitle ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*").Count >= 3;

    private static bool IsUniquenessExhaustion(NativeAssetAcquisitionException error) =>
        error.Message.Contains("no unexcluded", StringComparison.OrdinalIgnoreCase) ||
        error.Message.Contains("no factual unexcluded", StringComparison.OrdinalIgnoreCase);

    public static string BuildVerificationQuery(string query, string requiredSubject)
    {
        var queryWords = Regex.Matches(query ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        var subjectWords = Regex.Matches(requiredSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        if (subjectWords.Count == 0)
            return Regex.Replace((query ?? "").Trim(), @"\s+", " ");

        var subject = string.Join(" ", subjectWords.Select(CanonicalSubjectToken).Where(value => value.Length > 0));
        var remainder = queryWords
            .Where(queryWord => !subjectWords.Any(subjectWord => SubjectTokensEquivalent(queryWord, subjectWord)))
            .ToList();
        return string.Join(" ", new[] { "subject", subject, string.Join(" ", remainder) }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static string SceneEvidenceSubject(string query, string requiredSubject)
    {
        var evidenceWords = SceneEvidenceWords(query, requiredSubject);
        return SceneEvidenceMetadataAnchor(evidenceWords);
    }

    internal static string SceneEvidenceMetadataAnchor(string evidenceLabel)
    {
        var evidenceWords = Regex.Matches(evidenceLabel ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        return SceneEvidenceMetadataAnchor(evidenceWords);
    }

    private static string SceneEvidenceMetadataAnchor(IReadOnlyList<string> evidenceWords)
    {
        if (evidenceWords.Count == 0)
            return "";

        var lead = evidenceWords[0].ToLowerInvariant();
        if (BehaviorSceneEvidenceWords.Contains(lead))
            return lead;

        foreach (var word in evidenceWords)
        {
            var normalized = word.ToLowerInvariant();
            if (normalized.All(char.IsDigit) || WeakEvidenceLeadWords.Contains(normalized))
                continue;
            return normalized;
        }

        return lead;
    }

    public static string SceneEvidencePhrase(string query, string requiredSubject)
    {
        var evidenceWords = SceneEvidenceWords(query, requiredSubject);
        if (evidenceWords.Count == 0)
            return "";

        var lead = evidenceWords[0].ToLowerInvariant();
        var multiWord = BehaviorSceneEvidenceWords.Contains(lead) ||
            evidenceWords.Any(word => MultiWordEvidenceCueWords.Contains(word));
        if (!multiWord)
            return lead;

        return string.Join(" ", evidenceWords.Take(4).Select(word => word.ToLowerInvariant()));
    }

    private static IReadOnlyList<string> SceneEvidenceWords(string query, string requiredSubject)
    {
        var queryWords = Regex.Matches(query ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        var subjectWords = Regex.Matches(requiredSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
        if (queryWords.Count == 0 || subjectWords.Count == 0)
            return Array.Empty<string>();

        var start = -1;
        for (var index = 0; index <= queryWords.Count - subjectWords.Count; index++)
        {
            if (!SubjectSequenceMatchesAt(queryWords, subjectWords, index))
                continue;
            start = index + subjectWords.Count;
            break;
        }

        if (start < 0)
            return Array.Empty<string>();

        while (start <= queryWords.Count - subjectWords.Count &&
               SubjectSequenceMatchesAt(queryWords, subjectWords, start))
            start += subjectWords.Count;

        if (start >= queryWords.Count)
            return Array.Empty<string>();

        var lead = queryWords[start];
        if (lead.Length < 3 || GenericSceneLeadWords.Contains(lead))
            return Array.Empty<string>();

        return queryWords.Skip(start).ToArray();
    }

    private static bool SubjectSequenceMatchesAt(
        IReadOnlyList<string> queryWords,
        IReadOnlyList<string> subjectWords,
        int start)
    {
        if (start < 0 || start + subjectWords.Count > queryWords.Count)
            return false;

        for (var subjectIndex = 0; subjectIndex < subjectWords.Count; subjectIndex++)
            if (!SubjectTokensEquivalent(queryWords[start + subjectIndex], subjectWords[subjectIndex]))
                return false;
        return true;
    }

    public static string BuildEvidenceVerificationQuery(string query, string evidenceSubject)
    {
        var subjectMatch = Regex.Match(evidenceSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*");
        return subjectMatch.Success
            ? $"subject {subjectMatch.Value.ToLowerInvariant()}"
            : Regex.Replace((query ?? "").Trim(), @"\s+", " ");
    }

    public static string BuildEvidenceVerificationQuery(string query, string requiredSubject, string evidenceSubject)
    {
        var safeEvidenceSubject = evidenceSubject ?? "";
        var evidenceWords = Regex.Matches(safeEvidenceSubject, "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value.ToLowerInvariant())
            .ToList();
        if (evidenceWords.Count == 0)
            return BuildEvidenceVerificationQuery(query, safeEvidenceSubject);

        var behaviorEvidence = BehaviorSceneEvidenceWords.Contains(evidenceWords[0]);
        if (evidenceWords.Count == 1 && !behaviorEvidence)
            return BuildEvidenceVerificationQuery(query, safeEvidenceSubject);

        var subjectWords = Regex.Matches(requiredSubject ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => CanonicalSubjectToken(match.Value))
            .Where(value => value.Length > 0)
            .ToList();
        if (subjectWords.Count == 0)
            return BuildEvidenceVerificationQuery(query, safeEvidenceSubject);

        return $"subject {string.Join(" ", subjectWords)} {string.Join(" ", evidenceWords)}";
    }

    public static string UnrequestedSyntheticRepresentation(string query, string candidateTitle)
    {
        var queryWords = Regex.Matches(query ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titleWords = Regex.Matches(candidateTitle ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var word in SyntheticRepresentationWords)
            if (titleWords.Contains(word) && !queryWords.Contains(word))
                return word;

        var normalizedQuery = Regex.Replace((query ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        var normalizedTitle = Regex.Replace((candidateTitle ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        foreach (var phrase in SyntheticRepresentationPhrases)
        {
            var normalizedPhrase = Regex.Replace(phrase.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
            if (normalizedTitle.Contains(normalizedPhrase, StringComparison.Ordinal) &&
                !normalizedQuery.Contains(normalizedPhrase, StringComparison.Ordinal))
                return normalizedPhrase;
        }
        return "";
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
