# Plan 5: Masters, Vouchers, and Bills Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Users keep their books from the keyboard. The plan adds:
- the masters screens `ACCT`, `PRTY`, and `ITEM`;
- all eight voucher entry screens;
- bill by bill settlement, with party opening bills;
- `FIND`, `LOCK`, and editing a company profile in `CMPY`.

**Architecture:**
- **Core** gains bills and allocation (`Core/Bills`) and the tax-free rule (`Core/Gst/TaxMode`).
  - The host checks every allocation with Core. The client uses the same Core code to suggest allocations and to show live invoice totals.
  - The host calculates everything again before saving (spec 4, Host service).
- **Data** gains company migration `0002`, which adds:
  - opening bills;
  - allocations;
  - export invoice fields;
  - a save request id, so a retried save never makes a second voucher.
- **Host** gains:
  - allocation rules in `VoucherService`;
  - opening bills in `MasterService`;
  - paged lists, `FIND` search, voucher lookup by number, a lock read, and a company profile update.
  - Each new feature gets its gRPC method, REST route, and MCP tool (Plan 2: every plan that adds a host feature adds both).
- **Client.Model** gains a lookup cache for the open company, name fields with autocomplete, and the screens. The WPF project gets one view per screen and a keyboard entry grid.

**Tech Stack:** As in Plan 4: .NET 10, WPF, `CommunityToolkit.Mvvm` 8.4.2, gRPC, SQLite, `ModelContextProtocol.AspNetCore` 2.3.0, xUnit 2.9.3, and `FlaUI.UIA3` 5.0.0. No new packages.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. These sections apply:
- 4.1 (REST and MCP resources);
- 5.1, 5.2, 5.3, 5.4, and 5.6;
- 6.2 (Names);
- 6.3 (the Masters and Vouchers rows, and `FIND`, `LOCK`, `CMPY`);
- 6.4, 6.5, and 6.7;
- 7 (`invoice.pricesIncludeTax`);
- 13 ("Companies and GST registration", "Opening balances", "Remote ready client", "Vouchers and invoices", "Permissions").

**Scope, decided by the owner on 2026-10-31:**
- **In:**
  - masters and voucher entry screens;
  - bill by bill allocation and party opening bills (Plan 2 decision 9);
  - paged lists and client lookup caches (Plan 4 decision 4);
  - export invoice fields;
  - `FIND`, `LOCK`, and the `CMPY` profile edit (Plan 4 decision 3);
  - the CI actions bump;
  - the MCP wrong-type message (Plans 3 and 4 deferred it).
- **Out:**
  - reports screens (`DAYB`, `LEDG`, `TRLB`, `PNLS`, `BSHT`, `CFLO`, `OUTS`);
  - `PRNT`, `AUDT`, and `BKUP`.
- One plan, not split.

**Owner approvals on 2026-10-31:**
- Owner review items 1 to 4 of Plan 5 are approved as recommended: the bill rules, opening bills, company edit, and safe save retries.
- Execution is **native**: one session builds every task with superpowers:executing-plans, then one fresh reviewer on the most capable model checks the whole branch.
- The plan docs are committed to `main` before the branch.
- Approved on 2026-11-01: a composition company's typed GST rate is stored but not charged or shown in the totals. This was owner review item 7, from the Task 14 ruling.

**Roadmap as it now stands.** Plan 1's table used the old numbers; Plan 2 decision 6 renumbered the plans.

