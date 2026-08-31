using FactVaultManager.Desktop;
using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class CreateAdvancedUiCleanupTests
{
    [Theory]
    [InlineData("builder", "1   Setup")]
    [InlineData("draft", "2   Questions")]
    [InlineData("preview", "3   Preview")]
    [InlineData("publish", "4   Details")]
    [InlineData("export", "5   Finish")]
    public void Create_steps_use_plain_daily_workflow_labels(string key, string expected)
    {
        Assert.Equal(expected, FactburstDailyWorkspaceLayout.CreateStepLabel(key));
    }

    [Fact]
    public void Advanced_tools_are_grouped_without_duplicate_routes()
    {
        var groups = FactburstDailyWorkspaceLayout.AdvancedGroups;
        var tools = groups.SelectMany(group => group.Tools).ToList();

        Assert.Equal(3, groups.Count);
        Assert.Equal(tools.Count, tools.Select(tool => tool.Route).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(groups, group => group.Title == "Publishing & channel" && !group.Collapsed);
        Assert.Contains(groups, group => group.Title == "Content & assets" && !group.Collapsed);
    }

    [Fact]
    public void Legacy_and_diagnostic_routes_are_demoted_to_collapsed_group()
    {
        var troubleshooting = Assert.Single(
            FactburstDailyWorkspaceLayout.AdvancedGroups.Where(group => group.Collapsed));
        var routes = troubleshooting.Tools.Select(tool => tool.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Troubleshooting & legacy", troubleshooting.Title);
        Assert.Contains("Projects", routes);
        Assert.Contains("Production", routes);
        Assert.Contains("Asset Review", routes);
        Assert.DoesNotContain("Upload Manager", routes);
        Assert.DoesNotContain("Release Readiness", routes);
    }
}
