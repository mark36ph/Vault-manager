using FactVaultManager.Desktop;
using System.Xml.Linq;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeResolveExportTests
{
    [Fact]
    public void AdapterReportsMissingSourceAndTrackMismatch()
    {
        var timeline = new NativeTimeline();
        var track = timeline.AddTrack(new NativeTimelineTrack
        {
            Name = "Visuals",
            Kind = NativeTimelineTrackKind.Video,
        });
        track.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Audio,
            Start = 0,
            Duration = 1,
        });

        var issues = NativeResolveTimelineAdapter.Validate(timeline);

        Assert.Contains(issues, issue => issue.Contains("has no source", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("incompatible", StringComparison.Ordinal));
    }

    [Fact]
    public void FcpxmlExportReferencesAndValidatesPortableMedia()
    {
        var root = NewTempFolder();
        try
        {
            var package = Path.Combine(root, "Package");
            var mediaRoot = Path.Combine(package, "Media");
            var imageFolder = Path.Combine(mediaRoot, "Images");
            Directory.CreateDirectory(imageFolder);
            var image = Path.Combine(imageFolder, "frame.jpg");
            File.WriteAllBytes(image, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

            var builder = new NativeTimelineBuilder("Portable", width: 1080, height: 1920);
            builder.AddClip(
                "Visuals",
                NativeTimelineTrackKind.Video,
                NativeTimelineClipKind.Image,
                0,
                2,
                image,
                "Frame");

            var fcpxml = Path.Combine(package, "Portable.fcpxml");
            var result = NativeFcpXmlExporter.Export(builder.Timeline, fcpxml, mediaRoot);
            var validated = NativeFcpXmlExporter.ValidateMedia(fcpxml, package, new[] { image });
            var document = XDocument.Load(fcpxml);
            var asset = Assert.Single(document.Root?.Element("resources")?.Elements("asset") ?? []);
            var mediaRep = Assert.Single(asset.Elements("media-rep"));

            Assert.Equal(1, result.MediaCount);
            Assert.Equal(1, result.ClipCount);
            Assert.Single(validated);
            Assert.Equal("1.10", document.Root?.Attribute("version")?.Value);
            Assert.Equal("fcpxml", document.Root?.Name.LocalName);
            Assert.Null(asset.Attribute("src"));
            Assert.Equal("original-media", mediaRep.Attribute("kind")?.Value);
            Assert.Equal(new Uri(image).AbsoluteUri, mediaRep.Attribute("src")?.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QuizFcpxmlKeepsVideoAndAudioOnOneZeroBasedParent()
    {
        var root = NewTempFolder();
        try
        {
            var package = Path.Combine(root, "Package");
            var mediaRoot = Path.Combine(package, "Media");
            var imageFolder = Path.Combine(mediaRoot, "Images");
            var audioFolder = Path.Combine(mediaRoot, "Audio");
            Directory.CreateDirectory(imageFolder);
            Directory.CreateDirectory(audioFolder);
            var image = Path.Combine(imageFolder, "quizcard.png");
            var audio = Path.Combine(audioFolder, "question.mp3");
            File.WriteAllBytes(image, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            File.WriteAllBytes(audio, new byte[] { 0x49, 0x44, 0x33, 0x03 });

            var timeline = new NativeTimeline
            {
                Name = "Quiz",
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
            };
            timeline.Metadata["content_type"] = "quiz";
            var video = timeline.AddTrack(new NativeTimelineTrack
            {
                Name = "Quiz Cards",
                Kind = NativeTimelineTrackKind.Video,
            });
            video.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = 2,
                Duration = 8,
                Source = image,
                Name = "Question Card",
            });
            var narration = timeline.AddTrack(new NativeTimelineTrack
            {
                Name = "Quiz Narration",
                Kind = NativeTimelineTrackKind.Audio,
            });
            narration.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Audio,
                Start = 2,
                Duration = 4,
                Source = audio,
                Name = "Question Narration",
            });
            timeline.Validate();

            var fcpxml = Path.Combine(package, "Quiz.fcpxml");
            var exported = NativeFcpXmlExporter.Export(timeline, fcpxml, package);
            var sourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [image] = image,
                [audio] = audio,
            };
            var portable = new NativePortableResolvePackageResult(
                package,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "",
                "",
                sourceMap);
            var resolve = new NativeResolveFreeExportResult(
                portable,
                exported,
                0,
                new[] { image, audio },
                "");

            QuizFcpXmlTimelineSynchronizer.AlignToTimeline(resolve, timeline);

            var document = XDocument.Load(fcpxml);
            var sequence = document.Root?.Element("library")?.Element("event")?.Element("project")?.Element("sequence");
            var spine = sequence?.Element("spine");
            var parent = Assert.Single(spine?.Elements("gap") ?? []);
            var clips = parent.Elements("asset-clip").ToList();
            Assert.Equal("Quiz Timeline", parent.Attribute("name")?.Value);
            Assert.Equal("0s", parent.Attribute("offset")?.Value);
            Assert.Equal(2, clips.Count);
            Assert.Equal("1", clips.Single(clip => clip.Attribute("name")?.Value == "Question Card").Attribute("lane")?.Value);
            var narrationClip = clips.Single(clip => clip.Attribute("name")?.Value == "Question Narration");
            Assert.Equal("-1", narrationClip.Attribute("lane")?.Value);
            Assert.Equal("1", narrationClip.Attribute("enabled")?.Value);
            Assert.Equal("dialogue", narrationClip.Attribute("audioRole")?.Value);
            Assert.Equal("4dB", narrationClip.Element("adjust-volume")?.Attribute("amount")?.Value);
            Assert.All(clips, clip => Assert.Equal("60/30s", clip.Attribute("offset")?.Value));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StrictPlanRejectsInvalidTimeline()
    {
        var builder = new NativeTimelineBuilder("Invalid");
        builder.AddClip(
            "Visuals",
            NativeTimelineTrackKind.Video,
            NativeTimelineClipKind.Image,
            0,
            1,
            source: null);

        Assert.Throws<NativeResolveExportException>(() =>
            NativeResolveTimelineAdapter.BuildPlan(builder.Timeline, strict: true));
    }

    private static string NewTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