| Plan | Covers |
|---|---|
| 6 | Reports screens, `CFLO`, `OUTS` (reads this plan's allocations), `DAYB`, `PRNT`, PDF, printed invoices |
| 7 | GST files: `GST1`, `GS3B`, `G2BR`, `EINV`; checked cess rates |
| 8 | Custom roles, host.db restore, installer, release |

`AUDT` and `BKUP` are not placed yet. The owner places them when Plan 6 is written (owner review).

## Global Constraints

- **Earlier rules still hold.** Everything in the Global Constraints of Plans 2, 3, and 4 still holds:
  - build settings and central package versions;
  - money as `long` paise;
  - `TimeProvider`;
  - one line messages;
  - no WPF in `Client.Model`;
  - all screen text in `Resources/Text.resx`;
  - `{t:Txt}` and `{t:Name}` in XAML;
  - 30 second call deadlines;
  - the remote ready rules;
  - UI tests behind `OHK_UI_TESTS=1`.
- **Branch.** All work happens on the branch `plan-5-masters-vouchers`. The repository is public:
  - Nothing is pushed or merged until the owner says so.
  - `/security-review` runs before merging.
- **Messages** in this plan are exact text. Host messages are shown in the client as received.
- **Amounts typed in the client** are read with `Core.Amounts.Money.TryParse`, which accepts Indian or western commas and one or two decimals. They are shown with `IndianNumberFormat`.
  - Debit and credit columns show `Dr` and `Cr`, never colour alone (spec 6.6).
  - Quantities are typed with up to 3 decimals, and rates with up to 2.
- **Every screen loads in at most three host calls** (Plan 4). Lookups come from the company's `LookupCache` (Task 10), which loads when the company opens. It is not counted per screen.
- **Paging.**
  - `PageRequest.Limit` 0 means 500. More than 1000 is refused: `Ask for at most 1000 rows.`
  - `After` is the `Next` cursor from the previous page. An empty `Next` means the last page.
- **Every new gRPC method** has an REST route or an MCP tool where spec 4.1 lists the resource, and an audit record when it changes data.
- **Permissions** come from `CallContext` in the host. The client hides what the user cannot do, using `ShellContext.Has`. The host still refuses it.

## Review Focus

These failure modes are implied by the spec but not exercised by any example. Each has a test in the task named.

1. **Two users settle the same bill at once.**
   - Both see ₹10,000 open, and each allocates ₹10,000 in a receipt.
   - The second save is refused: `Bill INV/26-27/00001 has only 0.00 open.`
   - The bill is never over-settled.
   - Test in Task 5, with parallel saves.
2. **A settled bill is edited down, moved to another party, or cancelled.**
   - The host refuses: `This bill has 6,000.00 settled. Change those vouchers first.`
   - Allocations never exceed a bill.
   - Test in Task 5.
3. **A save whose reply is lost.**
   - The network drops after the host commits. The client retries with the same request id, possibly after the user changed the draft.
   - The host answers "already saved" with the first voucher.
   - The screen shows the stored voucher in edit mode: `Already saved as {number}. Showing the saved version.` It never clears as if the changed draft had been saved.
   - Only one voucher exists, and the numbering has no gap.
   - Tests in Tasks 5 and 13.
4. **Live totals on screen differ from what the host stores.**
   - The cases: tax inclusive prices, intra and inter state, a union territory, export under LUT, a composition company, reverse charge, and round off.
   - After saving, the screen's taxable value, each tax, round off, and total equal the stored voucher to the paisa.
   - Test in Task 14.
5. **Party and ledger names that match several records, none, or differ only by case or spacing,** such as `hdfc` against `HDFC Bank` and `HDFC Bank OD`.
   - Several matches show a pick list.
   - None offers to create one, which opens `PRTY` in a sub screen.
   - An exact match wins over prefixes.
   - Tests in Tasks 10 and 14.

---

## File Structure

```
.github/workflows/ci.yml                                   checkout and setup-dotnet v5 (Task 1)
src/OpenHisabKitab.Core/
  Bills/BillRef.cs, OpenBill.cs                            bills and their open amounts (Task 2)
  Bills/BillAllocator.cs, AllocationCheck.cs               oldest first suggestion; checks (Task 2)
  Gst/TaxMode.cs                                           the tax-free rule, moved from the host (Task 2)
  Gst/GstData.cs                                           + RatesOn(date) (Task 2)
  Settings/SettingsCatalog.cs                              + invoice.pricesIncludeTax (Task 6)
  Commands/Keys/KeyCommands.cs, DefaultKeybindings.cs      + record and line commands (Task 10)
src/OpenHisabKitab.Data/
  Migrations/Company/0002_bills_and_exports.sql            (Task 3)
  CompanyDb/BillRepository.cs                              opening bills, allocations, open bills (Task 3)
  CompanyDb/VoucherRepository.cs                           allocations, export, request id, paging, find (Tasks 3, 6)
  CompanyDb/CompanyRepository.cs, HostDb/CompanyListRepository.cs   profile update (Task 6)
src/OpenHisabKitab.Host/
  Application/MasterService.Bills.cs                       opening bills (Task 4)
  Application/VoucherService.Bills.cs                      allocation rules, open bills (Task 5)
  Application/VoucherService.Find.cs                       paging, find, by number (Task 6)
  Application/Paging.cs                                    PageRequest, Page<T> (Task 6)
  Application/CompanyService.cs, AdminService.cs           profile update, lock read (Task 6)
  Grpc/Endpoints.cs, Grpc/Wire.cs                          (Task 7)
  Mcp/McpArgumentErrors.cs                                 wrong-type message (Task 8)
  Api/*, Mcp/ReadTools.cs, Mcp/WriteTools.cs               (Task 9)
src/OpenHisabKitab.Contracts/Protos/*.proto                (Task 7)
src/OpenHisabKitab.Client.Model/
  Lookups/LookupCache.cs, LookupField.cs                   (Task 10)
  Screens/ScreenCommands.cs                                record and line commands for screens (Task 10)
  Screens/AccountsViewModel.cs                             ACCT (Task 11)
  Screens/ItemsViewModel.cs, PartiesViewModel.cs           ITEM, PRTY (Task 12)
  Screens/Entry/VoucherEntryViewModel.cs, EntryLine.cs, BillsPanelViewModel.cs, VoucherCodes.cs   (Task 13)
  Screens/Entry/JournalEntryViewModel.cs                   RCPT PYMT JRNL CNTR (Task 13)
  Screens/Entry/InvoiceEntryViewModel.cs, InvoiceTotals.cs SALE PURC CRNT DBNT (Task 14)
  Screens/FindViewModel.cs                                 FIND (Task 15)
  Screens/LockViewModel.cs, CompanyViewModel.cs            LOCK, CMPY edit (Task 16)
src/OpenHisabKitab.Client/
  Views/EntryGrid.cs                                       keyboard entry grid behaviour (Task 12)
  Views/AccountsView, ItemsView, PartiesView, JournalEntryView, InvoiceEntryView, BillsPanelView, FindView, LockView (.xaml)
  Input/KeyInput.cs                                        gridFocus for the entry grid (Task 12)
```

---

### Task 1: Branch and CI actions

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Branch.** Start from a clean `main`, where this plan and the Plan 5 owner review entries are already committed. Then run `git switch -c plan-5-masters-vouchers`.
- [ ] **Step 2: Bump the actions.** Change `actions/checkout@v4` to `actions/checkout@v5` and `actions/setup-dotnet@v4` to `actions/setup-dotnet@v5`. Nothing else changes, and `permissions: contents: read` stays.
- [ ] **Step 3: Verify.** `dotnet build OpenHisabKitab.slnx -c Release` has 0 warnings, and `dotnet test OpenHisabKitab.slnx -c Release --no-build` passes. The real check comes when the owner allows a push (Task 17): CI is green, with no Node 20 annotation.
- [ ] **Step 4: Commit** `ci: move checkout and setup-dotnet to v5`

---

### Task 2: Bills, allocation, and the tax-free rule in Core

**Files:**
- Create: `src/OpenHisabKitab.Core/Bills/BillRef.cs`, `Bills/OpenBill.cs`, `Bills/BillAllocator.cs`, `Bills/AllocationCheck.cs`, `src/OpenHisabKitab.Core/Gst/TaxMode.cs`
- Modify: `src/OpenHisabKitab.Core/Gst/GstData.cs`
- Modify: `src/OpenHisabKitab.Host/Application/VoucherService.Invoices.cs`: `TaxFree` calls `TaxMode.Decide`.
- Test: `tests/OpenHisabKitab.Core.Tests/Bills/BillAllocatorTests.cs`, `Bills/AllocationCheckTests.cs`, `Gst/TaxModeTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace OpenHisabKitab.Core.Bills;
public enum BillSource { Voucher, Opening }
public readonly record struct BillRef(BillSource Source, long Id);
/// <summary>Which side of a party's account its bills sit on: debit for receivables, credit for payables.</summary>
public enum PartySide { Receivable, Payable }
public static class PartySides
{
    /// <summary>True when a posting to the party raises what is owed (a bill); false when it settles bills.</summary>
    public static bool IsBill(PartySide side, Money posting);   // Receivable: posting > 0. Payable: posting < 0.
}
/// <summary>A bill as it stands. Amount is positive. Settled is what allocations already take from it.</summary>
public sealed record OpenBill(BillRef Ref, LedgerId Party, string Number, string? PartyDocNumber, DateOnly Date, DateOnly? DueDate, Money Amount, Money Settled)
{ public Money Open => Amount - Settled; }
public sealed record Allocation(BillRef Bill, Money Amount);
public static class BillAllocator
{
    /// <summary>Oldest first (spec 13): by date, then by Ref.Id. The bill named by first, if open, goes first.
    /// Each bill takes what is left, up to its open amount. Bills with nothing open are skipped.</summary>
    public static IReadOnlyList<Allocation> Suggest(IReadOnlyList<OpenBill> bills, Money amount, BillRef? first = null);
}
public enum AllocationError { NotPositive, UnknownBill, OtherParty, Duplicate, MoreThanOpen, MoreThanSettlement }
public sealed record AllocationProblem(AllocationError Error, BillRef? Bill);
public static class AllocationCheck
{
    /// <summary>The first problem, or null. bills holds the party's open bills, without this voucher's own earlier allocations.</summary>
    public static AllocationProblem? Check(LedgerId party, Money settlement, IReadOnlyList<Allocation> allocations,
        IReadOnlyDictionary<BillRef, OpenBill> bills);
}

namespace OpenHisabKitab.Core.Gst;
public enum TaxDecision { Taxed, TaxFree, RefuseUnregisteredReverseCharge, RefuseInputTax }
public static class TaxMode
{
    /// <summary>The host's former VoucherService.TaxFree rule, unchanged, so the client's live totals follow the same rule.</summary>
    public static TaxDecision Decide(GstRegistration registration, InvoiceKind kind, TradeDirection direction, bool reverseCharge, bool anyTaxedLine);
}
// GstData:
public IReadOnlyList<GstRate> RatesOn(DateOnly on);   // the valid rates on that date, ascending
```

- **The tax-free rule is moved, not changed.** `TaxMode.Decide` keeps the order of the current `TaxFree`:
  1. Export under LUT and SEZ without tax are `TaxFree`.
  2. Regular is `Taxed`.
  3. Unregistered with reverse charge is `RefuseUnregisteredReverseCharge`.
  4. Composition, inward, with reverse charge is `Taxed`.
  5. Inward with any taxed line is `RefuseInputTax`.
  6. Everything else is `TaxFree`.
- **The host maps the two refusals** to its existing messages. `InvoiceServiceTests` must pass unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
static OpenBill B(long id, string date, long open, long settled = 0) =>
    new(new BillRef(BillSource.Voucher, id), new LedgerId(7), $"INV/{id}", null, DateOnly.Parse(date), null, new Money(open + settled), new Money(settled));

[Fact] public void Suggest_takes_oldest_first_and_stops_at_the_amount()
{
    var bills = new[] { B(2, "2026-05-10", 4000_00), B(1, "2026-04-20", 3000_00), B(3, "2026-06-01", 5000_00) };
    var s = BillAllocator.Suggest(bills, new Money(5000_00));
    Assert.Equal([(1L, 3000_00L), (2L, 2000_00L)], s.Select(a => (a.Bill.Id, a.Amount.Paise)));
}

[Fact] public void Suggest_puts_the_named_bill_first_and_skips_settled_bills()
{
    var bills = new[] { B(1, "2026-04-20", 0, settled: 3000_00), B(2, "2026-05-10", 4000_00), B(3, "2026-06-01", 5000_00) };
    var s = BillAllocator.Suggest(bills, new Money(6000_00), first: new BillRef(BillSource.Voucher, 3));
    Assert.Equal([(3L, 5000_00L), (2L, 1000_00L)], s.Select(a => (a.Bill.Id, a.Amount.Paise)));
}

[Fact] public void Suggest_leaves_the_rest_on_account()
{
    var s = BillAllocator.Suggest([B(1, "2026-04-20", 1000_00)], new Money(2500_00));
    Assert.Equal(1000_00, Money.Sum(s.Select(a => a.Amount)).Paise);
}

// Property test, seeded as in Plan 1: 500 random sets of 0 to 8 bills with random open amounts, and a random amount.
// The suggestion total equals min(amount, total open). No allocation is more than its bill's open amount.
// Every allocation is positive. AllocationCheck.Check(the suggestion) is null.
[Fact] public void Suggest_is_always_a_valid_allocation() { /* as described */ }

[Theory]
[InlineData(0L, AllocationError.NotPositive)]
[InlineData(4000_01L, AllocationError.MoreThanOpen)]
public void Check_refuses_bad_amounts(long paise, AllocationError expected) { /* bill 1 has 4000_00 open; settlement 10000_00 */ }

[Fact] public void Check_refuses_more_than_the_settlement() { /* two bills of 3000_00, allocations 3000_00 each, settlement 5000_00 -> MoreThanSettlement */ }
[Fact] public void Check_refuses_a_bill_of_another_party_or_unknown_or_twice() { /* OtherParty, UnknownBill, Duplicate, each with its BillRef */ }

[Theory]  // one row per branch of the old TaxFree, with the expected decision
[InlineData("Regular", "Regular", "Outward", false, true, TaxDecision.Taxed)]
[InlineData("Regular", "ExportUnderLut", "Outward", false, true, TaxDecision.TaxFree)]
[InlineData("Regular", "SezWithoutTax", "Outward", false, true, TaxDecision.TaxFree)]
[InlineData("Unregistered", "Regular", "Inward", true, false, TaxDecision.RefuseUnregisteredReverseCharge)]
[InlineData("Composition", "Regular", "Inward", true, true, TaxDecision.Taxed)]
[InlineData("Composition", "Regular", "Inward", false, true, TaxDecision.RefuseInputTax)]
[InlineData("Composition", "Regular", "Outward", false, true, TaxDecision.TaxFree)]
[InlineData("Unregistered", "Regular", "Inward", false, false, TaxDecision.TaxFree)]
public void TaxMode_follows_the_host_rule(string reg, string kind, string dir, bool rc, bool taxed, TaxDecision expected) { }

[Fact] public void RatesOn_lists_the_valid_slabs()
{
    var rates = GstData.LoadEmbedded().RatesOn(new DateOnly(2026, 6, 1)).Select(r => r.BasisPoints);
    Assert.Equal([0, 10, 25, 100, 150, 300, 500, 600, 1200, 1800, 2800, 4000], rates);
}
```

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/OpenHisabKitab.Core.Tests --filter "FullyQualifiedName~Bills|FullyQualifiedName~TaxMode"` fails to compile.
- [ ] **Step 3: Implement** the types above, then change `VoucherService.TaxFree` to switch on `TaxMode.Decide`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Core.Tests` and `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~InvoiceService` pass.
- [ ] **Step 5: Commit** `feat: bills, oldest first allocation, and the tax-free rule in Core`

---

### Task 3: Company migration 0002 and bill storage

**Files:**
- Create: `src/OpenHisabKitab.Data/Migrations/Company/0002_bills_and_exports.sql`, `src/OpenHisabKitab.Data/CompanyDb/BillRepository.cs`
- Modify: `src/OpenHisabKitab.Data/CompanyDb/VoucherRepository.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/MigratorTests.cs`, `VoucherRepositoryTests.cs`, `BillRepositoryTests.cs` (new)

**Interfaces:**
- Consumes: `BillRef`, `BillSource`, `OpenBill`, `PartySide` (Task 2).
- Produces:

```csharp
public sealed record AllocationRow(LedgerId Party, BillRef Bill, Money Amount);
public sealed record OpeningBillRow(long Id, LedgerId Party, string Number, DateOnly Date, DateOnly? DueDate, Money Amount);  // Id 0 = new
public sealed record ExportDetails(string? PortCode, string? ShippingBillNumber, DateOnly? ShippingBillDate);
// InvoiceHeader gains a last parameter: ExportDetails? Export = null
// VoucherRow gains last parameters: IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null
//   (null Allocations is read as empty; Load always fills it)
public static class BillRepository
{
    public static IReadOnlyList<OpeningBillRow> OpeningBills(SqliteConnection c, LedgerId party);           // by date, then id
    public static long OpeningBillsVersion(SqliteConnection c, LedgerId party);
    /// <summary>Rows with Id 0 are inserted; rows with an Id are updated; stored rows not listed are deleted.
    /// The party's opening_bills_version goes up by one.</summary>
    public static void ReplaceOpeningBills(SqliteTransaction tx, LedgerId party, IReadOnlyList<OpeningBillRow> bills);
    /// <summary>The party's bills that are not cancelled, with settled totals. When excludingVoucher is given, that
    /// voucher's own allocations are left out of Settled, and the voucher is not listed as a bill.</summary>
    public static IReadOnlyList<OpenBill> Bills(SqliteConnection c, LedgerId party, PartySide side, int creditDays, long? excludingVoucher);
    public static Money SettledAgainst(SqliteConnection c, BillRef bill);
    public static long? VoucherByRequestId(SqliteConnection c, string requestId);
}
```

- **The migration:**

```sql
ALTER TABLE invoices ADD COLUMN port_code TEXT NULL;
ALTER TABLE invoices ADD COLUMN shipping_bill_number TEXT NULL;
ALTER TABLE invoices ADD COLUMN shipping_bill_date TEXT NULL;
ALTER TABLE vouchers ADD COLUMN request_id TEXT NULL;
CREATE UNIQUE INDEX vouchers_request_id ON vouchers(request_id) WHERE request_id IS NOT NULL;
CREATE INDEX invoices_party ON invoices(party_ledger_id);
-- Opening bills have their own version, so saving a party and then its bills never conflicts with itself.
ALTER TABLE parties ADD COLUMN opening_bills_version INTEGER NOT NULL DEFAULT 1;

CREATE TABLE opening_bills(
    id INTEGER PRIMARY KEY,
    party_ledger_id INTEGER NOT NULL REFERENCES parties(ledger_id),
    number TEXT NOT NULL,
    date TEXT NOT NULL,
    due_date TEXT NULL,
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0)
);
CREATE UNIQUE INDEX opening_bills_number ON opening_bills(party_ledger_id, number COLLATE NOCASE);

