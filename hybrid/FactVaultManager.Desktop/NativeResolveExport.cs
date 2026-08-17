using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FactVaultManager.Desktop;

public sealed class NativeResolveExportException : Exception
{
    public NativeResolveExportException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record NativeFcpXmlExportResult(string Path, int MediaCount, int ClipCount);

public sealed record NativePortableResolvePackageResult(
    string PackageFolder,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> CopiedMedia,
    IReadOnlyList<string> Warnings,
    string TimelinePlan,
    string Manifest,
    IReadOnlyDictionary<string, string> SourceMap);

public sealed record NativeResolveFreeExportResult(
    NativePortableResolvePackageResult Package,
    NativeFcpXmlExportResult FcpXml,
    int RemappedMedia,
    IReadOnlyList<string> ValidatedMedia,
    string ImportReadme);

public static class NativeResolveTimelineAdapter
{
    public static IReadOnlyList<string> Validate(
        NativeTimeline timeline,
        string? projectFolder = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var issues = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var root = string.IsNullOrWhiteSpace(projectFolder) ? null : Path.GetFullPath(projectFolder);

        foreach (var track in timeline.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (!seenIds.Add(clip.Id))
                    issues.Add($"duplicate clip id: {clip.Id}");

                var fileBacked = clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video or NativeTimelineClipKind.Audio;
                if (fileBacked && string.IsNullOrWhiteSpace(clip.Source))
                    issues.Add($"clip has no source: {clip.Id}");

                if (fileBacked && !string.IsNullOrWhiteSpace(clip.Source) && root is not null)
                {
                    var candidate = ResolveSource(clip.Source!, root);
                    if (!File.Exists(candidate))
                        issues.Add($"clip source does not exist: {clip.Source}");
                }

                var compatible = track.Kind switch
                {
                    NativeTimelineTrackKind.Video => clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video,
                    NativeTimelineTrackKind.Audio => clip.Kind == NativeTimelineClipKind.Audio,
                    NativeTimelineTrackKind.Subtitle => clip.Kind == NativeTimelineClipKind.Subtitle,
                    NativeTimelineTrackKind.Marker => clip.Kind == NativeTimelineClipKind.Marker,
                    _ => false,
                };
                if (!compatible)
                    issues.Add($"clip {clip.Id} is incompatible with {KindName(track.Kind)} track {track.Name}");
            }
        }

        return issues;
    }

