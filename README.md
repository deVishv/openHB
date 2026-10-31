# Open Hisab Kitab

Open source accounting for Indian businesses.

Status: in development. Not ready for use.

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build
dotnet test
```

## Running the host

The host is the service that holds the books. The desktop app connects to it.

```
dotnet run --project src/OpenHisabKitab.Host
```

- Data goes in `%ProgramData%\OpenHisabKitab`. Change it with `--Host:DataDirectory=<folder>`.
- The gRPC port is 47250. Change it with `--Host:GrpcPort=<port>`.
- On first start the host makes a TLS certificate. Its fingerprint is in `host-cert-fingerprint.txt` in the data folder, and in the log. Clients show this fingerprint the first time they connect.

To run it as a Windows service, from an administrator prompt:

```
sc.exe create OpenHisabKitabHost binPath= "C:\Program Files\OpenHisabKitab\OpenHisabKitab.Host.exe" start= auto
netsh advfirewall firewall add rule name="Open Hisab Kitab host" dir=in action=allow protocol=TCP localport=47250
```

The installer will do both.

## Running the desktop app

With the host running:

```
dotnet run --project src/OpenHisabKitab.Client
```

- On first run, type the host's name or IP address and its port (47250). The app shows the host's certificate fingerprint once. Compare it with `host-cert-fingerprint.txt` on the host PC, then trust it. A changed certificate later blocks the connection until it is accepted again.
- The first user is the Owner, set up from the host PC itself.
- Type a four letter function at the `>` prompt, such as `CMPY` (companies), `SETG` (settings), `KEYS` (keybindings), `USER` (password, API tokens, users), `DATE`, or `HELP`. Enter runs it in this tab, Shift+Enter in a new one, and Ctrl+Shift+P searches functions and settings.
- This PC's settings, keybindings, and trusted hosts are in `%LOCALAPPDATA%\OpenHisabKitab`. Use `--profile <folder>` to keep them elsewhere, for example to run a second client.
- UI smoke tests drive the real app and need a desktop: set `OHK_UI_TESTS=1`, then run `dotnet test tests/OpenHisabKitab.Client.UiTests`.

### Entering vouchers

- Masters: `ACCT` (chart of accounts and opening balances), `PRTY` (customers and suppliers, with opening bills), `ITEM` (goods and services).
- Vouchers: `SALE` (F6), `PURC` (F7), `RCPT` (F8), `PYMT` (F9), `JRNL` (F10), `CRNT`, `DBNT`, and `CNTR`. Tab and Enter move to the next field; Enter on the last field adds a line. Ctrl+S saves and shows the number the host gave.
- Bills: a receipt, payment, or note for a party is allocated to its open bills, oldest first, as soon as the amount is typed. Ctrl+B opens the bills to change it; what is not allocated stays on account.
- A party that does not exist yet: type its name and press Enter, and `PRTY` opens over the voucher.
- `FIND text` (F3) finds vouchers by number, party, amount, or narration; Enter opens one to edit, and Alt+Left returns. Ctrl+Shift+Delete cancels the open voucher.
- `LOCK date` locks the books up to that date. In `CMPY`, Edit changes the selected company's name, address, and GSTIN.
- REST: `GET /api/v1/companies/{company}/parties/{id}/open-bills`, `GET` and `PUT .../parties/{id}/opening-bills`, `PUT /api/v1/companies/{company}`, and `GET .../vouchers?search=text`. Voucher bodies take `allocations` and a `requestId`, so a retried save never makes a second voucher. MCP adds the `open_bills` tool.

## Local REST API and MCP server

Scripts and AI assistants on the host PC can read the books, and enter vouchers if allowed, through a REST API and an MCP server. Both are off by default.

- They share one port, 47251 by default, which listens on 127.0.0.1 only. With `api.allowLan` on, it also listens on the LAN, with TLS.
- Every request needs an API token: `Authorization: Bearer ohk_...`. A token acts as its user, and is read only unless created as read and write.
- REST routes are under `/api/v1`. The OpenAPI document is at `/api/v1/openapi.json`. Amounts are strings like `"12345.60"`, never JSON numbers.
- MCP uses Streamable HTTP at `/mcp`. Tools that create vouchers and parties also need `mcp.allowWrites` on.
- The Owner turns these on in `SETG` with the host settings `api.enabled` and `mcp.enabled`, and each user creates their own tokens in `USER`. Turning the API on takes effect when the host service restarts, and `SETG` says so.

### MCP bridge

For assistants that only launch local programs, `ohk-mcp-bridge.exe` (project `src/OpenHisabKitab.McpBridge`) speaks MCP over stdin and stdout and forwards to `/mcp`. It reads `%APPDATA%\OpenHisabKitab\mcp-bridge.json`, or the file given with `--config <path>`:

```json
{
  "endpoint": "http://127.0.0.1:47251/mcp",
  "token": "ohk_...",
  "certificateFingerprint": null
}
```

For an `https` endpoint, set `certificateFingerprint` to the host's fingerprint from `host-cert-fingerprint.txt`. The bridge refuses to start without it.

## Design

See `docs/superpowers/specs/` for the design and `docs/superpowers/plans/` for the build plans.

## Licence

MIT. See `LICENSE`.
