namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>Function codes and the host's voucher type names (spec 6.3, Vouchers).</summary>
public static class VoucherCodes
{
    static readonly (string Code, string Type)[] Pairs =
    [
        ("SALE", "Sale"), ("PURC", "Purchase"), ("CRNT", "CreditNote"), ("DBNT", "DebitNote"),
        ("RCPT", "Receipt"), ("PYMT", "Payment"), ("JRNL", "Journal"), ("CNTR", "Contra"),
    ];

    /// <summary>"Sale" gives "SALE"; "CreditNote" gives "CRNT".</summary>
    public static string CodeOf(string voucherType) =>
        Pairs.FirstOrDefault(p => p.Type == voucherType).Code ?? throw new ArgumentException($"Unknown voucher type {voucherType}.", nameof(voucherType));

    public static string TypeOf(string code) =>
        Pairs.FirstOrDefault(p => p.Code == code).Type ?? throw new ArgumentException($"Unknown voucher code {code}.", nameof(code));
}
