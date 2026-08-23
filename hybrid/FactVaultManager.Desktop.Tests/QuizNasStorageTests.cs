namespace FactVaultManager.Desktop.Tests;

public sealed class QuizNasStorageTests
{
    [Fact]
    public void Archive_CopiesVerifiesAndRebasesProjectBeforeLocalRemoval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-nas-archive-{Guid.NewGuid():N}");
        var projects = Path.Combine(root, "local-projects");
        var archive = Path.Combine(root, "nas-archive");
        var source = Path.Combine(projects, "Quizzes", "Space Quiz - Short - 001");
        try
        {
            Directory.CreateDirectory(source);
            var video = Path.Combine(source, "Space Quiz.mp4");
            var manifest = Path.Combine(source, "manifest.json");
            File.WriteAllBytes(video, new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(manifest, $"{{\"video\":\"{JsonPath(video)}\"}}");

            var destination = QuizProjectArchive.CopyAndVerify(source, projects, archive);

            Assert.True(Directory.Exists(source));
            Assert.Equal(
                Path.Combine(archive, "Quizzes", "Space Quiz - Short - 001"),
                destination);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(destination, "Space Quiz.mp4")));
            Assert.Contains(JsonPath(destination), File.ReadAllText(Path.Combine(destination, "manifest.json")),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Publish_CopiesCompletedPackageAndRebasesAllRecordedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-nas-publish-{Guid.NewGuid():N}");
        var session = Path.Combine(root, "staging");
        var projects = Path.Combine(root, "projects");
        try
        {
            var source = Path.Combine(session, "Quizzes", "History Quiz - Short - 001");
            var cards = Path.Combine(source, "Cards");
            var portable = Path.Combine(source, "Resolve", "Portable");
            Directory.CreateDirectory(cards);
            Directory.CreateDirectory(portable);
            Directory.CreateDirectory(Path.Combine(projects, "Quizzes", "History Quiz - Short - 001"));

            var card = Path.Combine(cards, "quizcard.png");
            var quizJson = Path.Combine(source, "quiz.json");
            var timelinePlan = Path.Combine(portable, "timeline-plan.json");
            var manifest = Path.Combine(portable, "manifest.json");
            var readme = Path.Combine(portable, "README.txt");
            var fcpxml = Path.Combine(portable, "timeline.fcpxml");
            File.WriteAllBytes(card, new byte[] { 1, 2, 3 });
            File.WriteAllText(quizJson, "{}");
            File.WriteAllText(timelinePlan, $"{{\"source\":\"{JsonPath(card)}\"}}");
            File.WriteAllText(manifest, $"{{\"root\":\"{JsonPath(source)}\"}}");
            File.WriteAllText(readme, source);
            File.WriteAllText(fcpxml, new Uri(card).AbsoluteUri);

            var timeline = new NativeTimeline { Name = "History Quiz" };
            var track = timeline.AddTrack(new NativeTimelineTrack
            {
                Name = "Quiz Cards",
                Kind = NativeTimelineTrackKind.Video,
            });
            track.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = 0,
                Duration = 1,
                Source = card,
            });

            var package = new NativePortableResolvePackageResult(
                portable,
                new[] { timelinePlan, manifest, readme, fcpxml },
                new[] { card },
                Array.Empty<string>(),
                timelinePlan,
                manifest,
                new Dictionary<string, string> { [card] = card });
            var resolve = new NativeResolveFreeExportResult(
                package,
                new NativeFcpXmlExportResult(fcpxml, 1, 1),
                1,
                new[] { card },
                readme);

            var published = QuizExportStaging.Publish(
                new QuizVideoBuildResult(source, quizJson, timeline, resolve),
                projects,
                session);

            Assert.EndsWith("History Quiz - Short - 002", published.ProjectFolder, StringComparison.Ordinal);
            Assert.True(Directory.Exists(published.ProjectFolder));
            Assert.False(Directory.Exists(session));
            Assert.StartsWith(published.ProjectFolder, published.QuizJson, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(published.ProjectFolder, track.Clips.Single().Source!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(published.ProjectFolder, published.ResolveExport.FcpXml.Path, StringComparison.OrdinalIgnoreCase);
            Assert.All(published.ResolveExport.Package.Files,
                path => Assert.StartsWith(published.ProjectFolder, path, StringComparison.OrdinalIgnoreCase));
            Assert.All(published.ResolveExport.Package.SourceMap,
                pair =>
                {
                    Assert.StartsWith(published.ProjectFolder, pair.Key, StringComparison.OrdinalIgnoreCase);
                    Assert.StartsWith(published.ProjectFolder, pair.Value, StringComparison.OrdinalIgnoreCase);
                });

            foreach (var file in Directory.EnumerateFiles(published.ProjectFolder, "*", SearchOption.AllDirectories)
                         .Where(path => new[] { ".json", ".txt", ".fcpxml" }.Contains(Path.GetExtension(path))))
            {
                Assert.DoesNotContain(source, File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupQueue_DeletesQueuedFolderAndRemovesDurableEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-nas-delete-{Guid.NewGuid():N}");
        var queue = Path.Combine(root, "data", "cleanup.json");
        var folder = Path.Combine(root, "projects", "Quizzes", "Quiz - 001.delete-test");
        try
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "locked-looking.txt");
            File.WriteAllText(file, "delete me");
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

            QuizFolderCleanupQueue.Enqueue(queue, Path.Combine(root, "projects"), folder);

            Assert.True(File.Exists(queue));
            Assert.Equal(1, QuizFolderCleanupQueue.ProcessPending(queue, Path.Combine(root, "projects")));
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(queue));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupQueue_RejectsFoldersOutsideProjectsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-nas-safe-{Guid.NewGuid():N}");
        var projects = Path.Combine(root, "projects");
        var queue = Path.Combine(root, "cleanup.json");

        Assert.Throws<InvalidOperationException>(() =>
            QuizFolderCleanupQueue.Enqueue(queue, projects, root));
        Assert.Throws<InvalidOperationException>(() =>
            QuizFolderCleanupQueue.Enqueue(queue, projects, projects));
    }

    private static string JsonPath(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);
}
