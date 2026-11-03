# Owner Review

Major decisions and data checks that need the owner. Smaller decisions are the builder's, recorded in each plan. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 6b: Printing, PDF, and printed invoices

Settled on 2026-11-03: voucher slips are in scope; issued invoices get an optional Ship to; each invoice keeps the buyer's and company's details as saved; and open decisions 1 to 5 were approved as recommended (recorded in the plan).

### Data checks before the first public release

1. **Printed wording** in `src/OpenHisabKitab.Core/Gst/InvoiceWording.cs` and plan Task 1: the titles (including `Bill of supply` for a Regular sale where every line is at 0%), the four export and SEZ endorsements, the composition line, the Rule 48 copy marks, `Reverse charge No` on every issued document, and an export or SEZ supply without tax showing the line's rate (such as 18%) with 0.00 IGST. A tax professional should confirm them.
2. **Sample printouts.** An accountant should check one printed tax invoice, export invoice, credit note, and bill of supply against Rules 46, 49, and 53 (plan Task 9, step 5).

## Plan 6: Reports, audit trail, and backup

### Open decision

1. **Depreciation credited somewhere other than Fixed Assets** (`CFLO`, `src/OpenHisabKitab.Core/Reports/CashFlow.cs`). The approved rule adds depreciation back in operating and takes the same amount out of the Fixed Assets line in investing. That is right when depreciation is credited to the asset ledgers. If it is credited to a separate ledger instead, such as Accumulated Depreciation or a provision outside Fixed Assets, investing shows an outflow that never happened, and operating is overstated by the same amount. The totals and closing cash stay correct. Options:
   - **Keep the rule.** Ask users to credit depreciation to the asset ledgers. Cost: nothing now; a misleading split for anyone who does otherwise.
   - **Take out only the depreciation credited under Fixed Assets** (recommended). Depreciation credited elsewhere is treated as a non cash movement in that ledger's own group, so no false outflow appears. Cost: a small Core change with tests, in any later plan.

### Data checks before the first public release

2. **Hand worked report figures.** The expected Schedule III, cash flow, and ageing figures in plan Tasks 2, 3, and 4 were worked by hand from the golden company. An accountant should check them once.

## Plan 5: Masters, vouchers, and bills

### Check before the first public release

1. **Export invoice fields** (Task 5): port code of 6 letters or digits, and shipping bill number of 3 to 7 digits. Each is optional at entry. Confirm against the GSTR-1 offline schema when Plan 7 adds the GST files.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025. It allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 7 (the GST files plan, renumbered from 6).

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan tasks, tests, and builder decisions**: `docs/superpowers/plans/`
