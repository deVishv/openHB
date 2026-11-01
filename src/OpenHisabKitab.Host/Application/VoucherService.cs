using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Saving, editing, and cancelling vouchers. Every rule is checked inside the company's write transaction, the number
/// is taken in that same transaction, and the audit record is written with it, so a failure anywhere leaves nothing.
/// </summary>
public sealed partial class VoucherService(CompanyHub hub, ChangeFeed feed, IBooksObserver books, TimeProvider time)
{
    // Builds a voucher's postings, and its invoice detail for GST documents, from the input and the current chart.
    delegate (IReadOnlyList<PostingLine> Postings, InvoiceHeader? Invoice) Build(SqliteTransaction tx, ChartOfAccounts chart);

    // Repeat is true when a retried save found its earlier voucher: nothing changed, so nothing is published.
    sealed record Outcome(SavedVoucher Saved, IReadOnlyList<PostingRecord> Postings, bool Repeat = false);

    static readonly HashSet<VoucherType> JournalTypes = [VoucherType.Receipt, VoucherType.Payment, VoucherType.Journal, VoucherType.Contra];

    public Task<SavedVoucher> SaveJournalAsync(CallContext ctx, int company, JournalInput input)
    {
        RequireJournalType(input.Type);
        return SaveAsync(ctx, company, input.Type, input.Date, input.Narration,
            (_, chart) => (CheckedLines(input.Type, input.Date, input.Lines, chart), null), input.Allocations ?? [], input.RequestId);
    }

    public Task<SavedVoucher> EditJournalAsync(CallContext ctx, int company, long id, long rowVersion, JournalInput input)
    {
        RequireJournalType(input.Type);
        return EditAsync(ctx, company, id, rowVersion, input.Type, input.Date, input.Narration,
            (_, chart) => (CheckedLines(input.Type, input.Date, input.Lines, chart), null), input.Allocations ?? []);
    }

