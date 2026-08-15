using System.Net;

namespace FactVaultManager.Desktop;

internal static class NativeAssetDownloadSecurity
{
    public const long MaxImageBytes = 50L * 1024 * 1024;
    public const long MaxVideoBytes = 500L * 1024 * 1024;
    public const int MaxRedirects = 5;

    public static async Task<Uri> ValidateRemoteUriAsync(string value, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new NativeAssetAcquisitionException("asset URL is invalid");
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new NativeAssetAcquisitionException("asset URL must use HTTPS");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new NativeAssetAcquisitionException("asset URL cannot contain embedded credentials");
        if (string.IsNullOrWhiteSpace(uri.Host) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new NativeAssetAcquisitionException("asset URL cannot target localhost");

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            if (!IsPublicAddress(literal))
                throw new NativeAssetAcquisitionException("asset URL cannot target a private or special-purpose network address");
            return uri;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception error) when (error is SocketException or ArgumentException)
        {
            throw new NativeAssetAcquisitionException($"asset host could not be resolved safely: {error.Message}");
        }

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new NativeAssetAcquisitionException("asset host resolves to a private or special-purpose network address");
        return uri;
    }

    public static long MaxBytesFor(string kind) =>
        kind.Equals("video", StringComparison.OrdinalIgnoreCase) ? MaxVideoBytes : MaxImageBytes;

    public static void ValidateContentLength(string kind, long? length)
    {
        if (length is null)
            return;
        var max = MaxBytesFor(kind);
        if (length.Value <= 0)
            throw new NativeAssetAcquisitionException("remote asset has an invalid content length");
        if (length.Value > max)
            throw new NativeAssetAcquisitionException($"remote asset is too large ({length.Value} bytes; limit {max} bytes)");
    }

    public static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > maxBytes)
                throw new NativeAssetAcquisitionException($"remote asset exceeded the download limit of {maxBytes} bytes");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public static bool IsSupportedDownloadedFile(string kind, string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaxBytesFor(kind))
            return false;

        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);

        if (kind.Equals("image", StringComparison.OrdinalIgnoreCase))
            return IsImageHeader(header[..read]);
        if (kind.Equals("video", StringComparison.OrdinalIgnoreCase))
            return IsVideoHeader(header[..read]);
        return false;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];
            var c = bytes[2];
            if (a == 0 || a == 10 || a == 127 || a >= 224) return false;
            if (a == 100 && b is >= 64 and <= 127) return false;
            if (a == 169 && b == 254) return false;
            if (a == 172 && b is >= 16 and <= 31) return false;
            if (a == 192 && b == 168) return false;
            if (a == 192 && b == 0 && (c == 0 || c == 2)) return false;
            if (a == 198 && (b == 18 || b == 19)) return false;
            if (a == 198 && b == 51 && c == 100) return false;
            if (a == 203 && b == 0 && c == 113) return false;
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast) return false;
            if ((bytes[0] & 0xFE) == 0xFC) return false; // fc00::/7 unique-local
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return false; // fe80::/10 link-local
            return true;
        }

        return false;
    }

    private static bool IsImageHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return true;
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return true;
        if (header.Length >= 6)
        {
            var signature = System.Text.Encoding.ASCII.GetString(header[..6]);
            if (signature is "GIF87a" or "GIF89a")
                return true;
        }
        return header.Length >= 12 &&
               System.Text.Encoding.ASCII.GetString(header[..4]) == "RIFF" &&
               System.Text.Encoding.ASCII.GetString(header.Slice(8, 4)) == "WEBP";
    }

    private static bool IsVideoHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 12 && System.Text.Encoding.ASCII.GetString(header.Slice(4, 4)) == "ftyp")
            return true;
        return header.Length >= 4 &&
               header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3;
    }
}
