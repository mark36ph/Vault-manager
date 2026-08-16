using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeScientificEvidenceMetadataVerifier : INativeAssetVerifier
{
    private static readonly HashSet<string> InternalScientificEvidenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anatomy", "anatomical", "gill", "gills", "heart", "hearts", "blood", "hemocyanin", "haemocyanin",
        "hemoglobin", "haemoglobin", "circulation", "circulatory", "organ", "organs", "protein", "proteins",
        "pigment", "pigments", "molecule", "molecules", "molecular", "chemical", "chemistry", "compound", "compounds",
        "cell", "cells", "tissue", "tissues", "bone", "bones", "muscle", "muscles", "vein", "veins",
        "artery", "arteries", "internal", "structure", "structural", "brain", "brains", "nerve", "nerves", "nervous",
        "lung", "lungs", "kidney", "kidneys", "liver", "stomach", "intestine", "intestines", "digestive", "digestion",
        "respiratory", "respiration", "skeleton", "skeletal", "genome", "genetic", "dna", "rna", "chromosome",
        "chromosomes", "hormone", "hormones", "enzyme", "enzymes",
    };

    private readonly INativeAssetVerifier _baseVerifier;

    public NativeScientificEvidenceMetadataVerifier(INativeAssetVerifier baseVerifier) =>
        _baseVerifier = baseVerifier ?? throw new ArgumentNullException(nameof(baseVerifier));

    public async Task<NativeAssetVerificationResult> VerifyAsync(
        string query,
        NativeAcquiredAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var result = await _baseVerifier.VerifyAsync(query, asset, cancellationToken);
        if (!result.Accepted || !result.RequestedSceneEvidenceVisible)
            return result;

        var evidenceWords = ScientificEvidenceWords(query);
        if (evidenceWords.Count == 0 || MetadataSupportsEvidence(asset.Candidate, evidenceWords))
            return result;

        return result with
        {
            RequestedSceneEvidenceVisible = false,
            Decision = result.Decision + "; internal/scientific scene evidence lacks metadata or source support",
        };
    }

    internal static IReadOnlyList<string> ScientificEvidenceWords(string query)
    {
        var words = Tokens(query);
        if (words.Count == 0)
            return Array.Empty<string>();

        var start = words[0].Equals("subject", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (start >= words.Count)
            return Array.Empty<string>();

        var subject = NativeNamedSubjectVerifier.ExplicitSubjectPhrase(query);
        var subjectWords = Tokens(subject);
        for (var index = 0; index < subjectWords.Count && start < words.Count; index++)
        {
            if (!NativeVerifiedAssetAcquisitionEngine.SubjectTokensEquivalent(words[start], subjectWords[index]))
                break;
            start++;
        }

        return words
            .Skip(start)
            .Where(InternalScientificEvidenceWords.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool MetadataSupportsEvidence(
        NativeAssetCandidate candidate,
        IEnumerable<string> evidenceWords)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var metadata = string.Join(" ", new[]
        {
            candidate.Title,
            candidate.Credit,
            candidate.SourcePage,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var metadataWords = Tokens(metadata);

        foreach (var evidenceWord in evidenceWords)
            if (metadataWords.Any(metadataWord =>
                    NativeVerifiedAssetAcquisitionEngine.SubjectTokensEquivalent(evidenceWord, metadataWord)))
                return true;

        return false;
    }

    private static List<string> Tokens(string value) =>
        Regex.Matches(value ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*")
            .Select(match => match.Value)
            .ToList();
}
