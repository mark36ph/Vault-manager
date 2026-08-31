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

    private static readonly IReadOnlyDictionary<string, string[]> DuplicateRepairQuizFamilies =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Science"] = ["science"],
            ["History"] = ["history"],
            ["Geography"] = ["geography"],
            ["Space"] = ["space"],
            ["Nature & Animals"] = ["nature animals", "nature and animals"],
            ["Technology"] = ["technology"],
            ["Arts & Literature"] = ["arts literature", "arts and literature"],
            ["Music"] = ["music"],
            ["Film"] = ["film"],
            ["Logos"] = ["logos", "logo", "icons", "icon"],
            ["Sports"] = ["sports", "sport"],
            ["Entertainment"] = ["entertainment"],
            ["Mathematics"] = ["mathematics", "maths", "math"],
            ["General Knowledge"] = ["general knowledge"],
        };

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

        // Do not try to guess which History row "owns" the shared C: folder. That shared path is
        // the corrupt signal we are repairing and may not contain enough metadata to identify one
        // owner safely. Instead, independently rescue any row that has a unique High-confidence
        // alternate project folder elsewhere. Unresolved rows remain on the shared C: folder and
        // are reconsidered on the next scan. This allows partial progress without weakening the
        // episode/type guards used for destination matching.
        var repairTargets = duplicateGroups
            .SelectMany(group => group.Histories)
            .OrderBy(history => history.Id)
            .ToList();

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

        var fingerprints = new List<QuizArchiveFolderFingerprint>();
        foreach (var folder in candidateFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fingerprint = TryInspectDuplicateRepairFolder(folder);
            if (fingerprint is not null)
                fingerprints.Add(fingerprint);
        }

        var plan = PlanDuplicatePathRepairTargets(repairTargets, fingerprints);
        return new QuizDuplicatePathRepairPreview(
            duplicateGroups.Count,
            duplicateGroups.Sum(group => group.Histories.Count),
            plan.Suggestions.Count,
            plan.Suggestions,
            plan.Conflicts);
    }

    internal static (
        IReadOnlyList<QuizDuplicatePathRepairSuggestion> Suggestions,
        IReadOnlyList<QuizDuplicatePathRepairConflict> Conflicts)
        PlanDuplicatePathRepairTargets(
            IReadOnlyList<QuizHistorySummary> repairTargets,
            IReadOnlyList<QuizArchiveFolderFingerprint> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(repairTargets);
        ArgumentNullException.ThrowIfNull(fingerprints);

        var candidatesByHistory = new Dictionary<int, List<QuizArchiveDeepCandidate>>();
        var candidatesByFolder = new Dictionary<string, List<QuizArchiveDeepCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var history in repairTargets.OrderBy(history => history.Id))
        {
            var candidates = new List<QuizArchiveDeepCandidate>();
            foreach (var fingerprint in fingerprints)
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
        var conflicts = new List<QuizDuplicatePathRepairConflict>();
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
                        ? $"No unique High-confidence alternate project folder. Best candidates: {string.Join(", ", historyCandidates.Take(3).Select(candidate => $"{Path.GetFileName(candidate.ArchiveFolder)} ({candidate.Score})"))}."
                        : "No High-confidence alternate C: or Z: project folder was found for this History row."));
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
                    $"The best alternate folder '{Path.GetFileName(best.ArchiveFolder)}' is also a strong match for another History row, so it was not assigned automatically."));
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

        return (suggestions, conflicts);
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

        // Folder identity is authoritative when it clearly names one of our stable quiz families.
        // Matching an episode number or finding stale/copied JSON metadata can never outweigh a
        // different family in the actual project-folder name.
        if (TryGetDuplicateRepairFamilyConflict(history, fingerprint.FolderName, out var familyConflict))
        {
            return candidate with
            {
                Confidence = QuizArchiveMatchConfidence.NoMatch,
                Score = 0,
                Evidence = [familyConflict],
            };
        }

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

    private static bool TryGetDuplicateRepairFamilyConflict(
        QuizHistorySummary history,
        string folderName,
        out string reason)
    {
        reason = "";
        var historyFamily = DuplicateRepairHistoryFamily(history);
        var folderFamily = DuplicateRepairFolderFamily(folderName);
        if (historyFamily.Length == 0 || folderFamily.Length == 0 ||
            string.Equals(historyFamily, folderFamily, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        reason = $"project folder family '{folderFamily}' conflicts with Quiz History family '{historyFamily}'";
        return true;
    }

    private static string DuplicateRepairHistoryFamily(QuizHistorySummary history)
    {
        foreach (var category in (history.Categories ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var family = DuplicateRepairFamilyFromText(category, requireWholeValue: true);
            if (family.Length > 0)
                return family;
        }

        foreach (var identity in new[] { history.SeriesName, history.Title })
        {
            var family = DuplicateRepairFamilyFromText(identity, requireWholeValue: false);
            if (family.Length > 0)
                return family;
        }

        return "";
    }

    private static string DuplicateRepairFolderFamily(string folderName) =>
        DuplicateRepairFamilyFromText(folderName, requireWholeValue: false);

    private static string DuplicateRepairFamilyFromText(string? value, bool requireWholeValue)
    {
        var normalized = NormalizeDuplicateRepairFamilyText(value);
        if (normalized.Length == 0)
            return "";

        foreach (var family in DuplicateRepairQuizFamilies)
        {
            foreach (var alias in family.Value.OrderByDescending(item => item.Length))
            {
                if (requireWholeValue)
                {
                    if (string.Equals(normalized, alias, StringComparison.Ordinal))
                        return family.Key;
                }
                else if (ContainsDuplicateRepairFamilyPhrase(normalized, alias))
                {
                    return family.Key;
                }
            }
        }

        return "";
    }

    private static bool ContainsDuplicateRepairFamilyPhrase(string normalized, string phrase) =>
        $" {normalized} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static string NormalizeDuplicateRepairFamilyText(string? value)
    {
        var chars = (value ?? "")
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(
            ' ',
            new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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
