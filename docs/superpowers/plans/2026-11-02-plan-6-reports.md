# Plan 6: Reports, Audit Trail, and Backup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Users read their books from the keyboard. The plan adds:
- the seven report screens `DAYB`, `LEDG`, `TRLB`, `PNLS`, `BSHT`, `CFLO`, and `OUTS`;
- drill-down from every report line to the next level, down to the voucher;
- CSV export of every report;
- the `AUDT` and `BKUP` screens.

**Architecture:**
- **Core** gains:
  - group kinds for the Schedule III headings;
  - `ReportLayout`, which builds the Schedule III and chart layouts of `BSHT` and `PNLS`, with a previous year column;
  - `Books.CashFlow`, using the indirect method;
  - `Outstanding`, which ages bills;
  - the setting `outstanding.ageingDays`.
  
  All of these read only `Books` and the chart (spec 5.1). No report sums postings on its own.
- **Data** gains:
  - company migration `0003`, which marks the default groups with their new kinds;
  - `BillRepository.AllBillsAsAt`, the bills of every party as at a date;
  - `VoucherRepository.Heads`, voucher numbers and counter ledgers for one statement page.
- **Host** gains:
  - five `Reports` methods: two layouts, cash flow, outstanding, and paged statements;
  - their REST routes and MCP tools;
  - the backup settings in the host settings list.
- **Client.Model** gains:
  - one `ReportScreen` base with a table model and CSV export;
  - the seven report screens, `AUDT`, and `BKUP`.
  
  The WPF project gets one `ReportView` that shows any report table, plus `AuditView` and `BackupView`.

**Tech Stack:** As in Plan 5: .NET 10, WPF, `CommunityToolkit.Mvvm` 8.4.2, gRPC, SQLite, `ModelContextProtocol.AspNetCore` 2.3.0, xUnit 2.9.3, and `FlaUI.UIA3` 5.0.0. No new packages. PDFsharp 6.2.4 comes in Plan 6b.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. These sections apply:
- 4.1 (REST and MCP: "every report in section 6.3 that exists in the build");
- 5.1 and 5.2 (invariants 2 to 5 and 9);
- 5.5 (`AUDT` verifies the chain);
- 6.2 (dates, periods, names);
- 6.3 (the Reports rows, `AUDT`, `BKUP`: "Every report can drill down", "exported to CSV");
- 6.5, 6.6 (Dr and Cr, brackets, Indian grouping), and 6.7;
- 7 (ageing buckets are a company setting);
- 8 (Data entry sees `DAYB`, own vouchers only);
- 9 (backup);
- 10 (`TRLB`, `PNLS`, `BSHT` under 1 s);
- 11 (golden fixtures);
- 13 (Schedule III chart, bill by bill settlement, `OUTS`).

**Scope, decided by the owner on 2026-11-02:**
- **In:**
  - the seven report screens with drill-down and CSV export;
  - their REST routes and MCP tools;
  - `AUDT` and `BKUP`, including the backup schedule (owner review Plan 5 item 2);
  - the `BKUP` restore of a company file.
- **Out, to Plan 6b:**
  - `PRNT`;
  - PDF export of reports;
  - printed invoices (Rule 46).
  
  Plan 6b uses PDFsharp 6.2.4, pinned. That is the last MIT version; 6.5.0 and later are AGPL. The owner chose it on 2026-11-02.
- **Out, to Plan 8:** restoring `host.db` (Plan 2 decision 10).

**Owner approvals on 2026-11-02:**
- Plan 6 owner review items 1 to 3 are approved as recommended: `OUTS` ageing, the `CFLO` classification, and the Schedule III layout.
- Execution is **native**: one session builds every task with superpowers:executing-plans, then one fresh reviewer on the most capable model checks the whole branch.
- The plan docs are committed to `main` before the branch.
- Approved after the build: backups go only to a drive of the host PC (never a network path or a path through a link), and the `PNLS` result line reads `Net profit / (loss)` when this year and last year differ in sign. These were owner review items 4 and 5.

**Roadmap as it now stands.**

| Plan | Covers |
|---|---|
| 6 | Reports screens, CSV, `AUDT`, `BKUP` (this plan) |
| 6b | `PRNT`, PDF (PDFsharp 6.2.4), printed invoices |
| 7 | GST files: `GST1`, `GS3B`, `G2BR`, `EINV`; checked cess rates |
| 8 | Custom roles, host.db restore, installer, release |

## Global Constraints

- **Earlier rules still hold.** Everything in the Global Constraints of Plans 2 to 5 still holds:
  - build settings and central package versions;
  - money as `long` paise;
  - `TimeProvider`;
  - one line messages;
  - no WPF in `Client.Model`;
  - all screen text in `Resources/Text.resx`;
  - `{t:Txt}` and `{t:Name}` in XAML;
  - 30 second call deadlines;
  - the remote ready rules;
  - UI tests behind `OHK_UI_TESTS=1`;
  - paging (`Limit` 0 means 500, more than 1000 is refused);
  - every screen loads in at most three host calls.
- **Branch.** All work happens on the branch `plan-6-reports`. The plan docs are committed to `main` before the branch is made, as in Plan 5.
  - Nothing is pushed or merged until the owner says so.
  - `/security-review` runs before merging.
- **Messages** in this plan are exact text. Host messages are shown in the client as received.
- **Amounts on screen** use `AmountText` (Task 8):
  - Indian grouping;
  - negatives in brackets, or with a minus when `ui.negativeAmounts` is `minus`;
  - `Dr` and `Cr` wherever a column holds either side (spec 6.6).
- **Dates on screen** use `DateText` with `ui.dateFormat`.
- **Report periods** come from the command or the screen's period box:
  - They are read by `PeriodInput.TryParse` or two `DateInput` dates.
  - They must sit in one financial year.
  - With no period given, the period is the open year's start to the working date. When the working date falls outside the open year, it is the whole open year.
  - An "as at" date defaults to the working date, clamped into the open year the same way.
- **Every report method in the host** goes through `ReportService.Open`. So it needs `ViewReports` and the company, and refuses a period across two years with `Choose a period within one financial year.`
  - `DAYB` is the exception: it uses `Vouchers.List`, which already limits Data entry to their own vouchers.
- **Drill-down commands** always carry explicit dates in `dd-mm-yyyy` form, such as `LEDG Rent 01-04-2026 31-03-2027`. Back then returns to the report, and a name that ends in a word like `apr` is never read as a period.
- **CSV** (Task 8):
  - UTF-8 with a byte order mark;
  - CRLF line ends, and quoting as RFC 4180 describes;
  - amounts as plain signed decimals with two places, such as `-12345.60`;
  - any text cell that starts with `=`, `+`, `-`, `@`, a tab, or a carriage return gets a leading `'`.
  
  Export needs the `Export` permission. The screen hides it otherwise.

## Review Focus

These failure modes are implied by the spec but not exercised by any example. Each has a test in the task named.

1. **`OUTS` as at a past date.**
   - A receipt dated after the "as at" date must not reduce a bill.
   - A bill dated after it must not appear.
   - For every party, the bills' open amounts plus the on account amount equal the party's ledger closing from `Books` on that date (spec 5.1).
   - Tests in Tasks 4 and 6.
2. **`CFLO` must reconcile.** This covers depreciation, a bank overdraft, capital brought in, and an opening difference. In each case, opening cash plus the three section totals equals the closing cash, and that equals the `Cash and cash equivalents` line of `BSHT` on the same date (invariant 9). Tests in Task 3, on hand worked and random books.
3. **Paged `LEDG`.** Page 2's opening balance equals page 1's last running balance. The last page's closing equals the `TRLB` closing (invariant 5). A ledger with more than one page of entries on one date splits cleanly. Test in Task 5.
4. **Permissions on every path.**
   - Data entry opens `DAYB` and sees only their own vouchers.
   - Every other report is refused to Data entry through gRPC, REST, and MCP.
   - Viewer can read reports but sees no export.
   - Without `ViewAudit`, `AUDT` shows the host refusal.
   - Only Owner can restore.
   - Tests in Tasks 5, 7, 9, 12, and 13.
