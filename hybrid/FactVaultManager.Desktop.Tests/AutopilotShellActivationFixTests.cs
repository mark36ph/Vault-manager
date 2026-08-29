using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotShellActivationFixTests
{
    [Fact]
    public void PrimaryNavigationPanel_RecognizesQuizFirstSidebarFromBuild47Screenshot()
    {
        var tags = new string?[] { "12", "13", "14", "15", "16", "17", "18", "5" };

        Assert.True(AutopilotNavigationLocator.IsPrimaryNavigationPanel(tags, 12));
    }

    [Fact]
    public void PrimaryNavigationPanel_RejectsInternalQuizWorkflowSidebar()
    {
        var tags = new string?[] { "builder", "draft", "preview", "publish", "export" };

        Assert.False(AutopilotNavigationLocator.IsPrimaryNavigationPanel(tags, 12));
    }

    [Fact]
    public void PrimaryNavigationPanel_RequiresSeveralNumericApplicationRoutes()
    {
        var tags = new string?[] { "12", "builder", "draft", "preview" };

        Assert.False(AutopilotNavigationLocator.IsPrimaryNavigationPanel(tags, 12));
    }

    [Fact]
    public void Build48Activation_WaitsForNavigationSectionsAndUsesQuizRouteInsteadOfDashboard()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotShellActivationFix.cs");

        Assert.Contains("ApplyNavigationSections();", source, StringComparison.Ordinal);
        Assert.Contains("if (!_navigationSectionsApplied)", source, StringComparison.Ordinal);
        Assert.Contains("_quizTabIndex.ToString()", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePrimaryAutopilotNavigationPanel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("⌂   Dashboard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutopilotStartup_RegistersShellActivationFixAfterAutopilotFirstUi()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var original = source.IndexOf("InitializeAutopilotFirstUi();", StringComparison.Ordinal);
        var fix = source.IndexOf("InitializeAutopilotShellActivationFix();", StringComparison.Ordinal);

        Assert.True(original >= 0);
        Assert.True(fix > original);
        Assert.Contains("CurrentBuildNumber =", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
