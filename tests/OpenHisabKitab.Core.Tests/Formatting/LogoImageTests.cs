using System.Buffers.Binary;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Core.Tests.Formatting;

public sealed class LogoImageTests
{
    /// <summary>A PNG signature and IHDR chunk of the given size, padded to the given length.</summary>
    internal static byte[] Png(int width, int height, int length = 64)
    {
        var b = new byte[Math.Max(length, 33)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        return b;
    }

    [Fact]
    public void Logo_accepts_png_and_refuses_jpeg_large_and_huge()
    {
        Assert.Null(LogoImage.Check(Png(200, 80)));
        Assert.Equal("Logo must be a PNG file.", LogoImage.Check([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal("Logo must be a PNG file.", LogoImage.Check([]));
        Assert.Equal("Logo must be at most 200 KB.", LogoImage.Check(Png(200, 80, LogoImage.MaxBytes + 1)));
        Assert.Null(LogoImage.Check(Png(200, 80, LogoImage.MaxBytes)));
        Assert.Equal("Logo must be at most 4000 by 4000 pixels.", LogoImage.Check(Png(4001, 80)));
        Assert.Equal("Logo must be at most 4000 by 4000 pixels.", LogoImage.Check(Png(0, 80)));
        Assert.Equal("Logo must be at most 4000 by 4000 pixels.", LogoImage.Check(Png(10, -5)));
    }
}
