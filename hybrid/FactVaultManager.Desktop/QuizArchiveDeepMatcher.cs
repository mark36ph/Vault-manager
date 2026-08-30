using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public enum QuizArchiveMatchConfidence
{
    NoMatch = 0,
    Possible = 1,
    High = 2,
    Exact = 3,
}

public sealed record QuizArchiveFolderFingerprint(
    string Folder,
    string FolderName,
    string NormalizedFolderName,
    string CanonicalFolderName,
    bool HasShortMarker,
    int? ExplicitEpisode,
    string SearchableText,
    IReadOnlyDictionary<string, long> Files,
    string RenderedVideoPath);

public sealed record QuizArchiveDeepCandidate(
    int HistoryId,
    string Label,
    string CurrentFolder,
    string ArchiveFolder,
    QuizArchiveMatchConfidence Confidence,
    int Score,
    IReadOnlyList<string> Evidence);

public static class QuizArchiveDeepMatcher
{
    private static readonly Regex ExplicitEpisodePattern = new(
        @"#\s*(?<episode>\d{1,3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record FingerprintCacheEntry(
        DateTime RootWriteUtc,
        DateTime CachedUtc,
        QuizArchiveFolderFingerprint Fingerprint);

    private static readonly object FingerprintCacheLock = new();
    private static readonly Dictionary<string, FingerprintCacheEntry> FingerprintCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan FingerprintCacheLifetime = TimeSpan.FromMinutes(10);
    private const int MaxFingerprintFiles = 350;
    private const int MaxJsonFiles = 12;
    private const long MaxJsonBytes = 512 * 1024;
    private const int MaxJsonStrings = 320;

    public static QuizArchiveFolderFingerprint InspectProjectFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Project folder is required.", nameof(folder));

        var fullFolder = Path.GetFullPath(folder);
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException($"Project folder was not found: {fullFolder}");

        var rootWriteUtc = Directory.GetLastWriteTimeUtc(fullFolder);
        lock (FingerprintCacheLock)
        {
            if (FingerprintCache.TryGetValue(fullFolder, out var cached) &&
                cached.RootWriteUtc == rootWriteUtc &&
                DateTime.UtcNow - cached.CachedUtc <= FingerprintCacheLifetime)
            {
                return cached.Fingerprint;
            }
        }

        var folderName = Path.GetFileName(fullFolder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var normalizedFolderName = Normalize(folderName);

        // Walk the folder only once for file-name/size and JSON evidence. The previous matcher
        // walked the same tree separately for each evidence type, which was especially costly on Z:.
        var sampledFiles = EnumerateFilesSafely(fullFolder)
            .Take(MaxFingerprintFiles)
            .ToList();
        var files = BuildFileMap(fullFolder, sampledFiles);
        var searchable = new StringBuilder(folderName);
        foreach (var relative in files.Keys)
            searchable.Append(' ').Append(relative);

        foreach (var jsonPath in sampledFiles
                     .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => string.Equals(
                         Path.GetFileName(path),
                         NativeProjectTimelineStore.TimelineFilename,
                         StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxJsonFiles))
        {
            AppendJsonSearchText(jsonPath, searchable);
        }

        string renderedVideo = "";
        try
        {
            renderedVideo = SocialVideoUploadRules.FindLikelyRenderedVideo(fullFolder) ?? "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            renderedVideo = "";
        }

        var fingerprint = new QuizArchiveFolderFingerprint(
            fullFolder,
            folderName,
            normalizedFolderName,
            CanonicalProjectIdentity(folderName),
            ContainsWord(normalizedFolderName, "short"),
            ExplicitEpisode(folderName),
            Normalize(searchable.ToString()),
            files,
            renderedVideo);

        lock (FingerprintCacheLock)
        {
            FingerprintCache[fullFolder] = new FingerprintCacheEntry(rootWriteUtc, DateTime.UtcNow, fingerprint);
            if (FingerprintCache.Count > 256)
            {
                var expiry = DateTime.UtcNow - FingerprintCacheLifetime;
                foreach (var key in FingerprintCache
                             .Where(pair => pair.Value.CachedUtc < expiry)
                             .Select(pair => pair.Key)
                             .ToList())
                {
                    FingerprintCache.Remove(key);
                }
            }
        }

        return fingerprint;
    }

