using System;
using System.IO;
using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class RetiredFactCreationCleanupTests
{
    [Fact]
    public void CurrentBuild_MatchesCurrentVersionAndBuildNumber()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var version = ReadRepositoryFile("version.json");
        Assert.Contains("CurrentBuildNumber = 219", buildInfo, StringComparison.Ordinal);
        Assert.Contains("\"build\": 219", version, StringComparison.Ordinal);
        Assert.Contains("\"latest_version\": \"1.0.199\"", version, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = FindRepositoryFile(relativePath) ?? throw new FileNotFoundException(relativePath);
        return File.ReadAllText(path);
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return null;
    }
}
