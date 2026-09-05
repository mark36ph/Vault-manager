using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class RetiredFactCreationCleanupTests
{
    [Fact]
    public void Build148_RemovesRetiredGenericFactCreationFiles()
    {
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/DesktopDataService.NewFact.cs"));
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/HybridProject.cs"));
        Assert.False(RepositoryFileExists("hybrid/FactVaultManager.Desktop/MainShellWindow.LegacyShellCompatibility.cs"));
    }

    [Fact]
    public void Build148_RemovesRetiredProjectWorkflowHooksFromShellCodeBehind()
    {
        var shell = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.xaml.cs");
        Assert.DoesNotContain("_projectsWorkflowInitialized", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeProjectsWorkflow(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshProjects(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyProjectsFilter(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderProjects(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyProjectProductionMetadata(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProjectOnScreenText(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProjectVisualPlan(", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Build150_RemovesRetiredWorkspaceSurfacesFromXaml()
    {
        var xaml = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.xaml");
        Assert.DoesNotContain("⌂   Dashboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("▤   Projects", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("▷   Production", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("□   Media Library", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("◉   Asset Review", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Open Production Workspace", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ready to start production.", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PrimaryNavigationPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainTabs\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build150_DoesNotRefreshRetiredWorkspaceDuringWindowLoad()
    {
        var shell = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.xaml.cs");
        Assert.DoesNotContain("RefreshAll();", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadMedia(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAssetReview(", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenProduction_Click", shell, StringComparison.Ordinal);
        Assert.Contains("LoadBootstrapSettingsInputs();", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Build150_NoLongerNeedsRuntimeWorkspaceHidingShim()
    {
        var cleanup = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizOnlyCleanup.cs");
        Assert.DoesNotContain("RemoveLegacyFactVideoWorkspaceSurfaces", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("retired generic Dashboard/Projects/Production/Media/Asset Review workspace", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void Build170_InstagramApprovalIsExplicitAndNotStartedAsStartupAutopilot()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var approval = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramPromoApproval.cs");
        Assert.Contains("CurrentBuildNumber = 170", buildInfo, StringComparison.Ordinal);
        Assert.Contains("InitializeInstagramPromoApprovalUi();", buildInfo, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeInstagramPromoFollowup();", buildInfo, StringComparison.Ordinal);
        Assert.Contains("Approve Page for Instagram Autopilot", approval, StringComparison.Ordinal);
        Assert.Contains("ApprovedFacebookPageId", approval, StringComparison.Ordinal);
    }

    [Fact]
    public void Build170_VersionManifestMatches()
    {
        var version = ReadRepositoryFile("version.json");
        Assert.Contains("\"build\": 170", version, StringComparison.Ordinal);
        Assert.Contains("\"latest_version\": \"1.0.149\"", version, StringComparison.Ordinal);
    }

    private static bool RepositoryFileExists(string relativePath) => FindRepositoryFile(relativePath) is not null;

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = FindRepositoryFile(relativePath) ?? throw new FileNotFoundException(relativePath);
        return File.ReadAllText(path);
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
