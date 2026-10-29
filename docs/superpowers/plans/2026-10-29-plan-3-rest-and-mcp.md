# Plan 3: Local REST API and MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let scripts and AI assistants on the host PC read and enter accounting data through a token protected REST API and an MCP server, both thin adapters over the Plan 2 application services, plus a stdio bridge for MCP clients that only launch local programs.

**Architecture:**
- A second Kestrel listener, the **API port**, serves HTTP/1.1 on 127.0.0.1 by default, or on every address with TLS when `api.allowLan` is on. The gRPC port keeps serving only gRPC.
- One middleware, `ApiGate`, runs first on every request:
  - It keeps routes on their own port.
  - It enforces `api.enabled` and `mcp.enabled`, the LAN rule, and the `Origin` check.
  - It turns the bearer API token into a `CallContext` whose `Source` is `Rest` or `Mcp`.
- Two new application services:
  - `ApiTokenService` creates, lists, revokes, and resolves tokens.
  - `HostSettingsService` holds the six `api.*` and `mcp.*` host settings.
  - The gRPC adapter exposes both, because the `USER` screen comes in a later plan.
- REST routes (`Host/Api`) and MCP tools (`Host/Mcp`) only translate. Every rule, permission check, and audit record stays in the services.
- `OpenHisabKitab.McpBridge` is a stdio to HTTP forwarder with no data or rules.

**Tech Stack:**
- .NET 10 and ASP.NET Core minimal APIs.
- `Microsoft.AspNetCore.OpenApi` 10.0.14.
- `ModelContextProtocol.AspNetCore` 2.3.0 (Streamable HTTP, stateless).
- xUnit 2.9.3 and `Microsoft.AspNetCore.Mvc.Testing`, as before.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. Sections 4.1, 5.5, 7 (host settings), 8 (API tokens), and 14 apply.

**SDK facts checked in a spike on 2026-10-29** (the plan depends on them):
- `AddMcpServer(o => o.ServerInfo = new() { Name = ..., Version = ... }).WithHttpTransport(o => o.Stateless = true).WithTools<T>()` and `app.MapMcp("/mcp")` build clean with `TreatWarningsAsErrors` next to this repo's pinned packages.
- The SDK hides the message of any exception thrown from a tool ("An error occurred invoking 'x'."). It also prefixes an `McpException` message. Tools must therefore return `ModelContextProtocol.Protocol.CallToolResult { IsError = true, Content = [new TextContentBlock { Text = message }] }` themselves.
- The SDK serializes a tool's return value with its own options, so a `Money` comes out as `{"paise":1234560}`. Tools therefore return a `CallToolResult` whose text this plan serializes itself.
- Replies are `text/event-stream` (`event: message` and `data: {...}` lines), even when the client also accepts JSON. A notification gets 202 with an empty body.
- The SDK binds tool arguments with its own JSON options, so strict `ConfigureHttpJsonOptions` settings do not affect it. An `initialize` with `_meta` and extra fields still works.
- When an argument has the wrong JSON type, such as a number where a string is declared, the SDK answers with the masked "An error occurred invoking '{tool}'." and the tool never runs. Unknown fields inside an argument object are ignored.

## Global Constraints

- Everything in Plan 2's Global Constraints still holds: build settings, central package versions, money as `long` paise, `TimeProvider`, one writer per company, one line messages, and commits.
- **New package versions** in `Directory.Packages.props`:
  - `Microsoft.AspNetCore.OpenApi` 10.0.14
  - `ModelContextProtocol.AspNetCore` 2.3.0
- **Amounts on the wire** (spec 4.1):
  - Money is a JSON string of rupees with exactly two decimals, such as `"12345.60"`, with a leading `-` for negative. Quantities use three decimals, and rates two (`"18.00"`).
  - A JSON number, a comma, a missing or extra decimal, a leading `+`, or a leading zero (`"01.00"`) is rejected.
  - Only `Core.Amounts.WireAmount` parses and formats them.
- **Dates on the wire** are `yyyy-MM-dd`. Request query parameters are parsed with `DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)`, never by minimal API binding.
- **Adapters hold no rules.** Code under `Host/Api` and `Host/Mcp` holds no SQL, no permission check, and no accounting rule. It calls one service method per operation. The one exception is the adapter level `mcp.allowWrites` message described in Task 8.
- **Default API port** is `47251`. The gRPC port stays `47250`.
- **Audit machine field** for REST and MCP calls is `Environment.MachineName` when the caller is on loopback, otherwise the caller's IP address as text.
- **Every response from the API port**, including 404 for an unknown route, 405, a body that is not valid JSON, and 500, uses the one error shape of Task 4.

## Review Focus

These failure modes are implied by the spec but no example exercises them. Each has a test in the task named.

1. **An amount in the wrong form.** A voucher whose line amount is `12345.6`, `"12,345.60"`, or the JSON number `12345.60` gets 400, nothing is saved, and the next good save still gets number 1. Test in Task 6.
2. **A web page in a browser calling loopback with a stolen or guessed token.** A request with `Origin: https://evil.example` is refused with 403 before the token is even looked at, so it never counts toward the throttle and never reaches a service. Test in Task 4.
3. **A read only token, or MCP with writes off, trying to change data.** It gets the exact refusal message, and no voucher, number, or audit record is written. Tests in Tasks 6 and 8.
4. **Two programs editing the same voucher.** The second edit over REST gets 409 with the current voucher in wire format. Test in Task 6.
5. **The wrong kind of credential on the wrong port.** An app session token on the API port gets 401. An API token on the gRPC port gets "Log in again.". Tests in Tasks 4 and 3.

---

## File Structure

```
Directory.Packages.props                               + OpenApi, ModelContextProtocol.AspNetCore (Tasks 5, 7)
OpenHisabKitab.slnx                                    + McpBridge and its tests (Task 9)
src/OpenHisabKitab.Core/Amounts/WireAmount.cs          strict wire parse and format (Task 1)
src/OpenHisabKitab.Data/HostDb/ApiTokenRepository.cs   api_tokens rows (Task 2)
src/OpenHisabKitab.Host/Application/
  HostSettings.cs                                      the six settings: definitions and typed read (Task 2)
  HostSettingsService.cs                               Owner reads and sets them (Task 2)
  ApiTokenService.cs                                   create, list, revoke, authenticate (Task 2)
  CallContext.cs, ServiceException.cs, LoginThrottle.cs  ReadOnlyReason, TooManyAttempts (Task 2)
src/OpenHisabKitab.Contracts/Protos/tokens.proto       Tokens service (Task 3)
src/OpenHisabKitab.Contracts/Protos/admin.proto        + host settings rpcs (Task 3)
src/OpenHisabKitab.Host/Grpc/Endpoints.cs              + TokensEndpoint, settings (Task 3)
src/OpenHisabKitab.Host/Api/
  ApiListener.cs                                       Kestrel API listener, IListenerAccessor (Task 4)
  ApiGate.cs                                           port, switches, LAN, Origin, bearer (Task 4)
  ApiErrors.cs                                         one error shape and status table (Task 4)
  ApiJson.cs                                           JSON options and output converters (Task 5)
  ApiModels.cs                                         request and response records, mapping (Tasks 5, 6)
  RestRoutes.cs                                        /api/v1 routes (Tasks 5, 6)
  OpenApiSetup.cs                                      /api/v1/openapi.json (Task 7)
src/OpenHisabKitab.Host/Mcp/
  McpResults.cs                                        CallToolResult helpers (Task 8)
  ReadTools.cs, WriteTools.cs                          MCP tools (Task 8)
src/OpenHisabKitab.McpBridge/                          stdio bridge (Task 9)
tests/OpenHisabKitab.Core.Tests/Amounts/WireAmountTests.cs
tests/OpenHisabKitab.Data.Tests/ApiTokenRepositoryTests.cs
tests/OpenHisabKitab.Host.Tests/ApiTokenServiceTests.cs, HostSettingsServiceTests.cs
tests/OpenHisabKitab.Host.Tests/Api/ApiHelpers.cs, ApiGateTests.cs, RestReadTests.cs, RestWriteTests.cs, OpenApiTests.cs, ApiKestrelTests.cs
tests/OpenHisabKitab.Host.Tests/Mcp/McpToolTests.cs
tests/OpenHisabKitab.McpBridge.Tests/BridgeTests.cs
```

---

### Task 1: Strict wire amounts in Core

