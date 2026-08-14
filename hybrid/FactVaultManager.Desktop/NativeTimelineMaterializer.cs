using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactVaultManager.Desktop;

public enum NativeTimelineAssetKind
{
    Image,
    Video,
    Audio,
    Subtitle,
}

public enum NativeTimelineAssetStatus
{
    Pending,
    Assigned,
    Missing,
}

public sealed class NativeTimelineAsset
{
    [JsonPropertyName("kind")]
    public NativeTimelineAssetKind Kind { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("status")]
    public NativeTimelineAssetStatus Status { get; set; } = NativeTimelineAssetStatus.Pending;

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("credit")]
    public string? Credit { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException("asset id must be a non-empty string");
        if (Duration is < 0)
            throw new InvalidDataException("asset duration cannot be negative");
        Metadata ??= new Dictionary<string, object?>();
    }

    public NativeTimelineAsset Clone()
    {
        var json = JsonSerializer.Serialize(this, NativeProjectTimelineStore.SerializerOptions);
        return JsonSerializer.Deserialize<NativeTimelineAsset>(json, NativeProjectTimelineStore.SerializerOptions)
            ?? throw new InvalidDataException("could not clone timeline asset");
    }
}

public sealed class NativeTimelineAssetAssignmentException : Exception
{
    public NativeTimelineAssetAssignmentException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class NativeTimelineAssetAssignmentEngine
{
    public const string MetadataKey = "assets";

    private readonly NativeTimeline _timeline;

    public NativeTimelineAssetAssignmentEngine(NativeTimeline timeline)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public IReadOnlyList<NativeTimelineAsset> AssetsForScene(string sceneId)
    {
        var scene = Scene(sceneId);
        if (!scene.Metadata.TryGetValue(MetadataKey, out var raw) || raw is null)
            return Array.Empty<NativeTimelineAsset>();

        try
        {
            var element = ToElement(raw);
            if (element.ValueKind != JsonValueKind.Array)
                throw new NativeTimelineAssetAssignmentException($"scene {sceneId} assets must be a list");
            var assets = new List<NativeTimelineAsset>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new NativeTimelineAssetAssignmentException($"scene {sceneId} contains invalid asset data");
                var asset = item.Deserialize<NativeTimelineAsset>(NativeProjectTimelineStore.SerializerOptions)
                    ?? throw new NativeTimelineAssetAssignmentException($"scene {sceneId} contains invalid asset data");
                asset.Validate();
                assets.Add(asset);
            }
            return assets;
        }
        catch (NativeTimelineAssetAssignmentException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new NativeTimelineAssetAssignmentException($"scene {sceneId} contains invalid asset data", error);
        }
    }

    public (NativeTimelineScene Scene, NativeTimelineAsset Asset)? Find(string assetId)
    {
        foreach (var scene in _timeline.Scenes)
        {
            foreach (var asset in AssetsForScene(scene.Id))
            {
                if (string.Equals(asset.Id, assetId, StringComparison.Ordinal))
                    return (scene, asset);
            }
        }
        return null;
    }

    public NativeTimelineAsset Assign(string sceneId, NativeTimelineAsset asset, bool allowDuplicate = false)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.Validate();
        if (Find(asset.Id) is not null && !allowDuplicate)
            throw new NativeTimelineAssetAssignmentException($"asset id already assigned: {asset.Id}");

