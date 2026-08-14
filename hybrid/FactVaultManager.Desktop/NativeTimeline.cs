using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public enum NativeTimelineTrackKind
{
    Video,
    Audio,
    Subtitle,
    Marker,
}

public enum NativeTimelineClipKind
{
    Image,
    Video,
    Audio,
    Subtitle,
    Marker,
}

public sealed class NativeTimelineTransition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "cut";

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    public void Validate()
    {
        if (Duration < 0)
            throw new InvalidDataException("transition duration cannot be negative");
    }
}

public sealed class NativeTimelineClip
{
    [JsonPropertyName("kind")]
    public NativeTimelineClipKind Kind { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("source_in")]
    public double SourceIn { get; set; }

    [JsonPropertyName("transition_in")]
    public NativeTimelineTransition? TransitionIn { get; set; }

    [JsonPropertyName("transition_out")]
    public NativeTimelineTransition? TransitionOut { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    [JsonPropertyName("id")]
    public string Id { get; set; } = NewId();

    [JsonIgnore]
    public double End => Start + Duration;

    public void Validate()
    {
        if (Start < 0)
            throw new InvalidDataException("clip start cannot be negative");
        if (Duration <= 0)
            throw new InvalidDataException("clip duration must be greater than zero");
        if (SourceIn < 0)
            throw new InvalidDataException("clip source_in cannot be negative");
        if (string.IsNullOrWhiteSpace(Id))
            Id = NewId();
        TransitionIn?.Validate();
        TransitionOut?.Validate();
        Metadata ??= new Dictionary<string, object?>();
    }

    internal static string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class NativeTimelineTrack
{
    [JsonPropertyName("kind")]
    public NativeTimelineTrackKind Kind { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("clips")]
    public List<NativeTimelineClip> Clips { get; set; } = new();

    [JsonPropertyName("id")]
    public string Id { get; set; } = NativeTimelineClip.NewId();

    [JsonIgnore]
    public double Duration => Clips.Count == 0 ? 0 : Clips.Max(clip => clip.End);

    public NativeTimelineClip AddClip(NativeTimelineClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        clip.Validate();
        Clips.Add(clip);
        Clips.Sort((left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : string.CompareOrdinal(left.Id, right.Id);
        });
        return clip;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = NativeTimelineClip.NewId();
        Clips ??= new List<NativeTimelineClip>();
        foreach (var clip in Clips)
            clip.Validate();
    }
}

public sealed class NativeTimelineScene
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("narration")]
    public string Narration { get; set; } = "";

    [JsonPropertyName("clip_ids")]
    public List<string> ClipIds { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    [JsonPropertyName("id")]
    public string Id { get; set; } = NativeTimelineClip.NewId();

    [JsonIgnore]
    public double End => Start + Duration;

    public void Validate()
    {
        if (Start < 0)
            throw new InvalidDataException("scene start cannot be negative");
        if (Duration <= 0)
            throw new InvalidDataException("scene duration must be greater than zero");
        if (string.IsNullOrWhiteSpace(Id))
            Id = NativeTimelineClip.NewId();
        ClipIds ??= new List<string>();
        Metadata ??= new Dictionary<string, object?>();
    }
}

public sealed class NativeTimeline
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Fact video";

    [JsonPropertyName("frame_rate")]
    public double FrameRate { get; set; } = 30;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;

    [JsonPropertyName("tracks")]
    public List<NativeTimelineTrack> Tracks { get; set; } = new();

