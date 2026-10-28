# Owner Review

Items that need the owner's check. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

Plan 2 has no open items. Its decisions are recorded in the design spec, and their history is in git.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025, and allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 6.

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan 1 tasks and tests**: `docs/superpowers/plans/2026-10-26-plan-1-foundation-and-core.md`
- **Plan 2 tasks and tests**: `docs/superpowers/plans/2026-10-27-plan-2-data-and-host.md`