`Money.TryParse` is meant for typing. It accepts commas, one decimal, and no decimals, so it cannot be used for the wire format.

**Files:**
- Create: `src/OpenHisabKitab.Core/Amounts/WireAmount.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Amounts/WireAmountTests.cs`

**Interfaces:**
- Produces, in `OpenHisabKitab.Core.Amounts` (`GstRate` is in `Core.Gst`):
  - `public static class WireAmount`
  - `static string Format(Money m)`, `static string Format(Quantity q)`, `static string Format(GstRate r)`
  - `static bool TryParseMoney(string? s, out Money m)`, `static bool TryParseQuantity(string? s, out Quantity q)`, `static bool TryParseRate(string? s, out GstRate r)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("12345.60", 1234560)]
[InlineData("-12345.60", -1234560)]
[InlineData("0.05", 5)]
[InlineData("0.00", 0)]
[InlineData("92233720368547758.07", long.MaxValue)]
[InlineData("-92233720368547758.08", long.MinValue)]
public void Parses_money(string s, long paise)
{
    Assert.True(WireAmount.TryParseMoney(s, out var m));
    Assert.Equal(paise, m.Paise);
    Assert.Equal(s, WireAmount.Format(m));
}

[Theory]
[InlineData("12345.6")] [InlineData("12345")] [InlineData("12,345.60")] [InlineData("+1.00")]
[InlineData("01.00")] [InlineData("-0.00")] [InlineData(" 1.00")] [InlineData("1.00 ")] [InlineData("1.000")]
[InlineData("")] [InlineData(null)] [InlineData("1e3")] [InlineData("92233720368547758.08")] [InlineData("١.٠٠")]
public void Rejects_other_money_forms(string? s) => Assert.False(WireAmount.TryParseMoney(s, out _));

[Fact]
public void Quantity_has_three_decimals()
{
    Assert.True(WireAmount.TryParseQuantity("2.500", out var q));
    Assert.Equal(2500, q.Thousandths);
    Assert.Equal("2.500", WireAmount.Format(q));
    Assert.False(WireAmount.TryParseQuantity("2.5", out _));
}

[Fact]
public void Rate_has_two_decimals_and_is_never_negative()
{
    Assert.True(WireAmount.TryParseRate("18.00", out var r));
    Assert.Equal(1800, r.BasisPoints);
    Assert.Equal("0.25", WireAmount.Format(new GstRate(25)));
    Assert.False(WireAmount.TryParseRate("-5.00", out _));
    Assert.False(WireAmount.TryParseRate("18", out _));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Core.Tests --filter FullyQualifiedName~WireAmountTests`
Expected: build error, `WireAmount` does not exist.

- [ ] **Step 3: Implement `WireAmount`**

Parse with one rule, shared by all three types and given the number of decimals:
- An optional `-`, then `0` or a nonzero ASCII digit followed by ASCII digits, then `.`, then exactly N ASCII digits.
- `-0.00` is refused, because it is never what the host writes.
- Accumulate in `checked` `long` arithmetic and return false on overflow. The minimum `long` must parse, so accumulate the negative value.
- `TryParseRate` refuses a minus sign, and refuses values above `int.MaxValue`.
- Format money the way `Money.ToString` does; it already writes this form. Format quantity and rate the same way with three and two decimals.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/OpenHisabKitab.Core.Tests --filter FullyQualifiedName~WireAmountTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenHisabKitab.Core/Amounts/WireAmount.cs tests/OpenHisabKitab.Core.Tests/Amounts/WireAmountTests.cs
git commit -m "feat(core): strict wire format for amounts, quantities, and rates"
```

---

### Task 2: API tokens and host settings services

**Files:**
- Create: `src/OpenHisabKitab.Data/HostDb/ApiTokenRepository.cs`
- Create: `src/OpenHisabKitab.Host/Application/HostSettings.cs`, `HostSettingsService.cs`, `ApiTokenService.cs`
- Modify: `src/OpenHisabKitab.Host/Application/CallContext.cs`, `ServiceException.cs`, `LoginThrottle.cs`, `src/OpenHisabKitab.Host/Grpc/Interceptors.cs` (status map only), `src/OpenHisabKitab.Host/HostSetup.cs` (register both services)
- Test: `tests/OpenHisabKitab.Data.Tests/ApiTokenRepositoryTests.cs`, `tests/OpenHisabKitab.Host.Tests/ApiTokenServiceTests.cs`, `tests/OpenHisabKitab.Host.Tests/HostSettingsServiceTests.cs`
- Modify: `tests/OpenHisabKitab.Host.Tests/TestHost.cs` (add `Tokens` and `HostSettings` properties)

**Interfaces:**
- Produces, in `OpenHisabKitab.Data.HostDb`:
  - `public sealed record ApiTokenRecord(int Id, int UserId, string Name, bool ReadWrite, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)`
  - `static int Insert(SqliteTransaction tx, int userId, string name, string tokenHash, bool readWrite, DateTimeOffset at)`
  - `static (ApiTokenRecord Token, string Hash)? FindByHash(SqliteConnection c, string tokenHash)`
  - `static IReadOnlyList<ApiTokenRecord> ListForUser(SqliteConnection c, int userId)` (newest first, revoked included)
  - `static ApiTokenRecord? Get(SqliteConnection c, int id)`
  - `static bool Revoke(SqliteTransaction tx, int id, DateTimeOffset at)` (false when already revoked)
  - `static void Touch(SqliteConnection c, int id, DateTimeOffset at)` (outside any transaction)
- Produces, in `OpenHisabKitab.Host.Application`:
  - `public sealed record HostSettingDefinition(string Key, string Type, string DefaultJson, string Description, string AllowedValues)`
  - `public sealed record ApiSettings(bool Enabled, int Port, bool AllowLan, IReadOnlyList<string> AllowedOrigins, bool McpEnabled, bool McpAllowWrites)` with `public static ApiSettings Read(SqliteConnection hostDb)`
  - `public static class HostSettings { public static IReadOnlyList<HostSettingDefinition> All; public const int DefaultApiPort = 47251; }`
  - `HostSettingsService(HostData data, OpenHisabKitabOptions options, TimeProvider time)`:
    - `IReadOnlyList<(HostSettingDefinition Definition, string Json)> List(CallContext ctx)`
    - `bool Set(CallContext ctx, string key, string json)` returns true when a restart is needed.
  - `public sealed record CreatedToken(int Id, string Name, bool ReadWrite, string Token)`
  - `ApiTokenService(HostData data, TimeProvider time)`:
    - `CreatedToken Create(CallContext ctx, string name, bool readWrite)`
    - `IReadOnlyList<ApiTokenRecord> List(CallContext ctx)`
    - `void Revoke(CallContext ctx, int id)`
    - `CallContext Authenticate(string? bearer, ChangeSource source, IPAddress? remote)`
  - `CallContext` gains a last positional parameter `string? ReadOnlyReason = null`. `Require(p)` throws `new ServiceException(ServiceError.PermissionDenied, ReadOnlyReason)` when the role has `p`, `ReadOnly` is the only reason it is refused, and `ReadOnlyReason` is set. Otherwise it throws `ServiceException.Denied()` as before.
  - `ServiceError.TooManyAttempts` is added. `ServiceException` gains `public TimeSpan? RetryAfter { get; init; }`. `LoginThrottle.Check` throws `TooManyAttempts` with `RetryAfter` set; its message is unchanged. gRPC maps `TooManyAttempts` to `Unauthenticated`, so gRPC behaviour is unchanged.

**The six settings** (spec 7). Each is stored as JSON in `host_settings`, and a missing key means the default.

| Key | Type | Default | Description | Allowed values | Restart needed when |
|---|---|---|---|---|---|
| `api.enabled` | bool | `false` | Turns on the local REST API port. | true, false | turned on |
| `api.port` | int | `47251` | Port for the REST API and MCP. | 1024 to 65535, not the gRPC port | changed |
| `api.allowLan` | bool | `false` | Also listen on the LAN, with TLS. | true, false | turned on |
| `api.allowedOrigins` | string[] | `[]` | Web origins allowed to call the API. | origins such as `https://example.com` | never |
| `mcp.enabled` | bool | `false` | Turns on the MCP endpoint. Needs api.enabled. | true, false | never |
| `mcp.allowWrites` | bool | `false` | Lets MCP create vouchers and parties. | true, false | never |

Turning `api.enabled` or `api.allowLan` off applies at once, because `ApiGate` reads the settings on every request (Task 4).

