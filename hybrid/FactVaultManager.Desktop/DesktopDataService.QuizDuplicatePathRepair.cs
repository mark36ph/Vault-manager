namespace FactVaultManager.Desktop;

public sealed record QuizDuplicatePathRepairSuggestion(
    int HistoryId,
    string Label,
    string CurrentFolder,
    string ProposedFolder,
    QuizArchiveMatchConfidence Confidence,
    int Score,
    string Evidence);

public sealed record QuizDuplicatePathRepairConflict(
    string SourceFolder,
    IReadOnlyList<int> HistoryIds,
    string Reason);

public sealed record QuizDuplicatePathRepairPreview(
    int DuplicateFolders,
    int DuplicateRows,
    int ConfidentRepairs,
    IReadOnlyList<QuizDuplicatePathRepairSuggestion> Suggestions,
    IReadOnlyList<QuizDuplicatePathRepairConflict> Conflicts);

public sealed record QuizDuplicatePathRepairApplyResult(
    int Updated,
    int Skipped,
    IReadOnlyList<string> Details);

public sealed partial class DesktopDataService
{
    private const int DuplicatePathUniquenessMargin = 25;

    public QuizDuplicatePathRepairPreview PreviewDuplicateQuizHistoryProjectFolders()
    {
        var settings = LoadSettings();
        var histories = GetQuizHistory();
        if (histories.Count == 0)
            return new QuizDuplicatePathRepairPreview(0, 0, 0, [], []);

        var duplicateGroups = histories
            .Where(history => IsExistingCDriveFolder(history.ProjectFolder))
            .GroupBy(history => NormalizeBulkArchivePath(history.ProjectFolder), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => new
            {
                Source = group.Key,
                Histories = group.OrderBy(history => history.Id).ToList(),
            })
            .OrderBy(group => group.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (duplicateGroups.Count == 0)
            return new QuizDuplicatePathRepairPreview(0, 0, 0, [], []);

        var duplicateIds = duplicateGroups
            .SelectMany(group => group.Histories)
            .Select(history => history.Id)
            .ToHashSet();
        var duplicateSources = duplicateGroups
            .Select(group => group.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Paths already used by a non-duplicate History row are reserved. Duplicate repair never
        // steals a known-good path from another row.
        var reservedFolders = histories
            .Where(history => !duplicateIds.Contains(history.Id))
            .Select(history => NormalizeBulkArchivePath(history.ProjectFolder))
            .Where(path => path.Length > 0 && Directory.Exists(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflicts = new List<QuizDuplicatePathRepairConflict>();
        var repairTargets = new List<QuizHistorySummary>();

        foreach (var group in duplicateGroups)
        {
            var sourceFingerprint = TryInspectDuplicateRepairFolder(group.Source);
            if (sourceFingerprint is null)
            {
                conflicts.Add(new QuizDuplicatePathRepairConflict(
                    group.Source,
                    group.Histories.Select(history => history.Id).ToList(),
                    "The shared C: project folder could not be inspected safely."));
                continue;
            }

            // Determine which one History row actually belongs to the shared C: project. Stored
            // project_folder is deliberately blanked during scoring so the corrupt shared path
            // cannot make every row look like an Exact match.
            var ownerCandidates = group.Histories
                .Select(history => EvaluateDuplicateRepairCandidate(history, sourceFingerprint))
                .Where(candidate => candidate.Confidence >= QuizArchiveMatchConfidence.High)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.HistoryId)
                .ToList();

            if (!HasUniqueBest(ownerCandidates, out var owner))
            {
                var ids = group.Histories.Select(history => history.Id).ToList();
                var detail = ownerCandidates.Count == 0
                    ? "No History row has a High-confidence identity match to this physical C: project."
                    : $"The likely owner is ambiguous between {string.Join(", ", ownerCandidates.Take(3).Select(candidate => $"#{candidate.HistoryId} ({candidate.Score})"))}.";
                conflicts.Add(new QuizDuplicatePathRepairConflict(group.Source, ids, detail));
                continue;
            }

            // Keep the uniquely identified owner on the physical C: folder for now. Every other
            // row in the group must be recovered to its own distinct folder before C: can be archived.
            repairTargets.AddRange(group.Histories.Where(history => history.Id != owner.HistoryId));
        }

        if (repairTargets.Count == 0)
        {
            return new QuizDuplicatePathRepairPreview(
                duplicateGroups.Count,
                duplicateGroups.Sum(group => group.Histories.Count),
                0,
                [],
                conflicts);
        }

        var candidateFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in EnumerateDuplicateRepairProjectFolders(GetProjectsRoot()))
        {
            var normalized = NormalizeBulkArchivePath(folder);
            if (normalized.Length == 0 || reservedFolders.Contains(normalized) || duplicateSources.Contains(normalized))
                continue;
            candidateFolders.Add(normalized);
        }

        if (!string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
        {
            try
            {
                var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
                if (Directory.Exists(quizRoot))
                {
                    foreach (var folder in Directory.EnumerateDirectories(quizRoot))
                    {
                        var normalized = NormalizeBulkArchivePath(folder);
                        if (normalized.Length == 0 || reservedFolders.Contains(normalized) || duplicateSources.Contains(normalized))
                            continue;
                        candidateFolders.Add(normalized);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // C: candidates remain usable even if Z: is temporarily unavailable.
            }
        }

        var fingerprints = new Dictionary<string, QuizArchiveFolderFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in candidateFolders)
        {
            var fingerprint = TryInspectDuplicateRepairFolder(folder);
            if (fingerprint is not null)
                fingerprints[folder] = fingerprint;
        }

        var candidatesByHistory = new Dictionary<int, List<QuizArchiveDeepCandidate>>();
        var candidatesByFolder = new Dictionary<string, List<QuizArchiveDeepCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var history in repairTargets)
        {
            var candidates = new List<QuizArchiveDeepCandidate>();
            foreach (var fingerprint in fingerprints.Values)
            {
                var candidate = EvaluateDuplicateRepairCandidate(history, fingerprint);
                if (candidate.Confidence < QuizArchiveMatchConfidence.High)
                    continue;
                candidates.Add(candidate);
                if (!candidatesByFolder.TryGetValue(fingerprint.Folder, out var folderCandidates))
                    candidatesByFolder[fingerprint.Folder] = folderCandidates = [];
                folderCandidates.Add(candidate);
            }

            candidatesByHistory[history.Id] = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.ArchiveFolder, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var pair in candidatesByFolder)
        {
            pair.Value.Sort((left, right) =>
            {
                var score = right.Score.CompareTo(left.Score);
                return score != 0 ? score : left.HistoryId.CompareTo(right.HistoryId);
            });
        }

        var suggestions = new List<QuizDuplicatePathRepairSuggestion>();
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var history in repairTargets.OrderBy(history => history.Id))
        {
            if (!candidatesByHistory.TryGetValue(history.Id, out var historyCandidates) ||
                !HasUniqueBest(historyCandidates, out var best))
            {
                conflicts.Add(new QuizDuplicatePathRepairConflict(
                    history.ProjectFolder,
                    [history.Id],
                    historyCandidates is { Count: > 0 }
                        ? $"No unique High-confidence destination. Best candidates: {string.Join(", ", historyCandidates.Take(3).Select(candidate => $"{Path.GetFileName(candidate.ArchiveFolder)} ({candidate.Score})"))}."
                        : "No High-confidence C: or Z: project folder was found for this History row."));
                continue;
            }

            if (!candidatesByFolder.TryGetValue(best.ArchiveFolder, out var folderCandidates) ||
                !HasUniqueBest(folderCandidates, out var folderWinner) ||
                folderWinner.HistoryId != history.Id ||
                !usedFolders.Add(best.ArchiveFolder))
            {
                conflicts.Add(new QuizDuplicatePathRepairConflict(
                    history.ProjectFolder,
                    [history.Id],
                    $"The best destination '{Path.GetFileName(best.ArchiveFolder)}' is also a strong match for another History row, so it was not assigned automatically."));
                continue;
            }

            suggestions.Add(new QuizDuplicatePathRepairSuggestion(
                history.Id,
                best.Label,
                history.ProjectFolder,
                best.ArchiveFolder,
                best.Confidence,
                best.Score,
                string.Join("; ", best.Evidence)));
        }

        return new QuizDuplicatePathRepairPreview(
            duplicateGroups.Count,
            duplicateGroups.Sum(group => group.Histories.Count),
            suggestions.Count,
            suggestions,
            conflicts);
    }

    public QuizDuplicatePathRepairApplyResult ApplyDuplicateQuizHistoryProjectFolderRepairs(
        IReadOnlyList<QuizDuplicatePathRepairSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (suggestions.Count == 0)
            return new QuizDuplicatePathRepairApplyResult(0, 0, []);

        var histories = GetQuizHistory();
        var byId = histories.ToDictionary(history => history.Id);
        var movingIds = suggestions.Select(suggestion => suggestion.HistoryId).ToHashSet();
        var requestedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        var skipped = 0;
        var details = new List<string>();

        foreach (var suggestion in suggestions.OrderBy(suggestion => suggestion.HistoryId))
        {
            if (!byId.TryGetValue(suggestion.HistoryId, out var history) ||
                !SameStoredPath(history.ProjectFolder, suggestion.CurrentFolder))
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: stored path changed after the repair preview");
                continue;
            }

            string destination;
            try
            {
                destination = Path.GetFullPath(suggestion.ProposedFolder);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: proposed path is invalid ({error.Message})");
                continue;
            }

            if (!Directory.Exists(destination) || !requestedFolders.Add(destination))
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: proposed folder is missing or was assigned twice");
                continue;
            }

            var ownedByStationaryHistory = histories.Any(other =>
                other.Id != history.Id &&
                !movingIds.Contains(other.Id) &&
                SameStoredPath(other.ProjectFolder, destination));
            if (ownedByStationaryHistory)
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: proposed folder is now owned by another History row");
                continue;
            }

            // Re-evaluate immediately before writing so a stale preview cannot silently relink a row
            // after the project folder has changed on disk.
            var fingerprint = TryInspectDuplicateRepairFolder(destination);
            var currentHistory = GetQuizHistory().FirstOrDefault(candidate => candidate.Id == history.Id);
            if (fingerprint is null || currentHistory is null ||
                !SameStoredPath(currentHistory.ProjectFolder, suggestion.CurrentFolder))
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: path or project contents changed before apply");
                continue;
            }

