using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Reports;

public enum LayoutStyle { ScheduleIII, Chart }

public enum LayoutSide { EquityAndLiabilities, Assets, Income, Expenses }

public enum LayoutLineKind { Heading, Group, Ledger, CashAndEquivalents, ProfitForPeriod, OpeningDifference, HeadingTotal, SideTotal, NetProfit }

/// <summary>
/// One line of a laid out statement. Amounts are positive on their normal side. Previous is null when the layout has
/// no previous year column. Labels are chart names; the fixed kinds have an empty label and are worded by the reader.
/// </summary>
public sealed record LayoutLine(LayoutSide Side, LayoutLineKind Kind, int Depth, string Label, GroupId? Group, LedgerId? Ledger,
    Money Current, Money? Previous);

public sealed record StatementLayout(LayoutStyle Style, bool HasPrevious, IReadOnlyList<LayoutLine> Lines);

/// <summary>
/// Lays out the balance sheet and profit and loss account (spec 6.3, BSHT): in Schedule III style, one line per group
/// directly under each heading, or in chart style, the whole tree with its ledgers. Figures come only from the reports
/// given; this class adds them up by group and never reads postings (spec 5.1).
/// </summary>
public static class ReportLayout
{
    public static StatementLayout BalanceSheet(ChartOfAccounts chart, BalanceSheet current, BalanceSheet? previous, LayoutStyle style)
    {
        var hasPrevious = previous is not null;
        var builder = new Builder(chart, style, hasPrevious);

        var reserves = chart.Groups.FirstOrDefault(g => g.Kind == GroupKind.ReservesAndSurplus)?.Id
            ?? chart.Groups.First(g => g.Parent is null && g.Nature == AccountNature.Liability).Id;
        var profit = new Extra(reserves, LayoutLineKind.ProfitForPeriod, current.CurrentProfit, previous?.CurrentProfit ?? Money.Zero);

        var lines = new List<LayoutLine>();
        var liabilities = builder.Side(LayoutSide.EquityAndLiabilities, AccountNature.Liability,
            Pairs(current.Liabilities, previous?.Liabilities), profit, lines);
        var difference = Pair.Of(current.OpeningDifference, previous?.OpeningDifference ?? Money.Zero);
        if (!difference.IsZero)
        {
            lines.Add(builder.Line(LayoutSide.EquityAndLiabilities, LayoutLineKind.OpeningDifference, 0, "", null, null, difference));
            liabilities += difference;
        }
        lines.Add(builder.Line(LayoutSide.EquityAndLiabilities, LayoutLineKind.SideTotal, 0, "", null, null, liabilities));

        var assets = builder.Side(LayoutSide.Assets, AccountNature.Asset, Pairs(current.Assets, previous?.Assets), null, lines);
        lines.Add(builder.Line(LayoutSide.Assets, LayoutLineKind.SideTotal, 0, "", null, null, assets));
        return new StatementLayout(style, hasPrevious, lines);
    }

    public static StatementLayout ProfitAndLoss(ChartOfAccounts chart, ProfitAndLoss current, ProfitAndLoss? previous, LayoutStyle style)
    {
        var hasPrevious = previous is not null;
        var builder = new Builder(chart, style, hasPrevious);
        var lines = new List<LayoutLine>();
        var income = builder.Side(LayoutSide.Income, AccountNature.Income, Pairs(current.Income, previous?.Income), null, lines);
        lines.Add(builder.Line(LayoutSide.Income, LayoutLineKind.SideTotal, 0, "", null, null, income));
        var expenses = builder.Side(LayoutSide.Expenses, AccountNature.Expense, Pairs(current.Expenses, previous?.Expenses), null, lines);
        lines.Add(builder.Line(LayoutSide.Expenses, LayoutLineKind.SideTotal, 0, "", null, null, expenses));
        lines.Add(builder.Line(LayoutSide.Expenses, LayoutLineKind.NetProfit, 0, "", null, null, income - expenses));
        return new StatementLayout(style, hasPrevious, lines);
    }

    static Dictionary<LedgerId, Pair> Pairs(IReadOnlyList<ReportRow> current, IReadOnlyList<ReportRow>? previous)
    {
        var map = current.ToDictionary(r => r.Ledger, r => Pair.Of(r.Amount, Money.Zero));
        foreach (var r in previous ?? [])
            map[r.Ledger] = map.TryGetValue(r.Ledger, out var p) ? p with { Previous = r.Amount } : Pair.Of(Money.Zero, r.Amount);
        return map;
    }

    readonly record struct Pair(Money Current, Money Previous)
    {
        public static Pair Of(Money current, Money previous) => new(current, previous);

        public bool IsZero => Current == Money.Zero && Previous == Money.Zero;

        public static Pair operator +(Pair a, Pair b) => new(a.Current + b.Current, a.Previous + b.Previous);

        public static Pair operator -(Pair a, Pair b) => new(a.Current - b.Current, a.Previous - b.Previous);
    }

    /// <summary>An amount that belongs to a group but to no ledger, such as the current year's profit.</summary>
    sealed record Extra(GroupId Group, LayoutLineKind Kind, Money Current, Money Previous)
    {
        public Pair Amount => Pair.Of(Current, Previous);
    }