    /// <summary>Cancels a voucher. It keeps its number and posts nothing (spec 5.4).</summary>
    public async Task CancelAsync(CallContext ctx, int company, long id, long rowVersion, string reason)
    {
        ctx.Require(Permission.CancelVoucher);
        ctx.RequireCompany(company);
        await hub.WriteAsync(company, tx =>
        {
            var c = tx.Connection!;
            var existing = Existing(ctx, c, id, rowVersion);
            CheckLock(c, existing.Date);
            CheckNotSettled(c, existing.Id);
            VoucherRepository.Cancel(tx, id, rowVersion);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "cancel", "voucher", Text.Key(id), Json(existing),
                JsonSerializer.Serialize(new { Cancelled = true, Reason = reason.Trim() })));
            return id;
        }, _ => Published(company, id, []));
    }

    public VoucherRow Get(CallContext ctx, int company, long id)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        var v = VoucherRepository.Get(c, id) ?? throw NotFound();
        if (OwnOnly(ctx) && v.CreatedBy != ctx.UserId) throw ServiceException.Denied();
        return v;
    }

    /// <summary>Vouchers in a period. A user who cannot view all vouchers sees only their own.</summary>
    public IReadOnlyList<VoucherRow> List(CallContext ctx, int company, DateRange range, VoucherType? type)
    {
        ctx.RequireCompany(company);
        var ownOnly = OwnOnly(ctx);
        using var c = hub.Open(company);
        return VoucherRepository.List(c, range, type, ownOnly ? ctx.UserId : null);
    }

    async Task<SavedVoucher> SaveAsync(CallContext ctx, int company, VoucherType type, DateOnly date, string narration, Build build,
        IReadOnlyList<AllocationRow> allocations, string? requestId)
    {
        ctx.Require(Permission.CreateVoucher);
        ctx.RequireCompany(company);
        if (requestId is { Length: > 64 }) throw new ServiceException(ServiceError.Invalid, "Request id is too long.");
        var outcome = await hub.WriteAsync(company, tx =>
        {
            var c = tx.Connection!;
            if (requestId is not null && Repeated(ctx, c, type, requestId) is { } repeat) return repeat;
            var fy = CheckYear(c, date);
            CheckLock(c, date);
            CheckOpenings(c);
            var chart = ChartRepository.Load(c);
            var (postings, invoice) = build(tx, chart);
            CheckAllocations(c, chart, type, postings, allocations, null);
            var (series, number) = NumberSeriesRepository.Take(tx, type, fy);
            string display;
            try { display = series.Format(fy, number); }
            catch (SeriesExhaustedException) { throw new ServiceException(ServiceError.Invalid, $"Series {series.Prefix(fy)} is full."); }

            var row = new VoucherRow(0, type, NumberSeriesRepository.DefaultSeries, fy.StartYear, number, display, date,
                narration.Trim(), false, ctx.UserId, 1, postings, invoice, allocations, requestId);
            var id = VoucherRepository.Insert(tx, row);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "create", "voucher", Text.Key(id), null, Json(row with { Id = id })));
            return new Outcome(new SavedVoucher(id, display, 1), Records(id, date, postings));
        }, o => { if (!o.Repeat) Published(company, o.Saved.Id, o.Postings); });
        return outcome.Saved;
    }

    async Task<SavedVoucher> EditAsync(CallContext ctx, int company, long id, long rowVersion, VoucherType type, DateOnly date,
        string narration, Build build, IReadOnlyList<AllocationRow> allocations)
    {
        ctx.Require(Permission.EditVoucher);
        ctx.RequireCompany(company);
        var outcome = await hub.WriteAsync(company, tx =>
        {
            var c = tx.Connection!;
            var existing = Existing(ctx, c, id, rowVersion);
            if (existing.Type != type) throw new ServiceException(ServiceError.Invalid, "Voucher type cannot change.");
            var fy = new FinancialYear(existing.FyStartYear);
            if (!fy.Range.Contains(date)) throw new ServiceException(ServiceError.Invalid, $"Date must stay in FY {fy.Label}.");
            CheckLock(c, existing.Date);
            CheckLock(c, date);
            CheckOpenings(c);
            var chart = ChartRepository.Load(c);
            var (postings, invoice) = build(tx, chart);
            CheckAllocations(c, chart, type, postings, allocations, id);
            CheckSettledBillEdit(c, chart, existing, invoice?.Party, postings);

            var row = existing with
            {
                Date = date, Narration = narration.Trim(), Postings = postings, Invoice = invoice, Allocations = allocations, RowVersion = rowVersion + 1,
            };
            VoucherRepository.Update(tx, row, rowVersion);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "edit", "voucher", Text.Key(id), Json(existing), Json(row)));
            return new Outcome(new SavedVoucher(id, existing.DisplayNumber, rowVersion + 1), Records(id, date, postings));
        }, o => Published(company, id, o.Postings));
        return outcome.Saved;
    }

    // Loads a voucher for a change: it must exist, be the caller's to change, not be cancelled, and not have changed since read.
    static VoucherRow Existing(CallContext ctx, SqliteConnection c, long id, long rowVersion)
    {
        var existing = VoucherRepository.Get(c, id) ?? throw NotFound();
        if (OwnOnly(ctx) && existing.CreatedBy != ctx.UserId) throw ServiceException.Denied();
        if (existing.Cancelled) throw new ServiceException(ServiceError.Invalid, "This voucher is cancelled.");
        if (existing.RowVersion != rowVersion)
            throw new ServiceException(ServiceError.Conflict, "Someone else changed this voucher. Showing the current version.", existing);
        return existing;
    }

    // A user who cannot view all vouchers works only with their own (spec 13, Permissions).
    static bool OwnOnly(CallContext ctx)
    {
        if (ctx.Has(Permission.ViewAllVouchers)) return false;
        // Seeing one's own vouchers is a read, so a read only token of a Data entry user keeps it.
        if (ctx.Permissions.Contains(Permission.CreateVoucher)) return true;
        throw ServiceException.Denied();
    }

    static FinancialYear CheckYear(SqliteConnection c, DateOnly date)
    {
        var fy = FinancialYear.Containing(date);
        if (!CompanyRepository.Years(c).Contains(fy))
            throw new ServiceException(ServiceError.Invalid, $"No financial year for {Text.Date(date)}. Add it in CMPY.");
        return fy;
    }

    static void CheckLock(SqliteConnection c, DateOnly date)
    {
        if (LockRepository.LockedUpTo(c) is { } locked && date <= locked)
            throw new ServiceException(ServiceError.Locked, $"Books are locked up to {Text.Date(locked)}.");
    }

    // Spec 13: the opening trial balance must balance before any voucher can be saved.
    static void CheckOpenings(SqliteConnection c)
    {
        var difference = Money.Sum(ChartRepository.Openings(c).Values);
        if (difference != Money.Zero)
        {
            var shown = IndianNumberFormat.Format(difference < Money.Zero ? -difference : difference);
            throw new ServiceException(ServiceError.OpeningDifference, $"Opening balances differ by {shown}. Fix them in ACCT.");
        }
    }

    // The typed lines are checked first, so a zero line is reported before consolidation would hide it.
    static IReadOnlyList<PostingLine> CheckedLines(VoucherType type, DateOnly date, IReadOnlyList<PostingLine> lines, ChartOfAccounts chart)
    {
        Check(new VoucherDraft(type, date, lines), chart);
        var consolidated = Postings.Consolidate(lines);
        Check(new VoucherDraft(type, date, consolidated), chart);
        return consolidated;
    }

    static void Check(VoucherDraft draft, ChartOfAccounts chart)
    {
        var errors = VoucherValidator.Validate(draft, chart);
        if (errors.Count > 0) throw new ServiceException(ServiceError.Invalid, Message(errors[0]));
    }

    static string Message(VoucherError e) => e switch
    {
        VoucherError.Unbalanced => "Debits and credits differ.",
        VoucherError.TooFewLines => "Enter at least two lines.",
        VoucherError.ZeroLine => "Remove lines with a zero amount.",
        VoucherError.UnknownLedger => "Unknown ledger.",
        VoucherError.ContraNonCashLedger => "Contra uses only cash and bank ledgers.",
        VoucherError.ReceiptWithoutCashDebit => "A receipt must debit cash or bank.",
        VoucherError.PaymentWithoutCashCredit => "A payment must credit cash or bank.",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    static void RequireJournalType(VoucherType type)
    {
        if (!JournalTypes.Contains(type)) throw new ServiceException(ServiceError.Invalid, $"Use the invoice entry for {type}.");
    }

    void Published(int company, long id, IReadOnlyList<PostingRecord> postings)
    {
        books.VoucherSaved(company, id, postings);
        feed.Publish(new ChangeEvent(company, ChangeKind.Voucher, id));
    }

    static IReadOnlyList<PostingRecord> Records(long id, DateOnly date, IReadOnlyList<PostingLine> lines) =>
        [.. lines.Select(l => new PostingRecord(id, date, l.Ledger, l.Amount))];

    static ServiceException NotFound() => new(ServiceError.NotFound, "No such voucher.");

    static string Json(VoucherRow v) => JsonSerializer.Serialize(v);
}
