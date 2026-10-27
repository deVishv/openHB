using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>Turns stored GSTIN text back into a <see cref="Gstin"/>. Stored values were validated when saved.</summary>
internal static class StoredGst
{
    static readonly GstData Data = GstData.LoadEmbedded();

    public static Gstin? Parse(string? text) =>
        text is null ? null
        : Gstin.TryParse(text, Data, out var g) ? g
        : throw new InvalidDataException($"Stored GSTIN {text} is not valid.");
}