-- One row per bill a settling voucher (receipt, payment, credit or debit note) settles. Exactly one bill column is set.
CREATE TABLE allocations(
    voucher_id INTEGER NOT NULL REFERENCES vouchers(id),
    party_ledger_id INTEGER NOT NULL REFERENCES ledgers(id),
    bill_voucher_id INTEGER NULL REFERENCES vouchers(id),
    opening_bill_id INTEGER NULL REFERENCES opening_bills(id),
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0),
    CHECK ((bill_voucher_id IS NULL) <> (opening_bill_id IS NULL))
);
CREATE INDEX allocations_voucher ON allocations(voucher_id);
CREATE INDEX allocations_bill_voucher ON allocations(bill_voucher_id);
CREATE INDEX allocations_opening_bill ON allocations(opening_bill_id);
```

- **What a voucher bill is.** A voucher bill is a voucher, not cancelled, of type `Sale`, `Purchase`, `CreditNote`, or `DebitNote`, whose net posting to the party is on the bill side (`PartySides.IsBill`).
  - Its amount is that net posting, as a positive value.
  - Its number is `display_number`, and its `PartyDocNumber` is the invoice's `party_doc_number`. Opening bills have a null `PartyDocNumber`.
  - Its due date is the voucher date plus `creditDays`.
- **How `VoucherRepository` changes:**
  - `WriteDetails` writes allocations and export columns, and `DeleteDetails` removes allocations.
  - `Cancel` also deletes the voucher's allocations.
  - `Insert` stores `RequestId`.
  - `Load` reads all of these with one more query, so it still runs a fixed number of queries.

- [ ] **Step 1: Write the failing tests**
  - `Version_1_company_file_migrates_to_2_keeping_its_vouchers`: create a file at version 1 with one sale, then migrate it. The sale reads back the same, with `Export` null and `Allocations` empty.
  - `Allocations_and_export_details_round_trip`
  - `Cancel_removes_the_vouchers_own_allocations`
  - `Bills_lists_open_amounts_and_skips_cancelled_vouchers`: an opening bill of 2,000.00, a sale of 11,800.00, and a cancelled sale. A receipt allocates 5,000.00 to the sale. `Bills` shows the opening bill with 2,000.00 open, the sale with 6,800.00 open, and no cancelled sale.
  - `Bills_excluding_a_voucher_leaves_out_its_own_allocations`
  - `Replace_opening_bills_keeps_ids_of_kept_rows`
  - `Request_id_is_unique`: inserting a second voucher with the same request id throws `SqliteException` with code 19.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the migration and the repository changes.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Data.Tests` passes.
- [ ] **Step 5: Commit** `feat: company migration 0002 for opening bills, allocations, export fields, and save ids`

---

### Task 4: Party opening bills in the host

**Files:**
- Create: `src/OpenHisabKitab.Host/Application/MasterService.Bills.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/OpeningBillsTests.cs`

**Interfaces:**
- Consumes: `BillRepository` (Task 3).
- Produces:

```csharp
public sealed record OpeningBills(long Version, IReadOnlyList<OpeningBillRow> Bills);
// MasterService:
public OpeningBills OpeningBills(CallContext ctx, int company, LedgerId party);
/// <summary>Replaces the party's opening bills. Returns their new version. The party's own row version does not change.</summary>
public Task<long> SetOpeningBillsAsync(CallContext ctx, int company, LedgerId party, long version, IReadOnlyList<OpeningBillRow> bills);
// Host helper, used here and in Task 5:
internal static PartySide SideOf(ChartOfAccounts chart, LedgerId party);   // under TradeReceivables: Receivable; else Payable
```

- **Rules,** checked in this order inside the write transaction. Each refusal is `Invalid` unless marked.
  - The caller needs `EditOpeningsAndRequiredLedgers`, as for opening balances.
  - Not a party: `No such party.` (`NotFound`)
  - A stale version: `Someone else changed these opening bills. Showing the current version.` (`Conflict`, current `OpeningBills`)
  - Books locked on or after the books start: `Books are locked up to {dd-mm-yyyy}.` (`Locked`), as in `SetOpeningAsync`.
  - A number trimmed to empty: `Line {n}: enter the bill number.`
  - A repeated number, ignoring case: `Line {n}: bill {number} is listed twice.`
  - A date on or after the books start: `Line {n}: an opening bill must be dated before {books start dd-mm-yyyy}.`
  - A due date before the date: `Line {n}: the due date is before the bill date.`
  - An amount that is not positive: `Line {n}: enter an amount more than zero.`
  - An unknown id: `Line {n}: unknown bill.`
  - A removed bill, or one cut below what is settled: `Bill {number} has {settled} settled. Change those vouchers first.`
- **The ledger opening balance is not changed.** The party's opening balance stays what `ACCT` sets. The bills are the detail behind it. Any difference is the party's opening "on account" amount, which `PRTY` shows and `OUTS` (Plan 6) uses. This rule is in owner review.
- **On success:**
  - The opening bills version goes up by one.
  - An audit record is written with action `set-opening-bills`, entity `party`, key = party id, and before and after JSON of the bill lists.
  - A `Masters` change is published.

- [ ] **Step 1: Write the failing tests:** one per rule above, plus:
  - `Saved_bills_read_back_with_a_new_version_and_audit`
  - `Data_entry_cannot_set_opening_bills`
  - `Removing_a_settled_opening_bill_is_refused`: this needs Task 5's allocation. Write the test now with `[Fact(Skip = "Task 5")]`, then remove the skip in Task 5.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~OpeningBills` passes.
- [ ] **Step 5: Commit** `feat: party opening bills in the host`

---

### Task 5: Allocations, open bills, export details, and safe retries in VoucherService

**Files:**
- Create: `src/OpenHisabKitab.Host/Application/VoucherService.Bills.cs`
- Modify: `VoucherModels.cs`, `VoucherService.cs`, `VoucherService.Invoices.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/AllocationTests.cs`, `InvoiceServiceTests.cs`, `VoucherServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 2 to 4.
- Produces:

```csharp
// JournalInput gains last parameters:  IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null
// InvoiceInput gains last parameters:  ExportDetails? Export = null, IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null
// VoucherService:
/// <summary>The party's bills with what is still open. Needs CreateVoucher or ViewAllVouchers.</summary>
public IReadOnlyList<OpenBill> OpenBills(CallContext ctx, int company, LedgerId party, long? excludingVoucher);
```

- **Safe retries** (Review Focus 3). This rule is in owner review.
  - `SavedVoucher` gains a last parameter, `bool AlreadySaved = false`.
  - When `RequestId` is set and a voucher created by the same user, of the same type, already holds it, `SaveJournalAsync` and `SaveInvoiceAsync` return that voucher's `SavedVoucher` with `AlreadySaved` true. They save nothing, take no number, and write no audit record.
  - The same id held by another user's voucher, or by another type, is refused: `This request id was already used for another voucher.`
  - The check runs inside the write transaction, before anything else.
  - A request id longer than 64 characters is refused: `Request id is too long.`
  - Edits do not need a request id: a retried edit with the old row version gets `Conflict`, and the client shows the current version.
- **Allocation rules,** checked in the write transaction after the postings are built. They apply on save and on edit.
  1. Only `Receipt`, `Payment`, `CreditNote`, and `DebitNote` may carry allocations: `Only receipts, payments, and credit and debit notes settle bills.`
  2. The party must be a party: `Unknown party.`
  3. The voucher's net posting to that party must be on the settle side: `This voucher does not settle bills of {party name}.`
  4. Then `AllocationCheck.Check` runs. The settlement is that net posting as a positive value, and the bills come from `BillRepository.Bills` excluding this voucher. Its errors map to:
     - `NotPositive`: `Enter an allocation more than zero.`
     - `UnknownBill`: `Unknown bill.`
     - `OtherParty`: `Bill {number} is not a bill of {party name}.`
     - `Duplicate`: `Bill {number} is allocated twice.`
     - `MoreThanOpen`: `Bill {number} has only {open} open.`
     - `MoreThanSettlement`: `Allocations are more than the {settlement} this voucher settles for {party name}.`
  5. What is not allocated stays on account. Nothing is stored for it.
- **Bills that are already settled** (Review Focus 2).
  - **Editing a voucher bill that has allocations against it.** Its party cannot change, and its new bill amount cannot be less than what is settled. Either case is refused: `This bill has {settled} settled. Change those vouchers first.`
  - **Cancelling a bill with allocations** is refused with the same message.
  - **Cancelling a settling voucher** drops its allocations, and the cancel audit record keeps them in the before JSON.
- **Export details** (spec 13, "Sales invoice types"). These rules are in owner review as a data check against the GSTR-1 schema in Plan 7.
  - They are allowed only when `Kind` is `ExportWithTax` or `ExportUnderLut`: `Port code and shipping bill are only for exports.`
  - Each field is optional. A port code must be 6 letters or digits, stored in upper case: `Port code must be 6 letters or digits.`
  - A shipping bill number must be 3 to 7 digits: `Shipping bill number must be 3 to 7 digits.`
  - A shipping bill number needs its date, and a date needs its number: `Enter both the shipping bill number and date.`
- **Audit.** The voucher JSON in audit records now includes `Allocations` and `Export`, because `VoucherRow` serialises them.

- [ ] **Step 1: Write the failing tests**
  - Each rule and message above, one test each.
  - `Receipt_allocated_to_a_sale_reduces_its_open_amount`
  - `Credit_note_settles_the_original_invoice`
  - `Debit_note_received_from_a_supplier_is_a_bill_and_cannot_carry_allocations`: this is an inward debit note; check its posting side.
  - `Editing_a_receipt_replaces_its_allocations`
  - `Parallel_receipts_cannot_over_settle_a_bill` (Review Focus 1): two receipts each allocate the full 10,000.00 open on one bill, through `Task.WhenAll`. One succeeds. The other fails with `Bill INV/26-27/00001 has only 0.00 open.`
  - `Editing_a_settled_sale_below_its_settled_amount_is_refused`, `Changing_the_party_of_a_settled_sale_is_refused`, `Cancelling_a_settled_sale_is_refused` (Review Focus 2).
  - `Cancelling_a_receipt_frees_its_bills`
  - `Retried_save_with_the_same_request_id_returns_the_first_voucher` (Review Focus 3): `AlreadySaved` is true, the voucher count stays 1, the second number in the series is still free, and the audit log grows by one record.
  - `Request_id_of_another_user_or_type_is_refused`
  - In `OpeningBillsTests`, remove the skip from `Removing_a_settled_opening_bill_is_refused`.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests` passes. The `Category=Performance` tests are run alone in Release.