5. **Periods and names typed at the prompt.**
   - `TRLB 1-3 30-4` crosses a year and says `Choose a period within one financial year.`.
   - `LEDG Rent apr` is Rent for April.
   - A drill into a ledger named `Sales apr` opens that ledger for the stated dates.
   - `OUTS payable 31-07-2026` reads both arguments.
   - Tests in Tasks 8, 9, and 11.

---

### Task 1: Group kinds for the Schedule III headings

**Files:**
- Modify: `src/OpenHisabKitab.Core/Accounts/GroupKind.cs`, `DefaultChart.cs`, `ChartOfAccounts.cs` (the root group method takes a kind)
- Create: `src/OpenHisabKitab.Data/Migrations/Company/0003_report_group_kinds.sql`
- Test: `tests/OpenHisabKitab.Core.Tests/Accounts/DefaultChartTests.cs`, `tests/OpenHisabKitab.Data.Tests/Migrations/CompanyMigrationTests.cs`

**Interfaces:**
- Produces: these `GroupKind` values, appended after `BankOverdraft` so stored names never change meaning:
  - `Equity`, `NonCurrentLiabilities`, `LongTermBorrowings`, `CurrentLiabilities`, `ShortTermBorrowings`;
  - `NonCurrentAssets`, `FixedAssets`, `Investments`, `CurrentAssets`;
  - `Income`, `Expenses`, `Depreciation`.
  
  `ChartOfAccounts.RootOf(GroupId) -> AccountGroup` returns the group's root, and `ChartOfAccounts.LevelOneOf(GroupId) -> AccountGroup?` returns its ancestor directly under a root (itself when it is one, null for a root). Later tasks use both.

The default chart gives each new group its kind. The migration marks the same groups in existing company files.
- `ChartOfAccounts.NextGroupId` numbers groups from 1 in creation order (max id + 1). `CompanyRepository.Create` saves them through `ChartRepository.SaveGroup`, which writes those ids as they are. So every company's default groups have ids 1 to 33, checked on 2026-11-02.
- The migration sets a kind by id, only where `kind = 'None'` and the parent id is the default one. So a group the user renamed keeps its kind, and nothing else changes.

| id | Group | Kind | Parent id |
|---|---|---|---|
| 1 | Equity | `Equity` | none |
| 4 | Non Current Liabilities | `NonCurrentLiabilities` | none |
| 5 | Long Term Borrowings | `LongTermBorrowings` | 4 |
| 6 | Current Liabilities | `CurrentLiabilities` | none |
| 7 | Short Term Borrowings | `ShortTermBorrowings` | 6 |
| 13 | Non Current Assets | `NonCurrentAssets` | none |
| 14 | Fixed Assets | `FixedAssets` | 13 |
| 15 | Investments | `Investments` | 13 |
| 16 | Current Assets | `CurrentAssets` | none |
| 24 | Income | `Income` | none |
| 27 | Expenses | `Expenses` | none |
| 32 | Depreciation | `Depreciation` | 27 |

- [ ] **Step 1: Write the failing tests:**
  - `Default_chart_marks_every_heading_kind`: each kind in the table is on the group with that name and id.
  - `Level_one_of_a_nested_group_is_its_heading_child`: a group added under Bank Overdraft gives Short Term Borrowings.
  - `Migration_0003_marks_default_groups_and_keeps_renames`:
    - Create a company file at migration 0002.
    - Rename group 14 to `Plant and Machinery`, and add a user group under group 13.
    - Migrate.
    - Group 14 has kind `FixedAssets` and keeps its new name. The user group stays `None`.
- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/OpenHisabKitab.Core.Tests --filter DefaultChart` fails: the kinds are not defined.
- [ ] **Step 3: Implement** the kinds, the default chart marks, `RootOf`, `LevelOneOf`, and the migration, as one `UPDATE groups SET kind = ... WHERE id = ... AND kind = 'None' AND parent_id IS ...` per row of the table.
- [ ] **Step 4: Verify.** The Core and Data tests pass.
- [ ] **Step 5: Commit** `feat: group kinds for Schedule III headings, with company migration 0003`

---

### Task 2: Schedule III and chart layouts

**Files:**
- Create: `src/OpenHisabKitab.Core/Reports/ReportLayout.cs`
- Modify: `src/OpenHisabKitab.Core/Reports/Books.cs` (`public DateOnly Start`, the books start date)
- Test: `tests/OpenHisabKitab.Core.Tests/Reports/ReportLayoutTests.cs`

**Interfaces:**
- Consumes: `GroupKind`, `RootOf`, `LevelOneOf` (Task 1), `BalanceSheet`, `ProfitAndLoss`.
- Produces:

```csharp
public enum LayoutStyle { ScheduleIII, Chart }
public enum LayoutSide { EquityAndLiabilities, Assets, Income, Expenses }
public enum LayoutLineKind { Heading, Group, Ledger, CashAndEquivalents, ProfitForPeriod, OpeningDifference, HeadingTotal, SideTotal, NetProfit }
public sealed record LayoutLine(LayoutSide Side, LayoutLineKind Kind, int Depth, string Label, GroupId? Group, LedgerId? Ledger,
    Money Current, Money? Previous);
public sealed record StatementLayout(LayoutStyle Style, bool HasPrevious, IReadOnlyList<LayoutLine> Lines);
public static class ReportLayout
{
    public static StatementLayout BalanceSheet(ChartOfAccounts chart, BalanceSheet current, BalanceSheet? previous, LayoutStyle style);
    public static StatementLayout ProfitAndLoss(ChartOfAccounts chart, ProfitAndLoss current, ProfitAndLoss? previous, LayoutStyle style);
}
```

**Rules.**
- **Order of sides.** `BSHT` lists Equity and Liabilities first, then Assets, as Schedule III does. `PNLS` lists Income, then Expenses, then one `NetProfit` line.
- **Headings and totals.** Each side lists its root groups in chart order as `Heading` lines (label: the group name).
  - Each heading ends with a `HeadingTotal`, and each side ends with a `SideTotal`.
  - A heading whose lines are all zero in both columns is left out, along with its total.
- **Schedule III style.** One `Group` line at depth 1 for each group directly under a heading, holding the sum of every ledger beneath it. Groups with `CashInHand` and `BankAccounts` kinds become one `CashAndEquivalents` line, placed where the first of them sits.
- **Chart style.** The whole tree:
  - groups at their depth, with subtree sums;
  - ledgers as `Ledger` lines one level below their group.
- **Current profit.** It belongs to the `ReservesAndSurplus` group.
  - In Schedule III style it is included in that group's line.
  - In chart style it is a `ProfitForPeriod` line under that group.
- **Opening difference.** A non zero opening difference is an `OpeningDifference` line at the end of the Equity and Liabilities side, before its total.
- **Zero lines.** A line whose current and previous amounts are both zero is left out.
- **Signs.** Amounts are positive on their normal side, as in `BalanceSheet` and `ProfitAndLoss`. An asset ledger in credit shows as a negative amount in its own line. Nothing is moved to the other side (owner review, Plan 6 item 3).
- **Labels.** Labels for `CashAndEquivalents`, `ProfitForPeriod`, `OpeningDifference`, `SideTotal`, and `NetProfit` are empty; the client words them.
- **Previous column.**
  - `previous` null means `HasPrevious` is false and every `Previous` is null.
  - Lines are matched across the two columns by group or ledger id. A line present only in the previous year still appears, with `Current` zero.

- [ ] **Step 1: Write the failing tests.** These are on `GoldenCompany`, and every figure is worked by hand from the fixture's comments and `GoldenCompanyTests`.
  - `Schedule_iii_balance_sheet_31_march_2027_with_previous_year`, with the previous year as at 31-03-2026:
    - Equity heading:
      - Capital `50,000.00` (previous `50,000.00`);
      - Reserves and Surplus `1,600.00` (previous `2,000.00`): Profit and Loss `2,000.00` plus the year's loss of `400.00`;
      - heading total `51,600.00` (previous `52,000.00`).
    - Non Current Liabilities is absent.
    - Current Liabilities heading:
      - Trade Payables `2,000.00` (previous `0.00`);
      - Duties and Taxes `2,255.00` (previous `1,800.00`): 857.50, 855.00, 2.50, 180.00, and 360.00;
      - heading total `4,255.00` (previous `1,800.00`).
    - Side total `55,855.00` (previous `53,800.00`).
    - Current Assets heading:
      - Trade Receivables `695.00` (previous `0.00`);
      - Cash and cash equivalents `53,720.00` (previous `52,720.00`);
      - GST Input Credit `1,440.00` (previous `1,080.00`);
      - side total `55,855.00` (previous `53,800.00`).
  - `First_year_has_no_previous_column`: `BalanceSheet(31-03-2026)` with `previous` null gives `HasPrevious` false.
  - `Schedule_iii_profit_and_loss_fy_2026_with_previous_year`:
    - Income:
      - Revenue from Operations `600.04` (previous `10,000.00`);
      - Other Income `1,000.00` (previous `0.00`);
      - total `1,600.04` (previous `10,000.00`).
    - Expenses:
      - Purchases `2,000.00` (previous `6,000.00`);
      - Other Expenses `0.04` (previous `2,000.00`): round off this year, rent last year;
      - total `2,000.04` (previous `8,000.00`).
    - `NetProfit` `-400.00` (previous `2,000.00`).
  - `Chart_style_lists_ledgers_under_their_groups`:
    - Current Assets heading at depth 0, Trade Receivables at depth 1, then `Gupta Enterprises` at depth 2.
    - A `ProfitForPeriod` line of `-400.00` under Reserves and Surplus.
    - Both side totals `55,855.00`.
  - `Opening_difference_is_its_own_line`: a `RandomBooks` seed that `InvariantTests.Some_seeds_have_an_opening_difference` uses gives one `OpeningDifference` line, and the side totals still agree.
  - `Asset_ledger_in_credit_stays_on_its_side`: a bank ledger at `-500.00` shows under Cash and cash equivalents as `-500.00`.
  - `Layout_side_totals_equal_the_report_totals` (theory over `InvariantTests.Seeds()`):
    - `BSHT` side totals equal `TotalAssets` and `TotalLiabilities`.
    - The `PNLS` `NetProfit` line equals `NetProfit`.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** `ReportLayout` and `Books.Start`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Core.Tests` passes.
