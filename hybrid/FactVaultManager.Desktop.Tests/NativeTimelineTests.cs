using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeTimelineTests
{
    [Fact]
    public void ClipValidationRejectsInvalidTiming()
    {
        Assert.Throws<InvalidDataException>(() =>
            new NativeTimelineClip { Start = -1, Duration = 1 }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new NativeTimelineClip { Start = 0, Duration = 0 }.Validate());
    }

    [Fact]
    public void BuilderSortsClipsAndTracksDuration()
    {
        var builder = new NativeTimelineBuilder("Ordering");
        builder.AddClip("Visuals", NativeTimelineTrackKind.Video, NativeTimelineClipKind.Image, 5, 1, "late.jpg");
        builder.AddClip("Visuals", NativeTimelineTrackKind.Video, NativeTimelineClipKind.Image, 1, 2, "early.jpg");

        var track = builder.Timeline.GetTrack("Visuals");
        Assert.NotNull(track);
        Assert.Equal(new[] { 1d, 5d }, track!.Clips.Select(clip => clip.Start).ToArray());
        Assert.Equal(6d, builder.Timeline.Duration);
    }

    [Fact]
    public void TimelineStoreRoundTripsNativeFormat()
    {
        var root = NewTempFolder();
        try
        {
            var builder = new NativeTimelineBuilder("Round trip", width: 1080, height: 1920);
            var clip = builder.AddClip(
                "Visuals",
                NativeTimelineTrackKind.Video,
                NativeTimelineClipKind.Image,
                0,
                2.5,
                "Assets/Images/example.jpg",
                "Example");
            builder.AddScene("Opening", 0, 2.5, "Hello world", new[] { clip.Id });

            var store = new NativeProjectTimelineStore(root);
            var path = store.Save(builder.Timeline);
            var loaded = store.Load();

            Assert.True(File.Exists(path));
            Assert.Equal("Round trip", loaded.Name);
            Assert.Equal(1080, loaded.Width);
            Assert.Equal(1920, loaded.Height);
            Assert.Equal("Assets/Images/example.jpg", loaded.Tracks.Single().Clips.Single().Source);
            Assert.Equal(clip.Id, loaded.Scenes.Single().ClipIds.Single());
            Assert.Contains("\"kind\": \"image\"", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SceneBuilderSplitsParagraphsAndEstimatesDuration()
    {
        var builder = new NativeSceneBuilder(wordsPerMinute: 120, minimumSceneDuration: 1, timingPrecision: 2);

        var scenes = builder.SplitScript("First line.\r\n\r\nSecond    line.");

        Assert.Equal(new[] { "First line.", "Second line." }, scenes);
        Assert.Equal(2d, builder.EstimateDuration("one two three four"));
    }

    private static string NewTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
