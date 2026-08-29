using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteVisibilityUiContractTests
{
    [Fact]
    public void Website_visibility_states_are_user_facing()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Live", FactburstWebsiteVisibility.DisplayState("published", "2026-08-29T11:00:00Z", now));
        Assert.Equal("Upcoming", FactburstWebsiteVisibility.DisplayState("published", "2026-08-30T11:00:00Z", now));
        Assert.Equal("Offline", FactburstWebsiteVisibility.DisplayState("draft", "2026-08-30T11:00:00Z", now));
    }
}