    [JsonPropertyName("scenes")]
    public List<NativeTimelineScene> Scenes { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = NativeTimelineClip.NewId();

    [JsonIgnore]
    public double Duration
    {
        get
        {
            var trackEnd = Tracks.Count == 0 ? 0 : Tracks.Max(track => track.Duration);
            var sceneEnd = Scenes.Count == 0 ? 0 : Scenes.Max(scene => scene.End);
            return Math.Max(trackEnd, sceneEnd);
        }
    }

    public NativeTimelineTrack AddTrack(NativeTimelineTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        track.Validate();
        Tracks.Add(track);
        return track;
    }

    public NativeTimelineScene AddScene(NativeTimelineScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Validate();
        Scenes.Add(scene);
        Scenes.Sort((left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : string.CompareOrdinal(left.Id, right.Id);
        });
        return scene;
    }

    public NativeTimelineTrack? GetTrack(string name) =>
        Tracks.FirstOrDefault(track => string.Equals(track.Name, name, StringComparison.Ordinal));

    public NativeTimeline Clone()
    {
        var json = JsonSerializer.Serialize(this, NativeProjectTimelineStore.SerializerOptions);
        return JsonSerializer.Deserialize<NativeTimeline>(json, NativeProjectTimelineStore.SerializerOptions)
            ?? throw new InvalidDataException("could not clone timeline");
    }

    public void Validate()
    {
        if (FrameRate <= 0)
            throw new InvalidDataException("frame_rate must be greater than zero");
        if (Width <= 0 || Height <= 0)
            throw new InvalidDataException("timeline dimensions must be greater than zero");
        if (string.IsNullOrWhiteSpace(Id))
            Id = NativeTimelineClip.NewId();
        Tracks ??= new List<NativeTimelineTrack>();
        Scenes ??= new List<NativeTimelineScene>();
        Metadata ??= new Dictionary<string, object?>();
        foreach (var track in Tracks)
            track.Validate();
        foreach (var scene in Scenes)
            scene.Validate();
    }
}

public sealed class NativeTimelineStorageException : Exception
{
    public NativeTimelineStorageException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class NativeProjectTimelineStore
{
    public const string TimelineFilename = "timeline.json";

    internal static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public string ProjectFolder { get; }
    public string Path { get; }

    public NativeProjectTimelineStore(string projectFolder, string filename = TimelineFilename)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("project folder is required", nameof(projectFolder));
        ProjectFolder = System.IO.Path.GetFullPath(projectFolder);
        Path = System.IO.Path.Combine(ProjectFolder, filename);
    }

    public bool Exists() => File.Exists(Path);

    public NativeTimeline Create(string name, bool overwrite = false, double frameRate = 30, int width = 1920, int height = 1080)
    {
        if (Exists() && !overwrite)
            return Load();
        var timeline = new NativeTimeline
        {
            Name = name,
            FrameRate = frameRate,
            Width = width,
            Height = height,
        };
        Save(timeline);
        return timeline;
    }

    public NativeTimeline Ensure(string name, double frameRate = 30, int width = 1920, int height = 1080) =>
        Exists() ? Load() : Create(name, frameRate: frameRate, width: width, height: height);

    public NativeTimeline Load()
    {
        if (!Exists())
            throw new FileNotFoundException("timeline file not found", Path);
        try
        {
            var payload = File.ReadAllText(Path);
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new NativeTimelineStorageException("timeline.json must contain a JSON object");
            var timeline = JsonSerializer.Deserialize<NativeTimeline>(payload, SerializerOptions)
                ?? throw new NativeTimelineStorageException($"invalid timeline data: {Path}");
            timeline.Validate();
            return timeline;
        }
        catch (NativeTimelineStorageException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new NativeTimelineStorageException($"could not read timeline: {Path}", error);
        }
    }

    public string Save(NativeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timeline.Validate();
        Directory.CreateDirectory(ProjectFolder);
        var serialized = JsonSerializer.Serialize(timeline, SerializerOptions) + Environment.NewLine;
        var temporary = System.IO.Path.Combine(
            ProjectFolder,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                writer.Write(serialized.Replace("\r\n", "\n", StringComparison.Ordinal));
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, Path, true);
            return Path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw new NativeTimelineStorageException($"could not save timeline: {Path}", error);
        }
    }
}

public sealed class NativeTimelineBuilder
{
    public NativeTimeline Timeline { get; }

    public NativeTimelineBuilder(string name, double frameRate = 30, int width = 1920, int height = 1080)
    {
        Timeline = new NativeTimeline
        {
            Name = name,
            FrameRate = frameRate,
            Width = width,
            Height = height,
        };
        Timeline.Validate();
    }

    public NativeTimelineTrack Track(string name, NativeTimelineTrackKind kind)
    {
        var existing = Timeline.GetTrack(name);
        if (existing is not null)
        {
            if (existing.Kind != kind)
                throw new InvalidOperationException($"track '{name}' already exists with kind '{existing.Kind.ToString().ToLowerInvariant()}'");
            return existing;
        }
        return Timeline.AddTrack(new NativeTimelineTrack { Name = name, Kind = kind });
    }

    public NativeTimelineClip AddClip(
        string trackName,
        NativeTimelineTrackKind trackKind,
        NativeTimelineClipKind clipKind,
        double start,
        double duration,
        string? source = null,
        string name = "",
        double sourceIn = 0,
        Dictionary<string, object?>? metadata = null)
    {
        var clip = new NativeTimelineClip
        {
            Kind = clipKind,
            Start = start,
            Duration = duration,
            Source = source,
            Name = name,
            SourceIn = sourceIn,
            Metadata = metadata is null ? new() : new(metadata),
        };
        return Track(trackName, trackKind).AddClip(clip);
    }

