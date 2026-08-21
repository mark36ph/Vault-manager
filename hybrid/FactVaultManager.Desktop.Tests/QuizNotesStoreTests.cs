using System.Text.Json.Nodes;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizNotesStoreTests
{
    [Fact]
    public void Save_RoundTripsNotesAndPreservesOtherSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(settings, "{\"projects_folder\":\"D:\\\\Projects\",\"quiz\":{\"logo_path\":\"logo.png\"}}");

            QuizNotesStore.Save(settings, "Make a geography quiz.\nSchedule it for Friday.");

            Assert.Equal("Make a geography quiz.\nSchedule it for Friday.", QuizNotesStore.Load(settings));
            var json = JsonNode.Parse(File.ReadAllText(settings))!;
            Assert.Equal("D:\\Projects", json["projects_folder"]!.GetValue<string>());
            Assert.Equal("logo.png", json["quiz"]!["logo_path"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsBlankWhenSettingsDoNotExist()
    {
        var settings = Path.Combine(Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"), "settings.json");

        Assert.Equal("", QuizNotesStore.Load(settings));
    }
}