    public static Dictionary<string, object?> BuildPlan(
        NativeTimeline timeline,
        string? projectFolder = null,
        bool strict = true,
        string? relativeSourceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var issues = Validate(timeline, projectFolder).ToList();
        if (strict && issues.Count > 0)
            throw new NativeResolveExportException(string.Join("; ", issues));

        var root = string.IsNullOrWhiteSpace(projectFolder) ? null : Path.GetFullPath(projectFolder);
        var relativeRoot = string.IsNullOrWhiteSpace(relativeSourceRoot) ? null : Path.GetFullPath(relativeSourceRoot);
        var tracks = new List<Dictionary<string, object?>>();

        for (var index = 0; index < timeline.Tracks.Count; index++)
        {
            var track = timeline.Tracks[index];
            var clips = new List<Dictionary<string, object?>>();
            foreach (var clip in track.Clips)
            {
                string source = "";
                if (!string.IsNullOrWhiteSpace(clip.Source))
                {
                    var resolved = root is null ? Path.GetFullPath(clip.Source!) : ResolveSource(clip.Source!, root);
                    source = relativeRoot is null
                        ? resolved
                        : NormalizeRelative(Path.GetRelativePath(relativeRoot, resolved));
                }

                clips.Add(new Dictionary<string, object?>
                {
                    ["id"] = clip.Id,
                    ["name"] = clip.Name,
                    ["kind"] = KindName(clip.Kind),
                    ["source"] = source,
                    ["start"] = clip.Start,
                    ["duration"] = clip.Duration,
                    ["end"] = clip.End,
                    ["source_in"] = clip.SourceIn,
                    ["transition_in"] = clip.TransitionIn is null ? null : new Dictionary<string, object?>
                    {
                        ["name"] = clip.TransitionIn.Name,
                        ["duration"] = clip.TransitionIn.Duration,
                    },
                    ["transition_out"] = clip.TransitionOut is null ? null : new Dictionary<string, object?>
                    {
                        ["name"] = clip.TransitionOut.Name,
                        ["duration"] = clip.TransitionOut.Duration,
                    },
                    ["metadata"] = clip.Metadata,
                });
            }

            tracks.Add(new Dictionary<string, object?>
            {
                ["id"] = track.Id,
                ["index"] = index + 1,
                ["name"] = track.Name,
                ["kind"] = KindName(track.Kind),
                ["clips"] = clips,
            });
        }

        var scenes = timeline.Scenes.Select(scene => new Dictionary<string, object?>
        {
            ["id"] = scene.Id,
            ["title"] = scene.Title,
            ["start"] = scene.Start,
            ["duration"] = scene.Duration,
            ["end"] = scene.End,
            ["narration"] = scene.Narration,
            ["clip_ids"] = scene.ClipIds,
            ["metadata"] = scene.Metadata,
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["timeline_id"] = timeline.Id,
            ["name"] = timeline.Name,
            ["frame_rate"] = timeline.FrameRate,
            ["resolution"] = new[] { timeline.Width, timeline.Height },
            ["duration"] = timeline.Duration,
            ["tracks"] = tracks,
            ["scenes"] = scenes,
            ["warnings"] = issues,
            ["metadata"] = timeline.Metadata,
        };
    }

    internal static string ResolveSource(string source, string projectFolder)
    {
        var path = source;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(projectFolder, path);
        return Path.GetFullPath(path);
    }

    internal static string KindName(NativeTimelineTrackKind kind) => kind.ToString().ToLowerInvariant();
    internal static string KindName(NativeTimelineClipKind kind) => kind.ToString().ToLowerInvariant();
    internal static string NormalizeRelative(string value) => value.Replace('\\', '/');
}

public static class NativeFcpXmlExporter
{
    public static NativeFcpXmlExportResult Export(
        NativeTimeline timeline,
        string destination,
        string? mediaBase = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.Duration <= 0)
            throw new NativeResolveExportException("timeline must contain at least one timed item");

        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var basePath = string.IsNullOrWhiteSpace(mediaBase) ? null : Path.GetFullPath(mediaBase);
        var fps = timeline.FrameRate;
        var roundedFps = Math.Max(1, (int)Math.Round(fps, MidpointRounding.ToEven));

        var mediaClips = new List<(NativeTimelineTrack Track, NativeTimelineClip Clip, string Source)>();
        foreach (var track in timeline.Tracks)
        {
            if (track.Kind is not (NativeTimelineTrackKind.Video or NativeTimelineTrackKind.Audio))
                continue;
            foreach (var clip in track.Clips)
            {
                if (clip.Kind is not (NativeTimelineClipKind.Image or NativeTimelineClipKind.Video or NativeTimelineClipKind.Audio) ||
                    string.IsNullOrWhiteSpace(clip.Source))
                    continue;
                var source = Path.GetFullPath(clip.Source!);
                if (!File.Exists(source))
                    throw new NativeResolveExportException($"clip source does not exist: {source}");
                mediaClips.Add((track, clip, source));
            }
        }

        var fcpxml = new XElement("fcpxml", new XAttribute("version", "1.10"));
        var resources = new XElement("resources");
        fcpxml.Add(resources);
        resources.Add(new XElement("format",
            new XAttribute("id", "r1"),
            new XAttribute("name", $"FFVideoFormat{timeline.Height}p{roundedFps}"),
            new XAttribute("frameDuration", Time(1.0 / fps, fps)),
            new XAttribute("width", timeline.Width),
            new XAttribute("height", timeline.Height),
            new XAttribute("colorSpace", "1-1-1 (Rec. 709)")));