**Exact messages:**
- Unknown key: `No host setting named {key}.`
- Wrong type or value: `{key} must be {AllowedValues}.`
- `api.port` equal to the gRPC port: `api.port must differ from the gRPC port {port}.`
- Bad origin: `api.allowedOrigins entries must look like https://example.com.` An entry must parse as an absolute `http` or `https` URI with no path other than `/`, no query, and no fragment.
- Token name empty, longer than 60 characters, or containing control characters: `Enter a token name of up to 60 characters.`
- Token name already used by one of the caller's unrevoked tokens: `You already have a token named {name}.`
- Token create or revoke from a REST or MCP caller: `Create and revoke tokens in Open Hisab Kitab.`
- Revoke of an id that is not the caller's: NotFound, `No such token.`
- Missing, unknown, or revoked bearer, or an inactive user: NotAuthenticated, `Send a valid API token.`
- Read only token reason: `This token is read only. Create a read and write token to make changes.`
- MCP writes off reason: `Changes through MCP are off. The Owner can turn on mcp.allowWrites.`

**Token rules:**
- Format: `ohk_` followed by the unpadded base64url of 32 random bytes. The stored hash is lowercase hex SHA-256 of the whole token's UTF-8 bytes. A token has 256 random bits, so a fast hash is enough. `PasswordHasher` would also make the `UNIQUE token_hash` lookup impossible.
- A request with no bearer at all throws `Send a valid API token.` without counting as a failure. Only a token that was sent and refused counts.
- Spec 4.1 slows down *failed* tokens. A valid token is 256 random bits, so it passes even while its address is slowed. That way one script on 127.0.0.1 with a bad token cannot lock out the assistant. `Authenticate` runs these steps in order:
  1. Look up the token by its hash, and re-read its user.
  2. When the token is unknown or revoked, or the user is inactive:
     - throttle `Check` on the key `api:{remote}`, which throws `TooManyAttempts` while the address is slowed;
     - otherwise record `Failed` and throw `Send a valid API token.`.
     The throttle is its own `LoginThrottle` instance.
  3. When the token is valid, call `Succeeded` for the address.
  4. Touch `last_used_at` when it is null or more than 60 seconds old.
  5. Build the context with `AuthService.ContextFor`, then set `Source`, `TokenName`, the machine (Global Constraints), `ReadOnly`, and `ReadOnlyReason`:
     - When the token is read only: `ReadOnly = true` with the read only reason.
     - When the source is MCP, the token is read and write, and `mcp.allowWrites` is off: `ReadOnly = true` with the MCP reason.
- Create and revoke append to the host audit log, with action `create-token` or `revoke-token`, entity `api-token`, the token id as key, and `{Name, ReadWrite}` as after or before. The token text is never logged.

- [ ] **Step 1: Write the failing repository test**

`ApiTokenRepositoryTests.Insert_find_list_revoke_touch` round trips one row through every method, asserts `FindByHash` returns null for an unknown hash, and asserts a second `Revoke` returns false.

- [ ] **Step 2: Write the failing service tests**

```csharp
[Fact]
public void Token_is_shown_once_and_only_its_hash_is_stored()
{
    using var h = new TestHost();
    var t = h.Tokens.Create(h.Owner.Context, "scripts", readWrite: false);
    Assert.Matches("^ohk_[A-Za-z0-9_-]{43}$", t.Token);
    using var c = h.Data.OpenHost();
    var stored = Sql.Query(c, "SELECT token_hash FROM api_tokens", r => r.GetString(0)).Single();
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(t.Token))), stored);
    Assert.DoesNotContain(t.Token, string.Join("|", h.Tokens.List(h.Owner.Context)));
    Assert.Empty(Sql.Query(c, "SELECT seq FROM audit_log WHERE instr(coalesce(before,'') || coalesce(after,''), $t) > 0",
        r => r.GetInt64(0), ("$t", t.Token)));
}

[Fact]
public void Authenticated_context_carries_source_token_name_and_user_limits()
{
    using var h = new TestHost();
    var a = h.NewCompany(); var b = h.NewCompany(TestHost.Profile("B Traders"));
    var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, a);
    var t = h.Tokens.Create(clerk.Context, "excel", readWrite: true);
    var ctx = h.Tokens.Authenticate(t.Token, ChangeSource.Rest, IPAddress.Loopback);
    Assert.Equal((ChangeSource.Rest, "excel", "clerk", false), (ctx.Source, ctx.TokenName, ctx.UserName, ctx.ReadOnly));
    Assert.Equal(Environment.MachineName, ctx.Machine);
    Assert.Throws<ServiceException>(() => ctx.RequireCompany(b));
    Assert.False(ctx.Has(Permission.ViewReports));
}

[Fact]
public void Read_only_token_refuses_writes_with_its_reason()
{
    using var h = new TestHost();
    var t = h.Tokens.Create(h.Owner.Context, "ro", readWrite: false);
    var ctx = h.Tokens.Authenticate(t.Token, ChangeSource.Rest, IPAddress.Loopback);
    var e = Assert.Throws<ServiceException>(() => ctx.Require(Permission.CreateVoucher));
    Assert.Equal("This token is read only. Create a read and write token to make changes.", e.Message);
    ctx.Require(Permission.ViewReports);
}

[Fact]
public void Mcp_read_write_token_is_read_only_until_writes_are_allowed()
{
    using var h = new TestHost();
    var t = h.Tokens.Create(h.Owner.Context, "claude", readWrite: true);
    var e = Assert.Throws<ServiceException>(() =>
        h.Tokens.Authenticate(t.Token, ChangeSource.Mcp, IPAddress.Loopback).Require(Permission.CreateVoucher));
    Assert.Equal("Changes through MCP are off. The Owner can turn on mcp.allowWrites.", e.Message);
    h.HostSettings.Set(h.Owner.Context, "mcp.allowWrites", "true");
    h.Tokens.Authenticate(t.Token, ChangeSource.Mcp, IPAddress.Loopback).Require(Permission.CreateVoucher);
}

[Fact]
public void Revoked_token_fails_on_the_next_request()
{
    using var h = new TestHost();
    var t = h.Tokens.Create(h.Owner.Context, "x", readWrite: false);
    h.Tokens.Revoke(h.Owner.Context, t.Id);
    var e = Assert.Throws<ServiceException>(() => h.Tokens.Authenticate(t.Token, ChangeSource.Rest, IPAddress.Loopback));
    Assert.Equal((ServiceError.NotAuthenticated, "Send a valid API token."), (e.Error, e.Message));
}

[Fact]
public void Inactive_user_token_fails()   // set users.active = 0 with Sql.Execute; Plan 8 adds the screen
{ /* create token, deactivate, Authenticate throws NotAuthenticated "Send a valid API token." */ }

[Fact]
public void Token_cannot_mint_or_revoke_tokens()
{
    using var h = new TestHost();
    var t = h.Tokens.Create(h.Owner.Context, "rw", readWrite: true);
    var ctx = h.Tokens.Authenticate(t.Token, ChangeSource.Rest, IPAddress.Loopback);
    Assert.Equal("Create and revoke tokens in Open Hisab Kitab.",
        Assert.Throws<ServiceException>(() => h.Tokens.Create(ctx, "more", true)).Message);
    Assert.Equal("Create and revoke tokens in Open Hisab Kitab.",
        Assert.Throws<ServiceException>(() => h.Tokens.Revoke(ctx, t.Id)).Message);
}

[Fact]
public void Users_see_and_revoke_only_their_own_tokens()
{ /* clerk cannot see owner's token in List; Revoke(owner token id) throws NotFound "No such token." */ }

[Fact]
public void Duplicate_and_bad_names_are_refused()
{ /* "" and 61 chars -> "Enter a token name of up to 60 characters."; second "x" -> "You already have a token named x."; after revoking, "x" is allowed again */ }

[Fact]
public void Five_bad_tokens_from_one_address_slow_that_address_only()
{
    using var h = new TestHost();
    var other = IPAddress.Parse("192.168.1.9");
    for (var i = 0; i < 5; i++) Assert.Throws<ServiceException>(() => h.Tokens.Authenticate("ohk_bad", ChangeSource.Rest, IPAddress.Loopback));
    var e = Assert.Throws<ServiceException>(() => h.Tokens.Authenticate("ohk_bad", ChangeSource.Rest, IPAddress.Loopback));
    Assert.Equal(ServiceError.TooManyAttempts, e.Error);
    Assert.Equal(TimeSpan.FromSeconds(30), e.RetryAfter);
    Assert.Equal(ServiceError.NotAuthenticated,
        Assert.Throws<ServiceException>(() => h.Tokens.Authenticate("ohk_bad", ChangeSource.Rest, other)).Error);
    var good = h.Tokens.Create(h.Owner.Context, "good", readWrite: false);
    h.Tokens.Authenticate(good.Token, ChangeSource.Rest, IPAddress.Loopback);   // a valid token is never slowed
}

[Fact]
public void Last_used_is_written_at_most_once_a_minute()
{ /* Authenticate twice 10 s apart -> last_used_at equals first time; advance 61 s -> updated */ }

[Fact]
public void Create_and_revoke_are_audited_without_the_token_text()
{ /* host audit has create-token and revoke-token, entity api-token, user owner; no row contains t.Token */ }
```

