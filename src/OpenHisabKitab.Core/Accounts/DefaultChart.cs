namespace OpenHisabKitab.Core.Accounts;

/// <summary>The chart every new company starts with, following Schedule III headings.</summary>
public static class DefaultChart
{
    public static ChartOfAccounts Create()
    {
        var c = ChartOfAccounts.Empty;

        GroupId Root(string name, AccountNature nature)
        {
            (c, var id) = c.AddRootGroup(name, nature);
            return id;
        }

        GroupId Sub(string name, GroupId parent, GroupKind kind = GroupKind.None)
        {
            (c, var id) = c.AddGroup(name, parent, kind);
            return id;
        }

        void Ledger(string name, GroupId group, RequiredLedger r) => (c, _) = c.AddLedger(name, group, r);

        var equity = Root("Equity", AccountNature.Liability);
        var capital = Sub("Capital", equity, GroupKind.Capital);
        var reserves = Sub("Reserves and Surplus", equity, GroupKind.ReservesAndSurplus);

        var nonCurrentLiabilities = Root("Non Current Liabilities", AccountNature.Liability);
        Sub("Long Term Borrowings", nonCurrentLiabilities);

        var currentLiabilities = Root("Current Liabilities", AccountNature.Liability);
        Sub("Short Term Borrowings", currentLiabilities);
        Sub("Trade Payables", currentLiabilities, GroupKind.TradePayables);
        var duties = Sub("Duties and Taxes", currentLiabilities, GroupKind.DutiesAndTaxes);
        Sub("Provisions", currentLiabilities);
        Sub("Other Current Liabilities", currentLiabilities);

        var nonCurrentAssets = Root("Non Current Assets", AccountNature.Asset);
        Sub("Fixed Assets", nonCurrentAssets);
        Sub("Investments", nonCurrentAssets);

        var currentAssets = Root("Current Assets", AccountNature.Asset);
        Sub("Inventories", currentAssets);
        Sub("Trade Receivables", currentAssets, GroupKind.TradeReceivables);
        var cashInHand = Sub("Cash in Hand", currentAssets, GroupKind.CashInHand);
        Sub("Bank Accounts", currentAssets, GroupKind.BankAccounts);
        var gstInput = Sub("GST Input Credit", currentAssets, GroupKind.GstInputCredit);
        Sub("Loans and Advances", currentAssets);
        Sub("Other Current Assets", currentAssets);

        var income = Root("Income", AccountNature.Income);
        var revenue = Sub("Revenue from Operations", income, GroupKind.RevenueFromOperations);
        Sub("Other Income", income, GroupKind.OtherIncome);

        var expenses = Root("Expenses", AccountNature.Expense);
        var purchases = Sub("Purchases", expenses, GroupKind.Purchases);
        Sub("Direct Expenses", expenses);
        Sub("Employee Benefits", expenses);
        Sub("Finance Costs", expenses);
        Sub("Depreciation", expenses);
        var otherExpenses = Sub("Other Expenses", expenses, GroupKind.OtherExpenses);

        Ledger("Cash", cashInHand, RequiredLedger.Cash);
        Ledger("Capital", capital, RequiredLedger.Capital);
        Ledger("Profit and Loss", reserves, RequiredLedger.ProfitAndLoss);
        Ledger("Round Off", otherExpenses, RequiredLedger.RoundOff);
        Ledger("Sales", revenue, RequiredLedger.Sales);
        Ledger("Purchases", purchases, RequiredLedger.Purchases);
        Ledger("Output CGST", duties, RequiredLedger.OutputCgst);
        Ledger("Output SGST", duties, RequiredLedger.OutputSgst);
        Ledger("Output UTGST", duties, RequiredLedger.OutputUtgst);
        Ledger("Output IGST", duties, RequiredLedger.OutputIgst);
        Ledger("Output Cess", duties, RequiredLedger.OutputCess);
        Ledger("Input CGST", gstInput, RequiredLedger.InputCgst);
        Ledger("Input SGST", gstInput, RequiredLedger.InputSgst);
        Ledger("Input UTGST", gstInput, RequiredLedger.InputUtgst);
        Ledger("Input IGST", gstInput, RequiredLedger.InputIgst);
        Ledger("Input Cess", gstInput, RequiredLedger.InputCess);
        return c;
    }
}