- [ ] **Step 5: Commit** `feat: Schedule III and chart layouts for balance sheet and profit and loss`

---

### Task 3: Cash flow, indirect method

**Files:**
- Create: `src/OpenHisabKitab.Core/Reports/CashFlow.cs`
- Modify: `src/OpenHisabKitab.Core/Reports/Books.cs` (`CashFlow`, and `public Money Closing(LedgerId ledger, DateOnly asAt)`)
- Test: `tests/OpenHisabKitab.Core.Tests/Reports/CashFlowTests.cs`, `GoldenCompanyTests.cs`, `InvariantTests.cs`

**Interfaces:**
- Consumes: Task 1 kinds, `LevelOneOf`, `RootOf`.
- Produces:

```csharp
public enum CashFlowSection { Operating, Investing, Financing }
public enum CashFlowItem { NetProfit, Depreciation, GroupChange }
public sealed record CashFlowLine(CashFlowSection Section, CashFlowItem Item, GroupId? Group, Money Amount);
public sealed record CashFlowStatement(DateRange Period, IReadOnlyList<CashFlowLine> Lines, Money Operating, Money Investing,
    Money Financing, Money NetChange, Money OpeningCash, Money ClosingCash);
// on Books:
public CashFlowStatement CashFlow(DateRange period);   // RequireSingleYear
public Money Closing(LedgerId ledger, DateOnly asAt);   // the year containing asAt, from its start
```

**Rules** (owner review, Plan 6 item 2):
- **Cash** means every ledger under `CashInHand` or `BankAccounts`. Bank overdraft is a borrowing, not cash.
- **Change.** For every other balance sheet ledger, its change over the period is closing minus opening, debit positive. Its cash effect is minus the change. Each `GroupChange` line sums the effects of one level-one group.
- **Operating.**
  - `NetProfit` comes first: `ProfitAndLoss(period).NetProfit`.
  - Then `Depreciation`: the net debit to ledgers under the `Depreciation` group, added back.
  - Then the changes of level-one groups under `CurrentAssets`, and those under `CurrentLiabilities` except `ShortTermBorrowings`.
- **Investing.** Level-one groups under `NonCurrentAssets`. The `FixedAssets` line also takes away the depreciation added back, so the line is what was spent. A `FixedAssets` line is made for this if needed.
- **Financing.** Level-one groups under `Equity` and `NonCurrentLiabilities`, plus `ShortTermBorrowings`.
- **Zero lines** are left out. Lines keep chart order within each section.
- **Cash balances.** `OpeningCash` is the cash ledgers' opening for the period, and `ClosingCash` is their closing. `NetChange` is the sum of the three sections, and it always equals `ClosingCash - OpeningCash`.

- [ ] **Step 1: Write the failing tests:**
  - `Golden_cash_flow_fy_2025`: these figures are worked by hand.
    - Lines: NetProfit `2,000.00`, Duties and Taxes `1,800.00`, GST Input Credit `-1,080.00`.
    - Operating `2,720.00`; Investing and Financing `0.00`.
    - Opening `50,000.00`, closing `52,720.00`.
  - `Golden_cash_flow_fy_2026`:
    - Lines in chart order: NetProfit `-400.00`, Trade Payables `2,000.00`, Duties and Taxes `455.00`, Trade Receivables `-695.00`, GST Input Credit `-360.00`.
    - Operating `1,000.00`.
    - Opening `52,720.00`, closing `53,720.00`.
  - `Depreciation_overdraft_and_capital`. The books start 1-4-2026 with Cash `10,000.00` Dr and Capital `10,000.00` Cr. The vouchers:
    - Machine (Fixed Assets) Dr / Cash Cr `6,000.00`;
    - Depreciation Dr / Machine Cr `600.00`;
    - Cash Dr / an overdraft ledger under Bank Overdraft Cr `3,000.00`;
    - Cash Dr / Capital Cr `2,000.00`;
    - Cash Dr / Sales Cr `5,000.00`.
    
    The expected figures:
    - Operating: NetProfit `4,400.00` plus Depreciation `600.00`, so `5,000.00`.
    - Investing: Fixed Assets `-6,000.00`.
    - Financing, in chart order: Capital `2,000.00`, then Short Term Borrowings `3,000.00`, so `5,000.00`.
    - NetChange `4,000.00`; opening `10,000.00`, closing `14,000.00`.
  - `Period_across_years_is_refused`: `ArgumentException`.
  - `Invariant_9_cash_flow_closing_equals_balance_sheet_cash` (theory over `InvariantTests.Seeds()`), for each financial year in the seed:
    - `ClosingCash` equals the `CashAndEquivalents` line of `ReportLayout.BalanceSheet` (Schedule III) at the period end.
    - `NetChange` equals `ClosingCash - OpeningCash`.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Core tests pass.
- [ ] **Step 5: Commit** `feat: cash flow statement by the indirect method`

---

### Task 4: Outstanding and ageing

**Files:**
- Create: `src/OpenHisabKitab.Core/Bills/Outstanding.cs`, `src/OpenHisabKitab.Core/Bills/AgeingBuckets.cs`
- Modify: `src/OpenHisabKitab.Core/Settings/SettingDefinition.cs`, `SettingsCatalog.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Bills/OutstandingTests.cs`, `tests/OpenHisabKitab.Core.Tests/Settings/SettingsCatalogTests.cs`

**Interfaces:**
- Consumes: `OpenBill`, `PartySide`.
- Produces:

