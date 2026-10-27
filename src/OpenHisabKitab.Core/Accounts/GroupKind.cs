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
}
