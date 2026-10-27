using System.Security.Cryptography.X509Certificates;
using OpenHisabKitab.Host.Security;

namespace OpenHisabKitab.Host.Tests;

public sealed class CertificateStoreTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
    readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 4, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Same_certificate_on_every_start()
    {
        using var first = new CertificateStore(folder, time).GetOrCreate();
        using var second = new CertificateStore(folder, time).GetOrCreate();
        Assert.Equal(first.Thumbprint, second.Thumbprint);
        Assert.True(second.HasPrivateKey);
    }

    [Fact]
    public void Certificate_names_and_lifetime()
    {
        using var cert = new CertificateStore(folder, time).GetOrCreate();
        Assert.Equal("CN=Open Hisab Kitab host", cert.Subject);
        var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Contains(Environment.MachineName, san.EnumerateDnsNames(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(System.Net.IPAddress.Loopback, san.EnumerateIPAddresses());
        Assert.Equal(time.GetUtcNow().AddDays(-1).UtcDateTime.Date, cert.NotBefore.ToUniversalTime().Date);
        Assert.Equal(time.GetUtcNow().AddYears(10).AddDays(-1).UtcDateTime.Date, cert.NotAfter.ToUniversalTime().Date);
    }

    [Fact]
    public void Fingerprint_is_sha256_in_pairs_and_written_to_a_file()
    {
        var store = new CertificateStore(folder, time);
        using var cert = store.GetOrCreate();
        var fingerprint = CertificateStore.Fingerprint(cert);
        Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", fingerprint);
        Assert.Equal(fingerprint, File.ReadAllText(Path.Combine(folder, "host-cert-fingerprint.txt")).Trim());
    }

    [Fact]
    public void Missing_key_file_makes_a_new_certificate()
    {
        using var first = new CertificateStore(folder, time).GetOrCreate();
        File.Delete(Path.Combine(folder, "host-cert.key"));
        using var second = new CertificateStore(folder, time).GetOrCreate();
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
    }
}
