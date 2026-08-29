using FactVaultManager.Desktop;
using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotTopBarLocatorTests
{
    [Fact]
    public void Header_action_panel_is_identified_by_refresh_and_updates()
    {
        Assert.True(AutopilotTopBarLocator.IsHeaderActionPanel(new[] { "↻ Refresh", "Updates", "▷ Production" }));
        Assert.False(AutopilotTopBarLocator.IsHeaderActionPanel(new[] { "Production", "Media Library" }));
    }

    [Theory]
    [InlineData("▷  Production")]
    [InlineData("▷ Production")]
    [InlineData("Production")]
    [InlineData("  PRODUCTION  ")]
    public void Production_action_matching_ignores_icon_spacing_and_case(string label)
    {
        Assert.True(AutopilotTopBarLocator.IsLegacyProductionAction(label));
    }

    [Theory]
    [InlineData("Render Final Video")]
    [InlineData("Generate + Fill Schedule")]
    [InlineData("")]
    public void Other_actions_are_not_treated_as_legacy_production(string label)
    {
        Assert.False(AutopilotTopBarLocator.IsLegacyProductionAction(label));
    }
}
