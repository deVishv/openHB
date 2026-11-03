using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenHisabKitab.Host.Tests;

/// <summary>The company logo printed on invoices, stored in the company file (Plan 6b).</summary>
public sealed class CompanyLogoTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;

    public CompanyLogoTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
    }

    public void Dispose() => host.Dispose();

    static byte[] Png(int width = 2, int height = 2, int length = 64)
    {
        var b = new byte[length];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        return b;
    }

    IReadOnlyList<StoredAuditEntry> LogoAudit()
    {
        using var c = host.Hub.Open(company);
        return [.. AuditLog.Read(c, new AuditFilter(Entity: "logo")).OrderBy(r => r.Seq)];
    }

    [Fact]
    public async Task Logo_set_get_remove_audited_with_hash()
    {
        Assert.Null(host.Companies.GetLogo(owner, company));
        var png = Png(length: 500);
        await host.Companies.SetLogoAsync(owner, company, png);
        var (stored, sha) = host.Companies.GetLogo(owner, company)!.Value;
        Assert.Equal(png, stored);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(png)), sha);

        var next = Png(length: 600);
        await host.Companies.SetLogoAsync(owner, company, next);
        await host.Companies.SetLogoAsync(owner, company, []);
        Assert.Null(host.Companies.GetLogo(owner, company));

        var audit = LogoAudit();
        Assert.Equal(["set", "set", "remove"], audit.Select(a => a.Entry.Action));
        Assert.Null(audit[0].Entry.Before);
        Assert.Equal(sha, JsonDocument.Parse(audit[0].Entry.After!).RootElement.GetProperty("sha256").GetString());
        Assert.Equal(500, JsonDocument.Parse(audit[0].Entry.After!).RootElement.GetProperty("bytes").GetInt32());
        Assert.Equal(audit[0].Entry.After, audit[1].Entry.Before);
        Assert.Equal(600, JsonDocument.Parse(audit[2].Entry.Before!).RootElement.GetProperty("bytes").GetInt32());
        Assert.Null(audit[2].Entry.After);
        Assert.All(audit, a => Assert.True((a.Entry.After?.Length ?? 0) < 200));
    }

    [Fact]
    public async Task Removing_no_logo_audits_nothing()
    {
        await host.Companies.SetLogoAsync(owner, company, []);
        Assert.Empty(LogoAudit());
    }

    [Fact]
    public async Task Logo_refused_when_not_png_or_too_big()
    {
        var jpeg = await Assert.ThrowsAsync<ServiceException>(() => host.Companies.SetLogoAsync(owner, company, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]));
        Assert.Equal((ServiceError.Invalid, "Logo must be a PNG file."), (jpeg.Error, jpeg.Message));
        var big = await Assert.ThrowsAsync<ServiceException>(() => host.Companies.SetLogoAsync(owner, company, Png(length: 204_801)));
        Assert.Equal("Logo must be at most 200 KB.", big.Message);
        Assert.Null(host.Companies.GetLogo(owner, company));
    }

    [Fact]
    public async Task Data_entry_and_viewer_cannot_set_logo()
    {
        foreach (var role in new[] { BuiltInRole.DataEntry, BuiltInRole.Viewer })
        {
            var user = host.NewUser(role.ToString().ToLowerInvariant(), role, company).Context;
            var e = await Assert.ThrowsAsync<ServiceException>(() => host.Companies.SetLogoAsync(user, company, Png()));
            Assert.Equal(ServiceError.PermissionDenied, e.Error);
            Assert.Null(host.Companies.GetLogo(user, company));
        }
    }
}
