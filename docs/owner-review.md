# Owner Review

Major decisions and data checks that need the owner. Smaller decisions are the builder's, recorded in each plan. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 4: Client shell

1. **Scope.** Plan 4 builds the shell and the system screens `HELP`, `DATE`, `RFSH`, `CMPY`, `SETG`, `KEYS`, and `USER`. Every other code shows `{CODE} is not in this version yet.` The screens for masters, vouchers, and reports come next, followed by `AUDT`, `LOCK`, `BKUP`, `FIND`, and `PRNT`. Cost if wrong: later plans are reordered. Cheapest to change before Task 7 is built.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025. It allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 6.

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan tasks, tests, and builder decisions**: `docs/superpowers/plans/`