        var assetIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nextAssetId = 2;
        foreach (var item in mediaClips)
        {
            if (assetIds.ContainsKey(item.Source))
                continue;
            var assetId = $"r{nextAssetId++}";
            assetIds[item.Source] = assetId;
            var asset = new XElement("asset",
                new XAttribute("id", assetId),
                new XAttribute("name", string.IsNullOrWhiteSpace(item.Clip.Name) ? Path.GetFileName(item.Source) : item.Clip.Name),
                new XAttribute("start", "0s"),
                new XAttribute("duration", Time(Math.Max(item.Clip.Duration, timeline.Duration), fps)),
                new XAttribute("hasVideo", item.Clip.Kind == NativeTimelineClipKind.Audio ? "0" : "1"),
                new XAttribute("hasAudio", item.Clip.Kind is NativeTimelineClipKind.Audio or NativeTimelineClipKind.Video ? "1" : "0"));
            asset.Add(new XElement("media-rep",
                new XAttribute("kind", "original-media"),
                new XAttribute("src", FileUrl(item.Source, basePath))));
            resources.Add(asset);
        }

        var library = new XElement("library");
        var eventElement = new XElement("event", new XAttribute("name", "FactVault Exports"));
        var project = new XElement("project", new XAttribute("name", timeline.Name));
        var sequence = new XElement("sequence",
            new XAttribute("duration", Time(timeline.Duration, fps)),
            new XAttribute("format", "r1"),
            new XAttribute("tcStart", "0s"),
            new XAttribute("tcFormat", "NDF"),
            new XAttribute("audioLayout", "stereo"),
            new XAttribute("audioRate", "48k"));
        var spine = new XElement("spine");
        sequence.Add(spine);
        project.Add(sequence);
        eventElement.Add(project);
        library.Add(eventElement);
        fcpxml.Add(library);

        var videoItems = mediaClips.Where(item => item.Track.Kind == NativeTimelineTrackKind.Video).OrderBy(item => item.Clip.Start).ToList();
        var audioItems = mediaClips.Where(item => item.Track.Kind == NativeTimelineTrackKind.Audio).OrderBy(item => item.Clip.Start).ToList();
        var cursor = 0.0;
        var clipCount = 0;

        foreach (var item in videoItems)
        {
            var gapFrames = (int)Math.Round((item.Clip.Start - cursor) * fps, MidpointRounding.ToEven);
            if (gapFrames > 0)
            {
                spine.Add(new XElement("gap",
                    new XAttribute("name", "Gap"),
                    new XAttribute("offset", Time(cursor, fps)),
                    new XAttribute("start", "0s"),
                    new XAttribute("duration", $"{gapFrames}/{roundedFps}s")));
            }

            spine.Add(AssetClip(item.Clip, item.Source, assetIds[item.Source], fps, null));
            var endFrames =
                (int)Math.Round(item.Clip.Start * fps, MidpointRounding.ToEven) +
                (int)Math.Round(item.Clip.Duration * fps, MidpointRounding.ToEven);
            cursor = endFrames / fps;
            clipCount++;
        }

        if (videoItems.Count == 0)
        {
            spine.Add(new XElement("gap",
                new XAttribute("name", "Primary Storyline"),
                new XAttribute("offset", "0s"),
                new XAttribute("start", "0s"),
                new XAttribute("duration", Time(timeline.Duration, fps))));
        }

