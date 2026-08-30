namespace FactVaultManager.Desktop;

public sealed record QuizArchivePathMatch(
    int HistoryId,
    string Label,
    string ArchiveFolder);

public sealed record QuizArchiveExistingPathMatch(
    int HistoryId,
    string Label,
    string CurrentFolder,
    string ArchiveFolder);

public sealed record QuizArchiveAuditEntry(
    int HistoryId,
    string Label,
    string CurrentFolder,
    IReadOnlyList<string> CandidateFolders);

public sealed record QuizArchiveFolderAudit(
    string ArchiveFolder,
    int? HistoryId,
    string HistoryLabel,
    string CurrentFolder,
    QuizArchiveMatchConfidence Confidence,
    int Score,
    bool IsUnique,
    IReadOnlyList<string> Evidence)
{
    public string ArchiveName => Path.GetFileName(ArchiveFolder);
    public string ConfidenceDisplay => QuizArchiveDeepMatcher.ConfidenceDisplay(Confidence) +
        (((Confidence is QuizArchiveMatchConfidence.Exact or QuizArchiveMatchConfidence.High) && !IsUnique)
            ? " (ambiguous)"
            : "");
    public string SuggestedQuiz => HistoryId.HasValue
        ? $"#{HistoryId.Value} {HistoryLabel}"
        : "—";
    public string CurrentFolderDisplay => string.IsNullOrWhiteSpace(CurrentFolder) ? "(missing / none)" : CurrentFolder;
    public string EvidenceDisplay => Evidence.Count == 0 ? "—" : string.Join("; ", Evidence);
    public bool HasSuggestion => HistoryId.HasValue && Confidence != QuizArchiveMatchConfidence.NoMatch;
    public bool IsConfidentRelink => HasSuggestion && IsUnique &&
        Confidence is QuizArchiveMatchConfidence.Exact or QuizArchiveMatchConfidence.High;
}

public sealed record QuizArchiveRelinkRequest(
    int HistoryId,
    string Label,
    string ExpectedCurrentFolder,
    string ArchiveFolder,
    QuizArchiveMatchConfidence Confidence);

public sealed record QuizArchiveMatchPreview(
    int ArchiveFolders,
    int HistoryEntries,
    int AlreadyLinked,
    int LocalPathExists,
    int ReadyToMatch,
    int Ambiguous,
    int Unmatched,
    IReadOnlyList<QuizArchivePathMatch> Matches,
    IReadOnlyList<string> UnlinkedArchiveFolders,
    IReadOnlyList<QuizArchiveExistingPathMatch> ExistingPathArchiveMatches,
    IReadOnlyList<QuizArchiveAuditEntry> AmbiguousEntries,
    IReadOnlyList<QuizArchiveAuditEntry> UnmatchedEntries,
    IReadOnlyList<QuizArchiveFolderAudit> FolderAudits,
    IReadOnlyList<QuizArchiveRelinkRequest> ConfidentRelinks);

public sealed record QuizArchiveMatchApplyResult(int Updated, int Skipped);

public sealed partial class DesktopDataService
{
    private const int ArchiveUniquenessMargin = 25;

    public QuizArchiveMatchPreview PreviewQuizArchiveMatches()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var archiveRoot = Path.GetFullPath(settings.NasArchiveFolder.Trim());
        var quizRoot = Path.Combine(archiveRoot, "Quizzes");
        if (!Directory.Exists(quizRoot))
            throw new DirectoryNotFoundException($"The quiz archive folder was not found: {quizRoot}");

