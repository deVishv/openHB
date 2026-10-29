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

## Local REST API and MCP server

Scripts and AI assistants on the host PC can read the books, and enter vouchers if allowed, through a REST API and an MCP server. Both are off by default.

- They share one port, 47251 by default, which listens on 127.0.0.1 only. With `api.allowLan` on, it also listens on the LAN, with TLS.
- Every request needs an API token: `Authorization: Bearer ohk_...`. A token acts as its user, and is read only unless created as read and write.
- REST routes are under `/api/v1`. The OpenAPI document is at `/api/v1/openapi.json`. Amounts are strings like `"12345.60"`, never JSON numbers.
- MCP uses Streamable HTTP at `/mcp`. Tools that create vouchers and parties also need `mcp.allowWrites` on.
- The Owner turns these on with the host settings `api.enabled` and `mcp.enabled`, and each user creates their own tokens. The settings and token screens (`SETG`, `USER`) arrive with the desktop app in a later plan. Turning the API on takes effect when the host restarts.

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