        var scene = Scene(sceneId);
        var assigned = asset.Clone();
        assigned.Status = NativeTimelineAssetStatus.Assigned;
        var assets = AssetsForScene(sceneId).Select(item => item.Clone()).ToList();
        assets.Add(assigned);
        scene.Metadata[MetadataKey] = assets;
        return assigned;
    }

    public NativeTimelineAsset Remove(string sceneId, string assetId)
    {
        var scene = Scene(sceneId);
        var assets = AssetsForScene(sceneId).Select(item => item.Clone()).ToList();
        var index = assets.FindIndex(asset => string.Equals(asset.Id, assetId, StringComparison.Ordinal));
        if (index < 0)
            throw new NativeTimelineAssetAssignmentException($"asset {assetId} is not assigned to scene {sceneId}");
        var removed = assets[index];
        assets.RemoveAt(index);
        scene.Metadata[MetadataKey] = assets;
        return removed;
    }

    public NativeTimelineAsset Move(string assetId, string targetSceneId)
    {
        var found = Find(assetId)
            ?? throw new NativeTimelineAssetAssignmentException($"unknown asset id: {assetId}");
        Scene(targetSceneId);
        if (string.Equals(found.Scene.Id, targetSceneId, StringComparison.Ordinal))
            return found.Asset;
        Remove(found.Scene.Id, assetId);
        return Assign(targetSceneId, found.Asset);
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in _timeline.Scenes)
        {
            IReadOnlyList<NativeTimelineAsset> assets;
            try
            {
                assets = AssetsForScene(scene.Id);
            }
            catch (NativeTimelineAssetAssignmentException error)
            {
                issues.Add(error.Message);
                continue;
            }

            foreach (var asset in assets)
            {
                if (!seen.Add(asset.Id))
                    issues.Add($"duplicate asset id: {asset.Id}");
                if (asset.Status == NativeTimelineAssetStatus.Assigned && string.IsNullOrWhiteSpace(asset.Path))
                    issues.Add($"assigned asset has no path: {asset.Id}");
            }
        }
        return issues;
    }

    private NativeTimelineScene Scene(string sceneId) =>
        _timeline.Scenes.FirstOrDefault(scene => string.Equals(scene.Id, sceneId, StringComparison.Ordinal))
        ?? throw new NativeTimelineAssetAssignmentException($"unknown scene id: {sceneId}");

    private static JsonElement ToElement(object value)
    {
        if (value is JsonElement element)
            return element;
        return JsonSerializer.SerializeToElement(value, NativeProjectTimelineStore.SerializerOptions);
    }
}

public sealed class NativeTimelineClipMaterializationException : Exception
{
    public NativeTimelineClipMaterializationException(string message) : base(message) { }
}

public sealed class NativeTimelineClipMaterializer
{
    public const string GeneratedBy = "asset_assignment";

    private readonly NativeTimeline _timeline;
    private readonly NativeTimelineAssetAssignmentEngine _assignments;