    sealed class Builder(ChartOfAccounts chart, LayoutStyle style, bool hasPrevious)
    {
        readonly ILookup<GroupId?, AccountGroup> children = chart.Groups.ToLookup(g => g.Parent);
        readonly ILookup<GroupId, LedgerAccount> ledgers = chart.Ledgers.ToLookup(l => l.Group);

        public LayoutLine Line(LayoutSide side, LayoutLineKind kind, int depth, string label, GroupId? group, LedgerId? ledger, Pair amount) =>
            new(side, kind, depth, label, group, ledger, amount.Current, hasPrevious ? amount.Previous : null);

        /// <summary>Adds the side's headings to lines and returns the side's total.</summary>
        public Pair Side(LayoutSide side, AccountNature nature, IReadOnlyDictionary<LedgerId, Pair> amounts, Extra? extra, List<LayoutLine> lines)
        {
            var total = default(Pair);
            foreach (var root in children[null].Where(g => g.Nature == nature))
            {
                var body = new List<LayoutLine>();
                if (style == LayoutStyle.ScheduleIII) LineItems(side, root, amounts, extra, body);
                else Tree(side, root.Id, 1, amounts, extra, body);
                if (body.Count == 0) continue;

                var sum = Sum(root.Id, amounts, extra);
                lines.Add(Line(side, LayoutLineKind.Heading, 0, root.Name, root.Id, null, default));
                lines.AddRange(body);
                lines.Add(Line(side, LayoutLineKind.HeadingTotal, 0, root.Name, root.Id, null, sum));
                total += sum;
            }
            return total;
        }

        // Schedule III: one line per group directly under the heading; cash in hand and bank accounts make one line.
        void LineItems(LayoutSide side, AccountGroup root, IReadOnlyDictionary<LedgerId, Pair> amounts, Extra? extra, List<LayoutLine> body)
        {
            var cash = default(Pair);
            var cashAt = -1;
            GroupId? cashGroup = null;
            foreach (var g in children[root.Id])
            {
                var sum = Sum(g.Id, amounts, extra);
                if (g.Kind is GroupKind.CashInHand or GroupKind.BankAccounts)
                {
                    if (cashAt < 0)
                    {
                        cashAt = body.Count;
                        cashGroup = g.Id;
                    }
                    cash += sum;
                    continue;
                }
                if (!sum.IsZero) body.Add(Line(side, LayoutLineKind.Group, 1, g.Name, g.Id, null, sum));
            }
            if (cashAt >= 0 && !cash.IsZero)
                body.Insert(cashAt, Line(side, LayoutLineKind.CashAndEquivalents, 1, "", cashGroup, null, cash));
            Ledgers(side, root.Id, 1, amounts, body);
        }

        // Chart style: each group with its subtree sum, then its ledgers and any extra, then its groups.
        void Tree(LayoutSide side, GroupId parent, int depth, IReadOnlyDictionary<LedgerId, Pair> amounts, Extra? extra, List<LayoutLine> body)
        {
            if (depth == 1) Ledgers(side, parent, depth, amounts, body);
            foreach (var g in children[parent])
            {
                var sum = Sum(g.Id, amounts, extra);
                if (sum.IsZero && !HasLines(g.Id, amounts, extra)) continue;
                body.Add(Line(side, LayoutLineKind.Group, depth, g.Name, g.Id, null, sum));
                Ledgers(side, g.Id, depth + 1, amounts, body);
                if (extra is not null && extra.Group == g.Id && !extra.Amount.IsZero)
                    body.Add(Line(side, extra.Kind, depth + 1, "", g.Id, null, extra.Amount));
                Tree(side, g.Id, depth + 1, amounts, extra, body);
            }
        }

        void Ledgers(LayoutSide side, GroupId group, int depth, IReadOnlyDictionary<LedgerId, Pair> amounts, List<LayoutLine> body)
        {
            foreach (var l in ledgers[group])
                if (amounts.TryGetValue(l.Id, out var a) && !a.IsZero)
                    body.Add(Line(side, LayoutLineKind.Ledger, depth, l.Name, group, l.Id, a));
        }

        Pair Sum(GroupId group, IReadOnlyDictionary<LedgerId, Pair> amounts, Extra? extra)
        {
            var sum = default(Pair);
            foreach (var l in ledgers[group]) sum += amounts.GetValueOrDefault(l.Id);
            if (extra is not null && extra.Group == group) sum += extra.Amount;
            foreach (var g in children[group]) sum += Sum(g.Id, amounts, extra);
            return sum;
        }

        // A group whose amounts cancel out still shows when something under it is not zero.
        bool HasLines(GroupId group, IReadOnlyDictionary<LedgerId, Pair> amounts, Extra? extra) =>
            ledgers[group].Any(l => amounts.TryGetValue(l.Id, out var a) && !a.IsZero)
            || (extra is not null && extra.Group == group && !extra.Amount.IsZero)
            || children[group].Any(g => HasLines(g.Id, amounts, extra));
    }
}