```csharp
public sealed record AgeingBuckets(IReadOnlyList<int> Days)
{
    public static AgeingBuckets Default { get; }                 // 30, 60, 90
    public static bool TryParse(string text, out AgeingBuckets buckets);   // "30,60,90"; 1 to 5 rising whole numbers, 1 to 999
    public int BucketOf(int daysOverdue);                        // 0 not due; 1..Days.Count; Days.Count + 1 beyond the last
    public int Count => Days.Count + 2;
}
public sealed record OutstandingBill(OpenBill Bill, DateOnly Due, int DaysOverdue, int Bucket);
public sealed record OutstandingParty(LedgerId Party, IReadOnlyList<OutstandingBill> Bills, IReadOnlyList<Money> Buckets,
    Money OnAccount, Money Total);
public sealed record OutstandingReport(DateOnly AsAt, PartySide Side, AgeingBuckets Ageing, IReadOnlyList<OutstandingParty> Parties,
    IReadOnlyList<Money> BucketTotals, Money OnAccountTotal, Money Total);
public sealed record PartyPosition(LedgerId Party, Money Closing, IReadOnlyList<OpenBill> Bills);
public static class Outstanding
{
    public static OutstandingReport Build(DateOnly asAt, PartySide side, AgeingBuckets ageing, IEnumerable<PartyPosition> parties);
}
```

**Rules** (owner review, Plan 6 item 1):
- **Due date.** A bill's due date is `DueDate` when set, otherwise the bill date.
- **Days overdue** is the as at date minus the due date. Zero or less is "not due".
- **Total** is the party's closing on its side: debit positive for receivables, credit positive for payables.
- **Open bills.** Bills with an open amount of zero or less are left out.
- **On account** is the total minus the sum of open bills. It is not aged.
- **Parties.** A party with no open bills and a zero total is left out. Parties keep their input order.
- **The setting:** `outstanding.ageingDays`, Text, company scope, default `"30,60,90"`, description `Ageing columns in OUTS, as days overdue.`
  - `SettingDefinition` gains an optional `Func<string, bool>? Rule` and `string? RuleText`.
  - `Validate` uses them after the type check. Here the message is `Setting outstanding.ageingDays must be 1 to 5 rising whole numbers of days, such as 30,60,90.`

- [ ] **Step 1: Write the failing tests:**
  - `Ages_bills_into_buckets_with_on_account`. As at 31-07-2026 with buckets 30, 60, 90, party A has a receivable closing of `9,000.00` Dr. Its bills:
    - B1, dated 1-4, due 1-5, amount `5,000.00`, settled `1,000.00`: 91 days overdue, so it goes beyond 90;
    - B2, dated 15-7, due 14-8, amount `3,000.00`: not due;
    - B3, dated 20-5, due 20-6, amount `2,500.00`: 41 days overdue, so it goes in 31 to 60.
    
    The expected buckets are `3,000.00 | 0.00 | 2,500.00 | 0.00 | 4,000.00`, on account `-500.00`, and total `9,000.00`.
  - `Payable_side_turns_credit_positive`: a supplier at `-2,000.00` with one open bill of `2,000.00` gives a total of `2,000.00` and on account `0.00`.
  - `Opening_bill_without_due_date_ages_from_its_date`
  - `Bucket_totals_sum_the_parties`
  - `Ageing_setting_rejects_falling_or_too_many_days`:
    - `"60,30"` and `"10,20,30,40,50,60"` give the rule message;
    - `"45"` is accepted.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Core tests pass.
- [ ] **Step 5: Commit** `feat: outstanding bills with ageing buckets and the outstanding.ageingDays setting`

---

### Task 5: Host: layouts, cash flow, and ledger pages

**Files:**
- Modify:
  - `src/OpenHisabKitab.Contracts/Protos/reports.proto`
  - `src/OpenHisabKitab.Host/Application/ReportService.cs` (it now also takes `CompanyHub`, for `Heads`)
  - `src/OpenHisabKitab.Host/Grpc/Endpoints.cs` (`ReportsEndpoint`)
  - `src/OpenHisabKitab.Host/Grpc/Wire.cs`
  - `src/OpenHisabKitab.Data/CompanyDb/VoucherRepository.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/ReportServiceTests.cs`, `tests/OpenHisabKitab.Data.Tests/VoucherRepositoryTests.cs`

**Interfaces:**
- Consumes: Tasks 2 and 3.
- Produces, in the proto:

```proto
rpc BalanceSheetLayout (LayoutAsAtRequest) returns (LayoutMsg);
rpc ProfitAndLossLayout (LayoutPeriodRequest) returns (LayoutMsg);
rpc CashFlow (PeriodRequest) returns (CashFlowMsg);
rpc LedgerStatementPage (StatementPageRequest) returns (StatementPageMsg);

// style: "ScheduleIII" or "Chart". The previous year column is filled when has_previous is set.
message LayoutAsAtRequest { int32 company = 1; string as_at = 2; string style = 3; }
message LayoutPeriodRequest { int32 company = 1; Period period = 2; string style = 3; }
message LayoutLineMsg { string side = 1; string kind = 2; int32 depth = 3; string label = 4; int32 group = 5; int32 ledger = 6;
  int64 current = 7; int64 previous = 8; }
message LayoutMsg { string style = 1; bool has_previous = 2; repeated LayoutLineMsg lines = 3; }
message CashFlowLineMsg { string section = 1; string item = 2; int32 group = 3; int64 amount = 4; }
message CashFlowMsg { Period period = 1; repeated CashFlowLineMsg lines = 2; int64 operating = 3; int64 investing = 4;
  int64 financing = 5; int64 net_change = 6; int64 opening_cash = 7; int64 closing_cash = 8; }
message StatementPageRequest { int32 company = 1; int32 ledger = 2; Period period = 3; PageMsg page = 4; }
// counter_ledgers: the voucher's other ledgers, largest amount first.
message StatementLineMsg { string date = 1; int64 voucher_id = 2; string type = 3; string number = 4; string narration = 5;
  int64 amount = 6; int64 balance = 7; repeated int32 counter_ledgers = 8; }
// opening: the balance before this page's first line. closing: the statement's closing, on every page.
message StatementPageMsg { int32 ledger = 1; Period period = 2; int64 opening = 3; repeated StatementLineMsg lines = 4;
  int64 closing = 5; string next = 6; }
```

- Produces, in `ReportService`:
  - `StatementLayout BalanceSheetLayout(CallContext ctx, int company, DateOnly asAt, LayoutStyle style)`
  - `StatementLayout ProfitAndLossLayout(CallContext ctx, int company, DateRange period, LayoutStyle style)`
  - `CashFlowStatement CashFlow(CallContext ctx, int company, DateRange period)`
  - `StatementPage StatementPage(CallContext ctx, int company, LedgerId ledger, DateRange period, PageRequest page)`
  - `public sealed record StatementLine(StatementRow Row, VoucherType Type, string Number, string Narration, IReadOnlyList<LedgerId> Counter)`
  - `public sealed record StatementPage(LedgerId Ledger, DateRange Period, Money Opening, IReadOnlyList<StatementLine> Lines, Money Closing, string Next)`
- Produces, in `VoucherRepository`: `IReadOnlyDictionary<long, VoucherHead> Heads(SqliteConnection c, IReadOnlyCollection<long> ids)` in one query, with the record `VoucherHead(long Id, VoucherType Type, string Number, string Narration)`.

**Rules:**
- **Previous year.**
  - For a balance sheet, the previous date is `asAt.AddYears(-1)`.
  - For profit and loss, the previous period is the same period a year earlier.
  - The previous column is filled only when the year containing it starts on or after `Books.Start`.
- **Statement pages.**
  - The page cursor is `Paging.DateCursor(date, voucherId)` of the page's last line, as `Vouchers.List` uses. A ledger has one posting per voucher (spec 5.1), and `Books` sorts by date and then voucher id, so the cursor is unique. A save between two page requests can then neither repeat nor skip a line.
  - `Paging.Limit` and `Paging`'s existing bad-cursor refusal apply.
  - Each page's `Opening` is the running balance just before its first line, so it already counts any voucher saved since the last page.
  - `Counter` comes from `CompanyBooks`: the voucher's postings other than this ledger, largest first. Add `CompanyBooks.PostingsOf(int company, long voucherId)` under the same lock.
  - `Heads` covers the page's voucher ids in one query.
