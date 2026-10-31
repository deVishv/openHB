# Owner Review

Major decisions and data checks that need the owner. Smaller decisions are the builder's, recorded in each plan. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 5: Masters, vouchers, and bills

### Decide before building, or say OK

1. **Bill by bill rules** (Tasks 2 and 5). Recommended:
   - **What counts as a bill.**
     - A sale, purchase, credit note, or debit note is a bill when it raises what the party owes or is owed. For a customer that is a debit, and for a supplier a credit.
     - Opening bills count too.
   - **What settles bills.** Receipts, payments, and notes that lower what is owed. Journals and contras never settle bills; their party amounts stay on account.
   - **The suggestion is applied automatically.** When an amount is entered, it is allocated oldest first, and the user can change it before saving.
     - A credit or debit note puts its original invoice first.
     - What is not allocated stays on account.
   - **Settled bills are protected.** A bill with settlements cannot be:
     - cancelled;
     - moved to another party;
     - edited below what is settled.

     The user changes the receipts first.
   - **Cancelling a receipt or payment frees its bills.** Ok
2. **Opening bills do not change the party's opening balance** (Task 4). Recommended:
   - The balance is set in `ACCT` and the bills in `PRTY`. Any difference is shown as "on account", for example an advance at the start.
   - Bills must be dated before the books start and be more than zero.
   - The other option is to make the opening balance always equal the bills' total. That is simpler, but it cannot hold an opening advance. Ok
3. **Editing a company profile** (Task 6). Recommended:
   - Name, address, and GSTIN can always change.
   - State and registration type cannot change once any voucher exists, because editing an old invoice would then recalculate its tax under the new profile.
   - The first financial year never changes. Ok
4. **A retried save never makes a second voucher** (Tasks 5 and 13). Recommended:
   - **The problem.** If the network drops after the host saves but before the reply arrives, the client cannot tell whether the save happened.
   - **The fix.** Each new voucher carries a request id. A repeat from the same user, for the same type, returns the first voucher, marked "already saved". The screen then shows that saved voucher with `Already saved as {number}. Showing the saved version.`, so any change typed in between is not mistaken for saved. Another user's or type's use of the id is refused.
   - **The other option** is to refuse the repeat outright. The user then has to look the voucher up in `FIND`. Ok

### Check before the first public release

5. **Export invoice fields** (Task 5): port code of 6 letters or digits, and shipping bill number of 3 to 7 digits. Each is optional at entry. Confirm against the GSTR-1 offline schema when Plan 7 adds the GST files.

### Plan order

6. **`AUDT` and `BKUP` screens** are not in Plan 5. Choose the plan for them when Plan 6 is written.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025. It allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 7 (the GST files plan, renumbered from 6).

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan tasks, tests, and builder decisions**: `docs/superpowers/plans/`
