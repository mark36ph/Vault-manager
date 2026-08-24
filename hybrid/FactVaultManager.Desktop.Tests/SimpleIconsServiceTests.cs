using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class SimpleIconsServiceTests
{
    [Theory]
    [InlineData("YouTube", "youtube")]
    [InlineData("Mercedes-Benz", "mercedesbenz")]
    [InlineData("H&M", "hm")]
    [InlineData("Café", "cafe")]
    public void CreateSlug_NormalizesBrandName(string brand, string expected)
    {
        Assert.Equal(expected, SimpleIconsCatalog.CreateSlug(brand));
    }

    [Fact]
    public void BuildIconUri_UsesOfficialBrandColourByDefault()
    {
        var uri = SimpleIconsCatalog.BuildIconUri("YouTube", SimpleIconColourMode.Brand);

        Assert.Equal("https://cdn.simpleicons.org/youtube", uri.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void BuildIconUri_CanRequestBlackIcon()
    {
        var uri = SimpleIconsCatalog.BuildIconUri("YouTube", SimpleIconColourMode.Black);

        Assert.Equal("https://cdn.simpleicons.org/youtube/000000", uri.AbsoluteUri.TrimEnd('/'));
    }
}
