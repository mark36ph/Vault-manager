using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeProductionException : Exception
{
    public NativeProductionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record NativeProductionProgress(
    string Stage,
    int Index,
    int Total,
    double Progress,
    string Status,
    string Message);

public sealed class NativeProductionAsset
{
    [JsonPropertyName("candidate")]
    public NativeAssetCandidate Candidate { get; set; } = new("", "", "", "image", "", 0, 0, 0, 0, "", "", "");

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("reused")]
    public bool Reused { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";
}

public sealed class NativeProductionContext
{
    public DesktopProject Project { get; init; } = null!;
    public string ProjectFolder { get; init; } = "";
    public AppSettingsModel AppSettings { get; init; } = null!;
    public string Topic { get; set; } = "";
    public string Research { get; set; } = "";
    public string Facts { get; set; } = "";
    public string Script { get; set; } = "";
    public List<NativeProductionAsset> Assets { get; set; } = new();
    public string? Voice { get; set; }
    public NativeTimeline? Timeline { get; set; }
    public NativeResolveFreeExportResult? Resolve { get; set; }
    public List<string> CompletedStages { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed record NativeProductionRunResult(
    IReadOnlyList<string> CompletedStages,
    IReadOnlyList<string> Warnings,
    NativeTimeline? Timeline,
    NativeResolveFreeExportResult? Resolve)
{
    public bool Succeeded => CompletedStages.Count == NativeProductionOrchestrator.Stages.Count;
}

public sealed class NativeProductionCheckpointStore
{
    public const string Filename = "production_checkpoint.json";
    private readonly string _path;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public NativeProductionCheckpointStore(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        _path = System.IO.Path.Combine(System.IO.Path.GetFullPath(projectFolder), Filename);
    }

    public string Path => _path;
    public bool Exists => File.Exists(_path);

    public void Save(NativeProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

        var payload = new Dictionary<string, object?>
        {
            ["topic"] = context.Topic,
            ["research"] = context.Research,
            ["facts"] = context.Facts,
            ["script"] = context.Script,
            ["image_prompts"] = context.Assets,
            ["voice"] = context.Voice,
            ["timeline"] = context.Timeline,
            ["completed_stages"] = context.CompletedStages,
            ["warnings"] = context.Warnings,
        };

        var temporary = System.IO.Path.ChangeExtension(_path, ".tmp");
        var json = JsonSerializer.Serialize(payload, Options) + "\n";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, _path, true);
    }

    public void LoadInto(NativeProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Exists)
            return;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(_path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new NativeProductionException($"could not read production checkpoint: {_path}", error);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new NativeProductionException($"could not read production checkpoint: {_path}");
            var root = document.RootElement;

            context.Topic = ReadString(root, "topic", context.Topic);
            context.Research = ReadJsonText(root, "research", context.Research);
            context.Facts = ReadJsonText(root, "facts", context.Facts);
            context.Script = ReadString(root, "script", context.Script);
            context.Voice = ReadNullableString(root, "voice", context.Voice);
            context.Warnings = ReadStringList(root, "warnings");
            context.CompletedStages = ReadStringList(root, "completed_stages")
                .Where(NativeProductionOrchestrator.IsStage)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (root.TryGetProperty("image_prompts", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
                context.Assets = ReadAssets(assetsElement);

            if (root.TryGetProperty("timeline", out var timelineElement) && timelineElement.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    context.Timeline = timelineElement.Deserialize<NativeTimeline>(NativeProjectTimelineStore.SerializerOptions);
                    context.Timeline?.Validate();
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
                {
                    throw new NativeProductionException("could not restore timeline from production checkpoint", error);
                }
            }

            if (context.CompletedStages.Contains("timeline", StringComparer.Ordinal) && context.Timeline is null)
                RollBackFrom(context, "timeline");

            context.CompletedStages.RemoveAll(stage => string.Equals(stage, "resolve", StringComparison.Ordinal));
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void RollBackFrom(NativeProductionContext context, string stage)
    {
        var index = NativeProductionOrchestrator.StageIndex(stage);
        context.CompletedStages = context.CompletedStages
            .Where(name => NativeProductionOrchestrator.StageIndex(name) < index)
            .ToList();
    }

    private static List<NativeProductionAsset> ReadAssets(JsonElement array)
    {
        var results = new List<NativeProductionAsset>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("path", out var pathElement) ||
                pathElement.ValueKind != JsonValueKind.String)
                continue;

            var path = pathElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(path))
                continue;

            NativeAssetCandidate candidate;
            if (item.TryGetProperty("candidate", out var candidateElement) && candidateElement.ValueKind == JsonValueKind.Object)
            {
                candidate = new NativeAssetCandidate(
                    ReadString(candidateElement, "provider", ""),
                    ReadString(candidateElement, "id", ""),
                    ReadString(candidateElement, "url", ""),
                    ReadString(candidateElement, "kind", "image"),
                    ReadString(candidateElement, "title", ""),
                    ReadInt(candidateElement, "width"),
                    ReadInt(candidateElement, "height"),
                    ReadDouble(candidateElement, "duration"),
                    ReadDouble(candidateElement, "score"),
                    ReadString(candidateElement, "credit", ""),
                    ReadString(candidateElement, "license", ""),
                    ReadString(candidateElement, "sourcePage", ReadString(candidateElement, "source_page", "")));
            }
            else
            {
                candidate = new NativeAssetCandidate("", "", "", "image", System.IO.Path.GetFileName(path), 0, 0, 0, 0, "", "", "");
            }

            results.Add(new NativeProductionAsset
            {
                Candidate = candidate,
                Path = path,
                Reused = ReadBool(item, "reused"),
                Query = ReadString(item, "query", ""),
            });
        }
        return results;
    }

    private static string ReadJsonText(JsonElement root, string name, string fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.GetRawText();
    }

    private static string ReadString(JsonElement root, string name, string fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }

    private static string? ReadNullableString(JsonElement root, string name, string? fallback)
    {
        if (!root.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static List<string> ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => item.Length > 0)
            .ToList();
    }

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static double ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed class NativeProductionOrchestrator
{
    private static readonly Regex SceneTimingPattern = new(
        @"(?m)^\s*(\d+(?:\.\d+)?)\s*[–—-]\s*(\d+(?:\.\d+)?)\s*(?:sec|secs|seconds?)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SearchWord = new("[A-Za-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> TopicStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from", "by", "at",
        "photo", "photography", "image", "video", "vertical", "portrait", "realistic", "documentary",
        "close", "up", "space", "science", "nature", "history", "technology", "engineering", "health",
        "medicine", "animals", "animal", "ocean", "geography", "physics", "chemistry", "biology",
        "astronomy", "earth", "environment", "transport", "architecture", "fact", "facts", "takes",
        "take", "longer", "shorter", "more", "less", "than", "is", "are", "was", "were", "has",
        "have", "had", "can", "could", "does", "did", "why", "how", "what", "when", "where",
        "first", "last", "great", "biggest", "largest", "smallest", "oldest", "newest", "fastest", "slowest",
    };

    public static readonly IReadOnlyList<string> Stages =
        new[] { "research", "facts", "script", "image_prompts", "voice", "timeline", "resolve" };

    private readonly AppSettingsModel _appSettings;
    private readonly Action<NativeProductionProgress>? _progress;

    public NativeProductionOrchestrator(AppSettingsModel appSettings, Action<NativeProductionProgress>? progress = null)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _progress = progress;
    }

    public async Task<NativeProductionRunResult> RunAsync(
        DesktopProject project,
        string projectFolder,
        string topic,
        string mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        projectFolder = System.IO.Path.GetFullPath(projectFolder);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException($"project folder was not found: {projectFolder}");

        mode = (mode ?? "produce").Trim().ToLowerInvariant();
        if (mode is not ("produce" or "reproduce" or "resume"))
            throw new NativeProductionException("mode must be produce, reproduce, or resume");
        if (mode == "produce" && !string.Equals(project.Status, "In Progress", StringComparison.Ordinal))
            throw new NativeProductionException("produce is only available for In Progress projects");
        if (mode == "reproduce" && !string.Equals(project.Status, "Completed", StringComparison.Ordinal))
            throw new NativeProductionException("reproduce is only available for Completed projects");

        topic = string.IsNullOrWhiteSpace(topic) ? project.Title.Trim() : topic.Trim();
        if (topic.Length == 0)
            throw new NativeProductionException("project topic is empty");

        var checkpoints = new NativeProductionCheckpointStore(projectFolder);
        if (mode == "resume" && !checkpoints.Exists)
            throw new NativeProductionException("this project has no production checkpoint to resume");

        var context = new NativeProductionContext
        {
            Project = project,
            ProjectFolder = projectFolder,
            AppSettings = _appSettings,
            Topic = topic,
            Script = project.Script ?? "",
        };

        if (mode == "resume")
        {
            checkpoints.LoadInto(context);
            NormalizeRestoredContext(context);
        }
        else
        {
            checkpoints.Clear();
            MarkProjectContentStagesComplete(context);
        }

        using var providers = NativeProductionProviders.FromProject(projectFolder, _appSettings);
        ConfigureProviderProgress(providers);

        try
        {
            for (var index = 0; index < Stages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stage = Stages[index];
                if (context.CompletedStages.Contains(stage, StringComparer.Ordinal))
                {
                    Report(stage, index + 1, "complete", StageCompletedMessage(stage, restored: mode == "resume"));
                    continue;
                }

                Report(stage, index + 1, "running", $"Running {stage.Replace('_', ' ')}");
                try
                {
                    await RunStageAsync(stage, context, providers, cancellationToken);
                    if (!context.CompletedStages.Contains(stage, StringComparer.Ordinal))
                        context.CompletedStages.Add(stage);
                    checkpoints.Save(context);
                    Report(stage, index + 1, "complete", StageCompletedMessage(stage, restored: false));
                }
                catch (Exception error)
                {
                    checkpoints.Save(context);
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    if (error is NativeProductionException)
                        throw;
                    throw new NativeProductionException($"stage {stage} failed: {error.Message}", error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            checkpoints.Save(context);
            throw;
        }

        if (context.CompletedStages.Count == Stages.Count)
            checkpoints.Clear();

        return new NativeProductionRunResult(
            context.CompletedStages.ToArray(),
            context.Warnings.ToArray(),
            context.Timeline,
            context.Resolve);
    }

    private async Task RunStageAsync(
        string stage,
        NativeProductionContext context,
        NativeProductionProviders providers,
        CancellationToken cancellationToken)
    {
        switch (stage)
        {
            case "research":
                context.Research = await providers.Research.GenerateAsync(
                    $"Research this topic for a factual short-form video: {context.Topic}", cancellationToken);
                break;

            case "facts":
                context.Facts = await providers.Facts.GenerateAsync(
                    $"Select the strongest verifiable facts from this research:\n{context.Research}", cancellationToken);
                break;

            case "script":
                context.Script = await providers.Script.GenerateAsync(
                    $"Write a concise narrated video script from these facts:\n{context.Facts}", cancellationToken);
                break;

            case "image_prompts":
                context.Assets = (await AcquireVisualsAsync(context, providers, cancellationToken)).ToList();
                break;

            case "voice":
                if (providers.Voice is null)
                {
                    context.Warnings.Add("Narration generation is disabled");
                    context.Voice = null;
                }
                else
                {
                    context.Voice = await providers.Voice.GenerateAsync(context.Script, context.ProjectFolder, cancellationToken);
                }
                break;

            case "timeline":
                context.Timeline = BuildTimeline(context);
                new NativeProjectTimelineStore(context.ProjectFolder).Save(context.Timeline);
                break;

            case "resolve":
                if (context.Timeline is null)
                    throw new NativeProductionException("resolve stage requires a timeline");
                context.Resolve = await BuildResolveAsync(context, cancellationToken);
                break;

            default:
                throw new NativeProductionException($"unknown production stage: {stage}");
        }
    }

    private async Task<IReadOnlyList<NativeProductionAsset>> AcquireVisualsAsync(
        NativeProductionContext context,
        NativeProductionProviders providers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Script))
            throw new NativeProductionException("image prompts stage requires a script");

        var sceneBuilder = new NativeSceneBuilder();
        var scenes = sceneBuilder.SplitScript(context.Script);
        if (scenes.Count == 0)
            throw new NativeProductionException("script did not contain any visual scenes");

        var imported = ImportedSceneSearches(context.Project.Notes);
        List<string> queries;
        if (imported.Count > 0)
        {
            context.Warnings.Add($"Using {imported.Count} imported scene search queries for asset selection");
            queries = imported.Select(query => AnchorImportedQuery(query, context)).ToList();
        }
        else
        {
            var generated = await providers.ImagePrompts.GenerateAsync(ImagePrompt(context), cancellationToken);
            queries = generated
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.Trim(' ', '-', '\t', '"'))
                .Where(line => line.Length > 0)
                .Select(line => AnchorGeneratedQuery(line, context))
                .ToList();
        }

        var fallbackCount = 0;
        if (queries.Count > scenes.Count)
            queries = queries.Take(scenes.Count).ToList();
        while (queries.Count < scenes.Count)
        {
            queries.Add(FallbackVisualQuery(scenes[queries.Count], context));
            fallbackCount++;
        }
        if (fallbackCount > 0)
            context.Warnings.Add($"Generated {fallbackCount} fallback visual searches for {scenes.Count} scenes");

        var destination = System.IO.Path.Combine(context.ProjectFolder, "Assets", "Acquired");
        var ratio = context.AppSettings.TimelineHeight > 0
            ? context.AppSettings.TimelineWidth / (double)context.AppSettings.TimelineHeight
            : (double?)null;

        var acquired = await providers.VerifiedAssetAcquisition.AcquireManyAsync(
            queries,
            destination,
            providers.Settings.AssetKind,
            providers.Settings.AssetLimit,
            ratio,
            providers.Settings.AssetAttempts,
            unique: true,
            cancellationToken);

        if (acquired.Count < scenes.Count)
            throw new NativeProductionException(
                $"Not enough acquired visuals to build the timeline: {acquired.Count} asset(s) for {scenes.Count} scene(s). Restart production from Find Visuals.");

        return acquired.Select((asset, index) => new NativeProductionAsset
        {
            Candidate = asset.Candidate,
            Path = System.IO.Path.GetFullPath(asset.Path),
            Reused = asset.Reused,
            Query = queries[index],
        }).ToArray();
    }

    private NativeTimeline BuildTimeline(NativeProductionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Script))
            throw new NativeProductionException("timeline stage requires a script");

        var builder = new NativeSceneBuilder();
        var timeline = builder.Build(
            context.Script,
            string.IsNullOrWhiteSpace(context.Project.Title) ? context.Topic : context.Project.Title,
            context.AppSettings.FrameRate,
            context.AppSettings.TimelineWidth,
            context.AppSettings.TimelineHeight);

        ApplyProjectSceneTimings(timeline, context.Project.OnScreenText, context.Warnings);
        if (context.Assets.Count > 0 && context.Assets.Count < timeline.Scenes.Count)
            throw new NativeProductionException(
                $"Not enough acquired visuals to build the timeline: {context.Assets.Count} asset(s) for {timeline.Scenes.Count} scene(s). Restart production from Find Visuals.");

        var assignments = new NativeTimelineAssetAssignmentEngine(timeline);
        for (var index = 0; index < timeline.Scenes.Count && index < context.Assets.Count; index++)
        {
            var item = context.Assets[index];
            var source = System.IO.Path.GetFullPath(item.Path);
            if (!File.Exists(source))
                throw new NativeProductionException($"Acquired visual is missing from disk: {source}. Restart production from Find Visuals.");

            var kind = string.Equals(item.Candidate.Kind, "video", StringComparison.OrdinalIgnoreCase)
                ? NativeTimelineAssetKind.Video
                : NativeTimelineAssetKind.Image;
            assignments.Assign(timeline.Scenes[index].Id, new NativeTimelineAsset
            {
                Kind = kind,
                Path = source,
                Status = NativeTimelineAssetStatus.Assigned,
                Duration = item.Candidate.Duration > 0 ? item.Candidate.Duration : null,
                Source = item.Candidate.SourcePage,
                Credit = item.Candidate.Credit,
                License = item.Candidate.License,
                Metadata = new()
                {
                    ["provider"] = item.Candidate.Provider,
                    ["query"] = item.Query,
                    ["candidate_id"] = item.Candidate.Id,
                    ["candidate_url"] = item.Candidate.Url,
                },
            });
        }
        new NativeTimelineClipMaterializer(timeline).Materialize();

        if (!string.IsNullOrWhiteSpace(context.Voice))
        {
            var voice = System.IO.Path.GetFullPath(context.Voice!);
            if (!File.Exists(voice))
                throw new NativeProductionException($"Narration file does not exist: {voice}");
            var narration = timeline.GetTrack("Narration")
                ?? timeline.AddTrack(new NativeTimelineTrack { Name = "Narration", Kind = NativeTimelineTrackKind.Audio });
            narration.Clips.Clear();
            narration.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Audio,
                Start = 0,
                Duration = Math.Max(0.1, timeline.Duration),
                Source = voice,
                Name = "Narration",
            });
        }

        var subtitleTrack = timeline.GetTrack("Subtitles")
            ?? timeline.AddTrack(new NativeTimelineTrack { Name = "Subtitles", Kind = NativeTimelineTrackKind.Subtitle });
        subtitleTrack.Clips.Clear();
        foreach (var scene in timeline.Scenes)
        {
            var text = MetadataString(scene.Metadata, "subtitle_text", scene.Narration);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var clip = subtitleTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Subtitle,
                Start = scene.Start,
                Duration = scene.Duration,
                Name = text,
                Metadata = new() { ["subtitle_text"] = text },
            });
            if (!scene.ClipIds.Contains(clip.Id, StringComparer.Ordinal))
                scene.ClipIds.Add(clip.Id);
        }

        timeline.Metadata["production_assets"] = context.Assets.Count;
        timeline.Metadata["production_unique_assets"] = context.Assets.Select(asset => System.IO.Path.GetFullPath(asset.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        timeline.Metadata["narration_attached"] = !string.IsNullOrWhiteSpace(context.Voice);
        timeline.Validate();
        return timeline;
    }

    private async Task<NativeResolveFreeExportResult> BuildResolveAsync(
        NativeProductionContext context,
        CancellationToken cancellationToken)
    {
        var ffmpeg = new NativeFfmpegTimelineService
        {
            Progress = (_, progress, message) => Report("resolve", 7, "running", message, progress),
        };
        var exportTimeline = context.Timeline!.Clone();
        await ffmpeg.SynchronizeVisualsToNarrationAsync(exportTimeline, cancellationToken);
        NativeFfmpegTimelineService.CloseVisualGaps(exportTimeline);

        var narration = exportTimeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .FirstOrDefault(clip => !string.IsNullOrWhiteSpace(clip.Source));
        double? narrationDuration = null;
        if (narration?.Source is { Length: > 0 })
            narrationDuration = await ffmpeg.MediaDurationAsync(narration.Source, cancellationToken);

        double? captionEndLimit = narrationDuration is null ? null : Math.Max(0, narrationDuration.Value - 1.25);
        await ffmpeg.ConvertStillsToVideoAsync(
            exportTimeline,
            context.ProjectFolder,
            context.Project.OnScreenText,
            captionEndLimit,
            cancellationToken);

        var outroAudio = System.IO.Path.Combine(context.ProjectFolder, "Voice", "fact_unlocked.mp3");
        if (narration is not null && File.Exists(outroAudio))
            await ffmpeg.AddFactUnlockedOutroAsync(exportTimeline, context.ProjectFolder, cancellationToken);

        NativeFfmpegTimelineService.CleanupUnusedResolveClips(exportTimeline, context.ProjectFolder);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = context.Project.Title,
            ["description"] = context.Project.Description,
            ["pinned_comment"] = context.Project.PinnedComment,
            ["script"] = context.Script,
            ["sources"] = context.Project.Sources,
        };

        return await Task.Run(
            () => new NativeResolveFreeExportService().Export(exportTimeline, context.ProjectFolder, metadata),
            cancellationToken);
    }

    private void ConfigureProviderProgress(NativeProductionProviders providers)
    {
        void OnProgress(string _, int current, int total, string message)
        {
            var local = total <= 0 ? 0 : Math.Clamp(current / (double)total, 0, 1);
            Report("image_prompts", 4, "running", message, local);
        }

        providers.AssetAcquisition.Progress = OnProgress;
        providers.VerifiedAssetAcquisition.Progress = OnProgress;
    }

    private void Report(string stage, int index, string status, string message, double? stageProgress = null)
    {
        if (_progress is null)
            return;
        var baseProgress = Math.Clamp((index - 1) / (double)Stages.Count, 0, 1);
        var overall = stageProgress is null
            ? (status == "complete" ? Math.Clamp(index / (double)Stages.Count, 0, 1) : baseProgress)
            : Math.Clamp((index - 1 + Math.Clamp(stageProgress.Value, 0, 1)) / Stages.Count, 0, 1);
        _progress(new NativeProductionProgress(stage, index, Stages.Count, overall, status, message));
    }

    private static void MarkProjectContentStagesComplete(NativeProductionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Script))
            throw new NativeProductionException("project script is empty");
        context.CompletedStages = new List<string> { "research", "facts", "script" };
        context.Research = "Using saved project content";
        context.Facts = "Using saved project content";
    }

    private static void NormalizeRestoredContext(NativeProductionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Script))
        {
            foreach (var stage in new[] { "research", "facts", "script" })
                if (!context.CompletedStages.Contains(stage, StringComparer.Ordinal))
                    context.CompletedStages.Add(stage);
        }

        context.CompletedStages = Stages.Where(stage => context.CompletedStages.Contains(stage, StringComparer.Ordinal)).ToList();

        if (context.CompletedStages.Contains("image_prompts", StringComparer.Ordinal))
        {
            if (context.Assets.Count == 0 || context.Assets.Any(asset => string.IsNullOrWhiteSpace(asset.Path) || !File.Exists(ResolveCheckpointPath(asset.Path, context.ProjectFolder))))
            {
                NativeProductionCheckpointStore.RollBackFrom(context, "image_prompts");
                context.Assets.Clear();
                context.Voice = null;
                context.Timeline = null;
            }
        }

        if (context.CompletedStages.Contains("voice", StringComparer.Ordinal) &&
            !string.IsNullOrWhiteSpace(context.Voice) &&
            !File.Exists(ResolveCheckpointPath(context.Voice!, context.ProjectFolder)))
        {
            NativeProductionCheckpointStore.RollBackFrom(context, "voice");
            context.Voice = null;
            context.Timeline = null;
        }

        if (context.CompletedStages.Contains("timeline", StringComparer.Ordinal))
        {
            var timelinePath = System.IO.Path.Combine(context.ProjectFolder, NativeProjectTimelineStore.TimelineFilename);
            if (context.Timeline is null && File.Exists(timelinePath))
                context.Timeline = new NativeProjectTimelineStore(context.ProjectFolder).Load();
            if (context.Timeline is null)
                NativeProductionCheckpointStore.RollBackFrom(context, "timeline");
        }
    }

    private static string ResolveCheckpointPath(string path, string projectFolder)
    {
        if (System.IO.Path.IsPathRooted(path))
            return System.IO.Path.GetFullPath(path);
        var direct = System.IO.Path.GetFullPath(path);
        if (File.Exists(direct))
            return direct;
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectFolder, path));
    }

    private static List<string> ImportedSceneSearches(string notes)
    {
        var text = (notes ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();
        var lines = text.Split('\n');
        var searches = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index].Trim(), "Search:", StringComparison.OrdinalIgnoreCase))
                continue;
            for (var next = index + 1; next < lines.Length; next++)
            {
                var candidate = lines[next].Trim(' ', '-', '\t');
                var lower = candidate.ToLowerInvariant();
                if (candidate.Length == 0)
                    continue;
                if (lower is "free sources:" or "search:" || lower.EndsWith(" sec", StringComparison.Ordinal))
                    break;
                searches.Add(candidate);
                break;
            }
        }
        return searches;
    }

    private static string ImagePrompt(NativeProductionContext context) =>
        "Create one stock-photo search query for each visual scene in the script below.\n\n" +
        $"Overall topic: {context.Topic}\n\n" +
        "Rules:\n" +
        "- Each query must directly depict the specific idea being narrated.\n" +
        "- Keep the main subject from the overall topic in every query when relevant.\n" +
        "- Prefer literal, documentary, realistic photography.\n" +
        "- Do not use abstract metaphors unless the script specifically requires one.\n" +
        "- Do not substitute unrelated objects just because they share a keyword.\n" +
        "- Include important nouns, locations, materials, weather, objects, or actions from that exact scene.\n" +
        "- Queries should work well on Pexels or Pixabay.\n" +
        "- Prefer portrait-friendly compositions when possible.\n" +
        "- Do not include numbering, explanations, quotation marks, or headings.\n" +
        "- Return exactly one search query per line.\n\n" +
        "Examples of specificity:\n" +
        "Bad: cold weather\n" +
        "Good: Eiffel Tower Paris winter snow cold weather\n\n" +
        "Bad: metal expands\n" +
        "Good: heated iron metal expansion close up engineering\n\n" +
        $"Script:\n{context.Script}";

    private static string AnchorGeneratedQuery(string query, NativeProductionContext context)
    {
        var anchor = string.IsNullOrWhiteSpace(context.Project.Category) ? context.Topic : context.Project.Category;
        if (string.IsNullOrWhiteSpace(anchor) || query.StartsWith(anchor, StringComparison.OrdinalIgnoreCase))
            return query;
        return $"{anchor} {query}".Trim();
    }

    private static string AnchorImportedQuery(string query, NativeProductionContext context)
    {
        var category = (context.Project.Category ?? "").Trim();
        var subject = TopicSubject(context.Topic, category);
        var words = RelevanceWords(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pieces = new List<string>();
        if (category.Length > 0 && !query.StartsWith(category, StringComparison.OrdinalIgnoreCase))
            pieces.Add(category);
        if (subject.Length > 0 && !words.Contains(subject))
            pieces.Add(subject);
        pieces.Add(query);
        return string.Join(" ", pieces).Trim();
    }

    private static string FallbackVisualQuery(string scene, NativeProductionContext context)
    {
        var subject = TopicSubject(context.Topic, context.Project.Category);
        var category = (context.Project.Category ?? "").Trim();
        var words = RelevanceWords(scene).Take(8).ToArray();
        return string.Join(" ", new[] { category, subject, string.Join(" ", words) }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
    }

    private static string TopicSubject(string topic, string category)
    {
        var categoryWords = RelevanceWords(category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SearchWord.Matches(topic ?? ""))
        {
            var word = match.Value;
            if (word.Length < 3 || TopicStopWords.Contains(word) || categoryWords.Contains(word))
                continue;
            return word;
        }
        return "";
    }

    private static IEnumerable<string> RelevanceWords(string value)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SearchWord.Matches((value ?? "").ToLowerInvariant()))
        {
            var word = match.Value;
            if (word.Length < 3 || TopicStopWords.Contains(word) || !seen.Add(word))
                continue;
            yield return word;
        }
    }

    private static bool ApplyProjectSceneTimings(NativeTimeline timeline, string onscreenText, List<string> warnings)
    {
        var text = (onscreenText ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        var timings = new List<(double Start, double End)>();
        foreach (Match match in SceneTimingPattern.Matches(text))
        {
            var start = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var end = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (end <= start)
                return false;
            timings.Add((start, end));
        }
        if (timings.Count == 0)
            return false;
        if (timings.Count != timeline.Scenes.Count)
        {
            warnings.Add(
                $"Imported on-screen timings were ignored because they do not match the timeline scene count ({timings.Count} timing range(s), {timeline.Scenes.Count} scene(s))");
            return false;
        }
        double? previousEnd = null;
        foreach (var timing in timings)
        {
            if (previousEnd is not null && timing.Start < previousEnd.Value)
            {
                warnings.Add("Imported on-screen timings were ignored because ranges overlap");
                return false;
            }
            previousEnd = timing.End;
        }
        for (var index = 0; index < timeline.Scenes.Count; index++)
        {
            timeline.Scenes[index].Start = timings[index].Start;
            timeline.Scenes[index].Duration = timings[index].End - timings[index].Start;
            timeline.Scenes[index].Metadata["timing_source"] = "imported_on_screen_text";
        }
        timeline.Metadata["scene_timing_source"] = "imported_on_screen_text";
        return true;
    }

    private static string MetadataString(Dictionary<string, object?> metadata, string key, string fallback)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
            return fallback;
        if (raw is JsonElement element)
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? fallback : element.ToString();
        return raw.ToString() ?? fallback;
    }

    private static string StageCompletedMessage(string stage, bool restored) =>
        restored ? "Restored from checkpoint" : stage switch
        {
            "research" or "facts" or "script" => "Using project content",
            "image_prompts" => "Visuals ready",
            "voice" => "Narration ready",
            "timeline" => "Timeline ready",
            "resolve" => "Resolve export ready",
            _ => "Complete",
        };

    public static bool IsStage(string stage) => Stages.Contains(stage, StringComparer.Ordinal);

    public static int StageIndex(string stage)
    {
        for (var index = 0; index < Stages.Count; index++)
            if (string.Equals(Stages[index], stage, StringComparison.Ordinal))
                return index;
        return int.MaxValue;
    }

    public static void RebaseProjectPaths(string oldFolder, string newFolder)
    {
        oldFolder = System.IO.Path.GetFullPath(oldFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        newFolder = System.IO.Path.GetFullPath(newFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var store = new NativeProjectTimelineStore(newFolder);
        if (!store.Exists())
            return;

        var timeline = store.Load();
        foreach (var clip in timeline.Tracks.SelectMany(track => track.Clips))
        {
            if (!string.IsNullOrWhiteSpace(clip.Source))
                clip.Source = RebasePath(clip.Source!, oldFolder, newFolder);
        }

        var assignments = new NativeTimelineAssetAssignmentEngine(timeline);
        foreach (var scene in timeline.Scenes)
        {
            var assets = assignments.AssetsForScene(scene.Id).Select(asset => asset.Clone()).ToList();
            var changed = false;
            foreach (var asset in assets)
            {
                if (string.IsNullOrWhiteSpace(asset.Path))
                    continue;
                var rebased = RebasePath(asset.Path!, oldFolder, newFolder);
                if (!string.Equals(rebased, asset.Path, StringComparison.OrdinalIgnoreCase))
                {
                    asset.Path = rebased;
                    changed = true;
                }
            }
            if (changed)
                scene.Metadata[NativeTimelineAssetAssignmentEngine.MetadataKey] = assets;
        }
        store.Save(timeline);
    }

    private static string RebasePath(string value, string oldFolder, string newFolder)
    {
        if (!System.IO.Path.IsPathRooted(value))
            return value;
        var path = System.IO.Path.GetFullPath(value);
        if (!path.Equals(oldFolder, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(oldFolder + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return path;
        var relative = System.IO.Path.GetRelativePath(oldFolder, path);
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(newFolder, relative));
    }
}
