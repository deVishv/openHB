namespace OpenHisabKitab.Core.Gst;

/// <summary>A company's GST registration. Composition companies issue bills of supply with no tax.</summary>
public enum GstRegistration { Regular, Composition, Unregistered }

/// <summary>Sales invoice types from spec section 13. Every kind except Regular and DeemedExport is inter state.</summary>
public enum InvoiceKind { Regular, ExportWithTax, ExportUnderLut, SezWithTax, SezWithoutTax, DeemedExport }