    public static QuizArchiveDeepCandidate Evaluate(
        QuizHistorySummary history,
        QuizArchiveFolderFingerprint archive,
        QuizArchiveFolderFingerprint? current = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(archive);

        var label = HistoryLabel(history);
        var currentFolder = (history.ProjectFolder ?? "").Trim();
        var evidence = new List<string>();
        var score = 0;
        var exactAnchor = false;
        var storedFolderExact = false;
        var historyShort = string.Equals(history.VideoType, "Short", StringComparison.Ordinal);

        // Exported quiz folders deliberately contain "Short" for vertical videos. A type mismatch
        // is therefore strong enough to reject a proposed folder before considering title overlap.
        if (archive.HasShortMarker != historyShort)
        {
            return new QuizArchiveDeepCandidate(
                history.Id,
                label,
                currentFolder,
                archive.Folder,
                QuizArchiveMatchConfidence.NoMatch,
                0,
                [historyShort ? "archive folder is not marked Short" : "archive folder is marked Short"]);
        }

        score += 20;
        evidence.Add(historyShort ? "Short/full-video format matches (Short)" : "Short/full-video format matches (full video)");

        var storedFolderName = StoredFolderName(history.ProjectFolder);
        if (storedFolderName.Length > 0)
        {
            if (string.Equals(Normalize(storedFolderName), archive.NormalizedFolderName, StringComparison.Ordinal))
            {
                score += 190;
                exactAnchor = true;
                storedFolderExact = true;
                evidence.Add("stored project folder name is an exact match");
            }
            else if (string.Equals(CanonicalProjectIdentity(storedFolderName), archive.CanonicalFolderName, StringComparison.Ordinal))
            {
                score += 125;
                evidence.Add("stored project folder identity matches after export suffix cleanup");
            }
        }

        var explicitEpisode = archive.ExplicitEpisode;
        if (explicitEpisode.HasValue && history.EpisodeNumber > 0)
        {
            if (explicitEpisode.Value != history.EpisodeNumber)
            {
                return new QuizArchiveDeepCandidate(
                    history.Id,
                    label,
                    currentFolder,
                    archive.Folder,
                    QuizArchiveMatchConfidence.NoMatch,
                    0,
                    [$"explicit archive episode #{explicitEpisode.Value:000} conflicts with history #{history.EpisodeNumber:000}"]);
            }

            score += 55;
            evidence.Add($"explicit episode #{history.EpisodeNumber:000} matches");
        }

        var identities = IdentitySignals(history);
        var folderIdentityMatched = false;
        foreach (var identity in identities)
        {
            if (identity.Normalized.Length < 3)
                continue;

            if (string.Equals(identity.Canonical, archive.CanonicalFolderName, StringComparison.Ordinal))
            {
                score += identity.Weight + 30;
                evidence.Add($"archive folder matches {identity.Source}");
                folderIdentityMatched = true;
                break;
            }

            if (ContainsPhrase(archive.NormalizedFolderName, identity.Normalized) ||
                ContainsPhrase(archive.CanonicalFolderName, identity.Canonical))
            {
                score += identity.Weight;
                evidence.Add($"archive folder contains {identity.Source}");
                folderIdentityMatched = true;
                break;
            }
        }

        var metadataSignals = 0;
        foreach (var identity in identities
                     .Where(item => item.Normalized.Length >= 4)
                     .OrderByDescending(item => item.Weight))
        {
            if (!ContainsPhrase(archive.SearchableText, identity.Normalized))
                continue;

            score += Math.Min(35, Math.Max(18, identity.Weight / 2));
            metadataSignals++;
            evidence.Add($"project files/JSON contain {identity.Source}");
            if (metadataSignals >= 2)
                break;
        }

        if (archive.RenderedVideoPath.Length > 0)
        {
            try
            {
                if (SocialUploadQueuePathFinder.MatchesVideoType(history.VideoType, archive.RenderedVideoPath))
                {
                    score += 12;
                    evidence.Add("rendered video type matches");
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The folder/name/metadata evidence remains usable if a video cannot be inspected.
            }
        }

        if (current is not null)
        {
            var overlap = SameFileCount(current.Files, archive.Files);
            if (overlap >= 5)
            {
                score += 105;
                exactAnchor = exactAnchor || folderIdentityMatched;
                evidence.Add($"{overlap} same relative files have identical sizes in the current and Z: copies");

                // An exact stored folder name plus a real file fingerprint is much stronger than a
                // second history row that merely shares the same broad series/title. This is the
                // distinguishing evidence for the remaining C: -> Z: backup ambiguities.
                if (storedFolderExact)
                {
                    score += 260;
                    exactAnchor = true;
                    evidence.Add("exact folder name + matching file fingerprint confirms the same project copy");
                }
            }
            else if (overlap >= 2)
            {
                score += 58;
                evidence.Add($"{overlap} same relative files have identical sizes in the current and Z: copies");
            }
            else if (overlap == 1)
            {
                score += 20;
                evidence.Add("1 same relative file has an identical size in the current and Z: copies");
            }
        }

        QuizArchiveMatchConfidence confidence;
        if (exactAnchor)
            confidence = QuizArchiveMatchConfidence.Exact;
        else if (score >= 145 && (folderIdentityMatched || metadataSignals > 0))
            confidence = QuizArchiveMatchConfidence.High;
        else if (score >= 70 && (folderIdentityMatched || metadataSignals > 0))
            confidence = QuizArchiveMatchConfidence.Possible;
        else
            confidence = QuizArchiveMatchConfidence.NoMatch;

        return new QuizArchiveDeepCandidate(
            history.Id,
            label,
            currentFolder,
            archive.Folder,
            confidence,
            confidence == QuizArchiveMatchConfidence.NoMatch ? 0 : score,
            confidence == QuizArchiveMatchConfidence.NoMatch ? Array.Empty<string>() : evidence);
    }

    public static string ConfidenceDisplay(QuizArchiveMatchConfidence confidence) => confidence switch
    {
        QuizArchiveMatchConfidence.Exact => "Exact",
        QuizArchiveMatchConfidence.High => "High",
        QuizArchiveMatchConfidence.Possible => "Possible",
        _ => "No match",
    };

    private static IReadOnlyList<(string Source, string Normalized, string Canonical, int Weight)> IdentitySignals(
        QuizHistorySummary history)
    {
        var values = new List<(string Source, string Value, int Weight)>
        {
            ("series", history.SeriesName, 85),
            ("quiz title", history.Title, 78),
            ("YouTube quiz identity", UploadTitleIdentity(history.YouTubeTitle), 72),
            ("category", history.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "", 52),
        };

        return values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => (
                item.Source,
                Normalize(item.Value),
                CanonicalProjectIdentity(item.Value),
                item.Weight))
            .Where(item => item.Item2.Length >= 3)
            .GroupBy(item => item.Item2, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Weight).First())
            .ToList();
    }

    private static IReadOnlyDictionary<string, long> BuildFileMap(string root, IReadOnlyList<string> sampledFiles)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in sampledFiles)
        {
            try
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                result[relative] = new FileInfo(path).Length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Ignore an individual unreadable file; other evidence can still identify the project.
            }
        }
        return result;
    }

    private static int SameFileCount(
        IReadOnlyDictionary<string, long> left,
        IReadOnlyDictionary<string, long> right)
    {
        var count = 0;
        foreach (var item in left)
        {
            if (right.TryGetValue(item.Key, out var length) && length == item.Value)
                count++;
        }
        return count;
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> folders = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(current).ToArray();
                folders = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            foreach (var file in files)
                yield return file;
            foreach (var folder in folders)
                pending.Push(folder);
        }
    }

    private static void AppendJsonSearchText(string path, StringBuilder searchable)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxJsonBytes)
                return;

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var remaining = MaxJsonStrings;
            AppendJsonStrings(document.RootElement, searchable, ref remaining);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Metadata is supplemental evidence only.
        }
    }

    private static void AppendJsonStrings(JsonElement element, StringBuilder searchable, ref int remaining)
    {
        if (remaining <= 0)
            return;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (remaining <= 0) break;
                    searchable.Append(' ').Append(property.Name);
                    AppendJsonStrings(property.Value, searchable, ref remaining);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (remaining <= 0) break;
                    AppendJsonStrings(item, searchable, ref remaining);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    searchable.Append(' ').Append(value);
                    remaining--;
                }
                break;
        }
    }

    private static int? ExplicitEpisode(string value)
    {
        var match = ExplicitEpisodePattern.Match(value ?? "");
        return match.Success && int.TryParse(match.Groups["episode"].Value, out var episode)
            ? episode
            : null;
    }

    private static string UploadTitleIdentity(string? title)
    {
        var value = (title ?? "").Trim();
        if (value.Length == 0)
            return "";

        var separator = value.LastIndexOf('|');
        if (separator >= 0)
            value = value[(separator + 1)..];
        var episodeMarker = value.LastIndexOf('#');
        if (episodeMarker >= 0)
            value = value[..episodeMarker];
        return value.Trim();
    }

    private static string CanonicalProjectIdentity(string? value)
    {
        var tokens = Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tokens.Count == 0)
            return "";

        if (tokens.Count > 1 && tokens[^1].Length == 3 && tokens[^1].All(char.IsDigit))
            tokens.RemoveAt(tokens.Count - 1);
        tokens.RemoveAll(token => string.Equals(token, "short", StringComparison.Ordinal));
        return string.Join(' ', tokens);
    }

    private static bool ContainsPhrase(string searchable, string identity)
    {
        if (identity.Length == 0)
            return false;
        return (" " + searchable + " ").Contains(" " + identity + " ", StringComparison.Ordinal);
    }

    private static bool ContainsWord(string searchable, string word) =>
        ContainsPhrase(searchable, Normalize(word));

    private static string Normalize(string? value)
    {
        var characters = (value ?? "").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string StoredFolderName(string? path) =>
        (path ?? "")
            .Trim()
            .TrimEnd('\\', '/')
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";

    private static string HistoryLabel(QuizHistorySummary history)
    {
        var series = history.SeriesName.Trim();
        if (series.Length == 0)
            series = history.Title.Trim();
        if (series.Length == 0)
            series = $"Quiz {history.Id}";
        var episode = history.EpisodeNumber > 0 ? $" #{history.EpisodeNumber:000}" : "";
        return $"{series}{episode} ({history.VideoType})";
    }
}
