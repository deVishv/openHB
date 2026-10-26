using OpenHisabKitab.Core.Tests.Fixtures;

namespace OpenHisabKitab.Core.Tests.Reports;

/// <summary>Spec 5.2 invariants 1 to 7, checked on 100 random companies. Every message names the seed.</summary>
public class InvariantTests
{
    static readonly FinancialYear Fy25 = new(2025);
    static readonly FinancialYear Fy26 = new(2026);

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var s = 1; s <= 100; s++) data.Add(s);
        return data;
    }

    static void Same<T>(int seed, string what, T expected, T actual) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"seed {seed}, {what}: expected {expected}, actual {actual}");

    static DateOnly RandomDate(Random rng, FinancialYear fy) => fy.Start.AddDays(rng.Next(fy.End.DayNumber - fy.Start.DayNumber + 1));

    static Money Closing(TrialBalance tb, LedgerId l) => tb.Rows.FirstOrDefault(r => r.Ledger == l)?.Closing ?? Money.Zero;

    static Money Opening(TrialBalance tb, LedgerId l) => tb.Rows.FirstOrDefault(r => r.Ledger == l)?.Opening ?? Money.Zero;

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariant_1_every_voucher_balances(int seed)
    {
        var (_, _, vouchers) = RandomBooks.Generate(seed);
        foreach (var v in vouchers) Same(seed, $"voucher {v.Id} sum", Money.Zero, Money.Sum(v.Lines.Select(l => l.Amount)));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariant_2_trial_balance_agrees(int seed)
    {
        var (books, _, _) = RandomBooks.Generate(seed);
        var rng = new Random(seed * 7919);
        var periods = new List<DateRange> { Fy25.Range, Fy26.Range };
        for (var i = 0; i < 5; i++)
        {
            var fy = rng.Next(2) == 0 ? Fy25 : Fy26;
            var a = RandomDate(rng, fy);
            var b = RandomDate(rng, fy);
            periods.Add(a <= b ? new DateRange(a, b) : new DateRange(b, a));
        }
        foreach (var p in periods)
        {
            var tb = books.TrialBalance(p);
            Same(seed, $"trial balance {p.From}..{p.To}", tb.TotalDebit, tb.TotalCredit);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariants_3_and_4_balance_sheet_balances_and_matches_profit(int seed)
    {
        var (books, _, _) = RandomBooks.Generate(seed);
        var rng = new Random(seed * 104729);
        for (var i = 0; i < 10; i++)
        {
            var d = RandomDate(rng, rng.Next(2) == 0 ? Fy25 : Fy26);
            var bs = books.BalanceSheet(d);
            Same(seed, $"balance sheet {d}", bs.TotalAssets, bs.TotalLiabilities);
            var pnl = books.ProfitAndLoss(new DateRange(FinancialYear.Containing(d).Start, d));
            Same(seed, $"profit to {d}", pnl.NetProfit, bs.CurrentProfit);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariant_5_statement_closing_matches_trial_balance(int seed)
    {
        var (books, chart, _) = RandomBooks.Generate(seed);
        foreach (var fy in new[] { Fy25, Fy26 })
        {
            var tb = books.TrialBalance(fy.Range);
            foreach (var l in chart.Ledgers)
                Same(seed, $"{l.Name} closing {fy.Label}", Closing(tb, l.Id), books.Statement(l.Id, fy.Range).Closing);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariant_6_year_carry_forward(int seed)
    {
        var (books, chart, _) = RandomBooks.Generate(seed);
        var tb25 = books.TrialBalance(Fy25.Range);
        var tb26 = books.TrialBalance(Fy26.Range);
        var profitAndLoss = chart.Required(RequiredLedger.ProfitAndLoss);
        foreach (var l in chart.Ledgers)
        {
            var nature = chart.NatureOf(l.Id);
            if (nature is AccountNature.Income or AccountNature.Expense)
                Same(seed, $"{l.Name} opens FY 2026-27 at zero", Money.Zero, Opening(tb26, l.Id));
            else if (l.Id != profitAndLoss)
                Same(seed, $"{l.Name} carries forward", Closing(tb25, l.Id), Opening(tb26, l.Id));
        }
        var netProfit25 = books.ProfitAndLoss(Fy25.Range).NetProfit;
        Same(seed, "Profit and Loss carry", Closing(tb25, profitAndLoss) - netProfit25, Opening(tb26, profitAndLoss));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariant_7_tax_ledgers_match_invoice_totals(int seed)
    {
        var (_, chart, vouchers) = RandomBooks.Generate(seed);
        var output = new[] { RequiredLedger.OutputCgst, RequiredLedger.OutputSgst, RequiredLedger.OutputUtgst, RequiredLedger.OutputIgst, RequiredLedger.OutputCess }
            .Select(chart.Required).ToArray();
        var input = new[] { RequiredLedger.InputCgst, RequiredLedger.InputSgst, RequiredLedger.InputUtgst, RequiredLedger.InputIgst, RequiredLedger.InputCess }
            .Select(chart.Required).ToArray();

        foreach (var v in vouchers.Where(v => v.Invoice is not null))
        {
            var inv = v.Invoice!;
            var r = inv.Result;
            Money[] taxes = [r.Cgst, r.Sgst, r.Utgst, r.Igst, r.Cess];
            var sign = inv.Kind == GstDocumentKind.CreditNote ? -1 : 1;
            Money Posted(LedgerId l) => v.Lines.FirstOrDefault(x => x.Ledger == l)?.Amount ?? Money.Zero;
            Money Signed(Money m, int s) => s * sign == 1 ? m : -m;

            for (var i = 0; i < taxes.Length; i++)
            {
                if (inv.Direction == TradeDirection.Outward)
                {
                    Same(seed, $"voucher {v.Id} output tax {i}", Signed(taxes[i], -1), Posted(output[i]));
                    Same(seed, $"voucher {v.Id} input tax {i}", Money.Zero, Posted(input[i]));
                }
                else
                {
                    Same(seed, $"voucher {v.Id} input tax {i}", Signed(taxes[i], 1), Posted(input[i]));
                    Same(seed, $"voucher {v.Id} output tax {i}", inv.ReverseCharge ? Signed(taxes[i], -1) : Money.Zero, Posted(output[i]));
                }
            }
        }
    }
}
