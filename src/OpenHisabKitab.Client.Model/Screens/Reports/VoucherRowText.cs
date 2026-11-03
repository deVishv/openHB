using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>What a voucher list shows about a voucher: its party and its amount (FIND and DAYB).</summary>
public static class VoucherRowText
{
    /// <summary>The invoice's party, else the first party among the postings, else "".</summary>
    public static string Party(VoucherMsg v, LookupCache lookups) =>
        v.Invoice is { } inv ? lookups.LedgerName(inv.Party)
        : v.Postings.Select(p => p.Ledger).FirstOrDefault(lookups.Parties.ContainsKey) is var id and > 0 ? lookups.LedgerName(id) : "";

    /// <summary>The total of the debit lines.</summary>
    public static Money DebitTotal(VoucherMsg v) => new(v.Postings.Where(p => p.Paise > 0).Sum(p => p.Paise));
}