        var lane = 1;
        foreach (var item in audioItems)
        {
            spine.Add(AssetClip(item.Clip, item.Source, assetIds[item.Source], fps, -lane));
            lane++;
            clipCount++;
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), fcpxml);
        document.Save(destination, SaveOptions.None);
        return new NativeFcpXmlExportResult(destination, assetIds.Count, clipCount);
    }

    public static IReadOnlyList<string> ValidateMedia(
        string fcpxmlPath,
        string packageFolder,
        IEnumerable<string>? expectedMedia = null)
    {
        fcpxmlPath = Path.GetFullPath(fcpxmlPath);
        var packageRoot = Path.GetFullPath(packageFolder);
        var mediaRoot = Path.Combine(packageRoot, "Media");
        var failures = new List<string>();
        var validated = new List<string>();
        XDocument document;
        try
        {
            document = XDocument.Load(fcpxmlPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new NativeResolveExportException($"Could not validate FCPXML: {fcpxmlPath}", error);
        }

        foreach (var asset in document.Root?.Element("resources")?.Elements("asset") ?? Enumerable.Empty<XElement>())
        {
            var src = ((string?)asset.Elements("media-rep")
                .FirstOrDefault(element => string.Equals((string?)element.Attribute("kind"), "original-media", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("src") ??
                (string?)asset.Attribute("src") ?? "").Trim();
            if (src.Length == 0)
            {
                failures.Add("FCPXML asset is missing its original media src path");
                continue;
            }

            string path;
            try
            {
                path = PathFromAssetSource(src, fcpxmlPath);
            }
            catch (NativeResolveExportException error)
            {
                failures.Add(error.Message);
                continue;
            }

            if (!IsWithinDirectory(path, mediaRoot))
            {
                failures.Add($"Asset is outside portable package Media folder: {path}");
                continue;
            }
            if (!File.Exists(path))
            {
                failures.Add($"Asset does not exist: {path}");
                continue;
            }
            validated.Add(Path.GetFullPath(path));
        }

        if (expectedMedia is not null)
        {
            var referenced = validated.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var expected in expectedMedia.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!referenced.Contains(expected))
                    failures.Add($"Expected media is not referenced by FCPXML: {expected}");
            }
        }

        if (failures.Count > 0)
            throw new NativeResolveExportException("FCPXML media validation failed:\n" + string.Join("\n", failures));
        return validated;
    }

    private static XElement AssetClip(NativeTimelineClip clip, string source, string assetId, double fps, int? lane)
    {
        var element = new XElement("asset-clip",
            new XAttribute("name", string.IsNullOrWhiteSpace(clip.Name) ? Path.GetFileName(source) : clip.Name),
            new XAttribute("ref", assetId),
            new XAttribute("offset", Time(clip.Start, fps)),
            new XAttribute("start", Time(clip.SourceIn, fps)),
            new XAttribute("duration", Time(clip.Duration, fps)));
        if (lane is not null)
            element.Add(new XAttribute("lane", lane.Value));
        return element;
    }

    private static string Time(double seconds, double fps)
    {
        var frames = Math.Max(0, (int)Math.Round(seconds * fps, MidpointRounding.ToEven));
        var denominator = Math.Max(1, (int)Math.Round(fps, MidpointRounding.ToEven));
        return $"{frames}/{denominator}s";
    }

    private static string FileUrl(string path, string? relativeTo)
    {
        var resolved = Path.GetFullPath(path);
        if (relativeTo is not null && !IsWithinDirectory(resolved, relativeTo))
            throw new NativeResolveExportException($"clip source is outside the requested portable media base: {resolved}");
        return new Uri(resolved).AbsoluteUri;
    }

    private static string PathFromAssetSource(string value, string fcpxmlPath)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                throw new NativeResolveExportException($"Unsupported FCPXML media URI scheme: {uri.Scheme}");
            return Path.GetFullPath(uri.LocalPath);
        }
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fcpxmlPath)!, Uri.UnescapeDataString(value)));
    }

    internal static bool IsWithinDirectory(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }
}

