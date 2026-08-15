namespace FactVaultManager.Desktop.Tests;

public sealed class TrustedMediaExecutableLocatorTests
{
    [Fact]
    public void ExplicitTrustedDirectory_AllowsConfiguredFfmpeg()
    {
        var folder = TestFolder();
        Directory.CreateDirectory(folder);
        try
        {
            var executable = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            File.WriteAllBytes(executable, [0]);

            var result = TrustedMediaExecutableLocator.Find(
                "ffmpeg",
                pathValue: "",
                explicitDirectory: folder,
                trustedRoots: Array.Empty<string>());

            Assert.Equal(Path.GetFullPath(executable), result, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RelativeExplicitDirectory_IsRejected()
    {
        Assert.Throws<NativeFfmpegTimelineException>(() =>
            TrustedMediaExecutableLocator.Find(
                "ffmpeg",
                pathValue: "",
                explicitDirectory: "relative\\ffmpeg",
                trustedRoots: Array.Empty<string>()));
    }

    [Fact]
    public void ArbitraryPathDirectory_IsIgnored()
    {
        var folder = TestFolder();
        Directory.CreateDirectory(folder);
        try
        {
            var executable = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            File.WriteAllBytes(executable, [0]);

            Assert.Throws<NativeFfmpegTimelineException>(() =>
                TrustedMediaExecutableLocator.Find(
                    "ffmpeg",
                    pathValue: folder,
                    explicitDirectory: "",
                    trustedRoots: Array.Empty<string>()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PathEntryUnderTrustedRoot_IsAllowed()
    {
        var root = TestFolder();
        var bin = Path.Combine(root, "FFmpeg", "bin");
        Directory.CreateDirectory(bin);
        try
        {
            var executable = Path.Combine(bin, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            File.WriteAllBytes(executable, [0]);

            var result = TrustedMediaExecutableLocator.Find(
                "ffprobe",
                pathValue: bin,
                explicitDirectory: "",
                trustedRoots: [root]);

            Assert.Equal(Path.GetFullPath(executable), result, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContainmentCheck_RejectsSiblingPrefixCollision()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVault", "trusted");
        var sibling = Path.Combine(Path.GetTempPath(), "FactVault", "trusted-malicious");

        Assert.False(TrustedMediaExecutableLocator.IsWithin(root, sibling));
    }

    private static string TestFolder() =>
        Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
}
