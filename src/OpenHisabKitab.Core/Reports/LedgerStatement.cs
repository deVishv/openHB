using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Reports;

public sealed record StatementRow(DateOnly Date, long VoucherId, Money Amount, Money Balance);

public sealed record LedgerStatement(LedgerId Ledger, DateRange Period, Money Opening, IReadOnlyList<StatementRow> Rows, Money Closing);