`HostSettingsServiceTests`:
- `Defaults_are_listed_with_their_definitions`: all six keys, in table order, with the default JSON.
- `Only_the_owner_sets_host_settings`: an Accountant gets `You do not have permission for this.`.
- `Values_are_validated` uses the exact messages above. The cases: `api.port` `"80"`, `"47250"`, and `"\"x\""`; `api.enabled` `"1"`; `api.allowedOrigins` `["https://a.example/path"]`; an unknown key.
- `Restart_needed_only_where_the_table_says`:
  - `api.enabled` false to true gives true, and true to false gives false.
  - `api.port` changed gives true.
  - `api.allowLan` on gives true, and off gives false.
  - `mcp.enabled` gives false.
- `Setting_changes_are_audited`: action `set`, entity `host-setting`, key the setting key, and before and after JSON.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test OpenHisabKitab.slnx --filter "FullyQualifiedName~ApiToken|FullyQualifiedName~HostSettingsService"`
Expected: build errors for the missing types.

- [ ] **Step 4: Implement the repository, the two services, and the `CallContext`, `ServiceException`, and `LoginThrottle` changes**

`HostSettingsService` needs `Permission.ManageHostSettings`. Setting a value writes the JSON exactly as given, after parsing, in a `WriteHost` transaction with the audit append.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test OpenHisabKitab.slnx`
Expected: PASS. The existing throttle tests still pass, because the throttle's message did not change; update any that assert `ServiceError.NotAuthenticated` for the throttled case to `TooManyAttempts`.

- [ ] **Step 6: Commit**

```bash
git add -A src tests
git commit -m "feat(host): API tokens and host settings services"
```

---

### Task 3: gRPC for tokens and host settings

The `USER` and `SETG` screens come in later plans. They need these calls, and so do this plan's own tests and the owner's first token.

**Files:**
- Create: `src/OpenHisabKitab.Contracts/Protos/tokens.proto`
- Modify: `src/OpenHisabKitab.Contracts/Protos/admin.proto`, `src/OpenHisabKitab.Host/Grpc/Endpoints.cs`, `src/OpenHisabKitab.Host/Grpc/Wire.cs`, `src/OpenHisabKitab.Host/HostSetup.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Grpc/GrpcEndToEndTests.cs`

**Interfaces:**
- Consumes: `ApiTokenService`, `HostSettingsService` (Task 2).
- Produces:
  - `service Tokens { rpc Create(CreateTokenRequest) returns (CreatedTokenMsg); rpc List(Empty) returns (TokenList); rpc Revoke(TokenRef) returns (Empty); }` in package `ohk.v1`.
    - `CreateTokenRequest{string name=1; bool read_write=2;}`
    - `CreatedTokenMsg{int32 id=1; string name=2; bool read_write=3; string token=4;}`
    - `TokenMsg{int32 id=1; string name=2; bool read_write=3; string created_at=4; string last_used_at=5; string revoked_at=6;}`, where an empty string means none and times are UTC `"O"`.
  - `Admin` gains `rpc ListHostSettings(Empty) returns (HostSettingList); rpc SetHostSetting(HostSettingRequest) returns (SetHostSettingReply);`.
    - `HostSettingMsg` carries `key`, `type`, `default_json`, `description`, `allowed_values`, and `json`.
    - `SetHostSetting` takes a new `HostSettingRequest{string key=1; string json=2;}` and returns `SetHostSettingReply{bool restart_needed=1;}`.

- [ ] **Step 1: Write the failing end to end tests**

- `Owner_creates_lists_and_revokes_a_token_over_grpc`: Create returns a token that starts with `ohk_`. List shows it with no `revoked_at`. After Revoke, List shows a `revoked_at`.
- `Api_token_is_not_a_session_on_the_grpc_port`: create a token, then call `Companies.List` with `authorization: Bearer {token}`. Expect `Unauthenticated` with detail `Log in again.`.
- `Owner_sets_a_host_setting_over_grpc`: `SetHostSetting("api.enabled","true")` returns `restart_needed = true`, and `ListHostSettings` shows `json == "true"`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~GrpcEndToEndTests`
Expected: build errors for the missing generated clients.

- [ ] **Step 3: Add the protos and the `TokensEndpoint` and admin methods** (translation only), and map `TokensEndpoint` in `MapOpenHisabKitab`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~GrpcEndToEndTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(grpc): token and host setting calls"
```

---

### Task 4: The API port and its gate

**Files:**
- Create: `src/OpenHisabKitab.Host/Api/ApiListener.cs`, `ApiGate.cs`, `ApiErrors.cs`
- Modify: `src/OpenHisabKitab.Host/HostApp.cs`, `src/OpenHisabKitab.Host/HostSetup.cs`, `tests/OpenHisabKitab.Host.Tests/Grpc/HostFactory.cs`
- Create: `tests/OpenHisabKitab.Host.Tests/Api/ApiHelpers.cs`, `ApiGateTests.cs`, `ApiKestrelTests.cs`

**Interfaces:**
- Consumes: `ApiSettings.Read`, `ApiTokenService.Authenticate` (Task 2).
- Produces:
  - `public interface IListenerAccessor { bool OnApiPort(HttpContext context); }`. The production `KestrelListener(int apiPort)` returns `context.Connection.LocalPort == apiPort`. Never use the `Host` header, because the caller controls it.
  - `public static class ApiListener { public static void Configure(KestrelServerOptions kestrel, ApiSettings settings, X509Certificate2 certificate); }`:
    - It does nothing when `!settings.Enabled`.
    - It calls `ListenAnyIP(settings.Port)` with `UseHttps(certificate)` when `AllowLan`, otherwise `Listen(IPAddress.Loopback, settings.Port)`.
    - It always sets `Protocols = HttpProtocols.Http1`, so gRPC cannot be served on the API port.
  - `internal static class ApiCall { public static CallContext Caller(this HttpContext c); }` reads `HttpContext.Items["ohk.context"]`.
  - `public static class ApiErrors`:
    - `static Task Write(HttpContext c, int status, string code, string message, object? current = null)`
    - `static (int Status, string Code) Map(ServiceError e)`
  - `internal sealed class ApiGate(RequestDelegate next, ...)`, added with `app.UseMiddleware<ApiGate>()` before routing.
  - In tests: `HostFactory` registers a `TestListener` that returns true when the request has the header `X-Test-Port: api`. `ApiHelpers` has:
    - `static HttpClient ApiClient(this HostFactory f, string? token)`, which sets that header and the bearer.
    - `static void EnableApi(this HostFactory f, bool mcp = false, bool mcpWrites = false)`, which sets the settings through `HostSettingsService` as the Owner.
    - `static string NewToken(this HostFactory f, string user = "owner", bool readWrite = false)`.

**`HostApp.Build`:**
- Right after `AddOpenHisabKitab`, and before `ConfigureKestrel`, open `host.db` with `new HostData(options.DataDirectory).OpenHost()`, which migrates it.
- Read `ApiSettings` and register `IListenerAccessor` as `new KestrelListener(settings.Port)`. The service collection is read only once the `ConfigureKestrel` callback runs, so this cannot happen there.
- Inside the existing `ConfigureKestrel` callback, call `ApiListener.Configure(kestrel, settings, certificate)`.

**`ApiGate` order** (stop at the first that applies).

Routing ignores case, so every path test here uses `Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)`; otherwise `/API/v1/...` or `/MCP` would slip past step 1 on the gRPC port. `ApiCall.Caller` throws NotAuthenticated `Send a valid API token.` when `Items` has no context, so a route the gate somehow skipped still refuses.