- [ ] **Step 5: Commit** `feat: bill by bill allocation, export details, and safe save retries in the host`

---

### Task 6: Paging, FIND search, voucher by number, lock read, company update, and the tax inclusive setting

**Files:**
- Create: `src/OpenHisabKitab.Host/Application/Paging.cs`, `Application/VoucherService.Find.cs`
- Modify: `VoucherRepository.cs`, `PartyRepository.cs`, `ItemRepository.cs`, `CompanyRepository.cs`, `HostDb/CompanyListRepository.cs`, `MasterService.cs`, `CompanyService.cs`, `AdminService.cs`, `Core/Settings/SettingsCatalog.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/FindTests.cs` (new), `CompanyServiceTests.cs`, `AdminServiceTests.cs`, `MasterServiceTests.cs`, `tests/OpenHisabKitab.Core.Tests/Settings/SettingsCatalogTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record PageRequest(int Limit = 0, string? After = null);
public sealed record Page<T>(IReadOnlyList<T> Rows, string? Next);
public sealed record VoucherSearch(string Text, DateRange? Range);
// VoucherService:
public Page<VoucherRow> List(CallContext ctx, int company, DateRange range, VoucherType? type, PageRequest page);   // by date, then id
public Page<VoucherRow> Find(CallContext ctx, int company, VoucherSearch search, PageRequest page);                // newest first
public VoucherRow GetByNumber(CallContext ctx, int company, VoucherType type, string displayNumber);
// MasterService:
public Page<Party> Parties(CallContext ctx, int company, PageRequest page);   // by ledger id
public Page<Item> Items(CallContext ctx, int company, PageRequest page);      // by id
// AdminService:
public DateOnly? LockedUpTo(CallContext ctx, int company);                   // needs company access only
// CompanyService:
public Task UpdateAsync(CallContext ctx, int id, CompanyProfile profile);
```

- **Unpaged methods stay.** The existing unpaged `List`, `Parties`, and `Items` remain for REST and MCP, which cap their output themselves.
- **Cursors are opaque text,** and a bad cursor is refused: `That page is no longer valid. Start again.`
  - The cursor for a list in date order is `yyyy-MM-dd/{id}`.
  - Find uses the same form, read from the newest end.
  - Parties and items use `{id}`.
- **Find** (spec 6.3, `FIND`: number, party, amount, or narration).
  - Text trimmed to empty is refused: `Enter text to find.`
  - A voucher matches when any of these holds, ignoring case:
    - its display number contains the text;
    - its narration contains the text;
    - the name of its invoice party, or of any party ledger in its postings, contains the text;
    - the text reads as an amount with `Money.TryParse` and equals the voucher's debit total.
  - Cancelled vouchers are included and show as cancelled.
  - `OwnOnly` limits the results as `List` does.
  - The search runs in SQL. The party ledger ids that match the text are found from the chart first, then passed in as a list.
- **Get by number.** No match is `No voucher {type} {number}.` (`NotFound`). `OwnOnly` applies.
- **Company update** (owner review item).
  - It needs `ManageCompanies`.
  - Name, address, and GSTIN can always change. The GSTIN must still match the state, and Regular still needs a GSTIN, as in `Create`.
  - The first financial year cannot change: `The first financial year cannot change.`
  - State and registration cannot change once the company has any voucher, cancelled ones included: `State and registration cannot change after vouchers are entered.`
  - Writes go to `company_profile`, then to the company list name in `host.db`.
  - Both trails are audited: action `edit`, entity `company`, with before and after JSON.
  - A `Settings` change is published.
- **New setting:** `new("invoice.pricesIncludeTax", SettingType.Boolean, "false", "New invoice lines take prices with tax included.", SettingScope.Company)` (spec 13, "Tax inclusive prices").

- [ ] **Step 1: Write the failing tests**
  - `Pages_cover_every_voucher_once`: 1,203 vouchers, read with `Limit` 500, come back as three pages with no repeats. The last page has `Next` null.
  - `Limit_over_1000_is_refused`, `A_bad_cursor_is_refused`
  - `Find_matches_number_party_amount_and_narration`: one test per kind of match. The amount case finds `11,800.00` and `11800` alike.
  - `Find_shows_a_data_entry_user_only_their_own_vouchers`
  - `GetByNumber_finds_a_voucher_and_refuses_an_unknown_number`
  - `LockedUpTo_reads_the_lock`
  - `Update_changes_name_address_and_gstin_and_renames_in_the_company_list`
  - `Update_refuses_a_state_change_once_vouchers_exist`, `Update_refuses_a_first_year_change`
  - In `SettingsCatalogTests`, `pricesIncludeTax_is_a_company_boolean`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests` and `dotnet test tests/OpenHisabKitab.Core.Tests` pass.
- [ ] **Step 5: Commit** `feat: paged lists, voucher find, lookup by number, lock read, and company update`

---

### Task 7: Contracts and gRPC endpoints

**Files:**
- Modify: `src/OpenHisabKitab.Contracts/Protos/common.proto`, `vouchers.proto`, `masters.proto`, `companies.proto`, `admin.proto`
- Modify: `src/OpenHisabKitab.Host/Grpc/Endpoints.cs`, `Grpc/Wire.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Grpc/GrpcEndToEndTests.cs`

**Interfaces:**
- Consumes: Tasks 4 to 6.
- Produces (proto). Existing field numbers stay as they are:

```proto
// common.proto
message PageRequest { int32 limit = 1; string after = 2; }

// vouchers.proto. bill_source is "Voucher" or "Opening".
message AllocationMsg { int32 party = 1; string bill_source = 2; int64 bill_id = 3; int64 paise = 4; }
// JournalMsg:        repeated AllocationMsg allocations = 5;  string request_id = 6;
// InvoiceMsg:        string port_code = 14; string shipping_bill_number = 15; string shipping_bill_date = 16;
//                    repeated AllocationMsg allocations = 17; string request_id = 18;
// InvoiceHeaderMsg:  string port_code = 12; string shipping_bill_number = 13; string shipping_bill_date = 14;
// VoucherMsg:        repeated AllocationMsg allocations = 14;
// SavedReply:        bool already_saved = 4;
// ListVouchersRequest: PageRequest page = 4;     VoucherList: string next = 2;
rpc OpenBills (OpenBillsRequest) returns (BillList);
rpc Find (FindVouchersRequest) returns (VoucherList);
rpc GetByNumber (VoucherNumberRef) returns (VoucherMsg);
// excluding_voucher 0 means none. due_date is empty when there is none.
message OpenBillsRequest { int32 company = 1; int32 party = 2; int64 excluding_voucher = 3; }
message BillMsg { string source = 1; int64 id = 2; int32 party = 3; string number = 4; string date = 5; string due_date = 6; int64 amount_paise = 7; int64 settled_paise = 8; string party_doc_number = 9; }
message BillList { repeated BillMsg bills = 1; }
// period absent means every year.
message FindVouchersRequest { int32 company = 1; string text = 2; Period period = 3; PageRequest page = 4; }
message VoucherNumberRef { int32 company = 1; string type = 2; string number = 3; }

// masters.proto. ListParties and ListItems now take ListRequest.
message ListRequest { int32 company = 1; PageRequest page = 2; }
// PartyList: string next = 2;   ItemList: string next = 2;
rpc GetOpeningBills (LedgerRef) returns (OpeningBillList);
// A stale version fails with ABORTED; the trailer "ohk-current-bin" holds the current OpeningBillList.
rpc SetOpeningBills (SetOpeningBillsRequest) returns (VersionReply);
// id 0 is a new bill.
message OpeningBillMsg { int64 id = 1; string number = 2; string date = 3; string due_date = 4; int64 amount_paise = 5; }
message OpeningBillList { repeated OpeningBillMsg bills = 1; int64 version = 2; }
message SetOpeningBillsRequest { int32 company = 1; int32 party = 2; int64 version = 3; repeated OpeningBillMsg bills = 4; }
message VersionReply { int64 version = 1; }

