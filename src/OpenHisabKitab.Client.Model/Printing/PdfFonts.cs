using PdfSharp.Fonts;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>
/// Gives PDFsharp the screen's fonts from the Windows fonts folder, so PDFs embed Segoe UI and Consolas. PDFsharp's font
/// resolver is global to the process and may be set once, so it is installed behind a Lazy.
/// </summary>
sealed class PdfFonts : IFontResolver
{
    public const string Text = "Segoe UI";
    public const string Number = "Consolas";

    static readonly string Folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    static readonly Lazy<bool> Installed = new(() =>
    {
        GlobalFontSettings.FontResolver = new PdfFonts();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Install() => _ = Installed.Value;

    public static string Family(PrintFace face) => face == PrintFace.Number ? Number : Text;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => familyName switch
    {
        Text => new FontResolverInfo(bold ? "segoeuib" : "segoeui"),
        Number => new FontResolverInfo(bold ? "consolab" : "consola"),
        _ => null,
    };

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Folder, faceName + ".ttf"));
}
