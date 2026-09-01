namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotMasterUiTests
{
    [Fact]
    public void MasterUi_IsBuiltFromTheRealAutopilotHeader()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotMasterUi.cs");

        Assert.Contains("_autopilotHealthText?.Parent is not StackPanel healthStack", source, StringComparison.Ordinal);
        Assert.Contains("new ToggleButton", source, StringComparison.Ordinal);
        Assert.Contains("preferences.AutoFillEnabled ? \"ON\" : \"OFF\"", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotScheduleTargetPlanner.AllowedTargets", source, StringComparison.Ordinal);
        Assert.Contains("Automatic refill is active", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterUi_QueuesImmediatelyInsteadOfWaitingForAnotherLoadedEvent()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotMasterUi.cs");

        Assert.Contains("Dispatcher.BeginInvoke(", source, StringComparison.Ordinal);
        Assert.Contains("new Action(EnsureAutopilotMasterUi)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded += (_, _) => Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.Contains("RetryAutopilotMasterUi();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TurningAutopilotOn_ImmediatelyEvaluatesScheduleWithoutFillClick()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotMasterUi.cs");
        var enable = source.IndexOf("SetAutopilotMasterEnabledAsync", StringComparison.Ordinal);
        var save = source.IndexOf("preferences.AutoFillEnabled = enabled", enable, StringComparison.Ordinal);
        var evaluate = source.IndexOf("await EvaluateAutomaticScheduleFillAsync()", save, StringComparison.Ordinal);

        Assert.True(enable >= 0);
        Assert.True(save > enable);
        Assert.True(evaluate > save);
        Assert.DoesNotContain("Fill schedule now", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedOnState_AutomaticallyEvaluatesWhenHomeUiAppears()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotMasterUi.cs");

        Assert.Contains("if (preferences.AutoFillEnabled)", source, StringComparison.Ordinal);
        Assert.Contains("EvaluateAutomaticScheduleFillAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInfo_InitializesMasterUiAndUsesBuild127()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("CurrentBuildNumber = 127", source, StringComparison.Ordinal);
        Assert.Contains("InitializeAutopilotMasterUi();", source, StringComparison.Ordinal);
        Assert.Contains("InitializeInstagramPromoFollowup();", source, StringComparison.Ordinal);
        Assert.Contains("InitializeLibraryPublicationStatusUi();", source, StringComparison.Ordinal);
        Assert.Contains("InitializeLibraryPlatformStatusFix();", source, StringComparison.Ordinal);
        Assert.Contains("InitializeLibraryPlatformSymbolFix();", source, StringComparison.Ordinal);
        var firstUi = source.IndexOf("InitializeAutopilotFirstUi();", StringComparison.Ordinal);
        var masterUi = source.IndexOf("InitializeAutopilotMasterUi();", StringComparison.Ordinal);
        Assert.True(masterUi > firstUi);
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