// companies.proto
rpc Update (UpdateCompanyRequest) returns (Empty);
message UpdateCompanyRequest { int32 company = 1; CompanyProfileMsg profile = 2; }

// admin.proto. locked_up_to is empty when the books are not locked.
rpc GetLock (CompanyRef) returns (LockMsg);
message LockMsg { string locked_up_to = 1; }
```

- **Endpoints only translate,** as before. `Wire` gains `Allocation`, `ToMsg(AllocationRow)`, `ToMsg(OpenBill)`, `OpeningBill`, `ToMsg(OpeningBillRow)`, `Page(PageRequest?)`, and `Export(InvoiceMsg)`.
- **The conflict trailer** for `SetOpeningBills` uses the existing `ErrorInterceptor` path, with `OpeningBills` mapped to `OpeningBillList`.

- [ ] **Step 1: Write the failing tests** in `GrpcEndToEndTests`:
  - `Receipt_with_allocation_round_trips_over_grpc`
  - `Open_bills_over_grpc`
  - `Opening_bills_conflict_carries_the_current_list`
  - `Find_and_get_by_number_over_grpc`
  - `Paged_parties_over_grpc`
  - `Company_update_and_lock_read_over_grpc`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Update existing callers of `ListParties` and `ListItems` in tests to send `ListRequest`.
- [ ] **Step 4: Verify.** `dotnet build OpenHisabKitab.slnx` has 0 warnings, and `dotnet test OpenHisabKitab.slnx` passes.
- [ ] **Step 5: Commit** `feat: gRPC for allocations, open and opening bills, find, paging, company update, and lock read`

---

### Task 8: The MCP wrong-type message

The owner decided this on 2026-10-31. With SDK 2.3.0, an argument of the wrong JSON type is rejected before the tool runs, and the assistant sees only `An error occurred invoking '{tool}'.`

**Files:**
- Create: `src/OpenHisabKitab.Host/Mcp/McpArgumentErrors.cs`
- Modify: the MCP registration in `src/OpenHisabKitab.Host/HostSetup.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Mcp/McpToolTests.cs`

- [ ] **Step 1: Spike, at most one hour.** In `ModelContextProtocol.AspNetCore` 2.3.0, look for a hook that runs around a tool call and sees the binding failure.
  - Candidates are a call-tool filter on the server builder, such as `WithCallToolFilter` or `AddCallToolFilter`, or `McpServerOptions` filters.
  - Read the SDK through context7 or the package's XML docs.
  - Record the finding in this plan's Builder decisions: the API used, or that none exists.
  - **If no hook exists, stop and ask the owner** before any switch to raw JSON arguments, because that loses the typed schema assistants see.
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public async Task A_number_where_an_amount_string_belongs_names_the_field()
{
    var r = await Call(Client(readWrite: true), "create_voucher", new { company = co, voucher = new {
        type = "receipt", date = "2026-05-10", narration = "x",
        lines = new object[] { new { ledgerId = b.Cash.Value, amount = 12345.6 }, new { ledgerId = b.Sales.Value, amount = "-12345.60" } } } });
    Assert.Equal("voucher.lines[0].amount must be a string, like \"12345.60\".", Error(r));
}

[Fact] public async Task A_string_where_a_whole_number_belongs_names_the_field()
{
    var r = await Call(Client(), "get_voucher", new { company = co, id = "seven" });
    Assert.Equal("id must be a whole number.", Error(r));
}
```

- [ ] **Step 3: Implement** `McpArgumentErrors.Message(JsonException e, string? parameter)`.
  - The field path comes from `JsonException.Path`, with the leading `$.` removed, prefixed by the parameter name for a nested argument.
  - The messages:
    - a string expected: `{path} must be a string, like "12345.60".`
    - a whole number expected: `{path} must be a whole number.`
    - a boolean expected: `{path} must be true or false.`
    - an object expected: `{path} must be an object.`
    - a list expected: `{path} must be a list.`
    - anything else: `{path} has the wrong type.`
  - Every other exception still goes through `McpResults.Run` as before.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~Mcp` passes.
- [ ] **Step 5: Commit** `fix: MCP names the field when an argument has the wrong type`

---

### Task 9: REST routes and MCP tools for the new host features

**Files:**
- Modify: `src/OpenHisabKitab.Host/Api/ApiModels.cs`, `ApiParse.cs`, `RestRoutes.cs`, `src/OpenHisabKitab.Host/Mcp/ReadTools.cs`, `WriteTools.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Api/RestWriteTests.cs`, `RestReadTests.cs`, `OpenApiTests.cs`, `Mcp/McpToolTests.cs`

**Interfaces:**
- Consumes: Tasks 4 to 6.
- Produces:

```csharp
public sealed record AllocationRequest(int PartyId, string BillSource, long BillId, string Amount);   // BillSource: "voucher" or "opening"
// JournalRequest gains: IReadOnlyList<AllocationRequest>? Allocations = null, string? RequestId = null
// InvoiceRequest gains: string? PortCode = null, string? ShippingBillNumber = null, string? ShippingBillDate = null,
//                       IReadOnlyList<AllocationRequest>? Allocations = null, string? RequestId = null
public sealed record AllocationDto(int PartyId, string Party, string BillSource, long BillId, string BillNumber, Money Amount);
public sealed record BillDto(string Source, long Id, string Number, string? PartyDocNumber, DateOnly Date, DateOnly? DueDate, Money Amount, Money Settled, Money Open);
public sealed record OpeningBillRequest(long? Id, string Number, string Date, string? DueDate, string Amount);
public sealed record OpeningBillsRequest(long Version, IReadOnlyList<OpeningBillRequest> Bills);
public sealed record OpeningBillDto(long Id, string Number, DateOnly Date, DateOnly? DueDate, Money Amount);
public sealed record OpeningBillsDto(long Version, IReadOnlyList<OpeningBillDto> Bills);
public sealed record CompanyUpdateRequest(string Name, string State, string Registration, string? Gstin, string Address);
// VoucherDto gains IReadOnlyList<AllocationDto> Allocations; InvoiceDto gains string? PortCode, string? ShippingBillNumber, DateOnly? ShippingBillDate.
```

- **Routes.** All take a bearer token, and the write routes need a read and write token, as now.
  - `GET /api/v1/companies/{company}/parties/{id}/open-bills?excludingVoucher=` returns `BillDto[]`.
  - `GET /api/v1/companies/{company}/parties/{id}/opening-bills` returns `OpeningBillsDto`.
  - `PUT /api/v1/companies/{company}/parties/{id}/opening-bills` takes `OpeningBillsRequest` and returns `{ "version": n }`. A stale version is 409 with the current `OpeningBillsDto`.
  - `PUT /api/v1/companies/{company}` takes `CompanyUpdateRequest` and returns 204.
  - `GET /api/v1/companies/{company}/vouchers` gains `search`. When it is given, `VoucherService.Find` runs, with `from` and `to` optional.
- **Parse errors** use the existing JSON paths, such as `$.allocations[0].amount`.
- **MCP tools.**
  - A new read tool, `open_bills(company, partyId)`: "A party's bills with what is still open, oldest first. Use the source and id to allocate a receipt, payment, or note to a bill." It carries the Amounts suffix.
  - `create_voucher` and `create_invoice` add to their descriptions: " To settle bills, add allocations: the party, the bill's source and id from open_bills, and the amount. What is not allocated stays on account."
  - `find_vouchers` uses `VoucherService.Find` when `text` is given, so party names and amounts match too.

- [ ] **Step 1: Write the failing tests**
  - `Rest_receipt_with_allocations_settles_a_bill`
  - `Rest_open_bills`
  - `Rest_opening_bills_get_put_and_conflict`
  - `Rest_company_update`
  - `Rest_voucher_search_matches_a_party_name`
  - `Rest_allocation_amount_as_a_number_is_refused_with_its_path`
  - `OpenApi_lists_the_new_routes`
  - `Mcp_open_bills_and_allocating_create_voucher`
  - `Mcp_find_vouchers_matches_a_party_name`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Host.Tests` passes.
- [ ] **Step 5: Commit** `feat: REST and MCP for allocations, open and opening bills, search, and company update`

---