- **Unknown style.** An unknown `style` string gives `style must be ScheduleIII or Chart.`

- [ ] **Step 1: Write the failing tests.** Seed through `CompanyBooks.Seed` and real saves, as `ReportServiceTests` does now.
  - `Balance_sheet_layout_fills_the_previous_year_from_the_second_year`
  - `Cash_flow_reconciles_after_saves`
  - `Statement_pages_continue_the_running_balance`: 1,203 cash receipts on one date, saved through `VoucherService` (not `Seed`, because `Heads` reads the database), with limit 500.
    - Pages hold 500, 500, and 203 lines.
    - Each page's `Opening` equals the previous page's last `Balance`.
    - The last `Closing` equals the `TrialBalance` closing for Cash.
  - `Statement_line_names_its_voucher_and_counter_ledgers`
  - `Receipt_saved_between_pages_is_not_repeated_or_skipped`: a receipt dated before the cursor changes page 2's `Opening`, and adds no line to page 2.
  - `Data_entry_is_refused_every_new_report`: each of the four methods gives `PermissionDenied`.
  - `Period_across_years_is_refused_with_one_line`: `ProfitAndLossLayout` and `CashFlow` give `Choose a period within one financial year.`
  - `Heads_reads_number_type_and_narration_in_one_query` (Data test)
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the service methods, `Wire` mapping (`LedgerId`/`GroupId` 0 means none), and the endpoint methods.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests` and the Data tests pass.
- [ ] **Step 5: Commit** `feat: host report layouts, cash flow, and paged ledger statements`

---

### Task 6: Host: outstanding

**Files:**
- Modify:
  - `src/OpenHisabKitab.Data/CompanyDb/BillRepository.cs`
  - `src/OpenHisabKitab.Host/Application/ReportService.cs`
  - `reports.proto`, `Endpoints.cs`, `Wire.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/BillRepositoryTests.cs`, `tests/OpenHisabKitab.Host.Tests/OutstandingServiceTests.cs`

**Interfaces:**
- Consumes: Task 4, `Books.Closing` (Task 3), `MasterService.SideOf`.
- Produces:
  - `BillRepository.AllBillsAsAt(SqliteConnection c, DateOnly asAt, IReadOnlyDictionary<LedgerId, (PartySide Side, int CreditDays)> parties) -> IReadOnlyDictionary<LedgerId, IReadOnlyList<OpenBill>>`, in three queries: opening bills, voucher nets by voucher and party, and allocations.
    - Bills are the party's opening bills, plus vouchers of the `BillTypes` dated on or before `asAt`, not cancelled, whose net is a bill on the party's side.
    - `Settled` counts only allocations whose own voucher is dated on or before `asAt`.
  - `ReportService.Outstanding(CallContext ctx, int company, DateOnly asAt, PartySide side) -> OutstandingReport`.
    - Parties are those on the side, sorted by name with `StringComparer.CurrentCultureIgnoreCase`.
    - Each closing comes from `Books.Closing`.
    - The ageing comes from the company setting `outstanding.ageingDays`, read with `CompanyRepository.ListSettings(c)` on the same connection. When it is unset or fails to parse, the ageing is `AgeingBuckets.Default`.
  - In the proto:

```proto
rpc Outstanding (OutstandingRequest) returns (OutstandingMsg);
// side: "Receivable" or "Payable".
message OutstandingRequest { int32 company = 1; string as_at = 2; string side = 3; }
message OutstandingBillMsg { string source = 1; int64 id = 2; string number = 3; string party_doc_number = 4; string date = 5;
  string due = 6; int64 amount = 7; int64 open = 8; int32 days_overdue = 9; int32 bucket = 10; }
message OutstandingPartyMsg { int32 party = 1; repeated OutstandingBillMsg bills = 2; repeated int64 buckets = 3; int64 on_account = 4; int64 total = 5; }
message OutstandingMsg { string as_at = 1; string side = 2; repeated int32 ageing_days = 3; repeated OutstandingPartyMsg parties = 4;
  repeated int64 bucket_totals = 5; int64 on_account_total = 6; int64 total = 7; }
