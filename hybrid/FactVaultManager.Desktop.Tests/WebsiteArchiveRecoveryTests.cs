using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteArchiveRecoveryTests
{
    [Fact]
    public void QuizHistoryRecovery_UsesSameScopeAsWebsiteAudit()
    {
        var field = typeof(DesktopDataService).GetField(
            "WebsiteQuizHistoryRecoveryLimit",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(2_000, field.GetRawConstantValue());
    }

    [Fact]
    public void QuizHistoryRecovery_HasJournaledArchiveRecoveryPass()
    {
        var method = typeof(DesktopDataService).GetMethod(
            "RecoverJournaledArchiveLinksForProjectRecovery",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), false)]
    [InlineData(typeof(DirectoryNotFoundException), false)]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    public void WebsiteSync_PreservesSpecificMissingFileDiagnostics(Type exceptionType, bool treatedAsGenericUnavailable)
    {
        var method = typeof(MainShellWindow).GetMethod(
            "IsUnavailableProjectError",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var error = (Exception)Activator.CreateInstance(exceptionType)!;
        var result = (bool)method.Invoke(null, [error])!;

        Assert.Equal(treatedAsGenericUnavailable, result);
    }
}