public static class NativePortableResolvePackageExporter
{
    public static NativePortableResolvePackageResult Export(
        NativeTimeline timeline,
        string projectFolder,
        IReadOnlyDictionary<string, string>? projectMetadata = null,
        bool strict = true,
        bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        projectFolder = Path.GetFullPath(projectFolder);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException($"Project folder could not be found: {projectFolder}");

        var issues = NativeResolveTimelineAdapter.Validate(timeline, projectFolder).ToList();
        if (strict && issues.Count > 0)
            throw new NativeResolveExportException(string.Join("; ", issues));

        var title = ProjectValue(projectMetadata, "title", timeline.Name);
        var packageName = SafeName(title, "Resolve Package");
        var packageFolder = Path.Combine(projectFolder, "Resolve", "Portable", packageName);
        if (Directory.Exists(packageFolder))
        {
            if (!overwrite)
                throw new IOException($"Portable package already exists: {packageFolder}");
            Directory.Delete(packageFolder, true);
        }

        var mediaRoot = Path.Combine(packageFolder, "Media");
        var metadataRoot = Path.Combine(packageFolder, "Metadata");
        var subtitlesRoot = Path.Combine(packageFolder, "Subtitles");
        Directory.CreateDirectory(mediaRoot);
        Directory.CreateDirectory(metadataRoot);
        Directory.CreateDirectory(subtitlesRoot);

        var warnings = new List<string>(issues);
        var copied = new List<string>();
        var copiedBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifestItems = new List<Dictionary<string, object?>>();
        var portable = timeline.Clone();

        foreach (var track in portable.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Kind is not (NativeTimelineClipKind.Image or NativeTimelineClipKind.Video or NativeTimelineClipKind.Audio))
                    continue;
                if (string.IsNullOrWhiteSpace(clip.Source))
                    continue;

                var source = NativeResolveTimelineAdapter.ResolveSource(clip.Source!, projectFolder);
                if (!File.Exists(source))
                {
                    var message = $"Missing media for clip {clip.Id}: {source}";
                    if (strict)
                        throw new NativeResolveExportException(message);
                    warnings.Add(message);
                    continue;
                }

                if (!copiedBySource.TryGetValue(source, out var destination))
                {
                    var category = clip.Kind switch
                    {
                        NativeTimelineClipKind.Image => "Images",
                        NativeTimelineClipKind.Video => "Video",
                        NativeTimelineClipKind.Audio => "Audio",
                        _ => "Other",
                    };
                    var folder = Path.Combine(mediaRoot, category);
                    Directory.CreateDirectory(folder);
                    destination = UniqueDestination(folder, source, clip.Id);
                    File.Copy(source, destination, false);
                    copiedBySource[source] = destination;
                    copied.Add(destination);
                    manifestItems.Add(new Dictionary<string, object?>
                    {
                        ["source"] = source,
                        ["package_path"] = NativeResolveTimelineAdapter.NormalizeRelative(Path.GetRelativePath(packageFolder, destination)),
                        ["size_bytes"] = new FileInfo(destination).Length,
                        ["sha256"] = Checksum(destination),
                    });
                }

                clip.Source = destination;
            }
        }

        var plan = NativeResolveTimelineAdapter.BuildPlan(
            portable,
            packageFolder,
            strict: false,
            relativeSourceRoot: packageFolder);
        var planPath = Path.Combine(packageFolder, "resolve_timeline_plan.json");
        WriteJson(planPath, plan);

        var subtitlePath = Path.Combine(subtitlesRoot, "captions.srt");
        var subtitleCount = WriteSubtitles(portable, subtitlePath);

        var metadata = new Dictionary<string, object?>
        {
            ["title"] = ProjectValue(projectMetadata, "title"),
            ["description"] = ProjectValue(projectMetadata, "description"),
            ["pinned_comment"] = ProjectValue(projectMetadata, "pinned_comment"),
            ["script"] = ProjectValue(projectMetadata, "script"),
            ["sources"] = ProjectValue(projectMetadata, "sources"),
            ["subtitle_count"] = subtitleCount,
        };
        var metadataPath = Path.Combine(metadataRoot, "project_metadata.json");
        WriteJson(metadataPath, metadata);
        foreach (var key in new[] { "title", "description", "pinned_comment", "script", "sources" })
            File.WriteAllText(Path.Combine(metadataRoot, key + ".txt"), Convert.ToString(metadata[key]) ?? "", new UTF8Encoding(false));

        var manifest = new Dictionary<string, object?>
        {
            ["format"] = "factvault-resolve-package",
            ["version"] = 1,
            ["project"] = packageName,
            ["media"] = manifestItems,
            ["warnings"] = warnings,
        };
        var manifestPath = Path.Combine(packageFolder, "package_manifest.json");
        WriteJson(manifestPath, manifest);
        ValidateManifest(manifestPath, packageFolder);

        var readme = Path.Combine(packageFolder, "README.txt");
        File.WriteAllText(readme,
            "Portable DaVinci Resolve Package\n" +
            "=================================\n\n" +
            "This folder is self-contained. Keep its folder structure unchanged.\n\n" +
            "Recommended workflow for DaVinci Resolve Free:\n" +
            "1. Open DaVinci Resolve.\n" +
            "2. Choose File > Import > Timeline.\n" +
            "3. Select the .fcpxml file in this folder.\n\n" +
            "No external scripting connection is required for the normal import workflow.\n" +
            "Media referenced by the FCPXML is stored inside this portable package.\n",
            new UTF8Encoding(false));

        var files = Directory.EnumerateFiles(packageFolder, "*", SearchOption.AllDirectories).ToArray();
        return new NativePortableResolvePackageResult(
            packageFolder,
            files,
            copied,
            warnings,
            planPath,
            manifestPath,
            copiedBySource);
    }

    public static IReadOnlyList<string> ValidateManifest(string manifestPath, string packageFolder)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var packageRoot = Path.GetFullPath(packageFolder);
        var mediaRoot = Path.Combine(packageRoot, "Media");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new NativeResolveExportException($"Could not read portable package manifest: {manifestPath}", error);
        }
        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format) || format.GetString() != "factvault-resolve-package")
                throw new NativeResolveExportException("Portable package manifest has an invalid format");
            if (!root.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
                throw new NativeResolveExportException("Portable package manifest media list is invalid");

            var validated = new List<string>();
            var failures = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var item in media.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    failures.Add($"Media entry {index} is invalid");
                    continue;
                }
                var packagePath = item.TryGetProperty("package_path", out var packagePathElement)
                    ? (packagePathElement.GetString() ?? "").Trim()
                    : "";
                if (packagePath.Length == 0)
                {
                    failures.Add($"Media entry {index} has no package_path");
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(packageRoot, packagePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!NativeFcpXmlExporter.IsWithinDirectory(path, mediaRoot))
                {
                    failures.Add($"Manifest media is outside package Media folder: {path}");
                    continue;
                }
                if (!seen.Add(path))
                {
                    failures.Add($"Manifest contains duplicate media path: {packagePath}");
                    continue;
                }
                if (!File.Exists(path))
                {
                    failures.Add($"Manifest media file is missing: {path}");
                    continue;
                }
                if (!item.TryGetProperty("size_bytes", out var sizeElement) || !sizeElement.TryGetInt64(out var expectedSize))
                {
                    failures.Add($"Manifest size is invalid for: {packagePath}");
                    continue;
                }
                var actualSize = new FileInfo(path).Length;
                if (actualSize != expectedSize)
                {
                    failures.Add($"Manifest size mismatch for {packagePath}: expected {expectedSize}, got {actualSize}");
                    continue;
                }
                var expectedHash = item.TryGetProperty("sha256", out var hashElement)
                    ? (hashElement.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                if (expectedHash.Length == 0 || !string.Equals(expectedHash, Checksum(path), StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"Manifest checksum mismatch for: {packagePath}");
                    continue;
                }
                validated.Add(path);
            }

            if (failures.Count > 0)
                throw new NativeResolveExportException("Portable package manifest validation failed:\n" + string.Join("\n", failures));
            return validated;
        }
    }

    private static int WriteSubtitles(NativeTimeline timeline, string path)
    {
        var entries = timeline.Tracks
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind == NativeTimelineClipKind.Subtitle)
            .Select(clip => (Clip: clip, Text: SubtitleText(clip)))
            .Where(item => item.Text.Length > 0)
            .OrderBy(item => item.Clip.Start)
            .ToList();
        var blocks = new List<string>();
        for (var index = 0; index < entries.Count; index++)
        {
            var item = entries[index];
            blocks.Add(
                $"{index + 1}\n" +
                $"{SrtTimestamp(item.Clip.Start)} --> {SrtTimestamp(item.Clip.End)}\n" +
                item.Text + "\n");
        }
        File.WriteAllText(path, string.Join("\n", blocks), new UTF8Encoding(false));
        return entries.Count;
    }

    private static string SubtitleText(NativeTimelineClip clip)
    {
        foreach (var key in new[] { "subtitle_text", "text" })
        {
            if (clip.Metadata.TryGetValue(key, out var value))
            {
                var text = Convert.ToString(value)?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        return clip.Name.Trim();
    }

    private static string SrtTimestamp(double seconds)
    {
        var milliseconds = Math.Max(0, (long)Math.Round(seconds * 1000, MidpointRounding.ToEven));
        var hours = milliseconds / 3_600_000;
        milliseconds %= 3_600_000;
        var minutes = milliseconds / 60_000;
        milliseconds %= 60_000;
        var secs = milliseconds / 1000;
        var millis = milliseconds % 1000;
        return $"{hours:00}:{minutes:00}:{secs:00},{millis:000}";
    }

    private static string UniqueDestination(string folder, string source, string clipId)
    {
        var candidate = Path.Combine(folder, Path.GetFileName(source));
        if (!File.Exists(candidate))
            return candidate;
        var stem = SafeName(Path.GetFileNameWithoutExtension(source), "media");
        var suffix = Path.GetExtension(source);
        var token = string.IsNullOrWhiteSpace(clipId) ? "media" : clipId[..Math.Min(8, clipId.Length)];
        candidate = Path.Combine(folder, $"{stem}_{token}{suffix}");
        var sequence = 2;
        while (File.Exists(candidate))
            candidate = Path.Combine(folder, $"{stem}_{token}_{sequence++}{suffix}");
        return candidate;
    }

    private static string Checksum(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteJson(string path, object payload)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options), new UTF8Encoding(false));
    }

    private static string ProjectValue(IReadOnlyDictionary<string, string>? metadata, string key, string fallback = "")
    {
        if (metadata is not null && metadata.TryGetValue(key, out var value) && value is not null)
            return value;
        return fallback;
    }

    private static string SafeName(string value, string fallback)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? "")
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or ' ' ? character : '_');
        var cleaned = string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}