```

- [ ] **Step 1: Write the failing tests:**
  - `As_at_leaves_out_later_receipts_and_later_bills` (Data). The setup:
    - a sale of `10,000.00` on 1-6;
    - a receipt on 10-7, allocating `10,000.00` to it;
    - a second sale on 5-8.
    
    As at 30-6, the first sale is open at `10,000.00` and the second is absent. As at 31-7, the first is open at `0.00`.
  - `Cancelled_bill_and_cancelled_receipt_drop_out` (Data)
  - `Open_bills_plus_on_account_equal_the_ledger_closing` (Host). This is Review Focus 1. Make three parties with:
    - opening bills;
    - part allocations;
    - an unallocated receipt;
    - a credit note.
    
    Then, for each of three "as at" dates, the sum of open plus on account equals `Books.Closing`, on the side's sign.
  - `Ageing_follows_the_company_setting`: set `outstanding.ageingDays` to `"15,45"`. Then there are 4 buckets, and a bill 20 days overdue sits in bucket 2.
  - `Data_entry_is_refused_outstanding`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Data and Host tests pass.
- [ ] **Step 5: Commit** `feat: outstanding receivables and payables as at a date`

---

### Task 7: REST routes and MCP tools for the new reports

**Files:**
- Modify: `src/OpenHisabKitab.Host/Api/RestRoutes.cs`, `ApiModels.cs`, `src/OpenHisabKitab.Host/Mcp/ReadTools.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Api/ReportRoutesTests.cs`, `tests/OpenHisabKitab.Host.Tests/Mcp/ReadToolsTests.cs`

**Interfaces:**
- Consumes: Tasks 5 and 6.
- Produces:

| Route | Service call |
|---|---|
| `GET .../reports/balance-sheet/layout?asAt=&style=` | `BalanceSheetLayout` |
| `GET .../reports/profit-and-loss/layout?from=&to=&style=` | `ProfitAndLossLayout` |
| `GET .../reports/cash-flow?from=&to=` | `CashFlow` |
| `GET .../reports/outstanding?asAt=&side=` | `Outstanding` |

- **DTOs.**
  - `LayoutDto(string Style, bool HasPrevious, IReadOnlyList<LayoutLineDto> Lines)`, where `LayoutLineDto(string Side, string Kind, int Depth, string Label, int? GroupId, int? LedgerId, Money Current, Money? Previous)`. The label is the chart name, or the client wording for the fixed kinds, so readers see the screen text.
  - `CashFlowDto`, where each line has `Section`, `Item`, `int? GroupId`, `string? Group`, and `Money Amount`.
  - `OutstandingDto`, where each party has `LedgerId`, `Party`, bills, `Buckets`, `OnAccount`, and `Total`.
- **Arguments.**
  - `style` defaults to `ScheduleIII`, and `side` defaults to `Receivable`.
  - A bad `side` gives 400 `side must be receivable or payable.`
- **MCP tools**, with `ReadOnly = true`:
  - `cash_flow(company, from, to)`;
  - `outstanding(company, asAt, side = "receivable")`;
  - `balance_sheet_layout(company, asAt, style = "ScheduleIII")`;
  - `profit_and_loss_layout(company, from, to, style = "ScheduleIII")`.
  
  Each description ends with the existing `Amounts` and `Dates` text. Results carry the wire amounts and the Indian text, as the existing tools do.
- **Day book and ledger statements** need no new route or tool. The existing `/vouchers?from=&to=` route and `find_vouchers` tool already serve `DAYB`, and `ledger-statement` and `ledger_statement` serve `LEDG`.
- **Wording.** The fixed kind wording lives in `Host/Application/Text.cs` as one switch, because the host cannot read the client's resources. Its values match `Text.resx` (Task 10), and a test compares the two.

- [ ] **Step 1: Write the failing tests:**
  - `Cash_flow_route_amounts_are_strings`
  - `Outstanding_route_reads_side_and_as_at`
  - `Layout_route_defaults_to_schedule_iii`
  - `Bad_side_is_400_with_one_line`
  - `Data_entry_token_gets_403_on_every_report_route` (Review Focus 4)
  - `Mcp_lists_the_four_new_tools_and_cash_flow_returns_indian_text`
  - `Mcp_outstanding_is_refused_for_data_entry`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Host tests pass, and `/api/v1/openapi.json` lists the routes.
- [ ] **Step 5: Commit** `feat: REST routes and MCP tools for cash flow, outstanding, and report layouts`

---

### Task 8: Report screen base, CSV export, `ReportView`, and `TRLB`

**Files:**
- Create:
  - `src/OpenHisabKitab.Client.Model/Formatting/AmountText.cs`
  - `src/OpenHisabKitab.Client.Model/Screens/Reports/ReportTable.cs`, `ReportScreen.cs`, `CsvExport.cs`, `ReportArgs.cs`, `TrialBalanceViewModel.cs`
  - `src/OpenHisabKitab.Client/Views/ReportView.xaml(.cs)`
- Modify:
  - `src/OpenHisabKitab.Core/Commands/Keys/KeyCommands.cs`, `DefaultKeybindings.cs`
  - `src/OpenHisabKitab.Client.Model/Shell/ShellViewModel.cs`
  - `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Reports/CsvExportTests.cs`, `ReportArgsTests.cs`, `TrialBalanceTests.cs`, `tests/OpenHisabKitab.Client.Model.Tests/Formatting/AmountTextTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class AmountText
{
    public static string Format(Money m, EffectiveSettings s);       // ui.negativeAmounts, through IndianNumberFormat.Format
    public static string DrCr(Money m);                              // delegates to IndianNumberFormat.FormatDrCr
}
public enum CellAlign { Left, Right }
public sealed record ReportColumn(string Header, CellAlign Align);
/// Amount, when set, is what CSV writes.
public sealed record ReportCell(string Text, Money? Amount = null);
/// Drill: the command Enter runs, or null.
public sealed record ReportLine(int Depth, IReadOnlyList<ReportCell> Cells, string? Drill, bool Emphasis);
public static class CsvExport { public static void Write(IReadOnlyList<ReportColumn> columns, IEnumerable<ReportLine> lines, Stream to); }
public static class ReportArgs
{
    /// Trailing period tokens (two dates, or one period word) come off the end; the rest is the name.
    public static (string Name, DateRange? Period, string? Error) NameAndPeriod(IReadOnlyList<string> args, DateOnly today);
    public static (string Rest, DateOnly? AsAt, string? Error) RestAndDate(IReadOnlyList<string> args, DateOnly today);
    public static DateRange DefaultPeriod(ShellContext ctx);
    public static DateOnly DefaultAsAt(ShellContext ctx);
    public static string Command(string code, string name, DateRange period);   // "LEDG Rent 01-04-2026 31-03-2027"
}
public abstract partial class ReportScreen : ScreenViewModel, IScreenCommands
{
    public IReadOnlyList<ReportColumn> Columns { get; protected set; }
    public ObservableCollection<ReportLine> Lines { get; }
    public string Heading { get; protected set; }      // "Trial balance, 01-04-2026 to 31-03-2027"
    public bool CanExport { get; }                      // Ctx.Has("Export")
    public bool HasMore { get; protected set; }
    public virtual Task MoreAsync();
    public abstract string ExportName { get; }          // "TRLB 2026-04-01 2027-03-31.csv"
    public event Action? ExportRequested;               // the view shows the save dialog, then calls WriteCsv
    public void WriteCsv(Stream to);
    public Task OpenAsync(ReportLine line, bool newTab);
    // IScreenCommands: "report.export" raises ExportRequested when CanExport; "report.style" is false here, true where a screen has styles.
    public override bool IsAffectedBy(ChangeEventMsg change);   // Voucher, Masters, Settings, Lock, Restore
}
```

- **New commands.**
  - `report.export` is bound to Ctrl+E, and `report.style` to Ctrl+G.
  - Both are added to `KeyCommands` and to the shell's screen command case.
- **`ReportView`.**
  - It builds a `GridView` from `Columns`. Amount columns are right aligned in Consolas.
  - Rows are indented by `Depth` times 16 px in the first column. `Emphasis` rows are bold.
  - Enter opens a line in this tab, and Shift+Enter opens it in a new tab.
  - The More button and End-of-list paging appear while `HasMore` is set.
  - The view's automation name is the heading. Each row's automation name is its cells joined with `, `.
- **`TRLB [group name] [period]`.**
  - Columns: Ledger, Opening, Debit, Credit, Closing. Opening and Closing are shown as Dr/Cr.
  - Rows are ledgers in chart order. Then come an `Opening difference` row when non zero, and a Total row (Debit total, Credit total).
  - A group name limits the rows to ledgers under that group, with a `Total for {group}` row. The heading reads `Trial balance, {group}, {from} to {to}`.
  - A ledger row drills to `LEDG {ledger} {from} {to}`.
  - One host call, `TrialBalance`. Names come from `Ctx.Lookups`.
  - An unknown group name says `No group named {name}.`. Several matches say `{name} matches several groups. Type more of the name.`, through `NameMatcher`.

- [ ] **Step 1: Write the failing tests:**
  - `Csv_has_bom_crlf_quotes_and_plain_amounts`:
    - The bytes start with `EF BB BF`.
    - A cell `Gupta, Pune` is quoted.
    - A cell `=SUM(A1)` becomes `'=SUM(A1)`.
    - An amount `-12,345.60` is written `-12345.60`.
  - `Name_and_period_take_trailing_period_words`: `["Rent","apr"]` gives Rent for April, and `["Sales","apr","01-04-2026","31-03-2027"]` gives `Sales apr` with the two dates.
  - `Period_across_years_says_one_line`: `TRLB 1-3 30-4` gives `Choose a period within one financial year.`
  - `Default_period_is_year_start_to_working_date`
  - `Trial_balance_lists_ledgers_with_dr_cr_and_totals_agree`
  - `Trial_balance_for_a_group_lists_only_its_ledgers`
  - `Enter_on_a_ledger_runs_ledg_with_dates`
  - `Viewer_cannot_export`: `CanExport` is false, and `report.export` cannot run.
  - `Amount_text_uses_brackets_or_minus_by_setting`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Register `TRLB`, and map `ReportScreen` to `ReportView` in `App.xaml`.
- [ ] **Step 4: Verify.** The client model tests pass, and `dotnet build` shows 0 warnings.
- [ ] **Step 5: Commit** `feat: report screen base with CSV export, and TRLB`

---

### Task 9: `LEDG` and `DAYB`

