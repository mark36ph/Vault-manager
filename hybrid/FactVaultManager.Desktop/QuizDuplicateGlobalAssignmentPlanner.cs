namespace FactVaultManager.Desktop;

internal sealed record QuizDuplicateGlobalAssignmentResult(
    IReadOnlyDictionary<int, QuizArchiveDeepCandidate> BestAssignments,
    IReadOnlySet<int> StableHistoryIds,
    int MatchedCount,
    int TotalScore);

internal static class QuizDuplicateGlobalAssignmentPlanner
{
    internal static QuizDuplicateGlobalAssignmentResult Plan(
        IReadOnlyDictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>> candidatesByHistory,
        int stabilityMargin)
    {
        ArgumentNullException.ThrowIfNull(candidatesByHistory);
        if (stabilityMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(stabilityMargin));

        var normalized = candidatesByHistory
            .OrderBy(pair => pair.Key)
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<QuizArchiveDeepCandidate>)pair.Value
                    .Where(candidate => candidate.HistoryId == pair.Key)
                    .GroupBy(candidate => candidate.ArchiveFolder, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(candidate => candidate.Score)
                        .ThenByDescending(candidate => candidate.Confidence)
                        .First())
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.ArchiveFolder, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var best = Solve(normalized, forbiddenHistoryId: null, forbiddenFolder: null);
        if (best.Assignments.Count == 0)
        {
            return new QuizDuplicateGlobalAssignmentResult(
                best.Assignments,
                new HashSet<int>(),
                best.MatchedCount,
                best.TotalScore);
        }

        var stable = new HashSet<int>();
        foreach (var assignment in best.Assignments.OrderBy(pair => pair.Key))
        {
            var alternative = Solve(
                normalized,
                forbiddenHistoryId: assignment.Key,
                forbiddenFolder: assignment.Value.ArchiveFolder);

            if (alternative.MatchedCount < best.MatchedCount ||
                (alternative.MatchedCount == best.MatchedCount &&
                 best.TotalScore - alternative.TotalScore >= stabilityMargin))
            {
                stable.Add(assignment.Key);
            }
        }