            var rechecked = EvaluateDuplicateRepairCandidate(currentHistory, fingerprint);
            if (rechecked.Confidence < QuizArchiveMatchConfidence.High ||
                rechecked.Score < suggestion.Score - DuplicatePathUniquenessMargin)
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: High-confidence identity was not retained at final recheck");
                continue;
            }

            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: database update was rejected");
                continue;
            }

            try
            {
                RequirePersistedQuizHistoryPath(history.Id, destination);
                updated++;
                details.Add($"History #{history.Id}: {suggestion.CurrentFolder} -> {destination}");
            }
            catch (InvalidOperationException error)
            {
                skipped++;
                details.Add($"History #{suggestion.HistoryId}: {error.Message}");
            }
        }

        return new QuizDuplicatePathRepairApplyResult(updated, skipped, details);
    }

    private static QuizArchiveDeepCandidate EvaluateDuplicateRepairCandidate(
        QuizHistorySummary history,
        QuizArchiveFolderFingerprint fingerprint)
    {
        // The current stored path is the corrupt signal we are repairing. Blank it so only format,
        // title/series/category, episode and project-file metadata can earn a match.
        var detached = history with { ProjectFolder = "" };
        var candidate = QuizArchiveDeepMatcher.Evaluate(detached, fingerprint, current: null);
        if (candidate.Confidence == QuizArchiveMatchConfidence.NoMatch)
            return candidate;

        var score = candidate.Score;
        var evidence = candidate.Evidence.ToList();

        // Older project folders often use "Series Quiz - 002" rather than an explicit "#002".
        // Treat that final three-digit sequence as episode evidence only when the name has no #episode
        // marker and is not an archived-copy collision suffix. This deliberately avoids interpreting
        // "Film Quiz #002 - Short - 001" as episode 001.
        var canUseTrailingSequence = !fingerprint.FolderName.Contains('#') &&
                                     !fingerprint.FolderName.Contains("archived-copy", StringComparison.OrdinalIgnoreCase);
        var trailingSequence = canUseTrailingSequence
            ? TrailingProjectSequence(fingerprint.FolderName)
            : null;
        if (trailingSequence.HasValue && history.EpisodeNumber > 0)
        {
            if (trailingSequence.Value == history.EpisodeNumber)
            {
                score += 110;
                evidence.Add($"trailing project sequence {trailingSequence.Value:000} matches episode #{history.EpisodeNumber:000}");
            }
            else
            {
                // Once a trailing sequence is trusted as episode evidence, a conflict is decisive.
                // Do not let title/series metadata outweigh a contradictory episode number.
                return candidate with
                {
                    Confidence = QuizArchiveMatchConfidence.NoMatch,
                    Score = 0,
                    Evidence = [$"trailing project sequence {trailingSequence.Value:000} conflicts with episode #{history.EpisodeNumber:000}"],
                };
            }
        }

        var confidence = score >= 145
            ? QuizArchiveMatchConfidence.High
            : score >= 70
                ? QuizArchiveMatchConfidence.Possible
                : QuizArchiveMatchConfidence.NoMatch;
        return candidate with
        {
            Confidence = confidence,
            Score = confidence == QuizArchiveMatchConfidence.NoMatch ? 0 : score,
            Evidence = confidence == QuizArchiveMatchConfidence.NoMatch ? Array.Empty<string>() : evidence,
        };
    }

    private static int? TrailingProjectSequence(string folderName)
    {
        var tokens = (folderName ?? "")
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return null;
        var last = tokens[^1];
        return last.Length == 3 && int.TryParse(last, out var value)
            ? value
            : null;
    }

    private static bool HasUniqueBest(
        IReadOnlyList<QuizArchiveDeepCandidate> candidates,
        out QuizArchiveDeepCandidate best)
    {
        best = null!;
        if (candidates.Count == 0)
            return false;
        best = candidates[0];
        return candidates.Count == 1 || best.Score - candidates[1].Score >= DuplicatePathUniquenessMargin;
    }

    private static QuizArchiveFolderFingerprint? TryInspectDuplicateRepairFolder(string folder)
    {
        try
        {
            return QuizArchiveDeepMatcher.InspectProjectFolder(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDuplicateRepairProjectFolders(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
                if (LooksLikeDuplicateRepairProjectFolder(child))
                    yield return child;
            }
        }
    }

    private static bool LooksLikeDuplicateRepairProjectFolder(string folder)
    {
        try
        {
            var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Contains("quiz", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(folder, NativeProjectTimelineStore.TimelineFilename)))
            {
                return true;
            }

            return SocialVideoUploadRules.FindLikelyRenderedVideo(folder) is not null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
