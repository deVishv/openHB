# Owner Review

Major decisions and data checks that need the owner. Smaller decisions are the builder's, recorded in each plan. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 6: Reports, audit trail, and backup

Decided by the owner on 2026-11-02:
- Plan 6 is split. Plan 6 has the reports, CSV export, `AUDT`, and `BKUP`. Plan 6b has `PRNT`, PDF, and printed invoices.
- PDF uses PDFsharp 6.2.4, pinned. That is the last MIT version; 6.5.0 and later are AGPL. It is used from Plan 6b.

### Approve before the build starts (recommended rules, plan Tasks 2 to 4)

1. **`OUTS` ageing** (Task 4):
   - Bills age from their due date: the bill date plus the party's credit days, or an opening bill's own due date, or else its bill date.
   - The columns are `Not due`, `1-30`, `31-60`, `61-90`, and `Over 90`, from the company setting `outstanding.ageingDays` (default `30,60,90`).
   - Receipts and payments not allocated to a bill show as `On account` and are not aged.
   - "As at" leaves out any bill or allocation dated after that date. Ok
2. **`CFLO` classification** (Task 3):
   - Cash means the Cash in Hand and Bank Accounts groups. A bank overdraft is a short term borrowing in financing, not negative cash, so `CFLO` closing cash equals the `BSHT` cash line.
   - Depreciation is added back in operating and taken out of the Fixed Assets line in investing.
   - Operating holds current assets and current liabilities other than short term borrowings. Investing holds non current assets. Financing holds equity, non current liabilities, and short term borrowings.
   - Interest and dividends are **not** moved out of operating. As I read AS 3 (please confirm), a non financial business shows interest paid in financing and interest or dividends received in investing. Doing that needs marked ledgers, so it is left for later. Cost if wrong: one more rule and a group kind. Ok
3. **`BSHT` and `PNLS` in Schedule III style** (Task 2):
   - Each group directly under a heading is one line item, except that Cash in Hand and Bank Accounts combine into `Cash and cash equivalents`. GST Input Credit and Duties and Taxes stay as their own lines.
   - The current year's profit is part of Reserves and Surplus.
   - The previous year's column is shown from the second year on. As I read Schedule III's General Instructions (please confirm), the first financial statements need no previous year's figures.
   - An asset ledger in credit, such as a bank account in overdraft under Bank Accounts, stays on its side in brackets. It is not moved to liabilities.
   - Notes to accounts are not produced. Ok
   
### Decided during the build, for the owner to confirm

4. **Backups go only to a drive of the host PC** (Task 13, after the automatic security review). `backup.folder` must be blank or a full path on a fixed or removable drive, such as `D:\Backups` or a USB disk. Network paths (`\\nas\share`) are refused before they are touched: opening one would send the host service's Windows credentials to that machine. So is a path through a folder link or junction, which could lead to such a share. A folder the host creates gets the same access rules as the data folder. Cost if wrong: no backup straight to a NAS; copying the backup folder there with Windows tools still works.

5. **The result line of `PNLS` with last year's column** (final review). When this year and last year differ in sign, the line reads `Net profit / (loss)`, with losses in brackets. Otherwise it reads `Net profit` or `Net loss` as before. Without this, last year's profit sat under the word "Net loss". REST and MCP use the same words. Cost if wrong: one label.

### Data checks before the first public release

6. **Hand worked report figures.** The expected Schedule III, cash flow, and ageing figures in plan Tasks 2, 3, and 4 were worked by hand from the golden company. An accountant should check them once.

## Plan 5: Masters, vouchers, and bills

### Check before the first public release

1. **Export invoice fields** (Task 5): port code of 6 letters or digits, and shipping bill number of 3 to 7 digits. Each is optional at entry. Confirm against the GSTR-1 offline schema when Plan 7 adds the GST files.

### Plan order

2. **`AUDT` and `BKUP` screens** are not in Plan 5. Choose the plan for them when Plan 6 is written. OK: placed in Plan 6 by the owner on 2026-11-02.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025. It allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 7 (the GST files plan, renumbered from 6).

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan tasks, tests, and builder decisions**: `docs/superpowers/plans/`
