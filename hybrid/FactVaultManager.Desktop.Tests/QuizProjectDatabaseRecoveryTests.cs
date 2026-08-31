using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizProjectDatabaseRecoveryTests
{
    [Fact]
    public void Archive_RestoresExactProjectSources_WithoutStoringDerivedVideo()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Cards"));
            Directory.CreateDirectory(Path.Combine(root, "Voice"));
            File.WriteAllText(Path.Combine(root, "quiz.json"), "{\"title\":\"Recovery Quiz\"}", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(root, "Cards", "question-01.png"), [1, 2, 3, 4, 5]);
            File.WriteAllBytes(Path.Combine(root, "Voice", "question-01.wav"), [8, 7, 6, 5, 4, 3]);
            File.WriteAllBytes(Path.Combine(root, "final-video.mp4"), [9, 9, 9]);

            var capture = QuizProjectDatabaseArchive.Capture(root);

            Assert.Equal(3, capture.FileCount);
            Assert.Equal("{\"title\":\"Recovery Quiz\"}", capture.QuizJson);
            Assert.True(capture.ArchiveBytes > 0);
            Assert.Equal(64, capture.Sha256.Length);

            Directory.Delete(root, recursive: true);
            var result = QuizProjectDatabaseArchive.Restore(capture.Archive, capture.Sha256, root);

            Assert.Equal(3, result.RestoredFiles);
            Assert.Equal("{\"title\":\"Recovery Quiz\"}", File.ReadAllText(Path.Combine(root, "quiz.json")));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(Path.Combine(root, "Cards", "question-01.png")));
            Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3 }, File.ReadAllBytes(Path.Combine(root, "Voice", "question-01.wav")));
            Assert.False(File.Exists(Path.Combine(root, "final-video.mp4")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_DoesNotOverwriteExistingFiles_ByDefault()
    {
        var source = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var destination = source + "-restore";
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "quiz.json"), "database copy");
            var capture = QuizProjectDatabaseArchive.Capture(source);

            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "quiz.json"), "current file");
            var result = QuizProjectDatabaseArchive.Restore(capture.Archive, capture.Sha256, destination);

            Assert.Equal(0, result.RestoredFiles);
            Assert.Equal(1, result.SkippedFiles);
            Assert.Equal("current file", File.ReadAllText(Path.Combine(destination, "quiz.json")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public void Restore_RejectsArchivePathTraversal()
    {
        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("../escape.txt");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write("blocked");
            }
            bytes = memory.ToArray();
        }
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var destination = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                QuizProjectDatabaseArchive.Restore(bytes, hash, destination));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.MOV")]
    [InlineData("clip.webm")]
    public void DerivedVideos_AreExcludedFromDatabaseArchive(string fileName)
    {
        Assert.True(QuizProjectDatabaseArchive.IsDerivedVideo(fileName));
    }
}
