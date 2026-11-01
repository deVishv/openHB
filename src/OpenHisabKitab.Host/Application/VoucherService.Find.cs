using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>What FIND looks for: text in a number, party, amount, or narration, optionally within dates.</summary>
public sealed record VoucherSearch(string Text, DateRange? Range, VoucherType? Type = null);

/// <summary>Paged lists, FIND (spec 6.3), and opening a voucher by its number.</summary>
public sealed partial class VoucherService
{
    /// <summary>One page of vouchers in a period, by date, then id. A user who cannot view all vouchers sees only their own.</summary>
    public Page<VoucherRow> List(CallContext ctx, int company, DateRange range, VoucherType? type, PageRequest page)
    {
        ctx.RequireCompany(company);
        var limit = Paging.Limit(page);
        var after = Paging.DateCursor(page.After);
        var ownOnly = OwnOnly(ctx);
        using var c = hub.Open(company);
        var rows = VoucherRepository.ListPage(c, range, type, ownOnly ? ctx.UserId : null, after, limit + 1);
        return Paging.Cut(rows, limit, v => Paging.DateCursor(v.Date, v.Id));
    }

    /// <summary>Vouchers whose number, narration, party, or debit total matches the text, newest first. Cancelled ones are included.</summary>
    public Page<VoucherRow> Find(CallContext ctx, int company, VoucherSearch search, PageRequest page)
    {
        ctx.RequireCompany(company);
        var text = search.Text.Trim();
        if (text.Length == 0) throw new ServiceException(ServiceError.Invalid, "Enter text to find.");
        var limit = Paging.Limit(page);
        var before = Paging.DateCursor(page.After);
        var ownOnly = OwnOnly(ctx);
        using var c = hub.Open(company);
        var chart = ChartRepository.Load(c);
        var parties = PartyRepository.List(c).Select(p => p.Ledger).ToHashSet();
        var matching = chart.Ledgers
            .Where(l => parties.Contains(l.Id) && l.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Id.Value)
            .ToList();
        Money? amount = Money.TryParse(text, out var m) ? m : null;
        var rows = VoucherRepository.Find(c, text, search.Range, search.Type, matching, amount, ownOnly ? ctx.UserId : null, before, limit + 1);
        return Paging.Cut(rows, limit, v => Paging.DateCursor(v.Date, v.Id));
    }

    public VoucherRow GetByNumber(CallContext ctx, int company, VoucherType type, string displayNumber)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        var v = VoucherRepository.GetByNumber(c, type, displayNumber.Trim())
            ?? throw new ServiceException(ServiceError.NotFound, $"No voucher {type} {displayNumber.Trim()}.");
        if (OwnOnly(ctx) && v.CreatedBy != ctx.UserId) throw ServiceException.Denied();
        return v;
    }
}
