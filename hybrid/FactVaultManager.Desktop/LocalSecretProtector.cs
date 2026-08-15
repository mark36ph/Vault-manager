using System.Security.Cryptography;
using System.Text;

namespace FactVaultManager.Desktop;

internal static class LocalSecretProtector
{
    private const string Prefix = "dpapi:v1:";
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("FactVaultManager/settings/api-credentials/v1"));

    public static string Protect(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || IsProtected(text))
            return text;

        var clearBytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var encrypted = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch (PlatformNotSupportedException error)
        {
            throw new InvalidOperationException("API credentials can only be saved securely on Windows.", error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public static string Unprotect(string? value)
    {
        var stored = (value ?? "").Trim();
        if (stored.Length == 0 || !IsProtected(stored))
            return stored;

        try
        {
            var encrypted = Convert.FromBase64String(stored[Prefix.Length..]);
            var clearBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(clearBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (Exception error) when (error is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                "Saved API credentials could not be decrypted for the current Windows user. Re-enter the provider keys in Settings.",
                error);
        }
    }

    public static bool NeedsMigration(string? value)
    {
        var stored = (value ?? "").Trim();
        return stored.Length > 0 && !IsProtected(stored);
    }

    internal static bool IsProtected(string? value) =>
        (value ?? "").StartsWith(Prefix, StringComparison.Ordinal);
}