1. Not on the API port, and the path starts with `/api` or `/mcp`: 404 `not_found`, `No such route.`.
2. Not on the API port: call `next`, so gRPC is unchanged.
3. On the API port, and the path does not start with `/api/v1` or `/mcp`: 404 `not_found`, `No such route.`.
4. Read `ApiSettings` from `host.db`. If `!Enabled`: 503 `unavailable`, `The API is off. The Owner can turn on api.enabled.`.
5. `!AllowLan` and the caller is not loopback (`IRemoteAddressAccessor`, `Loopback.Is`): 403 `permission_denied`, `The API accepts only programs on the host PC.`.
6. An `Origin` header is present and not in `AllowedOrigins` (ordinal, case insensitive, trailing `/` ignored): 403 `permission_denied`, `Requests from web pages are not allowed.`. This runs before the token is read.
7. The path starts with `/mcp` and `!McpEnabled`: 503 `unavailable`, `MCP is off. The Owner can turn on mcp.enabled.`.
8. Authenticate the `Authorization: Bearer` value with source `Mcp` for `/mcp`, otherwise `Rest`. Store the context in `Items["ohk.context"]`.
9. Call `next` inside a try block that maps exceptions with the status table below.

**Status table and error shape.** The body is always `{"error":{"code":"...","message":"...","current":...}}`, where `current` appears only on 409.

| ServiceError | HTTP | code |
|---|---|---|
| NotAuthenticated | 401, plus `WWW-Authenticate: Bearer` | `not_authenticated` |
| TooManyAttempts | 429, plus `Retry-After: {seconds}` | `too_many_attempts` |
| PermissionDenied | 403 | `permission_denied` |
| NotFound | 404 | `not_found` |
| Conflict | 409 | `conflict` |
| Invalid | 400 | `invalid` |
| Locked, OpeningDifference, SetupRequired, TooNew | 422 | `locked`, `opening_difference`, `setup_required`, `too_new` |
| Unavailable | 503 | `unavailable` |
| any other exception | 500, logged | `internal`, `Something went wrong. Details are in the host log.` |

- An endpoint that matched the route but not the method: 405 `method_not_allowed`, `This route does not accept {METHOD}.`.
- No endpoint matched: 404 `not_found`, `No such route.`.
- A request body that is not valid JSON, or a field of the wrong JSON type (a `JsonException`, including when wrapped in `BadHttpRequestException`): 400 `invalid`, `Check {JsonException.Path} in the request body.`. When there is no path: `The request body is not valid JSON.`.

Detect a 404 or 405 after `next` returns, when the status is 404 or 405 and the response has not started, and rewrite it in the error shape. Set `RouteHandlerOptions.ThrowOnBadRequest = true` so binding failures reach the gate as exceptions.

- [ ] **Step 1: Write the failing gate tests**

Add a route `GET /api/v1/info` returning `{"version": HostVersion.Current}` so the gate can be tested before Task 5. Like every route, it needs a token, because spec 4.1 allows no anonymous access.

```csharp
[Fact]
public async Task Info_needs_a_token_and_reports_the_host_version()
{
    using var f = new HostFactory(); f.EnableApi();
    var anon = await f.ApiClient(null).GetAsync("/api/v1/info");
    Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    Assert.Equal("Bearer", anon.Headers.WwwAuthenticate.Single().Scheme);
    Assert.Equal("""{"error":{"code":"not_authenticated","message":"Send a valid API token."}}""", await anon.Content.ReadAsStringAsync());
    var ok = await f.ApiClient(f.NewToken()).GetFromJsonAsync<JsonElement>("/api/v1/info");
    Assert.Equal(HostVersion.Current, ok.GetProperty("version").GetString());
}
```

Further tests in `ApiGateTests`. Each asserts the status and the exact body message:
- `Api_off_by_default_answers_503`, without `EnableApi`.
- `Session_token_is_refused_on_the_api_port`: log in with gRPC `SetupOwner`, then use that session token as the bearer. Expect 401.
- `Origin_is_refused_before_the_token_is_checked`: send `Origin: https://evil.example` with a bad token six times. Expect 403 every time. A following request with no `Origin` and a bad token gets 401, not 429, so the throttle never counted.
- `Allowed_origin_passes`: set `api.allowedOrigins` to `["https://ok.example"]` and send `Origin: https://ok.example/`. Expect 200.
- `Lan_caller_is_refused_unless_allowLan`: use `new HostFactory(callerIsLoopback: false)`. Expect 403 `The API accepts only programs on the host PC.`, then 200 after `api.allowLan` is set to true.
- `Api_routes_are_404_on_the_grpc_port`: send without `X-Test-Port`. Expect 404 `No such route.` for `/api/v1/info`, for `/API/v1/info`, and for a `POST /MCP` initialize.
- `Grpc_is_not_served_on_the_api_port`: call `/ohk.v1.Session/GetInfo` with the API header. Expect 404 in the error shape.
- `Unknown_route_and_wrong_method_use_the_error_shape`: `/api/v1/nothing` gives 404, and `DELETE /api/v1/info` gives 405 `This route does not accept DELETE.`.
- `Throttled_address_gets_429_with_retry_after`: six bad tokens give 429 with `Retry-After: 30`.
- `Mcp_off_answers_503`: a POST to `/mcp` with a good token gives 503 `MCP is off. The Owner can turn on mcp.enabled.`.

`ApiKestrelTests`, real Kestrel, following `KestrelTlsTests`:
- `Api_port_binds_loopback_only_by_default`:
  1. Before `HostApp.Build`, create `host.db` with `new HostData(folder).OpenHost()`.
  2. Find a free port: bind a `TcpListener` to port 0 on loopback, read its port, and stop it. Write `api.enabled=true` and `api.port={that port}` with `HostSettingsRepository.Set`.
  3. Start with `--Host:GrpcPort=0`.
  4. Assert one of the two server addresses is `http://127.0.0.1:{port}`.
  5. A plain HTTP `GET /api/v1/info` there gets 401 in the error shape.
  6. A `GET /api/v1/info` on the gRPC port, over HTTPS with the fingerprint check, gets 404.
- `Api_port_uses_tls_when_lan_is_allowed`: the same, with `api.allowLan=true`. The API address is the any-address form. HTTPS with the recorded fingerprint gets 401, and plain HTTP fails to connect or gets 400.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter "FullyQualifiedName~ApiGate|FullyQualifiedName~ApiKestrel"`
Expected: build errors, then FAIL.

- [ ] **Step 3: Implement the listener, the gate, the error writer, and `/api/v1/info`**

- [ ] **Step 4: Run them to verify they pass, then run the full suite**

Run: `dotnet test OpenHisabKitab.slnx`
Expected: PASS. The gRPC tests are unchanged.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(api): loopback API port with token gate and one error shape"
```

---

### Task 5: REST reads

**Files:**
- Create: `src/OpenHisabKitab.Host/Api/ApiJson.cs`, `ApiModels.cs`, `RestRoutes.cs`
- Modify: `src/OpenHisabKitab.Host/HostSetup.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Api/RestReadTests.cs`

