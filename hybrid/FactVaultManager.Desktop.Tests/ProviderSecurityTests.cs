namespace FactVaultManager.Desktop.Tests;

public sealed class ProviderSecurityTests
{
    [Fact]
    public void SensitiveQueryValues_AreRedactedFromLoggedUrls()
    {
        var uri = new Uri("https://pixabay.com/api/?key=super-secret&q=octopus&token=another-secret");

        var safe = NativeAssetProviderBase.RedactSensitiveQuery(uri);

        Assert.DoesNotContain("super-secret", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", safe, StringComparison.Ordinal);
        Assert.Contains("%5BREDACTED%5D", safe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("q=octopus", safe, StringComparison.OrdinalIgnoreCase);
    }
}
