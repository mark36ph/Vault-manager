using System.Globalization;
using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class DailyUiCleanupTests
{
    [Fact]
    public void MainWindow_ExposesDailyUiCleanupInitializer()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            nameof(MainShellWindow.InitializeDailyUiCleanup),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Theory]
    [InlineData("Scheduled 17-09-2026 00:00", "Scheduled 17 Sep")]
    [InlineData("Scheduled 17-09-2026 18:30", "Sched. 17 Sep 18:30")]
    [InlineData("Published", "Published")]
    [InlineData("Private", "Private")]
    public void QuizHistoryStatusDisplayConverter_UsesCompactStatus(string input, string expected)
    {
        var actual = QuizHistoryStatusDisplayConverter.Instance.Convert(
            input,
            typeof(string),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QuizHistoryCleanup_KeepsLessUsedActionsBehindMoreMenu()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "hybrid",
            "FactVaultManager.Desktop",
            "MainShellWindow.QuizHistoryUiCleanup.cs"));

        Assert.Contains("Open Publish", source, StringComparison.Ordinal);
        Assert.Contains("Reopen in Quiz Builder", source, StringComparison.Ordinal);
        Assert.Contains("Archive completed C → Z", source, StringComparison.Ordinal);
        Assert.Contains("Delete selected quiz", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".github")) &&
                Directory.Exists(Path.Combine(current.FullName, "hybrid")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
