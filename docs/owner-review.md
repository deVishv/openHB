# Owner Review

Major decisions and data checks that need the owner. Smaller decisions are the builder's, recorded in each plan. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 5: Masters, vouchers, and bills

### Check before the first public release

1. **Export invoice fields** (Task 5): port code of 6 letters or digits, and shipping bill number of 3 to 7 digits. Each is optional at entry. Confirm against the GSTR-1 offline schema when Plan 7 adds the GST files.

### Plan order

2. **`AUDT` and `BKUP` screens** are not in Plan 5. Choose the plan for them when Plan 6 is written.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025. It allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 7 (the GST files plan, renumbered from 6).

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan tasks, tests, and builder decisions**: `docs/superpowers/plans/`
