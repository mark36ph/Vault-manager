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