        var archiveFolders = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var histories = GetQuizHistory();
        var alreadyLinked = new List<QuizHistorySummary>();
        var existingOutsideArchive = new List<QuizHistorySummary>();
        var missingPath = new List<QuizHistorySummary>();
        var linkedArchiveFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var history in histories)
        {
            var current = (history.ProjectFolder ?? "").Trim();
            if (current.Length > 0 && Directory.Exists(current))
            {
                if (IsPathWithin(quizRoot, current))
                {
                    alreadyLinked.Add(history);
                    var linkedProjectFolder = ResolveTopLevelArchiveFolder(quizRoot, current);
                    if (linkedProjectFolder is not null)
                        linkedArchiveFolders.Add(linkedProjectFolder);
                }
                else
                {
                    existingOutsideArchive.Add(history);
                }
                continue;
            }

            missingPath.Add(history);
        }

        var unlinkedArchiveFolders = archiveFolders
            .Where(folder => !linkedArchiveFolders.Contains(folder))
            .ToList();
        var auditableHistories = existingOutsideArchive.Concat(missingPath).ToList();

        var currentFingerprints = new Dictionary<int, QuizArchiveFolderFingerprint>();
        foreach (var history in existingOutsideArchive)
        {
            var fingerprint = TryInspectFolder(history.ProjectFolder);
            if (fingerprint is not null)
                currentFingerprints[history.Id] = fingerprint;
        }

        var candidatesByFolder = new Dictionary<string, List<QuizArchiveDeepCandidate>>(StringComparer.OrdinalIgnoreCase);
        var candidatesByHistory = new Dictionary<int, List<QuizArchiveDeepCandidate>>();

        foreach (var archiveFolder in unlinkedArchiveFolders)
        {
            var archiveFingerprint = TryInspectFolder(archiveFolder);
            var candidates = new List<QuizArchiveDeepCandidate>();
            if (archiveFingerprint is not null)
            {
                foreach (var history in auditableHistories)
                {
                    currentFingerprints.TryGetValue(history.Id, out var currentFingerprint);
                    var candidate = QuizArchiveDeepMatcher.Evaluate(history, archiveFingerprint, currentFingerprint);
                    if (candidate.Confidence == QuizArchiveMatchConfidence.NoMatch)
                        continue;

                    candidates.Add(candidate);
                    if (!candidatesByHistory.TryGetValue(history.Id, out var historyCandidates))
                        candidatesByHistory[history.Id] = historyCandidates = new List<QuizArchiveDeepCandidate>();
                    historyCandidates.Add(candidate);
                }
            }

            candidatesByFolder[archiveFolder] = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.HistoryId)
                .ToList();
        }

        foreach (var pair in candidatesByHistory)
        {
            pair.Value.Sort((left, right) =>
            {
                var byScore = right.Score.CompareTo(left.Score);
                if (byScore != 0) return byScore;
                var byConfidence = right.Confidence.CompareTo(left.Confidence);
                if (byConfidence != 0) return byConfidence;
                return string.Compare(left.ArchiveFolder, right.ArchiveFolder, StringComparison.OrdinalIgnoreCase);
            });
        }

        var folderAudits = new List<QuizArchiveFolderAudit>();
        foreach (var archiveFolder in unlinkedArchiveFolders)
        {
            var candidates = candidatesByFolder[archiveFolder];
            if (candidates.Count == 0)
            {
                folderAudits.Add(new QuizArchiveFolderAudit(
                    archiveFolder,
                    null,
                    "",
                    "",
                    QuizArchiveMatchConfidence.NoMatch,
                    0,
                    false,
                    Array.Empty<string>()));
                continue;
            }

            var best = candidates[0];
            var folderUnique = candidates.Count == 1 || best.Score - candidates[1].Score >= ArchiveUniquenessMargin;
            var historyCandidates = candidatesByHistory[best.HistoryId];
            var otherHistoryFolderScore = historyCandidates
                .Where(candidate => !string.Equals(candidate.ArchiveFolder, archiveFolder, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.Score)
                .DefaultIfEmpty(int.MinValue / 2)
                .Max();
            var historyUnique = historyCandidates.Count == 1 ||
                                best.Score - otherHistoryFolderScore >= ArchiveUniquenessMargin;
            var unique = folderUnique && historyUnique;
            var evidence = best.Evidence.ToList();
            if (!folderUnique)
                evidence.Add("another Quiz History record scores similarly for this Z: folder");
            if (!historyUnique)
                evidence.Add("another Z: folder scores similarly for this Quiz History record");

            folderAudits.Add(new QuizArchiveFolderAudit(
                archiveFolder,
                best.HistoryId,
                best.Label,
                best.CurrentFolder,
                best.Confidence,
                best.Score,
                unique,
                evidence));
        }

        var confidentAudits = folderAudits
            .Where(audit => audit.IsConfidentRelink)
            .OrderBy(audit => audit.HistoryId)
            .ToList();
        var confidentRelinks = confidentAudits
            .Select(audit => new QuizArchiveRelinkRequest(
                audit.HistoryId!.Value,
                audit.HistoryLabel,
                audit.CurrentFolder,
                audit.ArchiveFolder,
                audit.Confidence))
            .ToList();

        var existingPathArchiveMatches = confidentAudits
            .Where(audit => audit.CurrentFolder.Length > 0 && Directory.Exists(audit.CurrentFolder))
            .Select(audit => new QuizArchiveExistingPathMatch(
                audit.HistoryId!.Value,
                audit.HistoryLabel,
                audit.CurrentFolder,
                audit.ArchiveFolder))
            .OrderBy(match => match.HistoryId)
            .ToList();

        var missingIds = new HashSet<int>(missingPath.Select(history => history.Id));
        var matches = confidentAudits
            .Where(audit => missingIds.Contains(audit.HistoryId!.Value))
            .Select(audit => new QuizArchivePathMatch(
                audit.HistoryId!.Value,
                audit.HistoryLabel,
                audit.ArchiveFolder))
            .OrderBy(match => match.HistoryId)
            .ToList();

        var readyMissingIds = new HashSet<int>(matches.Select(match => match.HistoryId));
        var ambiguousEntries = new List<QuizArchiveAuditEntry>();
        var unmatchedEntries = new List<QuizArchiveAuditEntry>();
        foreach (var history in missingPath)
        {
            if (readyMissingIds.Contains(history.Id))
                continue;

            var candidateFolders = candidatesByHistory.TryGetValue(history.Id, out var historyCandidates)
                ? historyCandidates
                    .OrderByDescending(candidate => candidate.Score)
                    .Select(candidate => candidate.ArchiveFolder)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            var entry = new QuizArchiveAuditEntry(
                history.Id,
                HistoryLabel(history),
                (history.ProjectFolder ?? string.Empty).Trim(),
                candidateFolders);
            if (candidateFolders.Count > 0)
                ambiguousEntries.Add(entry);
            else
                unmatchedEntries.Add(entry);
        }

        return new QuizArchiveMatchPreview(
            archiveFolders.Count,
            histories.Count,
            alreadyLinked.Count,
            existingOutsideArchive.Count,
            matches.Count,
            ambiguousEntries.Count,
            unmatchedEntries.Count,
            matches,
            unlinkedArchiveFolders,
            existingPathArchiveMatches,
            ambiguousEntries.OrderBy(entry => entry.HistoryId).ToList(),
            unmatchedEntries.OrderBy(entry => entry.HistoryId).ToList(),
            folderAudits.OrderBy(audit => audit.ArchiveName, StringComparer.OrdinalIgnoreCase).ToList(),
            confidentRelinks);
    }

    public QuizArchiveMatchApplyResult ApplyQuizArchiveMatches(IReadOnlyList<QuizArchivePathMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var histories = GetQuizHistory().ToDictionary(history => history.Id);
        var requests = matches
            .Select(match => new QuizArchiveRelinkRequest(
                match.HistoryId,
                match.Label,
                histories.TryGetValue(match.HistoryId, out var history) ? history.ProjectFolder : "",
                match.ArchiveFolder,
                QuizArchiveMatchConfidence.High))
            .ToList();
        return ApplyQuizArchiveRelinks(requests, allowExistingPaths: false);
    }

    public QuizArchiveMatchApplyResult ApplyQuizArchiveRelinks(
        IReadOnlyList<QuizArchiveRelinkRequest> requests,
        bool allowExistingPaths)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        if (!Directory.Exists(quizRoot))
            throw new DirectoryNotFoundException($"The quiz archive folder was not found: {quizRoot}");

        var histories = GetQuizHistory().ToDictionary(history => history.Id);
        var linkedArchiveOwners = histories.Values
            .Select(history =>
            {
                var current = (history.ProjectFolder ?? "").Trim();
                var top = current.Length > 0 && Directory.Exists(current) && IsPathWithin(quizRoot, current)
                    ? ResolveTopLevelArchiveFolder(quizRoot, current)
                    : null;
                return (history.Id, Folder: top);
            })
            .Where(item => item.Folder is not null)
            .GroupBy(item => item.Folder!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Id).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

        var updated = 0;
        var skipped = 0;
        var usedHistories = new HashSet<int>();
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            if (!usedHistories.Add(request.HistoryId))
            {
                skipped++;
                continue;
            }

            string archiveFolder;
            try
            {
                archiveFolder = Path.GetFullPath(request.ArchiveFolder);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                skipped++;
                continue;
            }

            if (!usedFolders.Add(archiveFolder) ||
                !histories.TryGetValue(request.HistoryId, out var history) ||
                !Directory.Exists(archiveFolder) ||
                !IsPathWithin(quizRoot, archiveFolder) ||
                !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, archiveFolder), archiveFolder, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (linkedArchiveOwners.TryGetValue(archiveFolder, out var ownerIds) &&
                ownerIds.Any(ownerId => ownerId != history.Id))
            {
                skipped++;
                continue;
            }

            var current = (history.ProjectFolder ?? "").Trim();
            if (!SameStoredPath(current, request.ExpectedCurrentFolder))
            {
                skipped++;
                continue;
            }

            if (!allowExistingPaths && current.Length > 0 && Directory.Exists(current))
            {
                skipped++;
                continue;
            }

            if (UpdateQuizHistoryProjectFolder(history.Id, archiveFolder))
            {
                updated++;
                linkedArchiveOwners[archiveFolder] = new HashSet<int> { history.Id };
            }
            else
            {
                skipped++;
            }
        }

        return new QuizArchiveMatchApplyResult(updated, skipped);
    }

    private static QuizArchiveFolderFingerprint? TryInspectFolder(string folder)
    {
        try
        {
            return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)
                ? null
                : QuizArchiveDeepMatcher.InspectProjectFolder(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ResolveTopLevelArchiveFolder(string quizRoot, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(quizRoot);
            var fullCandidate = Path.GetFullPath(candidate);
            var relative = Path.GetRelativePath(root, fullCandidate);
            if (relative == "." || Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                return null;

            var firstSegment = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstSegment)
                ? null
                : Path.GetFullPath(Path.Combine(root, firstSegment));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

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

    private static bool SameStoredPath(string left, string right)
    {
        left = (left ?? "").Trim();
        right = (right ?? "").Trim();
        if (left.Length == 0 || right.Length == 0)
            return left.Length == right.Length;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsPathWithin(string root, string candidate)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
            return relative == "." ||
                   (!Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
