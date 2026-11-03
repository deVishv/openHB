using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>Everything a printed GST document shows, gathered from the stored voucher, its saved details, and the settings.</summary>
public sealed record InvoicePrintInput(VoucherMsg Voucher, DocumentParty Company, GstRegistration Registration,
    DocumentParty Buyer, ShipTo? ShipTo, IReadOnlyList<string> Units, byte[]? Logo,
    string BankDetails, string Terms, string Signatory, bool ShowPrice, bool ShowDiscount)
{
    /// <summary>
    /// The buyer's and company's details as saved on the invoice; invoices saved before those were kept fall back to the
    /// masters. Each line's unit comes from its item.
    /// </summary>
    public static InvoicePrintInput From(VoucherMsg v, LookupCache lookups, EffectiveSettings s, byte[]? logo)
    {
        var inv = v.Invoice;
        var profile = lookups.Profile ?? new CompanyProfileMsg();
        var company = Saved(inv.CompanyDetails)
            ?? new DocumentParty(profile.Name, Blank(profile.Gstin), profile.Address, new StateCode(profile.State));
        var party = lookups.Parties.GetValueOrDefault(inv.Party);
        var buyer = Saved(inv.PartyDetails)
            ?? new DocumentParty(lookups.LedgerName(inv.Party), Blank(party?.Gstin), party?.Address ?? "",
                new StateCode(party?.State is { Length: 2 } state ? state : inv.PlaceOfSupply));
        var shipTo = inv.ShipTo is { } t
            ? new ShipTo(t.Name, t.Address, t.State.Length == 2 ? new StateCode(t.State) : null, Blank(t.Country))
            : null;
        IReadOnlyList<string> units = [.. inv.Lines.Select(l => lookups.Items.FirstOrDefault(i => i.Id == l.Entry.ItemId && l.Entry.ItemId != 0)?.Unit ?? "")];
        var registration = Enum.TryParse<GstRegistration>(profile.Registration, out var r) ? r : GstRegistration.Regular;
        return new InvoicePrintInput(v, company, registration, buyer, shipTo, units, logo,
            s.Text("invoice.bankDetails"), s.Text("invoice.terms"), s.Text("invoice.signatory"), s.Bool("invoice.showPrice"), s.Bool("invoice.showDiscount"));
    }

    static DocumentParty? Saved(DocumentPartyMsg? m) =>
        m is { Name.Length: > 0, State.Length: 2 } ? new DocumentParty(m.Name, Blank(m.Gstin), m.Address, new StateCode(m.State)) : null;

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
