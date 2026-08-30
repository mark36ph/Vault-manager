using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class LogoQuizProjectArtworkRepairTests
{
    [Fact]
    public void RepairAvailableArtwork_PersistsLogoAndImagePathInsideProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"logo-promo-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source-logo.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(
                Path.Combine(root, "quiz.json"),
                """
                {
                  "title": "Logo Quiz",
                  "quiz_type": "Logo",
                  "questions": [
                    {
                      "number": 1,
                      "id": 42,
                      "question": "Which logo is this?",
                      "answers": ["Facebook", "Instagram", "YouTube", "TikTok"],
                      "correct_index": 1
                    }
                  ]
                }
                """);

            var repaired = LogoQuizProjectArtworkRepair.RepairAvailableArtwork(
                root,
                brand => string.Equals(brand, "Instagram", StringComparison.Ordinal) ? source : null);

            Assert.Equal(1, repaired);
            var quiz = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "quiz.json")))!.AsObject();
            var question = quiz["questions"]!.AsArray()[0]!.AsObject();
            var imagePath = question["image_path"]!.GetValue<string>();
            Assert.False(Path.IsPathRooted(imagePath));
            Assert.StartsWith("Assets/QuestionImages/", imagePath, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, imagePath.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepairAvailableArtwork_DoesNotChangeStandardQuiz()
    {
        var root = Path.Combine(Path.GetTempPath(), $"logo-promo-standard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var quizPath = Path.Combine(root, "quiz.json");
            var original =
                """
                {
                  "title": "General Quiz",
                  "quiz_type": "Standard",
                  "questions": [
                    {
                      "number": 1,
                      "id": 7,
                      "question": "Question?",
                      "answers": ["A", "B", "C", "D"],
                      "correct_index": 0
                    }
                  ]
                }
                """;
            File.WriteAllText(quizPath, original);

            var repaired = LogoQuizProjectArtworkRepair.RepairAvailableArtwork(
                root,
                _ => throw new InvalidOperationException("Standard quizzes must not resolve logo artwork."));

            Assert.Equal(0, repaired);
            Assert.Equal(original, File.ReadAllText(quizPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build80_WiresAutomaticLogoPromoArtworkRepair()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var supervisor = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LogoQuizPromoArtworkRepair.cs");
        var repair = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/LogoQuizProjectArtworkRepair.cs");

        Assert.Contains("CurrentBuildNumber = 80", buildInfo, StringComparison.Ordinal);
        Assert.Contains("InitializeLogoQuizPromoArtworkRepair", buildInfo, StringComparison.Ordinal);
        Assert.Contains("LogoQuizProjectArtworkRepair.RepairAsync", supervisor, StringComparison.Ordinal);
        Assert.Contains("Assets", repair, StringComparison.Ordinal);
        Assert.Contains("QuestionImages", repair, StringComparison.Ordinal);
        Assert.Contains("image_path", repair, StringComparison.Ordinal);
        Assert.Contains("DownloadPngAsync", repair, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
