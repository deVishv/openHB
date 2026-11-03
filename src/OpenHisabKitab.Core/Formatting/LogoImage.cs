using System.Buffers.Binary;

namespace OpenHisabKitab.Core.Formatting;

/// <summary>
/// The company logo printed on invoices. Only PNG: PDFsharp's Core build cannot read progressive JPEG. Host and client
/// both check with this, so a logo the host stores is one every client can print.
/// </summary>
public static class LogoImage
{
    public const int MaxBytes = 204_800;
    public const string TooLarge = "Logo must be at most 200 KB.";
    const int MaxPixels = 4000;

    static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Null when the bytes may be stored as the logo; otherwise the one line reason.</summary>
    public static string? Check(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(Signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            return "Logo must be a PNG file.";
        if (bytes.Length > MaxBytes) return TooLarge;
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        if (width is < 1 or > MaxPixels || height is < 1 or > MaxPixels) return "Logo must be at most 4000 by 4000 pixels.";
        return null;
    }
}