**Files:**
- Create: `Screens/Reports/LedgerViewModel.cs`, `Screens/Reports/DayBookViewModel.cs`, `Screens/Reports/VoucherRowText.cs`
- Modify: `Screens/FindViewModel.cs` (uses `VoucherRowText`), `ScreenCatalog.cs`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Reports/LedgerTests.cs`, `DayBookTests.cs`

**Interfaces:**
- Consumes: `LedgerStatementPage` (Task 5), `Vouchers.List`, `ReportScreen` and `ReportArgs` (Task 8).
- Produces: `VoucherRowText.Party(VoucherMsg v, LookupCache lookups) -> string` and `VoucherRowText.DebitTotal(VoucherMsg v) -> Money`, moved out of `FindViewModel`.

- **`LEDG name [period]`.**
  - The name matches ledgers and parties through `NameMatcher`. With no name, the screen shows `Type a ledger name after LEDG.` and loads nothing.
  - Columns: Date, Particulars, Type, Number, Debit, Credit, Balance (Dr/Cr).
  - The first row is `Opening balance`, and the last is `Closing balance`. Each is shown once, not per page.
  - Particulars is the first counter ledger's name. With more than one, it reads `{name} and {n} more`.
  - Pages hold 500 rows, and More loads the next.
  - A row drills to `{CODE} {number}`.
- **`DAYB [period]`.**
  - The default period is the working date alone.
  - Columns: Date, Type, Number, Party or ledger, Amount, Narration, Status.
  - Cancelled vouchers show `Cancelled` and no amount.
  - Pages hold 500 rows.
  - A row drills to `{CODE} {number}`.
  - Data entry opens it and sees only their own vouchers, because the host filters.

- [ ] **Step 1: Write the failing tests:**
  - `Ledger_pages_continue_the_balance`: with 600 entries, the second page's first balance follows the first page's last.
  - `Ledger_name_ending_in_a_month_word_reads_the_dates`: a ledger `Sales apr` opened by drill shows its own entries.
  - `Ledger_row_opens_the_voucher`
  - `Ledger_without_a_name_asks_for_one`
  - `Day_book_defaults_to_the_working_date`
  - `Data_entry_sees_only_their_own_vouchers_in_day_book` (Review Focus 4)
  - `Find_still_shows_party_and_amount`: the existing FIND tests stay green after the move.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: LEDG and DAYB screens`

---

### Task 10: `PNLS` and `BSHT`

**Files:**
- Create: `Screens/Reports/StatementViewModel.cs` (one class for both codes)
- Modify: `ScreenCatalog.cs`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Reports/StatementTests.cs`

**Interfaces:**
- Consumes: `BalanceSheetLayout`, `ProfitAndLossLayout` (Task 5).
- Produces: `StatementViewModel : ReportScreen`, with `LayoutStyle Style` and `Task ToggleStyleAsync()`.

- **Arguments.** `PNLS [period]`, and `BSHT [as at date]`.
- **Columns.** Particulars, then `{as at or period end}` and, when `HasPrevious`, `{previous date}`.
- **Fixed wording**, in `Text.resx`:
  - `Equity and liabilities`, `Assets`, `Income`, and `Expenses` for the sides;
  - `Cash and cash equivalents`;
  - `Profit for the period`;
  - `Opening difference`;
  - `Total`;
  - `Net profit`, which reads `Net loss` when negative.
- **Style.** Ctrl+G (`report.style`) switches between Schedule III and the chart, and reloads. The heading names the style: `Balance sheet (Schedule III), as at 31-03-2027`.
- **Drill.**
  - A Group or CashAndEquivalents line drills to `TRLB {group} {year start} {date}`. For BSHT, the period runs from the start of the year containing the date.
  - A Ledger line drills to `LEDG`.
  - The NetProfit and ProfitForPeriod lines drill to `PNLS {period}`.

- [ ] **Step 1: Write the failing tests:**
  - `Balance_sheet_shows_schedule_iii_by_default_with_previous_year`
  - `Ctrl_g_switches_to_chart_style`
  - `Net_loss_is_worded_as_loss_in_brackets`
  - `Group_line_drills_to_trial_balance_of_that_group`
  - `Viewer_opens_reports_data_entry_sees_the_host_refusal`
  - `Host_text_matches_client_text_for_fixed_lines`: the host's Task 7 switch equals `Text.resx` for each kind.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: PNLS and BSHT screens with Schedule III and chart styles`

---

### Task 11: `CFLO` and `OUTS`

**Files:**
- Create: `Screens/Reports/CashFlowViewModel.cs`, `Screens/Reports/OutstandingViewModel.cs`
- Modify: `ScreenCatalog.cs`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Reports/CashFlowTests.cs`, `OutstandingTests.cs`

**Interfaces:**
- Consumes: `CashFlow` (Task 5) and `Outstanding` (Task 6).

- **`CFLO [period]`.**
  - Section headings: `Cash from operating activities`, `Cash from investing activities`, `Cash from financing activities`.
  - Line labels: `Net profit`, `Add: depreciation`, and `Change in {group}`.
  - Section totals follow, then `Net change in cash`, `Opening cash and cash equivalents`, and `Closing cash and cash equivalents`.
  - Drill:
    - a group line goes to `TRLB {group} {period}`;
    - Net profit goes to `PNLS {period}`;
    - the cash lines go to `TRLB {cash group} {period}`.
- **`OUTS [receivable|payable|r|p] [party name] [as at date]`.**
  - The side comes first, with `receivable` as the default, and the date comes last.
  - Summary: Party, then one column per bucket (`Not due`, `1-30`, `31-60`, `61-90`, `Over 90`, built from the setting), then `On account` and `Total`. A Total row ends it.
  - A party row drills to `OUTS {side} {party} {date}`. That bill view shows Bill, Party document, Date, Due, Days overdue, Amount, and Open, with the on account line last.
  - A voucher bill drills to `{CODE} {number}`. An opening bill drills nowhere.
  - The heading reads `Receivables as at 31-07-2026` or `Payables as at 31-07-2026`.
- **Bucket headings.** These are made from the days: `1-30`, `Over 90`.

- [ ] **Step 1: Write the failing tests:**
  - `Cash_flow_sections_and_closing_cash`
  - `Cash_flow_line_drills_to_its_group`
  - `Outstanding_reads_side_and_date`: `OUTS payable 31-07-2026` (Review Focus 5).
  - `Outstanding_party_drills_to_its_bills_and_a_bill_to_its_voucher`
  - `Bucket_headings_follow_the_setting`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: CFLO and OUTS screens`

---

### Task 12: `AUDT`

**Files:**
- Create: `Screens/AuditViewModel.cs`, `src/OpenHisabKitab.Client/Views/AuditView.xaml(.cs)`
- Modify: `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/AuditTests.cs`

**Interfaces:**
- Consumes: `Admin.ListAudit`, `Admin.VerifyAudit` (Plan 2).
- Produces: `AuditViewModel : ScreenViewModel`, with:
  - `string Target` (`company` or `host`)
  - `string From`
  - `string To`
  - `string User`
  - `string Entity`
  - `ObservableCollection<AuditRow> Rows`
  - `AuditRow? Selected`
  - `string Before`
  - `string After`
  - `string Verification`
  - `Task SearchAsync()`
  - `Task VerifyAsync()`

- **Arguments.** `AUDT` shows the open company's trail. `AUDT host` shows `host.db`'s trail and needs `ManageHostSettings`, which only the Owner has; the host checks it. `ListAudit` already returns newest first.
- **Filters.** From and To are dates, typed through `DateInput`. They are sent as the start of From and the end of To, in local time, converted to round trip text. The limit is 500, newest first.
- **Columns.** Time, User, PC, Source, Token, Action, Entity, Key.
- **Detail.** Enter shows Before and After as indented JSON, in a read only pane under the list.
- **Verify** (`_Verify`) shows one of:
  - `Chain intact: {count} records, anchor matches.`
  - `Chain broken at record {seq}.`
  - `Newest records are missing or the chain was rebuilt.`
  - `No anchor found.`
- **Permissions.** It needs `ViewAudit`. Otherwise the host refusal is shown as received.