### Task 10: Client lookups, name fields, and screen commands

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Lookups/LookupCache.cs`, `Lookups/LookupField.cs`, `Screens/ScreenCommands.cs`
- Modify: `Shell/ShellContext.cs`, `Shell/CompanyOpener.cs`, `Shell/ChangeWatcher.cs`, `Shell/ShellViewModel.cs`, `src/OpenHisabKitab.Core/Commands/Keys/KeyCommands.cs`, `DefaultKeybindings.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Lookups/LookupCacheTests.cs`, `Lookups/LookupFieldTests.cs`, `tests/OpenHisabKitab.Core.Tests/Commands/Keys/DefaultKeybindingsTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum LedgerUse { Any, CashOrBank, Party, SalesLine, PurchaseLine, NotParty }
public sealed record LedgerOption(int Id, string Name, string Group, string Nature, bool IsParty, bool IsCashOrBank, string Required);
public sealed class LookupCache
{
    public LookupCache(HostConnection host);
    /// <summary>Chart, profile, company settings, and every page of parties and items (Limit 1000).</summary>
    public Task LoadAsync(int company, CancellationToken ct = default);
    public void Clear();
    public int? Company { get; }
    public CompanyProfileMsg? Profile { get; }
    public ChartMsg Chart { get; }
    public IReadOnlyDictionary<int, PartyMsg> Parties { get; }
    public IReadOnlyList<ItemMsg> Items { get; }
    public IReadOnlyDictionary<string, string> CompanySettings { get; }
    public IReadOnlyList<LedgerOption> Ledgers(LedgerUse use);
    public string LedgerName(int id);
    /// <summary>Masters changes reload the chart, parties, and items. Settings changes reload the profile and settings.
    /// Another company's changes are ignored.</summary>
    public Task OnChangeAsync(ChangeEventMsg change);
    public event Action? Changed;
}
public sealed partial class LookupField<T> : ObservableObject where T : class
{
    public LookupField(Func<IReadOnlyList<T>> source, Func<T, string> name);
    [ObservableProperty] public partial string Text { get; set; }
    public IReadOnlyList<T> Suggestions { get; }          // the NameMatcher candidates for Text, at most 20, updated as Text changes
    public T? Value { get; }
    public bool OffersCreate { get; }                      // true after Commit found no match
    /// <summary>Enter or Tab. Uses NameMatcher (spec 6.2). One match sets Value and Text to its name. Several keep
    /// Suggestions open and return false. None sets OffersCreate and returns false. Empty Text clears Value.</summary>
    public bool Commit();
    public void Pick(T item);
    public void Set(T? item);
}
// ShellContext gains:
public LookupCache Lookups { get; }                         // created with Host
public Func<string, bool, Task>? Run { get; set; }          // runs a command in this tab or a new one; set by the shell
// Screens that take record and line commands:
public interface IScreenCommands { bool CanRun(string command); Task RunAsync(string command); }
```

- **New key commands** in `KeyCommands.All`, each with its default binding:

| Command | Key | When |
|---|---|---|
| `record.new` | `ctrl+n` | |
| `record.delete` | `ctrl+delete` | `!gridFocus && !inputFocus` |
| `line.add` | `ctrl+insert` | `gridFocus` |
| `line.remove` | `ctrl+delete` | `gridFocus` |
| `voucher.bills` | `ctrl+b` | |
| `voucher.cancelVoucher` | `ctrl+shift+delete` | |

  `ShellViewModel` routes these to the active screen when it implements `IScreenCommands` and `CanRun` is true. Otherwise they do nothing.
- **When the cache loads.**
  - `CompanyOpener.OpenAsync` loads it after the company opens.
  - `ChangeWatcher` calls `OnChangeAsync` before it marks the tabs.
  - Logout calls `Clear()`.
  - A cache load failure shows its message in the status bar. The company stays open, and the next change reloads the cache.

- [ ] **Step 1: Write the failing tests**
  - `Cache_loads_all_parties_across_pages`: 1,500 parties come back as two pages.
  - `Masters_change_reloads_parties`: another client adds a party, and the cache shows it after the change event.
  - `Other_company_changes_are_ignored`
  - `Ledgers_for_contra_are_only_cash_and_bank`
  - In `LookupFieldTests`:
    - `Exact_name_wins_over_prefix`: `HDFC Bank` against `HDFC Bank OD`.
    - `Case_and_spacing_do_not_matter`: `  hdfc   bank `.
    - `Several_matches_keep_the_pick_list` (Review Focus 5)
    - `No_match_offers_create`
    - `Empty_text_clears_the_value`
  - `Default_keys_bind_the_record_and_line_commands`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests` and `dotnet test tests/OpenHisabKitab.Core.Tests` pass.
- [ ] **Step 5: Commit** `feat: client lookup cache, name fields, and record and line commands`

---

### Task 11: `ACCT`

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Screens/AccountsViewModel.cs`, `src/OpenHisabKitab.Client/Views/AccountsView.xaml(.cs)`
- Modify: `Screens/ScreenCatalog.cs`, `App.xaml`, `Resources/Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/AccountsTests.cs`

**Interfaces:**
- Consumes: `LookupCache`, `IScreenCommands` (Task 10).
- Produces: `public sealed record ChartRow(int Id, bool IsGroup, int Depth, string Name, string Opening, string Required)`. `Opening` reads like `12,000.00 Dr`, or is empty.
  `AccountsViewModel : ScreenViewModel, IScreenCommands`, with:
  - `IReadOnlyList<ChartRow> Rows`
  - `ChartRow? Selected`
  - the form: `bool FormIsGroup`, `string FormName`, `LookupField<GroupMsg> FormParent`, `string FormOpening`, `string FormSide` (`Dr` or `Cr`)
  - `string OpeningDifference` (empty when zero)
  - `bool CanEditOpenings`

- **The screen** (spec 6.3, `ACCT`, and spec 13, "Opening balances").
  - The groups and ledgers show as one indented list, in chart order, under the heading `Chart of accounts`.
  - The footer shows `Opening difference {amount} Dr` or `Cr` while the openings do not balance. It also shows the host's message about it when a voucher save is refused.
  - `ACCT name` selects the matching ledger through `NameMatcher`.
- **Editing.**
  - Selecting a row loads it into the form.
  - `record.new` clears the form to make a new ledger under the selected group.
  - Ctrl+S saves. A new group calls `AddGroup`. A new ledger calls `AddLedger`, then `SetOpening` when the opening is not zero.
  - For an existing record, Ctrl+S renames it when the name changed and sets the opening when that changed.
  - `record.delete` removes a ledger after `Delete ledger {name}?`.
- **Who can edit.**
  - The opening fields are read only without `EditOpeningsAndRequiredLedgers`, and for income and expense ledgers.
  - Required ledgers show `Required` in their row.
  - Every refusal shows the host's message.
- **Loading** uses only the cache's chart. It reloads on the cache's `Changed` event. `IsAffectedBy` is true for `Masters`.

- [ ] **Step 1: Write the failing tests**
  - `Shows_groups_and_ledgers_indented_with_openings`
  - `Adds_a_ledger_with_a_credit_opening`: `FormOpening = "5,000"`, `FormSide = "Cr"`. The host then has −500000 paise.
  - `Shows_the_opening_difference_until_balanced`
  - `Renames_a_ledger`
  - `Deletes_a_ledger_after_confirming`
  - `Data_entry_cannot_edit_openings`
  - `Acct_with_a_name_selects_the_ledger`
  - `Loads_with_no_host_calls`: `Requests` does not grow on `LoadAsync`.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the model, then the view.
  - The view is a list on the left and the form on the right, with labels targeting their fields.
  - Buttons: `_Save`, `_New`, `_Delete`.
  - `AutomationProperties.Name` comes from `{t:Name}` everywhere.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests --filter FullyQualifiedName~Accounts` passes, and `TextResourceTests` passes.
- [ ] **Step 5: Commit** `feat: ACCT screen`

---

### Task 12: `ITEM` and `PRTY`

**Files:**
- Create: `Screens/ItemsViewModel.cs`, `Screens/PartiesViewModel.cs`, `Views/ItemsView.xaml(.cs)`, `Views/PartiesView.xaml(.cs)`, `src/OpenHisabKitab.Client/Views/EntryGrid.cs`
- Modify: `ScreenCatalog.cs`, `App.xaml`, `Text.resx`, `src/OpenHisabKitab.Client/Input/KeyInput.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/ItemsTests.cs`, `PartiesTests.cs`

**Interfaces:**
- Consumes: Tasks 7 and 10.
- Produces:

```csharp
public sealed record OpeningBillLine   // an editable row; strings as typed
{ public long Id; public string Number = "", Date = "", DueDate = "", Amount = ""; }
public sealed partial class PartiesViewModel : ScreenViewModel, IScreenCommands
{
    public static PartiesViewModel Create(ParsedCommand cmd, ShellContext ctx);
    /// <summary>The sub screen a voucher opens to create a party (spec 6.4). saved gets the new party's ledger id.</summary>
    public static PartiesViewModel ForNew(ShellContext ctx, string name, PartySide side, Func<int, Task> saved);
    // List: Parties (filtered by Filter), Selected. Form: Name, Group (receivable and payable groups), Gstin, State,
    // CreditDays, Address, ObservableCollection<OpeningBillLine> OpeningBills, OpeningSummary.
}
public sealed partial class ItemsViewModel : ScreenViewModel, IScreenCommands
{
    public static ItemsViewModel Create(ParsedCommand cmd, ShellContext ctx);
    // Form: Name, HsnSac, IsService, Unit, Rate (choice from GstData.RatesOn(WorkingDate), shown like "18%"), CessRate, DefaultPrice.
}
```

- **`ITEM`**
  - A filterable list (Ctrl+F focuses the filter), and a form.
  - Ctrl+S calls `SaveItem`. A conflict shows the host's message and loads the current item.
  - `ITEM name` selects the match.
- **`PRTY`**
  - Typing a valid GSTIN sets the state from its first two digits. The host still checks that they match.
  - Groups offered are those under Trade Receivables or Trade Payables.
  - **Opening bills grid** with the columns Number, Date, Due date, and Amount.
    - The summary line reads `Opening balance {x}; bills {y}; on account {z}`. The opening balance comes from the cache's chart.
    - The grid is read only without `EditOpeningsAndRequiredLedgers`.
    - Loading a selected party costs one call, `GetOpeningBills`.
  - **Ctrl+S** calls `SaveParty`, then `SetOpeningBills` when the bills changed, with the version from `GetOpeningBills`.
    - If the second call fails, the party is saved and the message shows. The bills stay on screen.
  - **Sub screen mode** (`ForNew`):
    - the name is filled in, and the group defaults to Trade Receivables for `Receivable`, otherwise Trade Payables;
    - after a successful save it awaits `saved(id)`;
    - Esc returns to the voucher without saving, after `Discard this party?` when anything was typed.

- [ ] **Step 1: Write the failing tests**
  - `Item_saves_and_appears_in_the_cache`
  - `Item_rate_choices_are_the_valid_slabs`
  - `Item_conflict_shows_the_current_item`
  - `Party_gstin_sets_the_state`
  - `Party_saves_with_opening_bills_and_shows_on_account`: opening 5,000.00 Dr and bills totalling 3,000.00 give `on account 2,000.00`.
  - `Party_opening_bill_errors_show_the_host_message`
  - `Sub_screen_returns_the_new_party`
  - `Sub_screen_escape_asks_before_discarding`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the models, then `EntryGrid`, then the views. Each view is a list and form like `ACCT`. The opening bills grid is the first `EntryGrid`.
  - `EntryGrid` is an attached behaviour on an `ItemsControl` of `TextBox` rows.
  - Tab and Enter move to the next field, and Shift+Tab moves back. Enter in the last field of the last row adds a row.
  - `line.add` and `line.remove` act on the current row.
  - It marks itself so that `KeyInput.Context` adds `gridFocus`.
  - Tasks 13 and 14 reuse it.
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: ITEM and PRTY screens with opening bills`

---

### Task 13: Voucher entry: the shared base, `RCPT`, `PYMT`, `JRNL`, and `CNTR`

**Files:**
- Create: `Screens/Entry/VoucherCodes.cs`, `Entry/EntryLine.cs`, `Entry/VoucherEntryViewModel.cs`, `Entry/BillsPanelViewModel.cs`, `Entry/JournalEntryViewModel.cs`
- Create: `Views/JournalEntryView.xaml(.cs)`, `Views/BillsPanelView.xaml(.cs)`
- Modify: `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/JournalEntryTests.cs`, `BillsPanelTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 7, 10, and 12 (`PartiesViewModel.ForNew`).
- Produces:

```csharp
public static class VoucherCodes
{
    public static string CodeOf(string voucherType);   // "Sale" -> "SALE", "CreditNote" -> "CRNT", "Contra" -> "CNTR", ...
    public static string TypeOf(string code);
}
public abstract partial class VoucherEntryViewModel : ScreenViewModel, IScreenCommands
{
    // Header: Date (text; default the working date), Narration. Edit mode: Existing (VoucherMsg?), Number.
    // Footer: Totals (one line), Difference (empty when balanced).
    public bool IsEditing { get; }
    public string RequestId { get; }                    // new Guid "N" per draft; kept across retries, renewed after a save
    public object? SubScreen { get; }                   // a PartiesViewModel while creating a party
    public BillsPanelViewModel? Bills { get; }          // open while allocating
    public override bool HasUnsavedEdits { get; }
    public override bool Cancel();                      // closes SubScreen or Bills; else, with unsaved edits, asks "Discard this voucher?"
    public Task CancelVoucherAsync();                   // asks "Cancel voucher {number}?"; then Vouchers.Cancel
    protected abstract Task<SavedReply> SaveNewAsync();
    protected abstract Task<SavedReply> SaveEditAsync();
    protected abstract void Fill(VoucherMsg v);
    protected abstract void Clear();
}
public sealed partial class EntryLine : ObservableObject
{   // JRNL and CNTR use Debit and Credit; RCPT and PYMT use Amount.
    public LookupField<LedgerOption> Ledger { get; }
    [ObservableProperty] public partial string Debit { get; set; }
    [ObservableProperty] public partial string Credit { get; set; }
    [ObservableProperty] public partial string Amount { get; set; }
    public IReadOnlyList<AllocationMsg> Allocations { get; set; }
    public string AllocationText { get; }               // "INV/26-27/00001 5,000.00; on account 0.00", or ""
}
public sealed partial class BillsPanelViewModel : ObservableObject
{
    public sealed partial class Row : ObservableObject { public BillMsg Bill { get; } [ObservableProperty] public partial string Allocate { get; set; } }
    public IReadOnlyList<Row> Rows { get; }
    public string OnAccount { get; }                    // settlement minus allocated, formatted
    public string? Problem { get; }                     // AllocationCheck problem in words, or null
    public void SuggestOldestFirst(BillRef? first = null);
    public IReadOnlyList<AllocationMsg> Result();
}
public sealed partial class JournalEntryViewModel : VoucherEntryViewModel
{
    public static Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx);   // "RCPT", or "RCPT RCT/26-27/00001" to edit
    public LookupField<LedgerOption>? Account { get; }  // RCPT: "Received in"; PYMT: "Paid from" (cash or bank). Null for JRNL and CNTR.
    public ObservableCollection<EntryLine> Lines { get; }
}
```

- **Layouts.**
  - `RCPT` and `PYMT` have an account field and lines of ledger plus amount. On save the account takes the balancing line: RCPT debits the account and credits each line; PYMT is the mirror.
  - `JRNL` and `CNTR` use Debit and Credit columns. `CNTR` lines offer only cash and bank ledgers.
  - The footer shows `Debit {x}  Credit {y}`, plus `Difference {z} Dr` or `Cr` until it reaches zero (spec 6.4, "Live totals").
- **Allocation** (RCPT and PYMT party lines).
  - When a party line's amount is committed, the screen calls `OpenBills` once for that party, excluding this voucher when editing. It then applies `SuggestOldestFirst` and shows `AllocationText` under the line.
  - `voucher.bills` opens the panel to change it. Esc closes the panel and keeps the changes.
  - The panel shows the Core `AllocationCheck` problem live, in the same words as the host.
  - Only bill-side parties get suggestions, decided by `PartySides.IsBill` on the line's sign.
- **Saving.**
  - Ctrl+S sends `JournalMsg` with `request_id`.
  - On success the status shows `Saved {number}.`. The form clears for the next voucher, keeping the date, and `RequestId` is renewed.
  - A failed save keeps everything, `RequestId` included, so a retry is safe (Review Focus 3).
  - A reply with `already_saved` loads the stored voucher with `GetByNumber` into edit mode, and shows `Already saved as {number}. Showing the saved version.`
  - In edit mode, Ctrl+S calls `EditJournal`.
  - A `Conflict` reloads the current voucher from the trailer and shows the host's message.
  - Editing a cancelled voucher shows it read only, with `Cancelled`.
- **Party lookup** (spec 6.4).
  - When a ledger field's `Commit` finds no match, Enter opens `PartiesViewModel.ForNew` as `SubScreen` with the typed name. Its `saved` callback sets the line's ledger.
  - Focus returns to the line's amount.
- **Grid keys** come from `EntryGrid` (Task 12). `line.add` inserts a line after the current one, and `line.remove` removes the current line.

- [ ] **Step 1: Write the failing tests**
  - `Receipt_saves_and_clears_for_the_next`: the status reads `Saved RCT/26-27/00001.`.
  - `Journal_shows_the_difference_until_balanced`: 1,000.00 Dr against 400.00 Cr shows `Difference 600.00 Dr`.
  - `Contra_offers_only_cash_and_bank`
  - `Receipt_party_line_suggests_oldest_first`: two sales, and a receipt line of 15,000.00 shows both bills settled in date order.
  - `Changing_an_allocation_in_the_panel_is_saved`: the stored voucher has the changed allocation.
  - `Panel_shows_more_than_open_in_host_words`
  - `A_failed_save_keeps_the_request_id_and_a_retry_saves_once`: `FailReplyOf` drops the first `SaveJournal` reply after the host commits. The user changes an amount and saves again. The screen shows the stored voucher, with its original amount, in edit mode with the already saved message, and the host has one voucher (Review Focus 3).
  - `Editing_by_number_loads_and_saves_the_edit`
  - `Edit_conflict_shows_the_current_version`
  - `Cancel_voucher_asks_then_cancels`
  - `Escape_asks_once_before_discarding`
  - `Unknown_party_opens_PRTY_and_returns_with_it`
  - `Locked_date_shows_the_host_message_and_keeps_the_entry`
  - `Opens_with_at_most_three_calls`
  - `ClientTestHost.FailRequest` already drops a request. Add a mode that drops the reply after the host handles the call, for the request id test: `FailReplyOf`.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the model, then the views.
- [ ] **Step 4: Verify.** The client model tests pass. Run the client against a host and enter a receipt from the keyboard only.
- [ ] **Step 5: Commit** `feat: voucher entry for receipts, payments, journals, and contras, with bill allocation`

---

### Task 14: Invoice entry: `SALE`, `PURC`, `CRNT`, `DBNT`

**Files:**
- Create: `Screens/Entry/InvoiceEntryViewModel.cs`, `Entry/InvoiceTotals.cs`, `Views/InvoiceEntryView.xaml(.cs)`
- Modify: `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/InvoiceEntryTests.cs`, `InvoiceTotalsTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 10, 12, and 13.
- Produces:

```csharp
public sealed record InvoiceTotalsView(Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess, Money RoundOff, Money Total, string? Problem);
public static class InvoiceTotals
{
    /// <summary>The same calculation the host makes, with Core: TaxMode, GstRules.Split, and InvoiceCalculator.
    /// Problem is the host's words for a line that cannot be calculated yet, or for a TaxMode refusal.</summary>
    public static InvoiceTotalsView Calculate(CompanyProfileMsg company, StateCode partyState, InvoiceMsg draft, bool roundOff);
}
public sealed partial class InvoiceLine : ObservableObject
{
    public LookupField<ItemMsg> Item { get; }           // picking fills Description, HsnSac, Rate, Cess, Price, and Ledger (Sales or Purchases)
    public LookupField<LedgerOption> Ledger { get; }
    // Description, HsnSac, Quantity, Price, Discount, Rate, Cess (strings as typed), PriceIncludesTax (bool)
}
public sealed partial class InvoiceEntryViewModel : VoucherEntryViewModel
{
    public static Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx);   // "SALE", or "SALE INV/26-27/00001" to edit
    // Header: Party (LookupField), PlaceOfSupply (StateOption, defaulted from the party), Direction (CRNT and DBNT only:
    // "Issued" or "Received"), Kind (SALE only), ReverseCharge (inward only), PartyDocNumber and PartyDocDate (inward only),
    // OriginalNumber and OriginalDate (CRNT and DBNT), PortCode, ShippingBillNumber, ShippingBillDate (export kinds only).
    public ObservableCollection<InvoiceLine> Lines { get; }
    public InvoiceTotalsView Totals { get; }            // recalculated on every change
}
```

- **Directions.**
  - `SALE` is Outward and `PURC` is Inward.
  - `CRNT` and `DBNT` default to Issued (Outward) and can be switched to Received.
- **Defaults.**
  - A new line's `PriceIncludesTax` takes `invoice.pricesIncludeTax` from the cache's company settings.
  - Round off follows `invoice.roundOff`.
- **Live footer** (spec 6.4): `Taxable`, `CGST`, `SGST` or `UTGST`, `IGST`, `Cess`, `Round off`, and `Total`. Only the taxes in use are shown. A line problem shows as one line under the grid.
- **Allocation for notes.**
  - When the computed party posting is on the settle side (an Issued credit note or a Received debit note to a payable), the note gets the same automatic suggestion and `voucher.bills` panel as a receipt.
  - The bill named by `OriginalNumber` goes first. For an Issued note, `OriginalNumber` is compared with the bill's `Number`. For a Received note, it is compared with the bill's `PartyDocNumber`, the supplier's own number. Both comparisons ignore case and surrounding spaces.
- **The rest works as in Task 13:** new party sub screen, save, edit by number, conflict, cancel, and request id.
- **Composition and unregistered companies.**
  - Composition companies see rate and cess forced to 0 on outward documents, as the host does.
  - Unregistered companies use the same screens with the rate, cess, place of supply, reverse charge, and export fields hidden. Lines go at rate 0, as Plan 2 decision 4 already has the host expect.

- [ ] **Step 1: Write the failing tests**
  - In `InvoiceTotalsTests`:
    - `Client_totals_equal_host_totals` (Review Focus 4). This is a theory over these cases:
      - intra state 18%;
      - inter state 18%;
      - a union territory without legislature;
      - tax inclusive 12%;
      - export under LUT;
      - SEZ with tax;
      - a composition company's sale;
      - a purchase with reverse charge;
      - round off on and off;
      - a line discount;
      - quantity 2.500.
    - Each case saves through `VoucherService.SaveInvoiceAsync` in the host test fixture, reads the stored voucher, and asserts every `InvoiceTotalsView` figure equals the stored sum to the paisa.
  - In `InvoiceEntryTests`:
    - `Picking_an_item_fills_the_line`
    - `Place_of_supply_defaults_from_the_party`
    - `New_line_takes_prices_with_tax_when_the_setting_is_on`
    - `Export_fields_show_only_for_export_kinds`
    - `Sale_saves_and_shows_the_number`
    - `Credit_note_suggests_the_original_invoice_first`
    - `Received_credit_note_matches_the_suppliers_invoice_number`
    - `Unknown_party_opens_PRTY_and_returns_with_it` (Review Focus 5)
    - `Unregistered_company_sale_hides_tax_fields_and_saves_at_rate_0`
    - `Opens_with_at_most_three_calls`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the model, then the view.
- [ ] **Step 4: Verify.** The client model tests pass. Run the client and enter a sale from the keyboard only.
- [ ] **Step 5: Commit** `feat: invoice entry for sales, purchases, and credit and debit notes`

---

### Task 15: `FIND`

**Files:**
- Create: `Screens/FindViewModel.cs`, `Views/FindView.xaml(.cs)`
- Modify: `ScreenCatalog.cs`, `App.xaml`, `Text.resx`, `Shell/ShellViewModel.cs` (sets `ShellContext.Run`)
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/FindTests.cs`