        return new QuizDuplicateGlobalAssignmentResult(
            best.Assignments,
            stable,
            best.MatchedCount,
            best.TotalScore);
    }

    private static AssignmentSolution Solve(
        IReadOnlyDictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>> candidatesByHistory,
        int? forbiddenHistoryId,
        string? forbiddenFolder)
    {
        var historyIds = candidatesByHistory.Keys.OrderBy(id => id).ToList();
        var folders = candidatesByHistory.Values
            .SelectMany(candidates => candidates)
            .Where(candidate => !IsForbidden(candidate, forbiddenHistoryId, forbiddenFolder))
            .Select(candidate => candidate.ArchiveFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var source = 0;
        var historyStart = 1;
        var folderStart = historyStart + historyIds.Count;
        var sink = folderStart + folders.Count;
        var graph = Enumerable.Range(0, sink + 1)
            .Select(_ => new List<FlowEdge>())
            .ToArray();

        var historyNodeById = historyIds
            .Select((id, index) => (id, node: historyStart + index))
            .ToDictionary(pair => pair.id, pair => pair.node);
        var folderNodeByPath = folders
            .Select((folder, index) => (folder, node: folderStart + index))
            .ToDictionary(pair => pair.folder, pair => pair.node, StringComparer.OrdinalIgnoreCase);

        foreach (var historyId in historyIds)
            AddEdge(graph, source, historyNodeById[historyId], capacity: 1, cost: 0, candidate: null);
        foreach (var folder in folders)
            AddEdge(graph, folderNodeByPath[folder], sink, capacity: 1, cost: 0, candidate: null);

        foreach (var historyId in historyIds)
        {
            if (!candidatesByHistory.TryGetValue(historyId, out var candidates))
                continue;

            foreach (var candidate in candidates
                         .Where(candidate => !IsForbidden(candidate, forbiddenHistoryId, forbiddenFolder))
                         .OrderByDescending(candidate => candidate.Score)
                         .ThenBy(candidate => candidate.ArchiveFolder, StringComparer.OrdinalIgnoreCase))
            {
                if (!folderNodeByPath.TryGetValue(candidate.ArchiveFolder, out var folderNode))
                    continue;
                AddEdge(
                    graph,
                    historyNodeById[historyId],
                    folderNode,
                    capacity: 1,
                    cost: -candidate.Score,
                    candidate: candidate);
            }
        }

        var flow = 0;
        var totalCost = 0;
        while (TryFindShortestAugmentingPath(graph, source, sink, out var previousNode, out var previousEdge, out var pathCost))
        {
            var node = sink;
            while (node != source)
            {
                var from = previousNode[node];
                var edgeIndex = previousEdge[node];
                var edge = graph[from][edgeIndex];
                edge.Capacity--;
                graph[node][edge.ReverseIndex].Capacity++;
                node = from;
            }

            flow++;
            totalCost += pathCost;
        }

        var assignments = new Dictionary<int, QuizArchiveDeepCandidate>();
        foreach (var historyId in historyIds)
        {
            var historyNode = historyNodeById[historyId];
            foreach (var edge in graph[historyNode])
            {
                if (edge.Candidate is not null && edge.Capacity == 0)
                {
                    assignments[historyId] = edge.Candidate;
                    break;
                }
            }
        }

        return new AssignmentSolution(assignments, flow, -totalCost);
    }

    private static bool TryFindShortestAugmentingPath(
        IReadOnlyList<List<FlowEdge>> graph,
        int source,
        int sink,
        out int[] previousNode,
        out int[] previousEdge,
        out int pathCost)
    {
        const int Infinity = int.MaxValue / 4;
        var nodeCount = graph.Count;
        var distance = Enumerable.Repeat(Infinity, nodeCount).ToArray();
        previousNode = Enumerable.Repeat(-1, nodeCount).ToArray();
        previousEdge = Enumerable.Repeat(-1, nodeCount).ToArray();
        distance[source] = 0;

        for (var pass = 0; pass < nodeCount - 1; pass++)
        {
            var changed = false;
            for (var from = 0; from < nodeCount; from++)
            {
                if (distance[from] == Infinity)
                    continue;

                for (var edgeIndex = 0; edgeIndex < graph[from].Count; edgeIndex++)
                {
                    var edge = graph[from][edgeIndex];
                    if (edge.Capacity <= 0)
                        continue;

                    var candidateDistance = distance[from] + edge.Cost;
                    if (candidateDistance >= distance[edge.To])
                        continue;

                    distance[edge.To] = candidateDistance;
                    previousNode[edge.To] = from;
                    previousEdge[edge.To] = edgeIndex;
                    changed = true;
                }
            }

            if (!changed)
                break;
        }

        if (distance[sink] == Infinity)
        {
            pathCost = 0;
            return false;
        }

        pathCost = distance[sink];
        return true;
    }

    private static void AddEdge(
        IReadOnlyList<List<FlowEdge>> graph,
        int from,
        int to,
        int capacity,
        int cost,
        QuizArchiveDeepCandidate? candidate)
    {
        var forward = new FlowEdge(
            to,
            reverseIndex: graph[to].Count,
            capacity,
            cost,
            candidate);
        var reverse = new FlowEdge(
            from,
            reverseIndex: graph[from].Count,
            capacity: 0,
            cost: -cost,
            candidate: null);
        graph[from].Add(forward);
        graph[to].Add(reverse);
    }

    private static bool IsForbidden(
        QuizArchiveDeepCandidate candidate,
        int? forbiddenHistoryId,
        string? forbiddenFolder) =>
        forbiddenHistoryId.HasValue &&
        candidate.HistoryId == forbiddenHistoryId.Value &&
        string.Equals(candidate.ArchiveFolder, forbiddenFolder, StringComparison.OrdinalIgnoreCase);

    private sealed record AssignmentSolution(
        IReadOnlyDictionary<int, QuizArchiveDeepCandidate> Assignments,
        int MatchedCount,
        int TotalScore);

    private sealed class FlowEdge(
        int to,
        int reverseIndex,
        int capacity,
        int cost,
        QuizArchiveDeepCandidate? candidate)
    {
        public int To { get; } = to;
        public int ReverseIndex { get; } = reverseIndex;
        public int Capacity { get; set; } = capacity;
        public int Cost { get; } = cost;
        public QuizArchiveDeepCandidate? Candidate { get; } = candidate;
    }
}