**Interfaces:**
- Consumes: `CompanyService`, `MasterService`, `VoucherService`, `ReportService`, `ApiCall.Caller` (Task 4), `WireAmount` (Task 1).
- Produces:
  - `public static class ApiJson`:
    - `static JsonSerializerOptions Rest`, also applied with `ConfigureHttpJsonOptions`. It uses camelCase, `JsonStringEnumConverter` with camelCase names, `UnmappedMemberHandling.Disallow`, `RespectNullableAnnotations = true`, `RespectRequiredConstructorParameters = true`, and the converters `MoneyWire`, `QuantityWire`, and `RateWire`. Each converter writes `WireAmount.Format` as a JSON string.
    - `static JsonSerializerOptions Mcp`, the same, except `MoneyWire` is replaced by `MoneyWithText`, which writes `{"amount":"12345.60","text":"12,345.60"}` using `IndianNumberFormat.Format(m)`.
  - Response records in `ApiModels.cs`, with typed `Money`, `Quantity`, and `GstRate` fields so each set of options renders them its own way:
    - `CompanyDto(int Id, string Name)`
    - `CompanyDetailDto(int Id, string Name, string State, string Registration, string? Gstin, string Address)`
    - `YearDto(int StartYear, DateOnly From, DateOnly To)`
    - `GroupDto(int Id, string Name, int? ParentId, string Nature)`
    - `LedgerDto(int Id, string Name, int GroupId, string? Required, Money Opening)`
    - `PartyDto(int LedgerId, string Name, string? Gstin, string State, int CreditDays, string Address, long RowVersion)`
    - `ItemDto(int Id, string Name, string HsnSac, bool IsService, string Unit, GstRate Rate, GstRate CessRate, Money DefaultPrice, long RowVersion)`
    - `VoucherDto(long Id, string Type, string Number, DateOnly Date, string Narration, bool Cancelled, long RowVersion, IReadOnlyList<PostingDto> Postings, InvoiceDto? Invoice)`
    - `PostingDto(int LedgerId, string Ledger, Money Amount)`
    - `InvoiceDto` mirrors `InvoiceHeader`, with ledger names beside ledger ids, and lines that mirror `InvoiceLineRow`.
    - `TrialBalanceDto`, `ProfitAndLossDto`, `BalanceSheetDto`, and `StatementDto` mirror the Core records. Every `LedgerId` becomes `int LedgerId, string Ledger`, the ledger's name from the chart, and statement rows also carry `string VoucherNumber`.
    - `static class ApiMap` holds one `To...Dto` method per record, taking the chart where names are needed.
  - `static RouteGroupBuilder MapRestApi(this WebApplication app)` maps the `/api/v1` group.

**Read routes.** Every `{company}` is an int. Each route calls the named service method and nothing else.

| Route | Service call |
|---|---|
| `GET /api/v1/companies` | `CompanyService.List` |
| `GET /api/v1/companies/{company}` | `CompanyService.Get` |
| `GET /api/v1/companies/{company}/years` | `CompanyService.Years` |
| `GET /api/v1/companies/{company}/groups` | `MasterService.Chart` |
| `GET /api/v1/companies/{company}/ledgers?search=` | `MasterService.Chart` and `Openings` |
| `GET /api/v1/companies/{company}/parties?search=` | `MasterService.Parties` |
| `GET /api/v1/companies/{company}/items` | `MasterService.Items` |
| `GET /api/v1/companies/{company}/vouchers?from=&to=&type=` | `VoucherService.List` |
| `GET /api/v1/companies/{company}/vouchers/{id}` | `VoucherService.Get` |
| `GET /api/v1/companies/{company}/reports/trial-balance?from=&to=` | `ReportService.TrialBalance` |
| `GET /api/v1/companies/{company}/reports/profit-and-loss?from=&to=` | `ReportService.ProfitAndLoss` |
| `GET /api/v1/companies/{company}/reports/balance-sheet?asAt=` | `ReportService.BalanceSheet` |
| `GET /api/v1/companies/{company}/reports/ledger-statement?ledger=&from=&to=` | `ReportService.Statement` |

- `search` keeps the names that `NameMatcher.Match(search, names)` returns as candidates, and keeps all names when `search` is absent. This is presentation only.
- A date that fails `ParseExact` gives 400 `{name} must look like 2026-04-01.`.
- `from` and `to` are required where listed. A missing one gives 400 `Add {name}.`.
- An unknown `type` gives 400 `type must be one of sale, purchase, creditNote, debitNote, receipt, payment, journal, contra.`.
- These four reports are the ones in this build (spec 4.1). Each later plan that adds a report adds its route here and its tool in `ReadTools`, as Plan 2 already says.

- [ ] **Step 1: Write the failing tests**

Seed with `TestHost`-style calls through `f.Services`: one company, one cash receipt of ₹12,345.60, and one sales invoice.

```csharp
[Fact]
public async Task Trial_balance_amounts_are_strings_with_two_decimals_and_ledger_names()
{
    var json = await api.GetStringAsync($"/api/v1/companies/{co}/reports/trial-balance?from=2026-04-01&to=2027-03-31");
    var tb = JsonDocument.Parse(json).RootElement;
    var cash = tb.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("ledger").GetString() == "Cash");
    Assert.Equal(JsonValueKind.String, cash.GetProperty("closing").ValueKind);
    Assert.Equal("12345.60", cash.GetProperty("closing").GetString());
    Assert.Equal(tb.GetProperty("totalDebit").GetString(), tb.GetProperty("totalCredit").GetString());
    Assert.DoesNotContain("paise", json, StringComparison.OrdinalIgnoreCase);
}
```

Also:
- `Lists_only_companies_the_token_user_can_open`: a Data entry token sees one of two companies, and the other company id gives 403.
- `Voucher_read_has_number_row_version_and_signed_postings`.
- `Data_entry_token_cannot_read_reports` gives 403 `You do not have permission for this.`.
- `Data_entry_token_lists_only_own_vouchers`.
- `Bad_date_is_400`: `from=01-04-2026` gives `from must look like 2026-04-01.`, and so does `from=2026-4-1`.
- `Ledger_search_uses_the_name_matcher`: `search=cas` returns Cash.
- `Unknown_voucher_is_404`: the message comes from the service.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~RestReadTests`
Expected: FAIL with 404 `No such route.`.

- [ ] **Step 3: Implement `ApiJson`, the response records, `ApiMap`, and the read routes**

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~RestReadTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(api): REST reads for companies, masters, vouchers, and reports"
```

---

### Task 6: REST writes

**Files:**
- Modify: `src/OpenHisabKitab.Host/Api/ApiModels.cs`, `RestRoutes.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Api/RestWriteTests.cs`

**Interfaces:**
- Consumes: the Task 5 records and routes, and `MasterService` and `VoucherService` write methods.
- Produces request records. Amounts, quantities, and rates are **strings**, so the MCP write tools in Task 8 can reuse these records and their JSON schema shows strings:
  - `GroupRequest(string Name, int ParentId)`
  - `LedgerRequest(string Name, int GroupId)`
  - `RenameRequest(string Name)`
  - `PartyRequest(string Name, int GroupId, string? Gstin, string State, int CreditDays, string Address, long? RowVersion)`
  - `ItemRequest(int? Id, string Name, string HsnSac, bool IsService, string Unit, string Rate, string CessRate, string DefaultPrice, long? RowVersion)`
  - `JournalRequest(string Type, DateOnly Date, string Narration, IReadOnlyList<LineRequest> Lines)`, with `LineRequest(int LedgerId, string Amount)`, positive for debit
  - `InvoiceRequest(...)` mirrors `InvoiceInput`, with `InvoiceLineRequest` using string `Quantity`, `UnitPrice`, `Discount`, `Rate`, and `CessRate`
  - `EditRequest<T>(long RowVersion, T Voucher)`
  - `CancelRequest(long RowVersion, string Reason)`
  - `SavedDto(long Id, string Number, long RowVersion)`
- `static class ApiParse` holds:
  - `static Money Money(string? s, string field)`, `Quantity Quantity(...)`, and `GstRate Rate(...)`. Each throws `ServiceException(Invalid, ...)` with one of:
    - `{field} must be a string like "12345.60".`
    - `{field} must be a string like "2.500".`
    - `{field} must be a string like "18.00".`
  - The field path is written the way System.Text.Json writes `JsonException.Path`, such as `$.lines[2].amount`, so both kinds of 400 name fields alike. For an MCP tool argument, the path starts at the argument, such as `$.voucher.lines[2].amount`.
  - `static JournalInput Journal(JournalRequest r)` and `InvoiceInput Invoice(InvoiceRequest r)`.

**Write routes:**

| Route | Service call | Success |
|---|---|---|
| `POST /api/v1/companies/{company}/groups` | `AddGroupAsync` | 201 `{"id":n}` |
| `POST /api/v1/companies/{company}/ledgers` | `AddLedgerAsync` | 201 `{"id":n}` |
| `PUT /api/v1/companies/{company}/ledgers/{id}/name` | `RenameAsync` | 204 |
| `POST /api/v1/companies/{company}/parties` | `SavePartyAsync` | 201 `{"id":n}` |
| `POST /api/v1/companies/{company}/items` | `SaveItemAsync` | 201 `{"id":n}` |
| `POST /api/v1/companies/{company}/vouchers/journal` | `SaveJournalAsync` | 201 `SavedDto` |
| `POST /api/v1/companies/{company}/vouchers/invoice` | `SaveInvoiceAsync` | 201 `SavedDto` |
| `PUT /api/v1/companies/{company}/vouchers/{id}/journal` | `EditJournalAsync` | 200 `SavedDto` |
| `PUT /api/v1/companies/{company}/vouchers/{id}/invoice` | `EditInvoiceAsync` | 200 `SavedDto` |
| `POST /api/v1/companies/{company}/vouchers/{id}/cancel` | `CancelAsync` | 204 |

