using System.Net;
using System.Text;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeAssetDownloadSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public void PrivateAndSpecialAddresses_AreRejected(string address)
    {
        Assert.False(NativeAssetDownloadSecurity.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddresses_AreAllowed(string address)
    {
        Assert.True(NativeAssetDownloadSecurity.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task HttpAndLoopbackUrls_AreRejectedBeforeDownload()
    {
        await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
            NativeAssetDownloadSecurity.ValidateRemoteUriAsync("http://example.com/image.jpg", CancellationToken.None));
        await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
            NativeAssetDownloadSecurity.ValidateRemoteUriAsync("https://127.0.0.1/image.jpg", CancellationToken.None));
    }

    [Fact]
    public void ImageValidation_UsesMagicBytesNotFilename()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var fake = Path.Combine(folder, "fake.jpg");
            File.WriteAllText(fake, "not really an image", Encoding.UTF8);
            Assert.False(NativeAssetDownloadSecurity.IsSupportedDownloadedFile("image", fake));

            var jpeg = Path.Combine(folder, "real.bin");
            File.WriteAllBytes(jpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
            Assert.True(NativeAssetDownloadSecurity.IsSupportedDownloadedFile("image", jpeg));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task StreamingLimit_StopsOversizedContent()
    {
        await using var source = new MemoryStream(new byte[1025]);
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
            NativeAssetDownloadSecurity.CopyWithLimitAsync(source, destination, 1024, CancellationToken.None));
    }
}