- [ ] **Step 1: Write the failing tests:**
  - `Lists_the_company_trail_newest_first`
  - `Filter_by_user_and_dates`
  - `Enter_shows_before_and_after`
  - `Verify_reports_an_intact_chain`
  - `Host_trail_needs_manage_host_settings`: an Accountant gets the host refusal for `AUDT host` (`AdminService.RequireTarget`).
  - `Data_entry_sees_the_refusal` (Review Focus 4)
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: AUDT screen`

---

### Task 13: `BKUP` and the backup settings

**Files:**
- Create: `Screens/BackupViewModel.cs`, `src/OpenHisabKitab.Client/Views/BackupView.xaml(.cs)`
- Modify:
  - `src/OpenHisabKitab.Host/Application/HostSettings.cs`, which adds `backup.intervalHours` (int, `24`, 1 to 720), `backup.keep` (int, `30`, 1 to 365), and `backup.folder` (string, `""`, meaning `Backups` in the data folder);
  - `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Host.Tests/HostSettingsServiceTests.cs`, `tests/OpenHisabKitab.Client.Model.Tests/Screens/BackupTests.cs`

**Interfaces:**
- Consumes: `Admin.BackupNow`, `ListBackups`, `Restore`, `ListHostSettings`, and `SetHostSetting` (Plans 2 and 4).
- Produces: `BackupViewModel : ScreenViewModel`, with:
  - `ObservableCollection<BackupRow> Rows`
  - `BackupRow? Selected`
  - `string IntervalHours`
  - `string Keep`
  - `string Folder`
  - `bool CanBackup`
  - `bool CanRestore`
  - `bool CanSchedule`
  - `Task BackupNowAsync()`
  - `Task RestoreAsync()`
  - `SaveAsync` (the schedule)

- **List.** It shows the open company's backups, newest first: Taken at, File.
- **Back up now** (`_Back up now`) adds a row and shows `Backed up to {file}.`
- **Restore** (`_Restore`).
  - It is shown only with `Restore`, so only the Owner sees it.
  - It asks `Restore {file}? Everyone is logged out and the current file is backed up first.`
  - When the host logs everyone out, the session screen follows, as it does after any logout.
- **Schedule.** The three host settings are shown only with `ManageHostSettings`. Ctrl+S saves the changed ones through `SetHostSetting`, and host refusals are shown as received.
- **Host settings validation.** Today `HostSettingsService.Normalise` checks every `int` as a port (1024 to 65535, not the gRPC port), and every other type as `string[]`. This task generalises it:
  - `HostSettingDefinition` gains `int? Min` and `int? Max`. `int` checks that range, and the gRPC port clash is checked only for `api.port`. `api.port` keeps 1024 to 65535.
  - A new `string` type stores a JSON string.
  - `backup.folder` must be blank or an absolute path: `backup.folder must be a full folder path.`
  - The folder must exist or be creatable when it is set: `Cannot use folder {path}.`
  - `api.port` and `api.allowedOrigins` behave as before, and their existing tests stay green.

- [ ] **Step 1: Write the failing tests:**
  - `Backup_settings_are_listed_and_validated` (Host)
  - `Scheduler_reads_the_listed_settings` (Host): with `backup.intervalHours` set to 1, the scheduler backs up after an hour of fake time.
  - `Backup_now_adds_a_row`
  - `Accountant_backs_up_but_cannot_restore`
  - `Owner_restore_asks_then_logs_everyone_out`
  - `Schedule_saves_changed_settings_only`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Host and client model tests pass.
- [ ] **Step 5: Commit** `feat: BKUP screen and backup schedule settings`

---

### Task 14: Performance, UI smoke tests, docs, owner review, and finishing

**Files:**
- Modify:
  - `tests/OpenHisabKitab.Host.Tests/CompanyBooksPerformanceTests.cs`
  - `tests/OpenHisabKitab.Client.UiTests/` (a new `ReportSmokeTests.cs`)
  - `README.md`, `docs/owner-review.md`
  - this plan's Builder decisions

- [ ] **Step 1: Performance** (spec 10). Extend the million line test with `Full_year_schedule_iii_balance_sheet_and_profit_and_loss_are_under_one_second`. Use the `Category=Performance` trait. It times `BalanceSheetLayout` and `ProfitAndLossLayout` after a save.
- [ ] **Step 2: UI smoke tests,** keyboard only, through FlaUI:
  - `Trlb_drills_to_ledg_and_back`: Enter on Cash, then Alt+Left.
  - `Bsht_switches_style_with_ctrl_g`
  - `Outs_shows_a_sale_as_not_due`
  - `Daybook_opens_a_voucher`
  - `Export_writes_a_csv`: the save dialog is driven by keys, and the file starts with the BOM.
  - `Audt_verify_reports_intact`
  - `Report_fits_at_28_px`: at `ui.fontSize` 28 in a 1024 by 768 window, the `TRLB` heading, the column headers, and the first row are visible.
- [ ] **Step 3: Run them.** `$env:OHK_UI_TESTS='1'; dotnet test tests/OpenHisabKitab.Client.UiTests` passes. Without the variable they are skipped.
- [ ] **Step 4: Docs.** `README.md` gains a short "Reports" section with:
  - the seven codes and their arguments;
  - drill-down and Ctrl+E;
  - Ctrl+G;
  - the new REST routes and MCP tools.
- [ ] **Step 5: Owner review.** Mark done any items the owner settled during the work. Add only new major decisions. Record smaller ones under Builder decisions.
- [ ] **Step 6: Final verification.**
  - `dotnet build OpenHisabKitab.slnx -c Release` has 0 warnings.
  - `dotnet test OpenHisabKitab.slnx -c Release --no-build` passes, with the UI tests skipped.
  - The UI tests pass with the variable set.
  - The three performance tests pass when run alone in Release.
- [ ] **Step 7: Commit** `test: report smoke and performance tests; docs and owner review for plan 6`
- [ ] **Step 8: Security review and integration.**
  - Run `/security-review` on the branch and fix what it finds.
  - Then ask the owner whether to push the branch and merge. After a push, confirm that CI is green.

## Builder decisions

These are the builder's calls, kept here for the record. The major ones are in `docs/owner-review.md`.

1. **One `ReportView` shows every report.** Each screen fills a column list and lines, and the view builds the grid from them. Cost if wrong: one view per report.
2. **Report screens use the host's figures as they are.** The client only formats and words them. So screens, REST, and MCP never disagree. Cost if wrong: none.
3. **Drill-down runs a command with explicit dates in this tab,** so Back, Forward, and tab titles work as for typed commands. `TRLB` takes an optional group name for the group level. Cost if wrong: an in-screen drill stack.
4. **`DAYB` defaults to the working date alone,** and other reports default to the year to the working date. Cost if wrong: one default.
5. **CSV amounts are plain signed decimals,** with no Indian commas and no Dr/Cr, so spreadsheets read them as numbers. Text that starts with `= + - @` gets a leading `'`, so a spreadsheet does not run it as a formula. The file has a BOM, so Excel reads ₹ and Indian names. Cost if wrong: one writer.
6. **New keys:** Ctrl+E exports and Ctrl+G switches report style. Both are rebindable. Cost if wrong: default keys.
7. **`LEDG` and `DAYB` page at 500 rows.** The statement is cut from the in-memory `Books`. The page cursor is the date and voucher id, as in `Vouchers.List`, so a save between pages never repeats or skips a line. Cost if wrong: none expected.
8. **Existing raw report routes and MCP tools are unchanged.** The layouts are new routes and tools, so scripts written against Plan 3 keep working. Cost if wrong: two extra routes.
9. **Group kinds are set by default group id in migration 0003,** so renamed groups keep their role. Cost if wrong: a name-based fallback.
10. **Outstanding bills carry their voucher type** (`OpenBill.Type`, `voucher_type` on the wire), so an `OUTS` bill line opens its voucher. Cost if wrong: one field.
11. **Screen access keys never use a menu bar letter** (F, E, V, H). Alt and that letter opens the menu, so the screen key could never be reached. The report smoke test checks this. Cost if wrong: labels.
12. **`TRLB` totals** sum the Debit and Credit columns, which always agree. A group view also totals Opening and Closing. Cost if wrong: two cells.
13. **The cash line of `BSHT` and `CFLO` drills to `TRLB` of its heading** (Current Assets), because it combines two groups. Cost if wrong: the user also sees other current assets.
## Done when

- Every task's tests pass in Release, with 0 warnings.
- With the host and client running, a user can do all of this from the keyboard only:
  - open `TRLB`, drill to a ledger, then to a voucher, and come back;
  - see `BSHT` in Schedule III style with last year's column, and switch to the chart;
  - read `CFLO` and `OUTS` as at a date;
  - export any of them to CSV;
  - verify the audit chain in `AUDT`;
  - back up from `BKUP`.
- The UI smoke tests pass with `OHK_UI_TESTS=1`.
- `docs/owner-review.md` holds any new major decision.
- The owner has decided about pushing and merging.
