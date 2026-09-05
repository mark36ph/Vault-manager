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
    public void Build181_InstagramApprovalIsExplicitAndNotStartedAsStartupAutopilot()
    {
        var approval = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramPromoApproval.cs");
        Assert.Contains("Approve Page for Instagram Autopilot", approval, StringComparison.Ordinal);
        Assert.Contains("ApprovedFacebookPageId", approval, StringComparison.Ordinal);
        var save = approval.IndexOf("_data.SaveSettings(settings);", StringComparison.Ordinal);
        var start = approval.IndexOf("InitializeInstagramPromoFollowup();", save, StringComparison.Ordinal);
        Assert.True(save >= 0);
        Assert.True(start > save);
    }

    [Fact]
    public void Build181_DeferredStartupIsSplitIntoYieldingPhases()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("QueueDeferredShellPhase(InitializeDeferredAutopilotPhase);", buildInfo, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredShellPhase(InitializeDeferredWebsitePhase);", buildInfo, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredShellPhase(InitializeDeferredHistoryAndMaintenancePhase);", buildInfo, StringComparison.Ordinal);
        Assert.Contains("private void InitializeDeferredQuizPhase()", buildInfo, StringComparison.Ordinal);
        Assert.Contains("private void InitializeDeferredAutopilotPhase()", buildInfo, StringComparison.Ordinal);
        Assert.Contains("private void InitializeDeferredWebsitePhase()", buildInfo, StringComparison.Ordinal);
        Assert.Contains("private void InitializeDeferredHistoryAndMaintenancePhase()", buildInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Build181_ReducesDailyCleanupStartupPasses()
    {
        var cleanup = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.DailyUiCleanup.cs");
        Assert.Contains("_dailyUiCleanupStartupPassesRemaining = 4", cleanup, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(750)", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("_dailyUiCleanupStartupPassesRemaining = 8", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("Interval = TimeSpan.FromMilliseconds(500)", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void Build181_InstagramBusinessLoginHasRealOAuthFlow()
    {
        var service = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/InstagramBusinessLoginService.cs");
        var ui = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramBusinessLogin.cs");
        Assert.Contains("https://www.instagram.com/oauth/authorize", service, StringComparison.Ordinal);
        Assert.Contains("authorization_code", service, StringComparison.Ordinal);
        Assert.Contains("ig_exchange_token", service, StringComparison.Ordinal);
        Assert.Contains("Waiting for Instagram sign-in", ui, StringComparison.Ordinal);
        Assert.Contains("Connect Instagram", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void Build184_RefreshesNavigationButtonIndexWhenItWasBuiltTooEarly()
    {
        var shell = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.CoreLifecycle.cs");
        Assert.Contains("EnsureNavigationButtonIndex", shell, StringComparison.Ordinal);
        Assert.Contains("_indexedNavigationButtons is { Count: > 0 }", shell, StringComparison.Ordinal);
        Assert.Contains("_indexedNavigationButtons = FindVisualChildren<Button>(root)", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Build185_BenchmarkDiscoversNavigationButtonsFromLiveVisualTree()
    {
        var diagnosticsUi = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.PerformanceDiagnosticsUi.cs");
        Assert.Contains("GetCurrentNavigationButtons();", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("var buttons = FindVisualChildren<Button>(root)", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("_indexedNavigationButtons = buttons;", diagnosticsUi, StringComparison.Ordinal);
        Assert.DoesNotContain("Where(button => button.IsVisible", diagnosticsUi, StringComparison.Ordinal);
    }

    [Fact]
    public void Build186_BenchmarkUsesPrimaryNavigationPanelWhenSidebarIsHidden()
    {
        var diagnosticsUi = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.PerformanceDiagnosticsUi.cs");
        Assert.Contains("ApplyNavigationSections();", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("PrimaryNavigationPanel.Children", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("OfType<Button>()", diagnosticsUi, StringComparison.Ordinal);
        Assert.DoesNotContain("button.IsVisible", diagnosticsUi, StringComparison.Ordinal);
    }

    [Fact]
    public void Build186_KeepsInAppPerformanceScanAndNavigationBenchmark()
    {
        var diagnostics = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnostics.cs");
        var diagnosticsUi = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.PerformanceDiagnosticsUi.cs");
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("Performance Diagnostics", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("Scan app now", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("Benchmark navigation", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("BenchmarkNavigationAsync", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("NavigateAndWaitAsync", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("SLOW", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("100 ms", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("ScanVisualTree", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("Visual elements", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("MEASURED OPERATIONS", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("public static string GetReport()", diagnostics, StringComparison.Ordinal);
        Assert.Contains("InitializePerformanceDiagnosticsUi();", buildInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Build189_AddsFullPerformanceProfileAndNextStartupProfiling()
    {
        var diagnostics = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/PerformanceDiagnostics.cs");
        var diagnosticsUi = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.PerformanceDiagnosticsUi.cs");
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var version = ReadRepositoryFile("version.json");

        Assert.Contains("Run full performance profile", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("Profile next startup", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("RunFullPerformanceProfileAsync", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("navigationCycles = 10", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("Percentile(sortedNavigation, 0.95)", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("RefreshQuizBank();", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("GC.CollectionCount(2)", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("GetSlowOperationReport(50)", diagnosticsUi, StringComparison.Ordinal);
        Assert.Contains("RequestStartupProfile", diagnostics, StringComparison.Ordinal);
        Assert.Contains("StartupProfileRequested", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ClearStartupProfileRequest", diagnostics, StringComparison.Ordinal);
        Assert.Contains("GetSlowOperationReport", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ClearStartupProfileRequest();", buildInfo, StringComparison.Ordinal);
        Assert.Contains("CurrentBuildNumber = 189", buildInfo, StringComparison.Ordinal);
        Assert.Contains("\"build\": 189", version, StringComparison.Ordinal);
        Assert.Contains("\"latest_version\": \"1.0.168\"", version, StringComparison.Ordinal);
    }

    [Fact]
    public void Build189_UpdaterUsesGitHubStableFeedAndLatestSetupAsset()
    {
        var updater = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/AppUpdateService.cs");
        Assert.Contains("new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)", updater, StringComparison.Ordinal);
        Assert.Contains("public const string StableSetupDownloadUrl = RepositoryUrl + \"/releases/latest/download/FactVaultManager-win-x64-stable-Setup.exe\";", updater, StringComparison.Ordinal);
        Assert.Contains("return await _manager.CheckForUpdatesAsync();", updater, StringComparison.Ordinal);
        Assert.Contains("return $\"Factburst Quiz Manager {CurrentVersion} is up to date.\";", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.166", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.167", updater, StringComparison.Ordinal);
    }

    [Fact]
    public void Build190_BumpsVersionAndBuildNumber()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var version = ReadRepositoryFile("version.json");
        Assert.Contains("CurrentBuildNumber = 190", buildInfo, StringComparison.Ordinal);
        Assert.Contains("\"build\": 190", version, StringComparison.Ordinal);
        Assert.Contains("\"latest_version\": \"1.0.169\"", version, StringComparison.Ordinal);
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
