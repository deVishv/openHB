using OpenHisabKitab.Client.Model.Connection;

namespace OpenHisabKitab.Client.Model.Tests.Connection;

public sealed class HostAddressTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("", "47250", "Enter the host name or IP address.")]
    [InlineData("   ", "47250", "Enter the host name or IP address.")]
    [InlineData("pc 1", "47250", "Enter the host name or IP address.")]
    [InlineData("pc-1", "0", "Enter a port from 1 to 65535.")]
    [InlineData("pc-1", "65536", "Enter a port from 1 to 65535.")]
    [InlineData("pc-1", "abc", "Enter a port from 1 to 65535.")]
    [InlineData("pc-1", "", "Enter a port from 1 to 65535.")]
    public void Bad_addresses_name_the_field(string name, string port, string message)
    {
        Assert.False(HostAddress.TryParse(name, port, out _, out var error));
        Assert.Equal(message, error);
    }

    [Theory]
    [InlineData(" pc-1 ", "47250", "pc-1:47250")]
    [InlineData("192.168.1.9", " 47250 ", "192.168.1.9:47250")]
    [InlineData("::1", "47250", "[::1]:47250")]
    public void Good_addresses_parse(string name, string port, string text)
    {
        Assert.True(HostAddress.TryParse(name, port, out var address, out var error), error);
        Assert.Equal(text, address.ToString());
    }

    [Fact]
    public void Known_hosts_survive_a_new_instance_and_ignore_name_case()
    {
        var path = Path.Combine(folder, "known-hosts.json");
        new KnownHosts(path).Trust(new HostAddress("PC-1", 47250), "AA:BB");
        var again = new KnownHosts(path);
        Assert.Equal("AA:BB", again.Fingerprint(new HostAddress("pc-1", 47250)));
        Assert.Null(again.Fingerprint(new HostAddress("pc-1", 47251)));
    }
}
