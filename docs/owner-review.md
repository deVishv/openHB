# Owner Review

Items that need the owner's check. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025, and allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 6.

### Decisions made during the build

Each line says what was decided and what it costs if wrong.

1. **Reverse charge** tax goes to separate RCM Payable ledgers. Owner decided. Cost if wrong: a ledger change before data exists.
2. **Tax inclusive prices.** The taxable value is nudged by up to 1 paisa so the line total equals the typed amount. When no nudge works, the line total can differ from the typed amount by 1 paisa, and screens must show the calculated total. Cost if wrong: a 1 paisa difference on some tax inclusive lines.
3. **Credit and debit notes** follow GST meaning: a credit note reduces and a debit note increases the original, for either side. A purchase return is the supplier's credit note. Cost if wrong: screen labels in Plan 4.
4. **Amounts with commas** must use Indian (12,34,567) or Western (1,234,567) grouping. `12,50` is rejected instead of being read as 1,250.00. Cost if wrong: some unusual typing is refused.
5. **Amounts too large to store** stop with an error instead of wrapping to a wrong total. Cost if wrong: the host must show the error clearly (Plan 2).
6. **Invisible characters** pasted into names (such as the zero width space) are removed. Cost if wrong: none expected.
7. **State code 96** (other country) cannot be a supplier. Places of supply 96 and 97 always take IGST. Cost if wrong: an unusual registration is refused.
8. **Typed years** must be from 1900 to 9998. Cost if wrong: none expected.
9. **Bank Overdraft** group added under Short Term Borrowings. Its ledgers count as bank in contra vouchers. Cost if wrong: one extra default group.
10. **Default chart of accounts** follows Schedule III headings. See `DefaultChart.cs`. Cost if wrong: renaming groups.

### Known limits carried to later plans

1. **Report speed.** Building the report engine for 1,000,000 posting lines takes about 0.55 s. Plan 2 must keep it in memory and update it, not rebuild it for every report.
2. **An empty date range value** (`default(DateRange)`) bypasses the start and end check. Code must always use the `bool` result of `PeriodInput.TryParse`. Plan 2 and Plan 3 code reviews should check this.

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan 1 tasks and tests**: `docs/superpowers/plans/2026-10-26-plan-1-foundation-and-core.md`