On 409, `current` is the `VoucherDto`, `PartyDto`, or `ItemDto` mapped from `ServiceException.Current`, which needs ledger names the gate cannot know. Each write handler therefore:
1. catches the Conflict;
2. maps `Current` to its response record using that company's chart;
3. rethrows `new ServiceException(ServiceError.Conflict, e.Message, dto)`.

The gate then writes `current` as given, with `ApiJson.Rest`. Openings, required ledgers, locks, and settings are not on REST in version 1 (spec 4.1 lists the resources).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Journal_saved_over_rest_gets_its_number_and_is_audited_as_rest_with_the_token_name()
{
    var r = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/journal", new
    {
        type = "receipt", date = "2026-05-10", narration = "cash sale",
        lines = new[] { new { ledgerId = cash, amount = "12345.60" }, new { ledgerId = sales, amount = "-12345.60" } },
    });
    Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    var saved = await r.Content.ReadFromJsonAsync<JsonElement>();
    Assert.EndsWith("1", saved.GetProperty("number").GetString());
    var audit = Admin.Audit(Owner, co, new AuditFilter()).Last();
    Assert.Equal((ChangeSource.Rest, "rw-script", "owner"), (audit.Entry.Source, audit.Entry.TokenName, audit.Entry.User));
}

[Theory]
[InlineData("\"12345.6\"", "$.lines[0].amount must be a string like \"12345.60\".")]
[InlineData("\"12,345.60\"", "$.lines[0].amount must be a string like \"12345.60\".")]
[InlineData("12345.60", "Check $.lines[0].amount in the request body.")]
public async Task Amount_in_the_wrong_form_is_400_and_uses_no_number(string amount, string message)
{
    var body = $$"""{"type":"receipt","date":"2026-05-10","narration":"","lines":[{"ledgerId":{{cash}},"amount":{{amount}}},{"ledgerId":{{sales}},"amount":"-12345.60"}]}""";
    var r = await rw.PostAsync($"/api/v1/companies/{co}/vouchers/journal", new StringContent(body, Encoding.UTF8, "application/json"));
    Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    Assert.Equal(message, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString());
    Assert.Empty(Vouchers.List(Owner, co, Fy2026, null));
    // the next good save still gets number 1
}
```

Also:
- `Read_only_token_write_is_403_with_its_reason_and_writes_nothing`: no voucher, and no audit record after the setup records.
- `Stale_row_version_is_409_with_the_current_voucher`: edit twice with the same row version. The second gets 409. `error.current.rowVersion` is the new version, and its amounts are strings.
- `Cancel_over_rest_keeps_the_number`.
- `Unknown_field_is_400`: `{"typo":1,...}` gives `Check $.typo in the request body.` (the path STJ reports).
- `Unbalanced_journal_message_comes_from_the_service`: assert the status is 400 and the message equals what `VoucherService` throws for the same input when called directly.
- `Invoice_over_rest_matches_the_same_invoice_saved_through_the_service`: compare tax totals and postings with a second company saved directly.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~RestWriteTests`
Expected: FAIL.

- [ ] **Step 3: Implement the request records, `ApiParse`, and the write routes**

- [ ] **Step 4: Run them to verify they pass, then run the full suite**

Run: `dotnet test OpenHisabKitab.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(api): REST writes with wire amounts and row version conflicts"
```

---

### Task 7: OpenAPI document

**Files:**
- Create: `src/OpenHisabKitab.Host/Api/OpenApiSetup.cs`
- Modify: `Directory.Packages.props`, `src/OpenHisabKitab.Host/OpenHisabKitab.Host.csproj`, `HostSetup.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Api/OpenApiTests.cs`

**Interfaces:**
- Produces:
  - `AddOpenApi("v1", ...)`, with a schema transformer that turns `Money`, `Quantity`, and `GstRate` into `type: string` with these patterns:
    - `^-?(0|[1-9][0-9]*)\.[0-9]{2}$` for money
    - `^-?(0|[1-9][0-9]*)\.[0-9]{3}$` for quantity
    - `^(0|[1-9][0-9]*)\.[0-9]{2}$` for rate
  - A document transformer that sets `info.version` to `HostVersion.Current` and adds a bearer security scheme.
  - `app.MapOpenApi("/api/v1/openapi.json")`. It sits behind `ApiGate`, so it needs a token.

- [ ] **Step 1: Write the failing tests**

- `Openapi_document_needs_a_token`: 401 without one.
- `Openapi_lists_every_rest_route`: for each path in the Task 5 and Task 6 tables, `paths` contains it.
- `Money_fields_are_strings_in_the_schema`: in the `TrialBalanceDto` schema, `totalDebit` resolves, following `$ref`, to `type: string` with the money pattern. `JournalRequest.lines[].amount` is `type: string`.
- `Openapi_version_is_the_host_version`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~OpenApiTests`
Expected: FAIL with 404.

- [ ] **Step 3: Add the package and the transformers**

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~OpenApiTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A Directory.Packages.props src tests
git commit -m "feat(api): OpenAPI document at /api/v1/openapi.json"
```

---

### Task 8: MCP server

**Files:**
- Create: `src/OpenHisabKitab.Host/Mcp/McpResults.cs`, `ReadTools.cs`, `WriteTools.cs`
- Modify: `Directory.Packages.props`, `src/OpenHisabKitab.Host/OpenHisabKitab.Host.csproj`, `HostSetup.cs` (`AddHttpContextAccessor`, `AddMcpServer`, `MapMcp("/mcp")`)
- Test: `tests/OpenHisabKitab.Host.Tests/Mcp/McpToolTests.cs`

**Interfaces:**
- Consumes: the services, `ApiMap`, `ApiParse`, the request records, `ApiJson.Mcp`, and `ApiCall.Caller` through `IHttpContextAccessor`.
- Produces:
  - `AddMcpServer(o => o.ServerInfo = new() { Name = "Open Hisab Kitab", Version = HostVersion.Current }).WithHttpTransport(o => o.Stateless = true).WithTools<ReadTools>().WithTools<WriteTools>()`.
  - `internal static class McpResults`:
    - `static CallToolResult Ok(object value)` serializes `value` with `ApiJson.Mcp` into one `TextContentBlock`.
    - `static async Task<CallToolResult> Run(Func<Task<object>> work)` returns `Ok`. It turns a `ServiceException` into `IsError = true` with exactly `e.Message`, and any other exception into `IsError = true` with `Something went wrong. Details are in the host log.`, logging that one.
  - `public const int RowLimit = 100`. A list result is `{"rows":[...],"omitted":n}`. When `n > 0`, it also has `"note":"{n} more rows. Narrow the dates or the search."`.

**Tools** (spec 4.1). Tool parameters are only `int`, `long`, `bool`, and `string`, plus the Task 6 request records. Dates are `yyyy-MM-dd` strings, parsed as in Task 5.

| Tool | Parameters | Calls |
|---|---|---|
| `list_companies` | none | `CompanyService.List` and `Years` |
| `search_ledgers` | `company`, `query` | the chart and parties, matched with `NameMatcher`; party rows add GSTIN and state |
| `ledger_statement` | `company`, `ledgerId`, `from`, `to` | `ReportService.Statement` (rows capped) |
| `trial_balance` | `company`, `from`, `to` | `ReportService.TrialBalance` |
| `profit_and_loss` | `company`, `from`, `to` | `ReportService.ProfitAndLoss` |
| `balance_sheet` | `company`, `asAt` | `ReportService.BalanceSheet` |
| `find_vouchers` | `company`, `from`, `to`, `type?`, `text?` | `VoucherService.List`; `text` filters on number or narration, case insensitive (capped) |
| `get_voucher` | `company`, `id` | `VoucherService.Get` |
| `create_voucher` | `company`, `voucher: JournalRequest` | `SaveJournalAsync` |
| `create_invoice` | `company`, `invoice: InvoiceRequest` | `SaveInvoiceAsync` |
| `create_party` | `company`, `party: PartyRequest` | `SavePartyAsync` |

