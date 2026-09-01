namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotProjectsFolderGuardTests
{
    [Fact]
    public void MissingProjectsFolder_IsNotReady()
    {
        var result = ProjectsFolderConfigurationGuard.Check("");

        Assert.False(result.Ready);
        Assert.Contains("Projects Folder", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingProjectsFolder_IsReady()
    {
        var folder = Path.Combine(Path.GetTempPath(), "factburst-projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = ProjectsFolderConfigurationGuard.Check(folder);

            Assert.True(result.Ready);
            Assert.Equal(Path.GetFullPath(folder), result.Path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MissingProjectsFolderException_IsRecognisedThroughInnerException()
    {
        var error = new Exception(
            "outer",
            new InvalidOperationException(ProjectsFolderConfigurationGuard.MissingProjectsFolderError));

        Assert.True(ProjectsFolderConfigurationGuard.IsMissingProjectsFolderException(error));
        Assert.False(ProjectsFolderConfigurationGuard.IsMissingProjectsFolderException(new InvalidOperationException("different")));
    }

    [Fact]
    public void AutopilotNavigation_BlocksBeforeNormalClickWhenProjectRootIsUnavailable()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotProjectsFolderGuard.cs");

        Assert.Contains("e.Handled = true;", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotFirstNavTag + \":Autopilot\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowProjectsFolderConfigurationRequired", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatcherConfigurationFailure_IsHandledWithoutApplicationShutdown()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/Program.cs");
        var guardStart = source.IndexOf("ProjectsFolderConfigurationGuard.IsMissingProjectsFolderException", StringComparison.Ordinal);
        var returnAt = source.IndexOf("return;", guardStart, StringComparison.Ordinal);
        var shutdownAt = source.IndexOf("Shutdown(-1)", guardStart, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(returnAt > guardStart);
        Assert.True(shutdownAt < 0 || returnAt < shutdownAt);
        Assert.Contains("ShowProjectsFolderConfigurationRequired", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build134_IsTheAutopilotProjectsRootGuard()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("CurrentBuildNumber = 134", build, StringComparison.Ordinal);
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
