using OpenHisabKitab.Host.Grpc;
using OpenHisabKitab.Host.Security;

namespace OpenHisabKitab.Client.Model.Tests;

public sealed class ProductTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Client_and_host_report_one_version()
    {
        Assert.Equal("0.1.0", ProductVersion.Current);
        Assert.Equal(ProductVersion.Current, HostVersion.Current);
    }

    [Fact]
    public void Fingerprint_keeps_the_host_format()
    {
        Directory.CreateDirectory(folder);
        using var cert = new CertificateStore(folder, TimeProvider.System).GetOrCreate();
        var f = CertificateFingerprint.Of(cert);
        Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", f);
        Assert.Equal(CertificateStore.Fingerprint(cert), f);
    }
}