    public NativeTimelineClipMaterializer(NativeTimeline timeline)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _assignments = new NativeTimelineAssetAssignmentEngine(timeline);
    }

    public IReadOnlyList<NativeTimelineClip> Materialize()
    {
        var validation = _assignments.Validate();
        if (validation.Count > 0)
            throw new NativeTimelineClipMaterializationException(string.Join("; ", validation));

        RemoveGeneratedClips();
        var created = new List<NativeTimelineClip>();
        foreach (var scene in _timeline.Scenes)
        {
            foreach (var asset in _assignments.AssetsForScene(scene.Id))
            {
                if (asset.Status != NativeTimelineAssetStatus.Assigned)
                    continue;
                var track = TrackFor(asset.Kind);
                var clip = BuildClip(scene, asset);
                track.AddClip(clip);
                if (!scene.ClipIds.Contains(clip.Id, StringComparer.Ordinal))
                    scene.ClipIds.Add(clip.Id);
                created.Add(clip);
            }
        }
        return created;
    }

    private NativeTimelineTrack TrackFor(NativeTimelineAssetKind kind)
    {
        var (name, trackKind, _) = TrackDefinition(kind);
        var track = _timeline.GetTrack(name);
        if (track is null)
            return _timeline.AddTrack(new NativeTimelineTrack { Name = name, Kind = trackKind });
        if (track.Kind != trackKind)
            throw new NativeTimelineClipMaterializationException(
                $"track '{name}' has kind {track.Kind.ToString().ToLowerInvariant()}, expected {trackKind.ToString().ToLowerInvariant()}");
        return track;
    }

    private NativeTimelineClip BuildClip(NativeTimelineScene scene, NativeTimelineAsset asset)
    {
        if (asset.Status != NativeTimelineAssetStatus.Assigned)
            throw new NativeTimelineClipMaterializationException(
                $"asset {asset.Id} is not assigned (status: {asset.Status.ToString().ToLowerInvariant()})");
        if (string.IsNullOrWhiteSpace(asset.Path))
            throw new NativeTimelineClipMaterializationException($"assigned asset has no path: {asset.Id}");

        var (_, _, clipKind) = TrackDefinition(asset.Kind);
        var transitionName = MetadataString(scene.Metadata, "transition", "cut");
        var transition = string.Equals(transitionName, "cut", StringComparison.OrdinalIgnoreCase)
            ? null
            : new NativeTimelineTransition { Name = transitionName };
        var metadata = new Dictionary<string, object?>(asset.Metadata, StringComparer.Ordinal)
        {
            ["generated_by"] = GeneratedBy,
            ["asset_id"] = asset.Id,
            ["scene_id"] = scene.Id,
            ["asset_kind"] = asset.Kind.ToString().ToLowerInvariant(),
            ["asset_duration"] = asset.Duration,
            ["source"] = asset.Source,
            ["credit"] = asset.Credit,
            ["license"] = asset.License,
        };

        return new NativeTimelineClip
        {
            Id = Uuid5Hex($"{_timeline.Id}:{scene.Id}:{asset.Id}:{GeneratedBy}"),
            Kind = clipKind,
            Start = scene.Start,
            Duration = scene.Duration,
            Source = asset.Path,
            Name = System.IO.Path.GetFileName(asset.Path),
            TransitionIn = transition,
            Metadata = metadata,
        };
    }

    private void RemoveGeneratedClips()
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in _timeline.Tracks)
        {
            var retained = new List<NativeTimelineClip>();
            foreach (var clip in track.Clips)
            {
                if (string.Equals(MetadataString(clip.Metadata, "generated_by", ""), GeneratedBy, StringComparison.Ordinal))
                    removed.Add(clip.Id);
                else
                    retained.Add(clip);
            }
            track.Clips = retained;
        }
        foreach (var scene in _timeline.Scenes)
            scene.ClipIds = scene.ClipIds.Where(id => !removed.Contains(id)).ToList();
    }

    private static (string TrackName, NativeTimelineTrackKind TrackKind, NativeTimelineClipKind ClipKind) TrackDefinition(
        NativeTimelineAssetKind kind) => kind switch
    {
        NativeTimelineAssetKind.Image => ("Video 1", NativeTimelineTrackKind.Video, NativeTimelineClipKind.Image),
        NativeTimelineAssetKind.Video => ("Video 1", NativeTimelineTrackKind.Video, NativeTimelineClipKind.Video),
        NativeTimelineAssetKind.Audio => ("Narration", NativeTimelineTrackKind.Audio, NativeTimelineClipKind.Audio),
        NativeTimelineAssetKind.Subtitle => ("Subtitles", NativeTimelineTrackKind.Subtitle, NativeTimelineClipKind.Subtitle),
        _ => throw new NativeTimelineClipMaterializationException($"unsupported asset kind: {kind}"),
    };

    private static string MetadataString(Dictionary<string, object?> metadata, string key, string fallback)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
            return fallback;
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? fallback;
            return element.ToString();
        }
        return raw.ToString() ?? fallback;
    }

    private static string Uuid5Hex(string name)
    {
        var namespaceBytes = Convert.FromHexString("6BA7B8119DAD11D180B400C04FD430C8");
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var payload = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, payload, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, payload, namespaceBytes.Length, nameBytes.Length);
        var hash = SHA1.HashData(payload);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
