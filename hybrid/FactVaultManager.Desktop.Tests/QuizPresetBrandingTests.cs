namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPresetBrandingTests
{
    [Fact]
    public void ResolveLogoPath_BlankPresetLogo_PreservesCurrentBrandLogo()
    {
        var current = Path.Combine("data", "quiz", "branding", "quiz_logo.png");

        Assert.Equal(current, QuizPresetBranding.ResolveLogoPath(current, ""));
    }

    [Fact]
    public void ResolveLogoPath_MissingPresetLogo_PreservesCurrentBrandLogo()
    {
        var current = Path.Combine("data", "quiz", "branding", "quiz_logo.png");
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.png");

        Assert.Equal(current, QuizPresetBranding.ResolveLogoPath(current, missing));
    }

    [Fact]
    public void ResolveLogoPath_ExistingPresetLogo_UsesPresetLogo()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var presetLogo = Path.Combine(root, "preset-logo.png");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(presetLogo, [0x89, 0x50, 0x4E, 0x47]);

            Assert.Equal(
                Path.GetFullPath(presetLogo),
                QuizPresetBranding.ResolveLogoPath("current-logo.png", presetLogo));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
