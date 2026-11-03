using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Reports;

public enum CashFlowSection { Operating, Investing, Financing }

public enum CashFlowItem { NetProfit, Depreciation, GroupChange }

/// <summary>One line of the cash flow. Group is the group directly under a heading for a GroupChange line; positive is cash in.</summary>
public sealed record CashFlowLine(CashFlowSection Section, CashFlowItem Item, GroupId? Group, Money Amount);

public sealed record CashFlowStatement(DateRange Period, IReadOnlyList<CashFlowLine> Lines, Money Operating, Money Investing,
    Money Financing, Money NetChange, Money OpeningCash, Money ClosingCash);

/// <summary>
/// The cash flow statement by the indirect method (spec 6.3, CFLO), with the classification the owner approved on
/// 2026-11-02 (Plan 6 Task 3 rules). Cash is the Cash in Hand and Bank Accounts groups; a bank overdraft is a
/// short term borrowing. Every figure comes from <see cref="Books"/> (spec 5.1).
/// </summary>
static class CashFlowBuilder
{
    public static CashFlowStatement Build(Books books, ChartOfAccounts chart, DateRange period)
    {
        Books.RequireSingleYear(period);
        var order = chart.Groups.Select((g, i) => (g.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var changes = new Dictionary<GroupId, (CashFlowSection Section, Money Amount)>();
        var openingCash = Money.Zero;
        var closingCash = Money.Zero;
        var depreciation = Money.Zero;

        foreach (var ledger in chart.Ledgers)
        {
            var group = ledger.Group;
            var nature = chart.NatureOf(ledger.Id);
            var activity = books.Activity(ledger.Id, period);
            if (nature is AccountNature.Income or AccountNature.Expense)
            {
                if (IsUnder(chart, group, GroupKind.Depreciation)) depreciation += activity.Debits - activity.Credits;
                continue;
            }
            if (IsUnder(chart, group, GroupKind.CashInHand) || IsUnder(chart, group, GroupKind.BankAccounts))
            {
                openingCash += activity.Opening;
                closingCash += activity.Closing;
                continue;
            }

            var effect = activity.Opening - activity.Closing;
            if (effect == Money.Zero) continue;
            var line = chart.LevelOneOf(group) ?? chart.RootOf(group);
            var section = SectionOf(chart.RootOf(group).Kind, line.Kind);
            changes[line.Id] = (section, changes.GetValueOrDefault(line.Id).Amount + effect);
        }

        // Depreciation is added back in operating and taken out of fixed assets, so that line is what was spent.
        var fixedAssets = chart.Groups.FirstOrDefault(g => g.Kind == GroupKind.FixedAssets);
        if (fixedAssets is null) depreciation = Money.Zero;
        else if (depreciation != Money.Zero)
            changes[fixedAssets.Id] = (CashFlowSection.Investing, changes.GetValueOrDefault(fixedAssets.Id).Amount - depreciation);

        var lines = new List<CashFlowLine>();
        var profit = books.ProfitAndLoss(period).NetProfit;
        if (profit != Money.Zero) lines.Add(new CashFlowLine(CashFlowSection.Operating, CashFlowItem.NetProfit, null, profit));
        if (depreciation != Money.Zero) lines.Add(new CashFlowLine(CashFlowSection.Operating, CashFlowItem.Depreciation, null, depreciation));
        foreach (var section in Enum.GetValues<CashFlowSection>())
            lines.AddRange(changes.Where(c => c.Value.Section == section && c.Value.Amount != Money.Zero)
                .OrderBy(c => order[c.Key])
                .Select(c => new CashFlowLine(section, CashFlowItem.GroupChange, c.Key, c.Value.Amount)));

        Money Total(CashFlowSection s) => Money.Sum(lines.Where(l => l.Section == s).Select(l => l.Amount));
        var operating = Total(CashFlowSection.Operating);
        var investing = Total(CashFlowSection.Investing);
        var financing = Total(CashFlowSection.Financing);
        return new CashFlowStatement(period, lines, operating, investing, financing, operating + investing + financing, openingCash, closingCash);
    }

    static CashFlowSection SectionOf(GroupKind root, GroupKind levelOne) => root switch
    {
        GroupKind.NonCurrentAssets => CashFlowSection.Investing,
        GroupKind.Equity or GroupKind.NonCurrentLiabilities => CashFlowSection.Financing,
        GroupKind.CurrentLiabilities when levelOne == GroupKind.ShortTermBorrowings => CashFlowSection.Financing,
        _ => CashFlowSection.Operating,
    };

    static bool IsUnder(ChartOfAccounts chart, GroupId group, GroupKind kind)
    {
        for (GroupId? g = group; g is { } id;)
        {
            var found = chart.Groups.First(x => x.Id == id);
            if (found.Kind == kind) return true;
            g = found.Parent;
        }
        return false;
    }
}