**Interfaces:**
- Consumes: `Vouchers.Find` (Task 7), `VoucherCodes` and the entry screens' number argument (Tasks 13 and 14), `ShellContext.Run` (Task 10).
- Produces: `public sealed record FoundRow(long Id, string Code, string Date, string Number, string Party, string Amount, string Narration, bool Cancelled)` and `FindViewModel : ScreenViewModel` with:
  - `string Query`
  - `ObservableCollection<FoundRow> Rows`
  - `bool HasMore`
  - `Task SearchAsync()`
  - `Task MoreAsync()`
  - `Task OpenAsync(FoundRow row, bool newTab)`

- **The screen** (spec 6.3, `FIND`).
  - `FIND text` searches at once. `FIND` alone focuses the query, and Enter in it searches.
  - Each page holds 100 rows. PageDown on the last row, or the `_More` button, loads the next page.
  - Enter on a row runs `{CODE} {number}` in this tab, so Alt+Left returns to the results. Shift+Enter runs it in a new tab.
  - Cancelled rows show `Cancelled` in their own column.
  - With no results the screen reads `No vouchers match {text}.`
  - The subject is the query.
  - `IsAffectedBy` is true for `Voucher`.

- [ ] **Step 1: Write the failing tests:**
  - `Find_by_party_name_lists_its_vouchers`
  - `More_loads_the_next_page`
  - `Enter_opens_the_voucher_in_its_entry_screen`: the tab becomes `SALE INV/26-27/00001`, and Back returns to `FIND`.
  - `No_results_says_so`
  - `Data_entry_sees_only_their_own`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: FIND screen`

---

### Task 16: `LOCK` and the `CMPY` profile edit

**Files:**
- Create: `Screens/LockViewModel.cs`, `Views/LockView.xaml(.cs)`
- Modify: `Screens/CompanyViewModel.cs`, `Views/CompanyView.xaml(.cs)`, `ScreenCatalog.cs`, `App.xaml`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/LockTests.cs`, `CompanyTests.cs`

**Interfaces:**
- Consumes: `Admin.GetLock`, `LockUpTo`, `Unlock`, `Companies.Update` (Task 7).
- Produces:
  - `LockViewModel : ScreenViewModel`, with:
    - `string Current` (`Locked up to {date}` or `Not locked`)
    - `string NewDate`
    - `bool CanLock`
    - `bool CanUnlock`
    - `SaveAsync` (lock)
    - `Task UnlockAsync()`
  - `CompanyViewModel` gains:
    - `bool Editing`
    - `Task EditSelectedAsync()`
    - `record.new` back to the create form
    - `SaveAsync` calls `Update` while `Editing`.

- **`LOCK`** (spec 5.6).
  - `LOCK date` fills the date.
  - Ctrl+S locks. A date earlier than the current lock asks `Unlock vouchers after {date}?` first, and needs `UnlockPeriod`, which the host checks.
  - `_Unlock` asks `Unlock all periods?`.
  - The date is typed through `DateInput` and shown through `DateText`.
  - `IsAffectedBy` is true for `Lock`.
- **`CMPY` edit.**
  - `_Edit` (or Enter on the form heading) loads the selected company's profile into the form under the heading `Edit company`. The first year is shown read only.
  - Ctrl+S calls `Update`, then refreshes the title, since the name may change.
  - Host refusals show as received.
  - It is shown only with `ManageCompanies`.

- [ ] **Step 1: Write the failing tests:**
  - `Lock_shows_the_current_lock_and_locks_a_date`
  - `Moving_the_lock_earlier_asks_first`
  - `Unlock_asks_then_unlocks`
  - `Accountant_cannot_unlock`
  - `Edit_renames_the_company_and_the_title`
  - `Edit_shows_the_host_refusal_for_a_state_change_after_vouchers`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: LOCK screen and company profile edit`

---

### Task 17: UI smoke tests, docs, owner review, and finishing

**Files:**
- Modify: `tests/OpenHisabKitab.Client.UiTests/ShellSmokeTests.cs` (or a new `EntrySmokeTests.cs`), `README.md`, `docs/owner-review.md`, this plan's Builder decisions

- [ ] **Step 1: UI smoke tests,** keyboard only, through FlaUI (spec 11):
  - `Acct_adds_a_ledger_with_an_opening`
  - `Item_and_party_with_an_opening_bill`
  - `Sale_with_an_item_line_saves_and_shows_its_number`
  - `Receipt_settles_that_sale`
  - `Find_by_party_opens_the_sale`
  - `Lock_then_editing_an_old_voucher_shows_the_lock_message`
  - `Entry_grid_fits_at_28_px`: at `ui.fontSize` 28 in a 1024 by 768 window, the invoice header, one line, and the footer are all visible.
- [ ] **Step 2: Run them.** `$env:OHK_UI_TESTS='1'; dotnet test tests/OpenHisabKitab.Client.UiTests` passes. Without the variable they are skipped.
- [ ] **Step 3: Docs.** `README.md` gains a short "Entering vouchers" section with:
  - the eight codes and their F keys;
  - how Ctrl+B allocates bills;
  - the new REST routes.
- [ ] **Step 4: Owner review.** Mark done any items the owner settled during the work. Add only new major decisions. Record smaller ones under Builder decisions below.
- [ ] **Step 5: Final verification.**
  - `dotnet build OpenHisabKitab.slnx -c Release` has 0 warnings.
  - `dotnet test OpenHisabKitab.slnx -c Release --no-build` passes, with the UI tests skipped.
  - The UI tests pass with the variable set.
  - The two performance tests pass when run alone in Release.
- [ ] **Step 6: Commit** `test: keyboard-only smoke tests for masters and vouchers; docs and owner review for plan 5`
- [ ] **Step 7: Security review and integration.**
  - Run `/security-review` on the branch and fix what it finds.
  - Then ask the owner whether to push the branch and merge. After a push, confirm that CI is green with no Node 20 annotation.

## Builder decisions

These are the builder's calls, kept here for the record. The major ones are in `docs/owner-review.md`.

1. **RCPT and PYMT have an account field and amount lines.** JRNL and CNTR use Debit and Credit columns. Cost if wrong: a layout change in two screens.
2. **The entry grid is an `ItemsControl` of `TextBox` rows,** not a WPF `DataGrid`. That way Tab, Enter, and Shift+Tab move exactly as spec 6.4 says, and every cell has its own automation name. Cost if wrong: one view helper.
3. **Screen actions use new rebindable commands:**
   - `record.new` (Ctrl+N)
   - `record.delete` (Ctrl+Delete)
   - `line.add` (Ctrl+Insert)
   - `line.remove` (Ctrl+Delete in a grid)
   - `voucher.bills` (Ctrl+B)
   - `voucher.cancelVoucher` (Ctrl+Shift+Delete)

   Cost if wrong: default keys.
4. **Saving a voucher clears the screen for the next one, keeping the date.** Cost if wrong: one line.
5. **The lookup cache loads the whole chart, party list, and item list when a company opens,** in pages of 1,000. The cost is counted at company open, not per screen. Cost if wrong: lazy loading per list.
6. **`FIND` lists newest first,** 100 rows a page. Cost if wrong: the sort order.
7. **A voucher opened from `FIND` runs as `{CODE} {number}`,** so Back, Forward, and the tab title work as for any command. Cost if wrong: none.
8. **Cancelling a voucher from the screen sends an empty reason.** The audit record still names the user and the time. Cost if wrong: one text field.
9. **MCP wrong-type spike result:** SDK 2.3.0 has `WithRequestFilters(f => f.AddCallToolFilter(...))`. A call-tool filter wrapping `next` catches the binding `JsonException`, whose path is relative to the failing argument. `McpArgumentErrors` then finds that argument by binding each one against its declared parameter type, and words the message in the existing `$.path must be ...` form. Cost if wrong: one filter.

## Done when

- Every task's tests pass in Release, with 0 warnings.
- With the host and client running, a user can do all of this from the keyboard only:
  - set openings in `ACCT`;
  - create an item and a party with an opening bill;
  - enter a sale, a receipt that settles it, a credit note, a journal, and a contra;
  - find the sale in `FIND` and edit it;
  - lock the period in `LOCK` and see the edit refused;
  - edit the company name in `CMPY`.
- The UI smoke tests pass with `OHK_UI_TESTS=1`.
- `docs/owner-review.md` holds any new major decision.
- The owner has decided about pushing and merging. After a push, CI is green with no Node 20 annotation.
