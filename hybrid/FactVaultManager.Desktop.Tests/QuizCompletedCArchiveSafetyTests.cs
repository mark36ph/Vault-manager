namespace FactVaultManager.Desktop.Tests;

public sealed class QuizCompletedCArchiveSafetyTests
{
    [Fact]
    public void AreDirectoriesEquivalent_RequiresCompleteRelativeFileAndSizeMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-archive-eq-" + Guid.NewGuid().ToString("N"));
        var left = Path.Combine(root, "left");
        var right = Path.Combine(root, "right");
        try
        {
            Directory.CreateDirectory(Path.Combine(left, "nested"));
            Directory.CreateDirectory(Path.Combine(right, "nested"));
            File.WriteAllText(Path.Combine(left, "quiz.json"), "same-data");
            File.WriteAllText(Path.Combine(right, "quiz.json"), "same-data");
            File.WriteAllText(Path.Combine(left, "nested", "video.txt"), "12345");
            File.WriteAllText(Path.Combine(right, "nested", "video.txt"), "12345");

            Assert.True(QuizProjectArchive.AreDirectoriesEquivalent(left, right));

            File.WriteAllText(Path.Combine(right, "nested", "video.txt"), "different-size");
            Assert.False(QuizProjectArchive.AreDirectoriesEquivalent(left, right));
        }
        finally
        {
            QuizProjectArchive.TryDelete(root);
        }
    }

    [Fact]
    public void CopyAndVerifyToQuizArchive_DoesNotOverwriteExistingFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-archive-copy-" + Guid.NewGuid().ToString("N"));
        var sourceParent = Path.Combine(root, "source");
        var source = Path.Combine(sourceParent, "Science Quiz - 001");
        var archive = Path.Combine(root, "archive", "Quizzes");
        var existing = Path.Combine(archive, "Science Quiz - 001");
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(source, "quiz.json"), "new-project");
            File.WriteAllText(Path.Combine(existing, "keep.txt"), "old-archive-must-remain");

            var destination = QuizProjectArchive.CopyAndVerifyToQuizArchive(source, archive);

            Assert.NotEqual(existing, destination, ignoreCase: true);
            Assert.Contains("archived-copy 001", destination, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("old-archive-must-remain", File.ReadAllText(Path.Combine(existing, "keep.txt")));
            Assert.Equal("new-project", File.ReadAllText(Path.Combine(destination, "quiz.json")));
        }
        finally
        {
            QuizProjectArchive.TryDelete(root);
        }
    }
}
