using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeAssetAcquisitionException : Exception
{
    public NativeAssetAcquisitionException(string message) : base(message) { }
}

public sealed record NativeAcquiredAsset(
    NativeAssetCandidate Candidate,
    string Path,
    bool Reused);

public sealed class NativeAssetAcquisitionEngine : IDisposable
{
    private static readonly HashSet<string> RelevanceStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from",
        "by", "at", "photo", "photography", "image", "video", "vertical", "portrait",
        "realistic", "documentary", "close", "up",
    };

    private static readonly HashSet<string> BroadQueryAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        "space", "science", "nature", "history", "technology", "engineering", "health",
        "medicine", "animals", "animal", "ocean", "geography", "physics", "chemistry",
        "biology", "astronomy", "earth", "environment", "transport", "architecture",
    };

    private readonly IReadOnlyList<INativeAssetProvider> _providers;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public Action<string, int, int, string>? Progress { get; set; }

    public NativeAssetAcquisitionEngine(
        IEnumerable<INativeAssetProvider> providers,
        HttpClient? client = null)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        if (_providers.Count == 0)
            throw new ArgumentException("at least one asset provider is required", nameof(providers));

        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsClient = client is null;
        if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0 (+desktop media downloader)");
    }

    public async Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(
        string query,
        string kind = "image",
        int limit = 20,
        double? targetRatio = null,
        bool requireSubject = false,
        CancellationToken cancellationToken = default)
    {
        query = Required(query, "query");
        var collected = new List<NativeAssetCandidate>();
        var errors = new List<string>();

        for (var index = 0; index < _providers.Count; index++)
        {
            var provider = _providers[index];
            Report("search", index + 1, _providers.Count, $"Searching {provider.Name}");
            try
            {
                var results = await provider.SearchAsync(query, kind, limit, cancellationToken);
                collected.AddRange(results.Where(item => item.Kind == kind && !string.IsNullOrWhiteSpace(item.Url)));
            }
            catch (Exception error)
            {
                errors.Add($"{provider.Name}: {error.Message}");
            }
        }

        var ranked = Rank(collected, targetRatio, query);
        ranked = PreferSubjectMatches(ranked, query, requireSubject);
        if (ranked.Count == 0 && errors.Count > 0)
            throw new NativeAssetAcquisitionException(string.Join("; ", errors));
        return ranked.Take(Math.Max(1, limit)).ToArray();
    }

    public async Task<NativeAcquiredAsset> AcquireAsync(
        string query,
        string destinationFolder,
        string kind = "image",
        int limit = 20,
        double? targetRatio = null,
        int attempts = 3,
        ISet<string>? excluded = null,
        CancellationToken cancellationToken = default)
    {
        if (attempts < 1)
            throw new ArgumentException("attempts must be at least 1", nameof(attempts));
        query = Required(query, "query");
        destinationFolder = Required(destinationFolder, "destination folder");
        Directory.CreateDirectory(destinationFolder);

        var requiredSubject = RequiredSubject(query);
        var candidates = (await SearchAsync(
            query, kind, limit, targetRatio,
            requireSubject: !string.IsNullOrWhiteSpace(requiredSubject),
            cancellationToken)).ToList();

        if (candidates.Count == 0)
        {
            var fallbacks = FallbackSearchQueries(query);
            for (var index = 0; index < fallbacks.Count; index++)
            {
                var fallback = fallbacks[index];
                Report("retry", index + 1, fallbacks.Count, $"No subject match; trying: {fallback}");
                candidates = (await SearchAsync(fallback, kind, limit, targetRatio, false, cancellationToken)).ToList();
                if (!string.IsNullOrWhiteSpace(requiredSubject))
                    candidates = candidates.Where(candidate => CandidateWords(candidate).Contains(requiredSubject)).ToList();
                if (candidates.Count > 0)
                    break;
            }
        }

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(requiredSubject))
        {
            Report("retry", 1, 1, "No direct subject match found; using best broader result");
            candidates = (await SearchAsync(query, kind, limit, targetRatio, false, cancellationToken)).ToList();
        }

        if (candidates.Count == 0)
            throw new NativeAssetAcquisitionException($"no {kind} assets found for: {query}");

        var blocked = excluded ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = candidates
            .Where(item => !blocked.Contains(CandidateKey(item)) && !blocked.Contains(item.Url))
            .ToList();
        var pool = distinct.Count > 0 ? distinct : candidates;
        var failures = new List<string>();
        var count = Math.Min(attempts, pool.Count);

        for (var index = 0; index < count; index++)
        {
            var candidate = pool[index];
            try
            {
                return await DownloadCandidateAsync(candidate, destinationFolder, index + 1, count, cancellationToken);
            }
            catch (Exception error)
            {
                failures.Add($"{candidate.Provider}/{candidate.Id}: {error.Message}");
            }
        }

        throw new NativeAssetAcquisitionException("all asset downloads failed: " + string.Join("; ", failures));
    }

    public async Task<IReadOnlyList<NativeAcquiredAsset>> AcquireManyAsync(
        IEnumerable<string> queries,
        string destinationFolder,
        string kind = "image",
        int limit = 20,
        double? targetRatio = null,
        int attempts = 3,
        bool unique = true,
        CancellationToken cancellationToken = default)
    {
        var items = queries.Select(value => (value ?? "").Trim()).Where(value => value.Length > 0).ToArray();
        var results = new List<NativeAcquiredAsset>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < items.Length; index++)
        {
            Report("acquire", index + 1, items.Length, items[index]);
            var result = await AcquireAsync(
                items[index], destinationFolder, kind, limit, targetRatio, attempts,
                unique ? used : null, cancellationToken);
            results.Add(result);
            if (unique)
            {
                used.Add(CandidateKey(result.Candidate));
                used.Add(result.Candidate.Url);
            }
        }

        return results;
    }

    public static IReadOnlyList<NativeAssetCandidate> Rank(
        IEnumerable<NativeAssetCandidate> candidates,
        double? targetRatio = null,
        string query = "")
    {
        var unique = new Dictionary<string, NativeAssetCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var key = string.IsNullOrWhiteSpace(candidate.Url) ? CandidateKey(candidate) : candidate.Url;
            if (!unique.TryGetValue(key, out var previous) || candidate.Score > previous.Score)
                unique[key] = candidate;
        }

        return unique.Values
            .OrderByDescending(candidate => RelevanceTuple(candidate, query).SubjectMatch)
            .ThenByDescending(candidate => RelevanceTuple(candidate, query).EarlyOverlap)
            .ThenByDescending(candidate => RelevanceTuple(candidate, query).LeadingMatch)
            .ThenByDescending(candidate => RelevanceTuple(candidate, query).Coverage)
            .ThenByDescending(candidate => candidate.Score + RatioBonus(candidate, targetRatio))
            .ThenByDescending(candidate => Math.Max(0L, (long)candidate.Width) * Math.Max(0, candidate.Height))
            .ThenBy(candidate => candidate.Duration)
            .ToArray();
    }

    private async Task<NativeAcquiredAsset> DownloadCandidateAsync(
        NativeAssetCandidate candidate,
        string folder,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        var destination = Destination(candidate, folder);
        if (CachedAssetIsUsable(candidate, destination))
        {
            Report("download", index, total, $"Reusing {Path.GetFileName(destination)}");
            return new NativeAcquiredAsset(candidate, destination, true);
        }

        if (File.Exists(destination)) File.Delete(destination);
        var temporary = destination + ".part";
        if (File.Exists(temporary)) File.Delete(temporary);
        Report("download", index, total, $"Downloading from {candidate.Provider}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
            request.Headers.Accept.ParseAdd("image/jpeg,image/png,image/webp,image/gif,video/*;q=0.9,*/*;q=0.8");
            if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri))
            {
                if (uri.Host.Equals("pixabay.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".pixabay.com", StringComparison.OrdinalIgnoreCase))
                    request.Headers.Referrer = new Uri("https://pixabay.com/");
                else if (uri.Host.Equals("pexels.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".pexels.com", StringComparison.OrdinalIgnoreCase))
                    request.Headers.Referrer = new Uri("https://www.pexels.com/");
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, cancellationToken);

            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("downloaded file is empty");
            if (!CachedAssetIsUsable(candidate, temporary))
                throw new IOException("downloaded image used an unsupported AVIF/HEIC container");

            File.Move(temporary, destination, true);
            return new NativeAcquiredAsset(candidate, destination, false);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static string Destination(NativeAssetCandidate candidate, string folder)
    {
        var suffix = "." + (candidate.Kind == "video" ? "mp4" : "jpg");
        if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 8)
                suffix = extension;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CandidateKey(candidate))))
            .ToLowerInvariant()[..12];
        var stem = SafeFilename(string.IsNullOrWhiteSpace(candidate.Title) ? candidate.Id : candidate.Title);
        return Path.Combine(folder, $"{stem}_{digest}{suffix}");
    }

    private static bool CachedAssetIsUsable(NativeAssetCandidate candidate, string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0)
            return false;
        if (!candidate.Kind.Equals("image", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            Span<byte> header = stackalloc byte[16];
            using var stream = File.OpenRead(path);
            var read = stream.Read(header);
            if (read >= 12 && Encoding.ASCII.GetString(header[4..8]) == "ftyp")
            {
                var brand = Encoding.ASCII.GetString(header[8..12]).ToLowerInvariant();
                if (brand is "avif" or "avis" or "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
                    return false;
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static List<NativeAssetCandidate> PreferSubjectMatches(
        IReadOnlyList<NativeAssetCandidate> candidates,
        string query,
        bool requireSubject)
    {
        if (candidates.Count == 0)
            return candidates.ToList();
        var subject = RequiredSubject(query);
        if (string.IsNullOrWhiteSpace(subject))
            return candidates.ToList();
        var matches = candidates.Where(candidate => CandidateWords(candidate).Contains(subject)).ToList();
        return matches.Count > 0 ? matches : requireSubject ? [] : candidates.ToList();
    }

    private static (int SubjectMatch, int EarlyOverlap, int LeadingMatch, double Coverage) RelevanceTuple(
        NativeAssetCandidate candidate,
        string query)
    {
        var queryWords = RelevanceWords(query);
        if (queryWords.Count == 0)
            return (0, 0, 0, 0);
        var candidateWords = CandidateWords(candidate);
        var overlap = queryWords.Count(candidateWords.Contains);
        var requiredSubject = RequiredSubject(query);
        var primary = string.IsNullOrWhiteSpace(requiredSubject) ? queryWords[0] : requiredSubject;
        return (
            candidateWords.Contains(primary) ? 1 : 0,
            queryWords.Take(4).Count(candidateWords.Contains),
            candidateWords.Contains(queryWords[0]) ? 1 : 0,
            (double)overlap / queryWords.Count);
    }

    private static double RatioBonus(NativeAssetCandidate candidate, double? targetRatio)
    {
        if (targetRatio is null || candidate.Width <= 0 || candidate.Height <= 0)
            return 0;
        var ratio = (double)candidate.Width / candidate.Height;
        return Math.Max(0, 1 - Math.Abs(ratio - targetRatio.Value));
    }

    private static HashSet<string> CandidateWords(NativeAssetCandidate candidate) =>
        RelevanceWords($"{candidate.Title} {candidate.SourcePage}").ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<string> RelevanceWords(string value)
    {
        var result = new List<string>();
        foreach (Match match in Regex.Matches((value ?? "").ToLowerInvariant(), "[a-zA-Z0-9]+"))
        {
            var word = match.Value;
            if (word.Length < 3 || RelevanceStopWords.Contains(word) || result.Contains(word, StringComparer.OrdinalIgnoreCase))
                continue;
            result.Add(word);
        }
        return result;
    }

    private static string RequiredSubject(string query)
    {
        var words = RelevanceWords(query);
        return words.Count >= 2 && BroadQueryAnchors.Contains(words[0]) ? words[1] : "";
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
        var meaningful = RelevanceWords(original);
        var subject = RequiredSubject(original);
        if (!string.IsNullOrWhiteSpace(subject))
        {
            var subjectText = words.FirstOrDefault(word => word.Equals(subject, StringComparison.OrdinalIgnoreCase)) ?? subject;
            var anchor = words.FirstOrDefault() ?? meaningful.FirstOrDefault() ?? "";
            var afterSubject = meaningful.Skip(2).Where(word => !word.Equals(subject, StringComparison.OrdinalIgnoreCase)).ToList();
            Add(string.Join(" ", new[] { anchor, subjectText }.Concat(afterSubject.Take(2))));
            foreach (var word in afterSubject.AsEnumerable().Reverse()) Add($"{subjectText} {word}");
            Add(string.Join(" ", new[] { subjectText }.Concat(afterSubject.Take(2))));
            Add(subjectText);
        }
        foreach (var length in new[] { 12, 9, 6, 4 })
            if (words.Count > length) Add(string.Join(" ", words.Take(length)));
        return variants;
    }

    private static string SafeFilename(string value, int maxLength = 60)
    {
        var cleaned = new string((value ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray()).Trim('.', '_');
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        if (cleaned.Length > maxLength) cleaned = cleaned[..maxLength].TrimEnd('.', '_', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "asset" : cleaned;
    }

    private static string CandidateKey(NativeAssetCandidate candidate) =>
        $"{candidate.Provider}:{(string.IsNullOrWhiteSpace(candidate.Id) ? candidate.Url : candidate.Id)}";

    private static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException($"{name} is required");
        return text;
    }

    private void Report(string stage, int current, int total, string message) =>
        Progress?.Invoke(stage, current, total, message);

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