public sealed class NativeResolveFreeExportService
{
    public NativeResolveFreeExportResult Export(
        NativeTimeline timeline,
        string projectFolder,
        IReadOnlyDictionary<string, string>? projectMetadata = null,
        bool strict = true,
        bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        projectFolder = Path.GetFullPath(projectFolder);
        var package = NativePortableResolvePackageExporter.Export(
            timeline,
            projectFolder,
            projectMetadata,
            strict,
            overwrite);

        var portable = timeline.Clone();
        var missing = new List<string>();
        var remapped = 0;
        foreach (var track in portable.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Kind is not (NativeTimelineClipKind.Image or NativeTimelineClipKind.Video or NativeTimelineClipKind.Audio) ||
                    string.IsNullOrWhiteSpace(clip.Source))
                    continue;
                var original = NativeResolveTimelineAdapter.ResolveSource(clip.Source!, projectFolder);
                if (!package.SourceMap.TryGetValue(original, out var copied) || !File.Exists(copied))
                {
                    missing.Add($"{(string.IsNullOrWhiteSpace(clip.Name) ? clip.Id : clip.Name)}: {original}");
                    continue;
                }
                clip.Source = copied;
                remapped++;
            }
        }
        if (missing.Count > 0)
            throw new NativeResolveExportException("Portable media mapping is incomplete:\n" + string.Join("\n", missing));

        var expectedMedia = portable.Tracks
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video or NativeTimelineClipKind.Audio)
            .Where(clip => !string.IsNullOrWhiteSpace(clip.Source))
            .Select(clip => Path.GetFullPath(clip.Source!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var title = projectMetadata is not null && projectMetadata.TryGetValue("title", out var configuredTitle) && !string.IsNullOrWhiteSpace(configuredTitle)
            ? configuredTitle
            : timeline.Name;
        var fileName = string.Join("_", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (fileName.Length == 0)
            fileName = "FactVault_Export";
        var fcpxmlPath = Path.Combine(package.PackageFolder, fileName + ".fcpxml");
        var fcpxml = NativeFcpXmlExporter.Export(portable, fcpxmlPath, package.PackageFolder);
        var validated = NativeFcpXmlExporter.ValidateMedia(fcpxml.Path, package.PackageFolder, expectedMedia);

        var importReadme = Path.Combine(package.PackageFolder, "IMPORT_IN_RESOLVE_FREE.txt");
        File.WriteAllText(importReadme,
            "DaVinci Resolve Free Import\n" +
            "===========================\n\n" +
            "1. Keep this entire Portable package folder together.\n" +
            "2. Open DaVinci Resolve and create or open a project.\n" +
            "3. Choose File > Import > Timeline.\n" +
            $"4. Select {Path.GetFileName(fcpxml.Path)}.\n" +
            "5. The FCPXML references only files inside this package's Media folder.\n" +
            $"6. Validated media files: {validated.Count}.\n",
            new UTF8Encoding(false));

        return new NativeResolveFreeExportResult(package, fcpxml, remapped, validated, importReadme);
    }
}
