namespace OpenHisabKitab.Core.Accounts;

/// <summary>Marks groups that the software needs to find by role, not by name.</summary>
public enum GroupKind
{
    None,
    Capital,
    ReservesAndSurplus,
    TradePayables,
    DutiesAndTaxes,
    GstInputCredit,
    CashInHand,
    BankAccounts,
    TradeReceivables,
    RevenueFromOperations,
    OtherIncome,
    Purchases,
    OtherExpenses,
    BankOverdraft,
    // Schedule III headings and the line items reports need by role (Plan 6). Appended so stored names keep their meaning.
    Equity,
    NonCurrentLiabilities,
    LongTermBorrowings,
    CurrentLiabilities,
    ShortTermBorrowings,
    NonCurrentAssets,
    FixedAssets,
    Investments,
    CurrentAssets,
    Income,
    Expenses,
    Depreciation,
}
