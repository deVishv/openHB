namespace OpenHisabKitab.Core.Commands;

public enum FunctionCategory { System, Masters, Vouchers, Reports, Gst }

public sealed record FunctionInfo(string Code, string Name, FunctionCategory Category);

/// <summary>Every function reachable from the prompt.</summary>
public static class FunctionCatalog
{
    public static IReadOnlyList<FunctionInfo> All { get; } =
    [
        new("HELP", "Help", FunctionCategory.System),
        new("SETG", "Settings", FunctionCategory.System),
        new("KEYS", "Keybindings", FunctionCategory.System),
        new("CMPY", "Company", FunctionCategory.System),
        new("USER", "Users", FunctionCategory.System),
        new("AUDT", "Audit trail", FunctionCategory.System),
        new("LOCK", "Period lock", FunctionCategory.System),
        new("BKUP", "Backup", FunctionCategory.System),
        new("DATE", "Working date", FunctionCategory.System),
        new("FIND", "Find", FunctionCategory.System),
        new("RFSH", "Refresh", FunctionCategory.System),
        new("PRNT", "Print", FunctionCategory.System),
        new("ACCT", "Accounts", FunctionCategory.Masters),
        new("PRTY", "Parties", FunctionCategory.Masters),
        new("ITEM", "Items", FunctionCategory.Masters),
        new("SALE", "Sales invoice", FunctionCategory.Vouchers),
        new("PURC", "Purchase invoice", FunctionCategory.Vouchers),
        new("CRNT", "Credit note", FunctionCategory.Vouchers),
        new("DBNT", "Debit note", FunctionCategory.Vouchers),
        new("RCPT", "Receipt", FunctionCategory.Vouchers),
        new("PYMT", "Payment", FunctionCategory.Vouchers),
        new("JRNL", "Journal", FunctionCategory.Vouchers),
        new("CNTR", "Contra", FunctionCategory.Vouchers),
        new("DAYB", "Day book", FunctionCategory.Reports),
        new("LEDG", "Ledger", FunctionCategory.Reports),
        new("TRLB", "Trial balance", FunctionCategory.Reports),
        new("PNLS", "Profit and loss", FunctionCategory.Reports),
        new("BSHT", "Balance sheet", FunctionCategory.Reports),
        new("CFLO", "Cash flow", FunctionCategory.Reports),
        new("OUTS", "Outstanding", FunctionCategory.Reports),
        new("GST1", "GSTR-1", FunctionCategory.Gst),
        new("GS3B", "GSTR-3B", FunctionCategory.Gst),
        new("G2BR", "GSTR-2B match", FunctionCategory.Gst),
        new("EINV", "E-invoice", FunctionCategory.Gst),
    ];

    public static FunctionInfo? Find(string code) =>
        All.FirstOrDefault(f => f.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Codes starting with the text first, then names with a word starting with it.</summary>
    public static IReadOnlyList<FunctionInfo> Search(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return All;
        var byCode = All.Where(f => f.Code.StartsWith(t, StringComparison.OrdinalIgnoreCase));
        var byName = All.Where(f => Text.Words(f.Name).Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase)));
        return byCode.Concat(byName).Distinct().ToList();
    }
}
