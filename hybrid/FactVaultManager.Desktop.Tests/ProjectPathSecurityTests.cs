namespace FactVaultManager.Desktop.Tests;

public sealed class ProjectPathSecurityTests
{
    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("folder/name")]
    [InlineData("folder\\name")]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    public void ValidateSegment_RejectsUnsafeProjectNames(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ProjectPathSecurity.ValidateSegment(value, "Project title"));
    }

    [Fact]
    public void CombineContained_RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(() =>
            ProjectPathSecurity.CombineContained(root, "In Progress", "..", "..", "escape"));
    }

    [Fact]
    public void ResolveContained_RejectsTamperedAbsolutePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "FactVaultManager-outside", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(() =>
            ProjectPathSecurity.ResolveContained(root, outside));
    }

    [Fact]
    public void NormalProjectPath_RemainsAllowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        var path = ProjectPathSecurity.CombineContained(root, "Completed", "Octopuses Have Three Hearts");

        Assert.StartsWith(Path.GetFullPath(root), path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Completed", "Octopuses Have Three Hearts"), path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryEnsureContained_ReturnsNullForFolderOutsideProjectsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "factburst-projects");
        var outside = Path.Combine(Path.GetTempPath(), "old-factburst-projects", "Space - 001");

        Assert.Null(ProjectPathSecurity.TryEnsureContained(root, outside));
    }
}
