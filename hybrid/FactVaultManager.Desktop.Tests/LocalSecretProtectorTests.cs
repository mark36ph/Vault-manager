namespace FactVaultManager.Desktop.Tests;

public sealed class LocalSecretProtectorTests
{
    [Fact]
    public void ProtectedCredential_RoundTripsForCurrentWindowsUser()
    {
        const string secret = "sk-test-secret-value";

        var protectedValue = LocalSecretProtector.Protect(secret);
        var clear = LocalSecretProtector.Unprotect(protectedValue);

        Assert.True(LocalSecretProtector.IsProtected(protectedValue));
        Assert.DoesNotContain(secret, protectedValue, StringComparison.Ordinal);
        Assert.Equal(secret, clear);
    }

    [Fact]
    public void LegacyPlaintextCredential_RemainsReadableAndNeedsMigration()
    {
        const string legacy = "legacy-plaintext-key";

        Assert.True(LocalSecretProtector.NeedsMigration(legacy));
        Assert.Equal(legacy, LocalSecretProtector.Unprotect(legacy));
    }

    [Fact]
    public void EmptyCredential_RemainsEmpty()
    {
        Assert.Equal("", LocalSecretProtector.Protect(""));
        Assert.Equal("", LocalSecretProtector.Unprotect(""));
        Assert.False(LocalSecretProtector.NeedsMigration(""));
    }
}
