using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>Chart of accounts, opening balances, parties, and items.</summary>
public sealed partial class MasterService(CompanyHub hub, ChangeFeed feed, IBooksObserver books, TimeProvider time)
{
    static readonly GstData Gst = GstData.LoadEmbedded();

    public ChartOfAccounts Chart(CallContext ctx, int company)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        return ChartRepository.Load(c);
    }

    public IReadOnlyDictionary<LedgerId, Money> Openings(CallContext ctx, int company)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        return ChartRepository.Openings(c);
    }

    public Task<GroupId> AddGroupAsync(CallContext ctx, int company, string name, GroupId parent) =>
        ChangeChartAsync<GroupId>(ctx, company, "create", (tx, chart) =>
        {
            var (next, id) = Charted(() => chart.AddGroup(name, parent));
            var group = next.Groups.Single(g => g.Id == id);
            ChartRepository.SaveGroup(tx, group);
            return (id.Value, "group", (object?)null, (object?)group, id);
        });

    public Task<LedgerId> AddLedgerAsync(CallContext ctx, int company, string name, GroupId group) =>
        ChangeChartAsync<LedgerId>(ctx, company, "create", (tx, chart) =>
        {
            var (next, id) = Charted(() => chart.AddLedger(name, group));
            ChartRepository.SaveLedger(tx, next.Ledger(id));
            return (id.Value, "ledger", (object?)null, (object?)next.Ledger(id), id);
        });

    public Task RenameAsync(CallContext ctx, int company, LedgerId id, string name) =>
        ChangeChartAsync<int>(ctx, company, "rename", (tx, chart) =>
        {
            var before = Known(chart, id);
            if (before.Required is not null) ctx.Require(Permission.EditOpeningsAndRequiredLedgers);
            var next = Charted(() => chart.RenameLedger(id, name));
            ChartRepository.SaveLedger(tx, next.Ledger(id));
            return (id.Value, "ledger", before, next.Ledger(id), 0);
        });

    public Task RenameGroupAsync(CallContext ctx, int company, GroupId id, string name) =>
        ChangeChartAsync<int>(ctx, company, "rename", (tx, chart) =>
        {
            var before = chart.Groups.SingleOrDefault(g => g.Id == id) ?? throw new ServiceException(ServiceError.NotFound, "No such group.");
            var after = Charted(() => chart.RenameGroup(id, name)).Groups.Single(g => g.Id == id);
            ChartRepository.SaveGroup(tx, after);
            return (id.Value, "group", before, after, 0);
        });

    public Task RemoveLedgerAsync(CallContext ctx, int company, LedgerId id) =>
        ChangeChartAsync<int>(ctx, company, "delete", (tx, chart) =>
        {
            var before = Known(chart, id);
            Charted(() => chart.RemoveLedger(id));
            var c = tx.Connection!;
            if (ChartRepository.HasPostings(c, id) || ChartRepository.Openings(c).ContainsKey(id))
                throw new ServiceException(ServiceError.Invalid, $"Ledger {before.Name} has entries and cannot be deleted.");
            ChartRepository.DeleteLedger(tx, id);
            return (id.Value, "ledger", before, (object?)null, 0);
        });

    /// <summary>Sets a ledger's opening balance at the books start. Positive is a debit.</summary>
    public Task SetOpeningAsync(CallContext ctx, int company, LedgerId id, Money amount)
    {
        ctx.Require(Permission.EditOpeningsAndRequiredLedgers);
        return ChangeChartAsync<int>(ctx, company, "set-opening", (tx, chart) =>
        {
            var c = tx.Connection!;
            Known(chart, id);
            // Spec 5.2 invariant 6: profit and loss ledgers open at zero each year.
            if (amount != Money.Zero && chart.NatureOf(id) is AccountNature.Income or AccountNature.Expense)
                throw new ServiceException(ServiceError.Invalid, "Income and expense ledgers have no opening balance.");
            var booksStart = new FinancialYear(CompanyRepository.GetProfile(c).FirstFyStartYear).Start;
            if (LockRepository.LockedUpTo(c) is { } locked && locked >= booksStart)
                throw new ServiceException(ServiceError.Locked, $"Books are locked up to {Text.Date(locked)}.");
            var before = ChartRepository.Openings(c).GetValueOrDefault(id);
            ChartRepository.SetOpening(tx, id, amount);
            return (id.Value, "opening", before.ToString(), amount.ToString(), 0);
        });
    }

    /// <summary>Creates a party with its ledger when Ledger is default, otherwise updates it. Returns the party's ledger.</summary>
    public Task<LedgerId> SavePartyAsync(CallContext ctx, int company, string name, GroupId group, Party p) =>
        ChangeChartAsync<LedgerId>(ctx, company, p.Ledger == default ? "create" : "edit", (tx, chart) =>
        {
            if (!IsUnder(chart, group, GroupKind.TradeReceivables) && !IsUnder(chart, group, GroupKind.TradePayables))
                throw new ServiceException(ServiceError.Invalid, "Parties belong under Trade Receivables or Trade Payables.");
            if (p.Gstin is { } g && g.State != p.State)
                throw new ServiceException(ServiceError.Invalid, "GSTIN state does not match the party state.");

            var c = tx.Connection!;
            LedgerId id;
            Party? before = null;
            if (p.Ledger == default)
            {
                (var next, id) = Charted(() => chart.AddLedger(name, group));
                ChartRepository.SaveLedger(tx, next.Ledger(id));
            }
            else
            {
                id = p.Ledger;
                before = PartyRepository.Get(c, id) ?? throw new ServiceException(ServiceError.NotFound, "No such party.");
                if (before.RowVersion != p.RowVersion)
                    throw new ServiceException(ServiceError.Conflict, "Someone else changed this party. Showing the current version.", before);
                if (chart.Ledger(id).Group != group) throw new ServiceException(ServiceError.Invalid, "A party cannot change group.");
                ChartRepository.SaveLedger(tx, Charted(() => chart.RenameLedger(id, name)).Ledger(id));
            }
            PartyRepository.Upsert(tx, p with { Ledger = id });
            return (id.Value, "party", (object?)before, (object?)PartyRepository.Get(c, id), id);
        });

    public IReadOnlyList<Party> Parties(CallContext ctx, int company)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        return PartyRepository.List(c);
    }

    /// <summary>Creates an item when Id is 0, otherwise updates it. Returns the item id.</summary>
    public Task<int> SaveItemAsync(CallContext ctx, int company, Item item)
    {
        ctx.Require(Permission.EditMasters);
        ctx.RequireCompany(company);
        var i = item with { Name = item.Name.Trim(), HsnSac = item.HsnSac.Trim(), Unit = item.Unit.Trim() };
        if (i.Name.Length == 0) throw new ServiceException(ServiceError.Invalid, "Enter an item name.");
        if (!HsnPattern().IsMatch(i.HsnSac)) throw new ServiceException(ServiceError.Invalid, "HSN or SAC must be 4, 6, or 8 digits.");
        if (!Gst.IsValidRate(i.Rate, Text.Today(time)))
            throw new ServiceException(ServiceError.Invalid, $"GST rate {Text.Rate(i.Rate)}% is not valid.");

        return hub.WriteAsync(company, tx =>
        {
            var c = tx.Connection!;
            Item? before = null;
            int id;
            try
            {
                if (i.Id == 0) id = ItemRepository.Insert(tx, i);
                else
                {
                    before = ItemRepository.Get(c, i.Id) ?? throw new ServiceException(ServiceError.NotFound, "No such item.");
                    if (before.RowVersion != i.RowVersion)
                        throw new ServiceException(ServiceError.Conflict, "Someone else changed this item. Showing the current version.", before);
                    ItemRepository.Update(tx, i);
                    id = i.Id;
                }
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19)
            {
                throw new ServiceException(ServiceError.Invalid, $"Item {i.Name} already exists.");
            }
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), i.Id == 0 ? "create" : "edit", "item", Text.Key(id),
                Json(before), Json(ItemRepository.Get(c, id))));
            return id;
        }, id => feed.Publish(new ChangeEvent(company, ChangeKind.Masters, id)));
    }

    public IReadOnlyList<Item> Items(CallContext ctx, int company)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        return ItemRepository.List(c);
    }

    // One chart change: check rights, run it in the company's write transaction, audit it, then tell the books and clients.
    async Task<T> ChangeChartAsync<T>(CallContext ctx, int company, string action,
        Func<SqliteTransaction, ChartOfAccounts, (long Key, string Entity, object? Before, object? After, T Result)> change)
    {
        ctx.Require(Permission.EditMasters);
        ctx.RequireCompany(company);
        var (key, result) = await hub.WriteAsync(company, tx =>
        {
            var (key, entity, before, after, result) = change(tx, ChartRepository.Load(tx.Connection!));
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), action, entity, Text.Key(key), Json(before), Json(after)));
            return (key, result);
        }, r =>
        {
            books.ChartChanged(company);
            feed.Publish(new ChangeEvent(company, ChangeKind.Masters, r.key));
        }).ConfigureAwait(false);
        return result;
    }

    static LedgerAccount Known(ChartOfAccounts chart, LedgerId id) =>
        chart.Contains(id) ? chart.Ledger(id) : throw new ServiceException(ServiceError.NotFound, "No such ledger.");

    static T Charted<T>(Func<T> act)
    {
        try { return act(); }
        catch (ChartException e) { throw new ServiceException(ServiceError.Invalid, e.Message); }
    }

    static bool IsUnder(ChartOfAccounts chart, GroupId group, GroupKind kind)
    {
        var target = chart.Group(kind);
        var byId = chart.Groups.ToDictionary(g => g.Id);
        for (GroupId? g = group; g is { } id && byId.ContainsKey(id); g = byId[id].Parent)
            if (id == target) return true;
        return false;
    }

    static string? Json(object? value) => value is null ? null : JsonSerializer.Serialize(value, value.GetType());

    [GeneratedRegex(@"^\d{4}(\d{2}){0,2}$")]
    private static partial Regex HsnPattern();
}
