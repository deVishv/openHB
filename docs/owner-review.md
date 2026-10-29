# Owner Review

Items that need the owner's check. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 3: Local REST API and MCP server

Decisions made while planning (`docs/superpowers/plans/2026-10-29-plan-3-rest-and-mcp.md`). Each has its cost if wrong. Cheapest to change before Tasks 2 and 4 are built.

1. **API port 47251** by default; the gRPC port stays 47250. Cost if wrong: one setting default.
2. **Restart to apply.** Turning the API on, changing its port, or allowing the LAN takes effect when the host service restarts. Turning the API or the LAN off applies at once. Cost if wrong: a live Kestrel rebind in the host.
3. **Token format.** `ohk_` followed by 43 random characters, stored only as a SHA-256 hash. Cost if wrong: existing tokens must be recreated.
4. **MCP write tools stay listed when writes are off.** They answer "Changes through MCP are off. The Owner can turn on mcp.allowWrites." Cost if wrong: tool list filtering per request.
5. **Failed tokens are slowed per address.** A valid token always passes, so one script on the host PC with a bad token cannot lock out the assistant. Cost if wrong: a different throttle rule.
6. **No firewall rule for the API port** when `api.allowLan` is on. The installer (Plan 8) opens only the gRPC port, as spec 4 says. Cost if wrong: one installer rule.
7. **The Owner cannot revoke another user's token** in version 1. Cost if wrong: one service method and a screen action.
8. **REST does not cover** openings, required ledgers, locks, settings, audit, or backups. Spec 4.1 lists the resources. Cost if wrong: more routes.
9. **MCP can create invoices** (`create_invoice`) as well as receipts, payments, journals, and contras, and parties. Spec 4.1 says "create a voucher". Cost if wrong: remove one tool. Ok
10. **No way to turn the API on or create a token without the client.** Plan 3 adds the host calls, but the `SETG` and `USER` screens come with the client in Plan 4. Until then only a gRPC client can do it. Options: wait for Plan 4, or add a small host command line tool. Cost if wrong: the API cannot be tried before Plan 4. Wait for Plan 4

## Plan 2: Data and host

No open items. Its decisions are recorded in the design spec, and their history is in git.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025, and allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 6.

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan 1 tasks and tests**: `docs/superpowers/plans/2026-10-26-plan-1-foundation-and-core.md`
- **Plan 2 tasks and tests**: `docs/superpowers/plans/2026-10-27-plan-2-data-and-host.md`