- Each tool's `Description` is one plain sentence that says amounts are strings like `"12345.60"`. Write tools also say: `Off unless the Owner turns on mcp.allowWrites and the token can write.`
- Write tools are always listed. When writes are off, the service refuses with the `ReadOnlyReason` from Task 2. No adapter check is needed.
- Write tools take the typed Task 6 request records, so the assistant sees their full schema. An argument of the wrong JSON type never reaches the tool. The SDK answers `An error occurred invoking '{tool}'.` (spike), which is acceptable because the schema says string. An amount string in the wrong form does reach the tool and gets the `ApiParse` message.

- [ ] **Step 1: Write the failing tests**

Tests post raw JSON-RPC to `/mcp` through `f.ApiClient(token)`, with `Accept: application/json, text/event-stream`. A small test helper `McpCall(HttpClient, string tool, object args)` reads the `data:` line of the SSE reply and returns `result`.

```csharp
[Fact]
public async Task Initialize_reports_the_host_version()
{
    var init = await Rpc(api, "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } });
    Assert.Equal(HostVersion.Current, init.GetProperty("serverInfo").GetProperty("version").GetString());
}

[Fact]
public async Task Trial_balance_amounts_carry_wire_value_and_indian_text()
{
    // seed: one cash receipt of 1234567.80, so Cash closes at that amount

    var r = await McpCall(api, "trial_balance", new { company = co, from = "2026-04-01", to = "2027-03-31" });
    var text = r.GetProperty("content")[0].GetProperty("text").GetString()!;
    Assert.Contains("""{"amount":"1234567.80","text":"12,34,567.80"}""", text);
    Assert.DoesNotContain("paise", text, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Service_refusal_reaches_the_assistant_word_for_word()
{
    var viewer = f.NewToken("viewer", readWrite: true);
    var r = await McpCall(f.ApiClient(viewer), "create_voucher", new { company = co, voucher = Receipt("100.00") });
    Assert.True(r.GetProperty("isError").GetBoolean());
    Assert.Equal("You do not have permission for this.", r.GetProperty("content")[0].GetProperty("text").GetString());
}
```

Also:
- `Writes_are_refused_until_allowWrites_and_nothing_is_saved`: the message is `Changes through MCP are off. The Owner can turn on mcp.allowWrites.`. No voucher exists and no audit record is added.
- `Created_voucher_is_audited_as_mcp_with_the_token_name`: after `mcp.allowWrites=true`, check the `Source` and `TokenName` of the audit record, and that the result carries the number.
- `Find_vouchers_caps_rows_and_says_how_many_were_left_out`: seed 105 receipts. Expect `rows` length 100, `omitted` 105 minus 100, and the note text.
- `Tools_list_has_every_tool_with_string_amount_schemas`: `tools/list` has the eleven names. In `create_voucher`'s input schema, `voucher.lines.items.amount` is `type: string`.
- `Unknown_company_is_a_tool_error_not_a_crash`.
- `Wrong_amounts_save_nothing`, with `mcp.allowWrites` on:
  - amount `"12345.6"` gives the tool error `$.voucher.lines[0].amount must be a string like "12345.60".`;
  - amount `12345.60` as a JSON number gives `An error occurred invoking 'create_voucher'.`;
  - in both cases no voucher is saved.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.Host.Tests --filter FullyQualifiedName~McpToolTests`
Expected: FAIL. `/mcp` answers 404 `No such route.` once the gate lets it through.

- [ ] **Step 3: Add the package, `McpResults`, and the two tool classes**

- [ ] **Step 4: Run them to verify they pass, then run the full suite**

Run: `dotnet test OpenHisabKitab.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A Directory.Packages.props src tests
git commit -m "feat(mcp): MCP server with read tools and guarded write tools"
```

---

### Task 9: The stdio bridge, docs, and owner review

**Files:**
- Create: `src/OpenHisabKitab.McpBridge/OpenHisabKitab.McpBridge.csproj` (console app, `net10.0-windows`, no package references)
- Create: `src/OpenHisabKitab.McpBridge/Program.cs`, `Bridge.cs`, `BridgeConfig.cs`
- Create: `tests/OpenHisabKitab.McpBridge.Tests/OpenHisabKitab.McpBridge.Tests.csproj` and `BridgeTests.cs`. The project references the bridge, `src/OpenHisabKitab.Host`, and `Microsoft.AspNetCore.Mvc.Testing`. It does not reference the host test project, so no tests are found twice.
- Create: `tests/OpenHisabKitab.McpBridge.Tests/BridgeHost.cs`, a small `WebApplicationFactory<Program>` like `HostFactory`. It uses a temporary folder, turns on the API and MCP through `HostSettingsService`, creates an Owner token, and marks every request as API port with its own `IListenerAccessor`.
- Modify: `OpenHisabKitab.slnx`, `README.md` (one section: what the API and MCP server do, that they are off by default, that the `SETG` and `USER` screens to turn them on and create tokens arrive with the client in Plan 4, and the bridge config file format), `docs/owner-review.md`

**Interfaces:**
- Produces:
  - `public sealed record BridgeConfig(Uri Endpoint, string Token, string? CertificateFingerprint)` with `public static BridgeConfig Load(string path)`. The JSON file has the keys `endpoint`, `token`, and `certificateFingerprint`. The default path is `%APPDATA%\OpenHisabKitab\mcp-bridge.json`, and `--config <path>` overrides it.
  - `public static class Bridge { public static Task<int> RunAsync(TextReader stdin, TextWriter stdout, TextWriter stderr, HttpMessageHandler handler, BridgeConfig config, CancellationToken ct); }`. For each stdin line:
    1. POST the line to `config.Endpoint` with `Authorization: Bearer`, `Content-Type: application/json`, and `Accept: application/json, text/event-stream`.
    2. On 202 with an empty body, write nothing.
    3. On `text/event-stream`, write each `data:` payload as one line.
    4. On `application/json`, write the body as one line.
    5. On a non-2xx reply to a request, write a JSON-RPC error for the request's `id`, with code `-32000` and the message taken from the host's `error.message`, as one line.
    6. On a non-2xx reply to a notification (a line with no `id`), write nothing to stdout and log the message to stderr.
  - `public static int CheckConfig(BridgeConfig config, TextWriter stderr)` returns 0 when the config can be used, or 2 after writing the reason. `Program.Main` calls it first.
  - Only JSON-RPC goes to stdout; diagnostics go to stderr. The method returns 0 at end of input.
  - `Program.Main` builds a `SocketsHttpHandler`:
    - An `https` endpoint is accepted only when `CertificateFingerprint` is set and matches `CertificateStore.Fingerprint`. Copy the twelve line fingerprint function instead of referencing the host.
    - An `https` endpoint without a fingerprint stops at start with: `Add certificateFingerprint to the bridge config. Find it in the host log.` on stderr, and exit code 2.
    - Certificate validation is never turned off.

- [ ] **Step 1: Write the failing tests**

`BridgeTests` drives `Bridge.RunAsync` against `BridgeHost.Server.CreateHandler()`.
- `Initialize_list_and_call_round_trip`: three stdin lines (`initialize`, `tools/list`, and a `tools/call` of `list_companies`) give three stdout lines. Each parses as JSON-RPC with the matching `id`.
- `Notification_writes_nothing`.
- `Bad_token_becomes_a_json_rpc_error_with_the_host_message`: message `Send a valid API token.`, with the matching `id`.
- `Https_without_fingerprint_refuses_to_start`: `Bridge.CheckConfig` with an `https` endpoint and no fingerprint returns 2 and writes the exact stderr line.
- `Failed_notification_writes_nothing_to_stdout`: with a bad token, a notification line leaves stdout empty and writes the message to stderr.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/OpenHisabKitab.McpBridge.Tests`
Expected: build errors.

- [ ] **Step 3: Implement the bridge and add both projects to `OpenHisabKitab.slnx`**

- [ ] **Step 4: Run the whole suite**

Run: `dotnet build OpenHisabKitab.slnx` then `dotnet test OpenHisabKitab.slnx`
Expected: build with 0 warnings; all tests PASS.

- [ ] **Step 5: Bring `docs/owner-review.md` up to date**

The Plan 3 section was written with this plan. Update it only if building changed a decision, and delete any item the owner has marked OK.

- [ ] **Step 6: Commit**

```bash
git add -A OpenHisabKitab.slnx README.md docs/owner-review.md src/OpenHisabKitab.McpBridge tests/OpenHisabKitab.McpBridge.Tests
git commit -m "feat(bridge): stdio MCP bridge; docs and owner review for Plan 3"
```