    public NativeTimelineScene AddScene(
        string title,
        double start,
        double duration,
        string narration = "",
        IEnumerable<string>? clipIds = null,
        Dictionary<string, object?>? metadata = null)
    {
        return Timeline.AddScene(new NativeTimelineScene
        {
            Title = title,
            Start = start,
            Duration = duration,
            Narration = narration,
            ClipIds = clipIds?.ToList() ?? new(),
            Metadata = metadata is null ? new() : new(metadata),
        });
    }
}

public sealed class NativeSceneBuilder
{
    private static readonly Regex ParagraphBreak = new("\\n\\s*\\n+", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex Word = new("\\b[\\w'-]+\\b", RegexOptions.Compiled);

    public double WordsPerMinute { get; }
    public double MinimumSceneDuration { get; }
    public int TimingPrecision { get; }

    public NativeSceneBuilder(double wordsPerMinute = 150, double minimumSceneDuration = 1, int timingPrecision = 3)
    {
        if (wordsPerMinute <= 0) throw new ArgumentException("words_per_minute must be greater than zero", nameof(wordsPerMinute));
        if (minimumSceneDuration <= 0) throw new ArgumentException("minimum_scene_duration must be greater than zero", nameof(minimumSceneDuration));
        if (timingPrecision < 0) throw new ArgumentException("timing_precision cannot be negative", nameof(timingPrecision));
        WordsPerMinute = wordsPerMinute;
        MinimumSceneDuration = minimumSceneDuration;
        TimingPrecision = timingPrecision;
    }

    public IReadOnlyList<string> SplitScript(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var normalized = script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0) return Array.Empty<string>();
        return ParagraphBreak.Split(normalized)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .Select(paragraph => Whitespace.Replace(paragraph, " ").Trim())
            .ToArray();
    }

    public double EstimateDuration(string narration)
    {
        ArgumentNullException.ThrowIfNull(narration);
        var wordCount = Word.Matches(narration).Count;
        var duration = wordCount * 60.0 / WordsPerMinute;
        return Math.Round(Math.Max(MinimumSceneDuration, duration), TimingPrecision, MidpointRounding.ToEven);
    }

    public NativeTimeline Build(string script, string name = "Fact video", double frameRate = 30, int width = 1920, int height = 1080)
    {
        var timeline = new NativeTimeline
        {
            Name = name,
            FrameRate = frameRate,
            Width = width,
            Height = height,
            Tracks =
            [
                new() { Name = "Video 1", Kind = NativeTimelineTrackKind.Video },
                new() { Name = "Narration", Kind = NativeTimelineTrackKind.Audio },
                new() { Name = "Subtitles", Kind = NativeTimelineTrackKind.Subtitle },
                new() { Name = "Markers", Kind = NativeTimelineTrackKind.Marker },
            ],
            Metadata = new()
            {
                ["generated_from"] = "script",
                ["words_per_minute"] = WordsPerMinute,
            },
        };

        var cursor = 0.0;
        var index = 0;
        foreach (var narration in SplitScript(script))
        {
            index++;
            var duration = EstimateDuration(narration);
            timeline.AddScene(new NativeTimelineScene
            {
                Title = $"Scene {index}",
                Start = Math.Round(cursor, TimingPrecision, MidpointRounding.ToEven),
                Duration = duration,
                Narration = narration,
                Metadata = new()
                {
                    ["scene_number"] = index,
                    ["word_count"] = Word.Matches(narration).Count,
                    ["visuals"] = Array.Empty<object>(),
                    ["keywords"] = Array.Empty<object>(),
                    ["subtitle_text"] = narration,
                    ["transition"] = "cut",
                    ["notes"] = "",
                },
            });
            cursor = Math.Round(cursor + duration, TimingPrecision, MidpointRounding.ToEven);
        }
        timeline.Validate();
        return timeline;
    }

    public NativeTimeline BuildAndSave(string projectFolder, string script, string name = "Fact video", double frameRate = 30, int width = 1920, int height = 1080)
    {
        var timeline = Build(script, name, frameRate, width, height);
        new NativeProjectTimelineStore(projectFolder).Save(timeline);
        return timeline;
    }
}
