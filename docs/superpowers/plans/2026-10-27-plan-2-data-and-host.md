# Plan 2: Data and Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the SQLite data layer and the host Windows service: migrations, users and login, companies and masters, voucher saving with numbering, locks, audit trail, reports, backups, and the gRPC transport, all behind one transport agnostic application service layer.

**Architecture:**
- `OpenHisabKitab.Data` owns every SQL statement: connections, migrations, repositories, the hash chained audit log, and SQLite online backup.
- `OpenHisabKitab.Host` holds the application services (`Host/Application`) and the gRPC adapters (`Host/Grpc`). The services check permissions, validate with `Core`, write in one transaction, append the audit record, and publish a change event. The adapters only translate messages. Plan 3 adds REST and MCP adapters over the same services.
- `OpenHisabKitab.Contracts` holds the `.proto` files.
- Reports come from an in-memory `CompanyBooks` cache per company that applies each saved voucher as a delta and rebuilds `Core.Reports.Books` lazily.

**Tech Stack:**
- .NET 10 (SDK 10.0.403 is installed; `global.json` already pins the feature band).
- ASP.NET Core with Kestrel and HTTP/2.
- `Microsoft.Data.Sqlite` with plain SQL, no ORM.
- `Grpc.AspNetCore`, `Grpc.Tools`, `Google.Protobuf`.
- `Microsoft.Extensions.Hosting.WindowsServices`.
- xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. Sections 4, 5, 8, 9, and 13 apply. Section 4.1 is Plan 3, but this plan reserves its storage.

**Plans after this one** (this table replaces the one in Plan 1):

| Plan | Covers |
|---|---|
| 3 | Local REST API and MCP server (spec 4.1): API port, tokens, REST adapters, MCP tools, stdio bridge |
| 4 | Client shell: window, prompt, tabs, F key row, settings, keybindings, theme, accessibility |
| 5 | Masters and voucher entry screens, bill by bill allocation, party opening bills |
| 6 | Reports screens, `CFLO`, `OUTS`, `DAYB`, printing and PDF |
| 7 | GST files: `GST1`, `GS3B`, `G2BR`, `EINV` |
| 8 | Users, custom roles, host.db restore, installer, release |

Each later plan that adds a host feature also adds its REST route and MCP tool, once Plan 3 exists.

## Global Constraints

- **Build settings.** Every project inherits `Directory.Build.props`: `net10.0`, nullable, `TreatWarningsAsErrors`.
- **Package versions** live in a new `Directory.Packages.props` (central package management). Pin these versions:
  - `Microsoft.Data.Sqlite` 10.0.14
  - `Grpc.AspNetCore` 2.84.0
  - `Grpc.Net.Client` 2.84.0
  - `Grpc.Tools` 2.85.0
  - `Google.Protobuf` 3.36.2
  - `Microsoft.Extensions.Hosting.WindowsServices` 10.0.14
  - `System.Security.Cryptography.ProtectedData` 10.0.14
  - `Microsoft.AspNetCore.Mvc.Testing` 10.0.14
  - `Microsoft.Extensions.TimeProvider.Testing` 10.11.0
  - Keep the Plan 1 test packages at their current versions.
- **No em dash or en dash** in any file. `RepositoryTextTests` already scans the repo. In C#, never type `\u` escapes for these characters.
- **Money.** Store `long` paise in `INTEGER` columns. Quantities: `long` thousandths. Rates: `int` basis points. No `REAL` column anywhere. A test in Task 2 enforces this.
- **Dates** are stored as `TEXT` `yyyy-MM-dd`. Timestamps are stored as `TEXT` UTC in round trip format (`"O"`). Always use `CultureInfo.InvariantCulture`.
- **Time.** Code that reads the clock takes a `TimeProvider`. Tests use `FakeTimeProvider`.
- **SQLite pragmas** on every connection: `journal_mode=WAL`, `foreign_keys=ON`, `synchronous=FULL`, `busy_timeout=5000`.
- **One writer per company.** Every write takes the company's `SemaphoreSlim(1)` and runs in one `BEGIN IMMEDIATE` transaction that also appends the audit record. Reads never take the lock.
- **Messages.** `ServiceException` messages are one line, state what happened and what to do, and contain no filler (spec 6.7). Tests assert the exact text where this plan gives it.
- **Commits.** Commit after each task with a conventional prefix. End each message with the session's attribution lines.

## Review Focus

These failure modes are implied by the spec but no example exercises them. Each has a test in the task named.

1. **Two users saving at the same moment.** Eight parallel savers of 50 vouchers each must get numbers 1 to 400 with no gap and no duplicate. Test in Task 16.
2. **Editing a voucher into a locked period, or out of its financial year.** Moving a voucher dated 10-05-2026 to 10-04-2027 must be refused, and so must moving an unlocked voucher to a date on or before the lock date. Tests in Task 10.
3. **A crash or failure halfway through a save.** If the audit append throws, no voucher row, no posting, and no number may remain, and the next save gets the same number. Test in Task 10.
4. **A database from a newer version of the software.** Opening a file whose `user_version` is above the newest migration must fail with "This file needs a newer Open Hisab Kitab." and must not change the file. Test in Task 2.
5. **Data entry user reading another user's voucher by id.** `Get` must return `PermissionDenied`, and `List` must not include it. Test in Task 10.

---

## File Structure

```
Directory.Packages.props                         central package versions (Task 2)
src/OpenHisabKitab.Core/
  Accounts/ChartOfAccounts.cs                    + Load, Rename, RemoveLedger, max+1 ids (Task 1)
  Gst/Gstin.cs                                   GSTIN value type with checksum (Task 1)
  Gst/Registration.cs                            GstRegistration, InvoiceKind enums (Task 1)
src/OpenHisabKitab.Data/
  SqliteDatabase.cs                              open with pragmas, transactions
  Migrations/Migrator.cs                         user_version based runner
  Migrations/Host/0001_initial.sql               host.db schema
  Migrations/Company/0001_initial.sql            company db schema
  Audit/AuditEntry.cs, Audit/AuditLog.cs         hash chained log
  HostDb/UserRepository.cs                       users, roles, company access
  HostDb/CompanyListRepository.cs                company list
  HostDb/HostSettingsRepository.cs               key value host settings
  CompanyDb/CompanyRepository.cs                 profile, financial years, settings
  CompanyDb/ChartRepository.cs                   groups, ledgers, openings
  CompanyDb/PartyRepository.cs, ItemRepository.cs
  CompanyDb/VoucherRepository.cs                 vouchers, postings, invoice lines
  CompanyDb/NumberSeriesRepository.cs            series and next number
  CompanyDb/LockRepository.cs                    period lock
  Backup/BackupManager.cs                        online backup, integrity check, retention, restore
src/OpenHisabKitab.Contracts/
  Protos/*.proto                                 gRPC contracts
src/OpenHisabKitab.Host/
  Program.cs, HostOptions.cs                     Kestrel, TLS, Windows service
  Security/CertificateStore.cs                   self signed certificate, fingerprint
  Application/                                   services, CallContext, permissions, errors
  Application/Books/CompanyBooks.cs              report cache
  Grpc/                                          adapters and interceptors
tests/OpenHisabKitab.Data.Tests/
tests/OpenHisabKitab.Host.Tests/
```

---

### Task 1: Core additions for stored data

The host must rebuild a chart from database rows, and parties need GSTIN checks. Today `ChartOfAccounts` can only grow from `Empty`, and it assigns ids as `Count + 1`, which collides after a removal.

**Files:**
- Modify: `src/OpenHisabKitab.Core/Accounts/ChartOfAccounts.cs`
- Create: `src/OpenHisabKitab.Core/Gst/Gstin.cs`, `src/OpenHisabKitab.Core/Gst/Registration.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Accounts/ChartLoadTests.cs`, `tests/OpenHisabKitab.Core.Tests/Gst/GstinTests.cs`

**Interfaces:**
- Produces:
  - `public static ChartOfAccounts ChartOfAccounts.Load(IEnumerable<AccountGroup> groups, IEnumerable<LedgerAccount> ledgers)`. Throws `ChartException` on duplicate ids, unknown parent, cycle, a child whose `Nature` differs from its parent's, duplicate normalised names, a ledger in an unknown group, or any `RequiredLedger` value present zero or more than one time.
  - `public ChartOfAccounts RenameGroup(GroupId id, string name)` and `RenameLedger(LedgerId id, string name)`, with the same name rules as adding.
  - `public ChartOfAccounts RemoveLedger(LedgerId id)`. Throws `ChartException("Required ledger {name} cannot be deleted.")` for required ledgers. The caller checks for postings.
  - New ids are `max(existing) + 1`, or 1 when empty.
  - `public readonly record struct Gstin` with `static bool TryParse(string? text, out Gstin gstin)`, `string Value`, `StateCode State`.
  - `public enum GstRegistration { Regular, Composition, Unregistered }`.
  - `public enum InvoiceKind { Regular, ExportWithTax, ExportUnderLut, SezWithTax, SezWithoutTax, DeemedExport }`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Load_round_trips_default_chart()
{
    var c = DefaultChart.Create();
    var loaded = ChartOfAccounts.Load(c.Groups, c.Ledgers);
    Assert.Equal(c.Groups, loaded.Groups);
    Assert.Equal(c.Ledgers, loaded.Ledgers);
    Assert.Equal(c.Required(RequiredLedger.Cash), loaded.Required(RequiredLedger.Cash));
}

[Fact]
public void Load_rejects_missing_required_ledger()
{
    var c = DefaultChart.Create();
    var ledgers = c.Ledgers.Where(l => l.Required != RequiredLedger.RoundOff);
    Assert.Throws<ChartException>(() => ChartOfAccounts.Load(c.Groups, ledgers));
}

[Fact]
public void Load_rejects_group_cycle() { /* two groups each naming the other as parent: throws ChartException */ }

[Fact]
public void Load_rejects_child_with_other_nature() { /* Income child under an Asset parent: throws */ }

[Fact]
public void New_id_after_removal_does_not_collide()
{
    var c = DefaultChart.Create();
    var (c1, a) = c.AddLedger("A", c.Group(GroupKind.CashInHand));
    var (c2, b) = c1.AddLedger("B", c.Group(GroupKind.CashInHand));
    var (c3, d) = c2.RemoveLedger(a).AddLedger("D", c.Group(GroupKind.CashInHand));
    Assert.NotEqual(b, d);
}

[Fact]
public void Required_ledger_cannot_be_removed() =>
    Assert.Throws<ChartException>(() => DefaultChart.Create().RemoveLedger(DefaultChart.Create().Required(RequiredLedger.Cash)));

[Fact]
public void Rename_to_existing_name_throws() { /* rename "A" to "cash" throws ChartException */ }

[Theory]
[InlineData("27AAPFU0939F1ZV", "27")]
[InlineData("29AAGCB7383J1Z4", "29")]
[InlineData(" 24aaacc1206d1zm ", "24")]
public void Valid_gstin(string text, string state)
{
    Assert.True(Gstin.TryParse(text, out var g));
    Assert.Equal(state, g.State.Value);
    Assert.Equal(text.Trim().ToUpperInvariant(), g.Value);
}

[Theory]
[InlineData("07AAACI1681G1ZN")]   // checksum should be R
[InlineData("27AAPFU0939F1Z")]    // 14 characters
[InlineData("00AAPFU0939F1ZV")]   // no state 00
[InlineData("27AAPFU0939F1Z-")]
[InlineData("")]
[InlineData(null)]
public void Invalid_gstin(string? text) => Assert.False(Gstin.TryParse(text, out _));
```

- [ ] **Step 2: Run them and confirm they fail to compile.** Run: `dotnet test OpenHisabKitab.slnx --filter "ChartLoadTests|GstinTests"`.

- [ ] **Step 3: Implement.** The GSTIN check digit uses this algorithm (characters `0-9A-Z`, values 0 to 35):

```text
factor = 1, sum = 0
for each of the first 14 characters c:
    p = value(c) * factor
    sum += p / 36 + p % 36
    factor = (factor == 1) ? 2 : 1
check = value^-1((36 - sum % 36) % 36)
```

The state prefix must be a known `StateCode` (use `StateCode`'s own validation). Trim and upper case the input with the invariant culture before checking.

- [ ] **Step 4: Run the whole suite.** Expected: all Plan 1 tests and the new ones pass.

- [ ] **Step 5: Commit** `feat: load, rename, and remove in chart of accounts; GSTIN type`.

---

### Task 2: Data project, connections, and migrations

**Files:**
- Create: `Directory.Packages.props`, `src/OpenHisabKitab.Data/OpenHisabKitab.Data.csproj`, `SqliteDatabase.cs`, `Migrations/Migrator.cs`, `Migrations/Host/0001_initial.sql` (empty schema placeholder replaced in Task 4), `Migrations/Company/0001_initial.sql` (replaced in Task 5)
- Create: `tests/OpenHisabKitab.Data.Tests/` project, `MigratorTests.cs`, `TempDb.cs`
- Modify: `OpenHisabKitab.slnx`, `tests/OpenHisabKitab.Core.Tests/OpenHisabKitab.Core.Tests.csproj` (drop inline versions)

**Interfaces:**
- Produces:
  - `public static class SqliteDatabase { public static SqliteConnection Open(string path); public static SqliteTransaction BeginWrite(SqliteConnection c); }`. `Open` creates the file if missing and applies the pragmas from Global Constraints. `BeginWrite` returns an immediate transaction.
  - `public enum Schema { Host, Company }`.
  - `public static class Migrator { public static int Latest(Schema s); public static MigrationResult Migrate(SqliteConnection c, Schema s, Action<int>? beforeMigrating = null); }`
  - `public sealed record MigrationResult(int From, int To);`
  - `public sealed class DatabaseTooNewException() : Exception("This file needs a newer Open Hisab Kitab.");`
  - Test helper `TempDb : IDisposable` with `string Path` and `SqliteConnection Open()`, deleting the folder on dispose (call `SqliteConnection.ClearAllPools()` first).

Migrations are embedded resources named `NNNN_name.sql` under `Migrations/Host` and `Migrations/Company`. The version is `PRAGMA user_version`. Each script runs in its own transaction and sets `user_version` inside it. `beforeMigrating(from)` is called once, only when at least one script will run and `from > 0`, so the host can back up first.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void New_file_migrates_to_latest() { /* Migrate on empty file: result From 0, To Migrator.Latest(Schema.Company); user_version equals Latest */ }
[Fact] public void Second_run_does_nothing() { /* second Migrate returns From == To and does not call beforeMigrating */ }
[Fact] public void Before_migrating_is_not_called_for_a_new_file() { }
[Fact] public void Pragmas_are_set() { /* journal_mode "wal", foreign_keys 1, synchronous 2 */ }

[Fact]
public void Newer_file_is_refused_and_left_unchanged()
{
    using var db = new TempDb();
    using (var c = db.Open()) Exec(c, "PRAGMA user_version = 999; CREATE TABLE t(x INTEGER);");
    var before = File.ReadAllBytes(db.Path);
    using (var c = db.Open())
    {
        var ex = Assert.Throws<DatabaseTooNewException>(() => Migrator.Migrate(c, Schema.Company));
        Assert.Equal("This file needs a newer Open Hisab Kitab.", ex.Message);
    }
    SqliteConnection.ClearAllPools();
    Assert.Equal(before, File.ReadAllBytes(db.Path));
}

[Theory]
[InlineData(Schema.Host)]
[InlineData(Schema.Company)]
public void No_floating_point_columns(Schema s) { /* after migrating, no column in sqlite_schema tables has declared type REAL, FLOAT, or DOUBLE */ }
```

- [ ] **Step 2: Run, confirm failure.** `dotnet test OpenHisabKitab.slnx --filter MigratorTests`.
- [ ] **Step 3: Implement** `SqliteDatabase` and `Migrator`. Scripts are read with `Assembly.GetManifestResourceStream`, sorted by their four digit prefix. A gap in the numbering is a startup error (throw `InvalidOperationException`).
- [ ] **Step 4: Run the full suite.** Expected: pass.
- [ ] **Step 5: Commit** `feat: data project with SQLite migrations`.

---

### Task 3: Hash chained audit log

Spec 5.5. Both `host.db` and every company file have an `audit_log` table with the same shape, created by their `0001_initial.sql` (add the table now in both scripts).

**Files:**
- Create: `src/OpenHisabKitab.Data/Audit/AuditEntry.cs`, `Audit/AuditLog.cs`
- Modify: both `0001_initial.sql`
- Test: `tests/OpenHisabKitab.Data.Tests/AuditLogTests.cs`

**Interfaces:**
- Produces:
  - `public enum ChangeSource { App, Rest, Mcp, System }`
  - `public sealed record AuditEntry(DateTimeOffset At, string User, string Machine, ChangeSource Source, string? TokenName, string Action, string Entity, string EntityKey, string? Before, string? After);` Before and After are JSON text or null.
  - `public sealed record StoredAuditEntry(long Seq, AuditEntry Entry, string PrevHash, string Hash);`
  - `public static class AuditLog { public static long Append(SqliteTransaction tx, AuditEntry e); public static AuditVerification Verify(SqliteConnection c); public static IReadOnlyList<StoredAuditEntry> Read(SqliteConnection c, AuditFilter f); }`
  - `public sealed record AuditVerification(long Count, long? FirstBadSeq);` with `bool Ok => FirstBadSeq is null`.
  - `public sealed record AuditFilter(DateTimeOffset? From = null, DateTimeOffset? To = null, string? User = null, string? Entity = null, int Limit = 500);`

Table: `audit_log(seq INTEGER PRIMARY KEY, at TEXT, user TEXT, machine TEXT, source TEXT, token_name TEXT NULL, action TEXT, entity TEXT, entity_key TEXT, before TEXT NULL, after TEXT NULL, prev_hash TEXT, hash TEXT)`. Triggers make it append only: `BEFORE UPDATE` and `BEFORE DELETE` on `audit_log` call `RAISE(ABORT, 'audit log is append only')`.

Hash: lowercase hex SHA-256 of the UTF-8 bytes of `prev_hash + "\n" + canonical`, where `canonical` is `JsonSerializer.Serialize` of an anonymous object with properties in this order: `seq, at, user, machine, source, tokenName, action, entity, entityKey, before, after`. `at` is formatted `"O"` in UTC. The first record's `prev_hash` is 64 zeros.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Empty_log_verifies() { /* Count 0, Ok */ }
[Fact] public void Three_entries_verify() { /* append 3, Verify: Count 3, Ok; seqs 1,2,3; second PrevHash == first Hash */ }

[Fact]
public void Edited_row_is_detected()
{
    // append 3, drop the update trigger, change row 2's after text, Verify: FirstBadSeq == 2
}

[Fact] public void Deleted_middle_row_is_detected() { /* drop delete trigger, delete seq 2: FirstBadSeq == 3 */ }
[Fact] public void Update_is_blocked_by_trigger() { /* UPDATE audit_log throws SqliteException containing "append only" */ }
[Fact] public void Rolled_back_append_leaves_nothing() { /* Append inside tx, Rollback: Verify Count 0 */ }
[Fact] public void Hash_does_not_depend_on_culture() { /* set CurrentCulture to "hi-IN" and "de-DE", same entry gives same hash */ }
```

- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.** `Verify` walks rows in `seq` order. It reports the first row whose `seq` is not previous plus one, whose `prev_hash` differs from the previous row's `hash`, or whose recomputed hash differs.
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: append only hash chained audit log`.

---

### Task 4: host.db schema and repositories

**Files:**
- Modify: `Migrations/Host/0001_initial.sql`
- Create: `HostDb/UserRepository.cs`, `HostDb/CompanyListRepository.cs`, `HostDb/HostSettingsRepository.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/HostDbTests.cs`

**Interfaces:**
- Produces:
  - `public enum BuiltInRole { Owner, Accountant, DataEntry, Viewer }`
  - `public sealed record UserRecord(int Id, string Name, string PasswordHash, BuiltInRole Role, bool Active, long RowVersion);`
  - `UserRepository`: `int Count(SqliteConnection c)`, `UserRecord? Find(SqliteConnection c, string name)` (case insensitive), `UserRecord? Get(SqliteConnection c, int id)`, `IReadOnlyList<UserRecord> List(SqliteConnection c)`, `int Insert(SqliteTransaction tx, string name, string hash, BuiltInRole role)`, `void SetPassword(SqliteTransaction tx, int id, string hash)`, `void SetCompanies(SqliteTransaction tx, int userId, IReadOnlyList<int> companyIds)`, `IReadOnlyList<int> Companies(SqliteConnection c, int userId)`.
  - `public sealed record CompanyListEntry(int Id, string Name, string FileName);`
  - `CompanyListRepository`: `int Insert(SqliteTransaction tx, string name)` (file name is `company-{id}.db`), `IReadOnlyList<CompanyListEntry> List(SqliteConnection c)`, `CompanyListEntry? Get(SqliteConnection c, int id)`, `void Rename(SqliteTransaction tx, int id, string name)`.
  - `HostSettingsRepository`: `string? Get(SqliteConnection c, string key)`, `void Set(SqliteTransaction tx, string key, string json)`.

Tables: `users` (name unique `COLLATE NOCASE`), `user_companies(user_id, company_id)` with foreign keys, `companies`, `host_settings(key PRIMARY KEY, value TEXT)`, `audit_log` (Task 3).

Also create, unused until Plan 3, `api_tokens(id INTEGER PRIMARY KEY, user_id INTEGER REFERENCES users, name TEXT, token_hash TEXT UNIQUE, read_write INTEGER, created_at TEXT, last_used_at TEXT NULL, revoked_at TEXT NULL)`. Spec 4.1 needs it, and creating it now keeps Plan 3 from needing a host.db migration.

Custom roles are Plan 8. Storing the role as a `TEXT` built in name keeps that change to an added table.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - Insert then `Find("OWNER")` finds user `owner`.
  - A duplicate name in a different case throws `SqliteException` (unique).
  - `SetCompanies` replaces the previous set.
  - `SetCompanies` with an unknown company id throws (foreign key).
  - `SetPassword` increments `RowVersion`.
  - The company file name is `company-1.db` for the first company.
  - Setting round trip.
  - The `api_tokens` table exists.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: host database users, companies, and settings`.

---

### Task 5: Company file schema, profile, chart, parties, items

**Files:**
- Modify: `Migrations/Company/0001_initial.sql`
- Create: `CompanyDb/CompanyRepository.cs`, `CompanyDb/ChartRepository.cs`, `CompanyDb/PartyRepository.cs`, `CompanyDb/ItemRepository.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/CompanyDbTests.cs`

**Interfaces:**
- Consumes: `ChartOfAccounts.Load` (Task 1), `Gstin`, `GstRegistration`.
- Produces:
  - `public sealed record CompanyProfile(string Name, StateCode State, GstRegistration Registration, Gstin? Gstin, string Address, int FirstFyStartYear);`
  - `CompanyRepository`: `void Create(SqliteTransaction tx, CompanyProfile p, ChartOfAccounts chart)` writes the profile, the first financial year, the chart, and the default number series (Task 6 table). Also: `CompanyProfile GetProfile(SqliteConnection c)`, `void UpdateProfile(SqliteTransaction tx, CompanyProfile p)`, `IReadOnlyList<FinancialYear> Years(SqliteConnection c)`, `void AddYear(SqliteTransaction tx, FinancialYear fy)`, `string? GetSetting(SqliteConnection c, string key)`, `void SetSetting(SqliteTransaction tx, string key, string json)`.
  - `ChartRepository`: `ChartOfAccounts Load(SqliteConnection c)`, `IReadOnlyDictionary<LedgerId, Money> Openings(SqliteConnection c)`, `void SaveGroup(SqliteTransaction tx, AccountGroup g)` (upsert), `void SaveLedger(SqliteTransaction tx, LedgerAccount l)` (upsert), `void DeleteLedger(SqliteTransaction tx, LedgerId id)`, `void SetOpening(SqliteTransaction tx, LedgerId id, Money amount)`, `bool HasPostings(SqliteConnection c, LedgerId id)`.
  - `public sealed record Party(LedgerId Ledger, Gstin? Gstin, StateCode State, int CreditDays, string Address, long RowVersion);`
  - `PartyRepository`: `Party? Get(SqliteConnection c, LedgerId l)`, `IReadOnlyList<Party> List(SqliteConnection c)`, `void Upsert(SqliteTransaction tx, Party p)` (bumps `RowVersion`).
  - `public sealed record Item(int Id, string Name, string HsnSac, bool IsService, string Unit, GstRate Rate, GstRate CessRate, Money DefaultPrice, long RowVersion);`
  - `ItemRepository`: `Item? Get`, `IReadOnlyList<Item> List`, `int Insert(SqliteTransaction tx, Item i)`, `void Update(SqliteTransaction tx, Item i)`.

Ledger and group ids are the `Core` ids, stored as given. Ledgers carry `required TEXT NULL` holding the `RequiredLedger` name, and `opening_paise INTEGER NOT NULL DEFAULT 0`. Item names are unique `COLLATE NOCASE`.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - Create with `DefaultChart.Create()`, then `Load` equals the original groups and ledgers.
  - The profile round trips, including a null GSTIN.
  - `SetOpening` then `Openings` returns the amount.
  - A party for an unknown ledger throws (foreign key).
  - An item round trips its rate in basis points, for example 1800.
  - `Years` after `AddYear(2027)` is `[2026, 2027]`.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: company database profile, chart, parties, items`.

---

### Task 6: Vouchers, number series, period lock

**Files:**
- Modify: `Migrations/Company/0001_initial.sql`
- Create: `CompanyDb/VoucherRepository.cs`, `CompanyDb/NumberSeriesRepository.cs`, `CompanyDb/LockRepository.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/VoucherRepositoryTests.cs`

**Interfaces:**
- Consumes: `NumberSeries`, `PostingLine`, `PostingRecord`, `VoucherType`, `InvoiceKind`, `TradeDirection`.
- Produces:
  - `public sealed record InvoiceLineRow(int? ItemId, LedgerId Ledger, string Description, string HsnSac, Quantity Quantity, Money UnitPrice, Money Discount, GstRate Rate, GstRate CessRate, bool PriceIncludesTax, Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess);`
  - `public sealed record InvoiceHeader(LedgerId Party, TradeDirection Direction, InvoiceKind Kind, StateCode PlaceOfSupply, bool ReverseCharge, string? PartyDocNumber, DateOnly? PartyDocDate, string? OriginalNumber, DateOnly? OriginalDate, Money RoundOff, IReadOnlyList<InvoiceLineRow> Lines);`
  - `public sealed record VoucherRow(long Id, VoucherType Type, string Series, int FyStartYear, long Number, string DisplayNumber, DateOnly Date, string Narration, bool Cancelled, int CreatedBy, long RowVersion, IReadOnlyList<PostingLine> Postings, InvoiceHeader? Invoice);`
  - `NumberSeriesRepository`: `void EnsureDefaults(SqliteTransaction tx, FinancialYear fy)`, `(NumberSeries Series, long Next) Take(SqliteTransaction tx, VoucherType type, FinancialYear fy, string series = "Main")`. `Take` increments `next` in the same statement (`UPDATE ... RETURNING`).
  - `VoucherRepository`: `long Insert(SqliteTransaction tx, VoucherRow v)` (Id and RowVersion ignored on input; RowVersion starts at 1), `bool Update(SqliteTransaction tx, VoucherRow v, long expectedRowVersion)` (false when the row version differs), `bool Cancel(SqliteTransaction tx, long id, long expectedRowVersion)` (sets the flag and deletes the postings), `VoucherRow? Get(SqliteConnection c, long id)`, `IReadOnlyList<VoucherRow> List(SqliteConnection c, DateRange range, VoucherType? type, int? createdBy)`, `IEnumerable<PostingRecord> AllPostings(SqliteConnection c)`, `IReadOnlyList<PostingRecord> PostingsOf(SqliteConnection c, long voucherId)`.
  - `LockRepository`: `DateOnly? LockedUpTo(SqliteConnection c)`, `void Set(SqliteTransaction tx, DateOnly? upTo)`.

Default series, all width 5:

| Type | Pattern |
|---|---|
| Sale | `INV/{FY}/` |
| Purchase | `PUR/{FY}/` |
| CreditNote | `CN/{FY}/` |
| DebitNote | `DN/{FY}/` |
| Receipt | `RCT/{FY}/` |
| Payment | `PAY/{FY}/` |
| Journal | `JV/{FY}/` |
| Contra | `CON/{FY}/` |

`vouchers` has `UNIQUE(type, series, fy_start, number)`. Postings live in `postings(voucher_id, date, ledger_id, amount_paise)` with an index on `(ledger_id, date)`. The posting date is copied from the voucher, so an edit rewrites it.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - `Take` twice returns 1 then 2.
  - `Take` for a new financial year after `EnsureDefaults` starts at 1.
  - Insert then `Get` round trips postings and invoice lines.
  - `Update` with a stale row version returns false and changes nothing.
  - `Cancel` removes the postings but `Get` still returns the voucher with `Cancelled` true and its number.
  - `List` with `createdBy` filters.
  - A posting to an unknown ledger throws (foreign key).
  - Inserting the same number twice throws (unique).
  - `LockedUpTo` round trips null and a date.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: voucher storage, number series, period lock`.

---

### Task 7: Backup manager

Spec 9.

**Files:**
- Create: `src/OpenHisabKitab.Data/Backup/BackupManager.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/BackupManagerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record BackupFile(string Path, string SourceName, DateTimeOffset TakenAt);`
  - `public sealed class BackupManager(TimeProvider time)` with:
    - `BackupFile Backup(string sourcePath, string folder)`: names the file `{source file name without .db}-{yyyyMMdd-HHmmss}.db`, uses `SqliteConnection.BackupDatabase`, runs `PRAGMA integrity_check` on the copy, and deletes the copy and throws `BackupFailedException` unless the result is `ok`.
    - `IReadOnlyList<BackupFile> List(string folder, string sourceName)`: newest first.
    - `void Prune(string folder, string sourceName, int keep)`
    - `void RestoreInto(string backupPath, SqliteConnection live)`: checks the backup's integrity first, then backs it up into `live`.
  - `public sealed class BackupFailedException(string message) : Exception(message);`

- [ ] **Step 1: Write the failing tests.** Cover these:
  - The backup contains the rows and passes the integrity check.
  - The file name uses the fake clock time.
  - `Prune(keep: 30)` with 32 files leaves the newest 30.
  - `Prune` only touches files of that source name.
  - A truncated backup file is refused by `RestoreInto` with `BackupFailedException`, and the live database is unchanged.
  - A backup taken while another connection holds an open write transaction still succeeds, with the last committed data.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: online backup with integrity check and retention`.

---

### Task 8: Host project, call context, permissions, login

Spec 8.

**Files:**
- Create: `src/OpenHisabKitab.Host/OpenHisabKitab.Host.csproj` (`Microsoft.NET.Sdk.Web`), `Application/CallContext.cs`, `Application/Permission.cs`, `Application/Roles.cs`, `Application/ServiceException.cs`, `Application/PasswordHasher.cs`, `Application/SessionStore.cs`, `Application/LoginThrottle.cs`, `Application/HostData.cs`, `Application/AuthService.cs`, `Application/UserService.cs`
- Create: `tests/OpenHisabKitab.Host.Tests/` project with `TestHost.cs` (builds the application services over a temp data directory with a `FakeTimeProvider`), `AuthServiceTests.cs`, `PasswordHasherTests.cs`
- Modify: `OpenHisabKitab.slnx`

**Interfaces:**
- Consumes: Task 3 and Task 4 repositories.
- Produces:
  - `public enum Permission { ManageUsers, ManageCompanies, EditMasters, CreateVoucher, EditVoucher, CancelVoucher, ViewAllVouchers, ViewReports, ViewGstReports, Export, LockPeriod, UnlockPeriod, Backup, Restore, ViewAudit, ManageHostSettings }`
  - `public static class Roles { public static IReadOnlySet<Permission> Of(BuiltInRole r); }`, following spec 8:
    - Owner: all.
    - Accountant: all except `ManageUsers`, `UnlockPeriod`, `Restore`, `ManageHostSettings`.
    - DataEntry: `EditMasters`, `CreateVoucher`, `EditVoucher`.
    - Viewer: `ViewAllVouchers`, `ViewReports`, `ViewGstReports`.
  - `public sealed record CallContext(int UserId, string UserName, IReadOnlySet<Permission> Permissions, IReadOnlySet<int> Companies, string Machine, ChangeSource Source, string? TokenName, bool ReadOnly)` with `void Require(Permission p)` and `void RequireCompany(int id)`.
    - `Require` throws `PermissionDenied` "You do not have permission for this." when missing.
    - When `ReadOnly` is true, any write permission is denied the same way. Only `View*` and `Export` count as reads.
    - Owners pass `RequireCompany` for every company.
  - `public enum ServiceError { NotAuthenticated, PermissionDenied, NotFound, Conflict, Locked, Invalid, OpeningDifference, SetupRequired, Unavailable }`
  - `public sealed class ServiceException(ServiceError error, string message) : Exception(message)`, with `ServiceError Error`. Conflict also carries `object? Current`.
  - `public static class PasswordHasher { public static string Hash(string password); public static bool Verify(string password, string stored); }`
    - Format: `pbkdf2-sha256$600000${saltBase64}${hashBase64}`.
    - Uses `Rfc2898DeriveBytes.Pbkdf2` with a 16 byte salt and a 32 byte hash.
    - Compares with `CryptographicOperations.FixedTimeEquals`.
  - `public sealed class HostData(string dataDirectory)` with `string HostDbPath`, `string CompanyPath(int id)`, `string BackupFolder` (default `{data}\Backups`, overridden by host setting `backup.folder`), and `SqliteConnection OpenHost()` (migrates on first open).
  - `public sealed record Session(string Token, CallContext Context, DateTimeOffset ExpiresAt);`
  - `SessionStore(TimeProvider)`:
    - `Session Create(CallContext ctx)`: the token is 32 random bytes, base64url.
    - `CallContext? Resolve(string token)`: sliding 12 hour idle expiry.
    - `void Remove(string token)`.
    - `void RemoveAll()`.
    - `void RemoveUser(int userId)`.
  - `LoginThrottle(TimeProvider)`:
    - After 5 failures for one user name, further attempts are refused for 30 s.
    - The wait doubles with each further failure, up to 15 minutes.
    - A success resets it.
  - `AuthService`:
    - `bool NeedsSetup()`.
    - `Session SetupOwner(string name, string password, string machine, bool isLoopback)`.
    - `Session Login(string name, string password, string machine)`.
    - `void Logout(string token)`.
    - `void ChangePassword(CallContext ctx, string current, string next)`.
  - `UserService`:
    - `int Create(CallContext ctx, string name, string password, BuiltInRole role)`.
    - `void SetCompanies(CallContext ctx, int userId, IReadOnlyList<int> companies)`.
    - `IReadOnlyList<UserRecord> List(CallContext ctx)`.
    - All three require `ManageUsers` and are audited in host.db.

Exact messages:

| Case | Error | Message |
|---|---|---|
| Wrong name or password | NotAuthenticated | `Wrong user name or password.` |
| Throttled | NotAuthenticated | `Too many attempts. Try again in {n} seconds.` |
| Setup from another PC | PermissionDenied | `Set up the Owner on the host PC.` |
| Setup after users exist | Invalid | `The Owner is already set up.` |
| Login before setup | SetupRequired | `Set up the Owner first.` |
| Password shorter than 8 | Invalid | `Use at least 8 characters.` |

`SetupOwner` and `ChangePassword` are audited. A password change ends the user's other sessions.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - Hash and verify round trip; a wrong password fails.
  - Two hashes of the same password differ.
  - A tampered hash string returns false and does not throw.
  - Setup from non loopback is refused.
  - Setup twice is refused.
  - Login with the wrong password gives the exact message.
  - The sixth failure is throttled; after advancing the fake clock 31 s, a login works.
  - The session expires after 12 h idle but not after 11 h with activity at 6 h.
  - DataEntry lacks `ViewReports`.
  - A read only context is denied `CreateVoucher`.
  - The Accountant cannot create users.
  - The host.db audit has the setup entry.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: host login, sessions, roles, and permissions`.

---

### Task 9: Companies and masters services

**Files:**
- Create: `Application/CompanyHub.cs`, `Application/ChangeFeed.cs`, `Application/CompanyService.cs`, `Application/MasterService.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/CompanyServiceTests.cs`, `MasterServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 4 to 6 repositories, `Gstin`, `GstData.LoadEmbedded()`.
- Produces:
  - `CompanyHub(HostData data, TimeProvider time)`:
    - `SqliteConnection Open(int companyId)`: migrates on first open per process, calling a pre migration backup.
    - `Task<T> WriteAsync<T>(int companyId, Func<SqliteTransaction, T> work)`: takes the company semaphore, `BeginWrite`, runs `work`, commits. It rolls back on any exception.
    - The write lock is `SemaphoreSlim(1)` per company.
  - `public enum ChangeKind { Masters, Voucher, Lock, Settings, Restore }`
  - `public sealed record ChangeEvent(int CompanyId, ChangeKind Kind, long? EntityId);`
  - `ChangeFeed`:
    - `void Publish(ChangeEvent e)`.
    - `IAsyncEnumerable<ChangeEvent> Subscribe(CancellationToken ct)`: one bounded `Channel` per subscriber; when it is full, the oldest event is dropped.
  - `CompanyService`:
    - `int Create(CallContext ctx, CompanyProfile p)`: requires `ManageCompanies`. Seeds `DefaultChart.Create()`. The creator is given access.
    - `IReadOnlyList<CompanyListEntry> List(CallContext ctx)`: only accessible companies.
    - `CompanyProfile Get(CallContext ctx, int id)`.
    - `void AddYear(CallContext ctx, int id)`: adds the year after the latest only.
    - `void SetSetting(CallContext ctx, int id, string key, string json)`.
  - `MasterService`, all requiring `EditMasters` for writes and company access for everything:
    - `ChartOfAccounts Chart(CallContext ctx, int company)`.
    - `GroupId AddGroup(CallContext ctx, int company, string name, GroupId parent)`.
    - `LedgerId AddLedger(CallContext ctx, int company, string name, GroupId group)`.
    - `void Rename(CallContext ctx, int company, LedgerId id, string name)`.
    - `void RenameGroup(CallContext ctx, int company, GroupId id, string name)`.
    - `void RemoveLedger(CallContext ctx, int company, LedgerId id)`.
    - `void SetOpening(CallContext ctx, int company, LedgerId id, Money amount)`.
    - `LedgerId SaveParty(CallContext ctx, int company, string name, GroupId group, Party p)`.
    - `int SaveItem(CallContext ctx, int company, Item i)`.
    - `IReadOnlyList<Party> Parties(...)`.
    - `IReadOnlyList<Item> Items(...)`.

Rules:

| Rule | Error | Message |
|---|---|---|
| Regular registration needs a GSTIN whose state equals the company state | Invalid | `GSTIN state does not match the company state.` |
| Composition and Unregistered may omit the GSTIN | | |
| A party group must be Trade Receivables or Trade Payables, or under one | Invalid | `Parties belong under Trade Receivables or Trade Payables.` |
| A party's GSTIN state must equal its state | Invalid | (same as the company message) |
| An item's HSN or SAC is 4, 6, or 8 digits | Invalid | `HSN or SAC must be 4, 6, or 8 digits.` |
| Item rates must be valid on today's date (`GstData.IsValidRate`) | Invalid | `GST rate {r}% is not valid.` |
| Removing a ledger with postings | Invalid | `Ledger {name} has entries and cannot be deleted.` |
| A `ChartException` | Invalid | The `ChartException` message |
| Opening balances are dated at the books start, so `SetOpening` is refused when the lock date is on or after it | Locked | `Books are locked up to {dd-mm-yyyy}.` |

Every write appends an audit entry, with the before and after values as JSON, and publishes a `ChangeEvent`.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - Create a company, then `Chart` has `Cash`.
  - A user without access gets `PermissionDenied` on `Get`.
  - A GSTIN state mismatch gives the exact message.
  - Removing a ledger with postings is refused.
  - Renaming a required ledger works.
  - A party under Sales is refused.
  - A rate of 7% is refused.
  - Every write adds one audit row and one change event.
  - Two writes to different companies run concurrently; two to the same company run in order (use a gate in `work`).
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: company and masters services`.

---

### Task 10: Voucher service for journal type vouchers

Receipt, Payment, Journal, Contra.

**Files:**
- Create: `Application/VoucherService.cs`, `Application/VoucherModels.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/VoucherServiceTests.cs`

**Interfaces:**
- Consumes: `VoucherValidator`, `Postings.Consolidate`, Task 6 repositories, `CompanyHub`, `CompanyBooks.Apply` (Task 12; until then call a no op hook `IVoucherObserver.Saved(int company, long voucherId, IReadOnlyList<PostingRecord> postings)`, which `CompanyBooks` implements in Task 12).
- Produces:
  - `public sealed record JournalInput(VoucherType Type, DateOnly Date, string Narration, IReadOnlyList<PostingLine> Lines);`
  - `public sealed record SavedVoucher(long Id, string DisplayNumber, long RowVersion);`
  - `VoucherService`:
    - `Task<SavedVoucher> SaveJournalAsync(CallContext ctx, int company, JournalInput input)`.
    - `Task<SavedVoucher> EditJournalAsync(CallContext ctx, int company, long id, long rowVersion, JournalInput input)`.
    - `Task CancelAsync(CallContext ctx, int company, long id, long rowVersion, string reason)`.
    - `VoucherRow Get(CallContext ctx, int company, long id)`.
    - `IReadOnlyList<VoucherRow> List(CallContext ctx, int company, DateRange range, VoucherType? type)`.

Rules, checked inside the write transaction in this order, with these exact messages:

| # | Rule | Error | Message |
|---|---|---|---|
| 1 | Type must be Receipt, Payment, Journal, or Contra | Invalid | `Use the invoice entry for {type}.` |
| 2 | The date's financial year must exist and the date must be on or after the books start | Invalid | `No financial year for {dd-mm-yyyy}. Add it in CMPY.` |
| 3 | The date must be after the lock date. On edit, the old date also. | Locked | `Books are locked up to {dd-mm-yyyy}.` |
| 4 | `Books.OpeningDifference` (from the cache, or computed from `Openings`) must be zero | OpeningDifference | `Opening balances differ by {amount}. Fix them in ACCT.` |
| 5 | `VoucherValidator.Validate` on the consolidated lines must be empty | Invalid | First error mapped to text (table below) |
| 6 | An edit keeps the financial year | Invalid | `Date must stay in FY {label}.` |
| 7 | A stale row version | Conflict | `Someone else changed this voucher. Showing the current version.` The `ServiceException` carries the current `VoucherRow`. |
| 8 | A cancelled voucher cannot be edited or cancelled again | Invalid | `This voucher is cancelled.` |

Amounts in messages use `IndianNumberFormat`. Error text for `VoucherError`:

| Error | Message |
|---|---|
| Unbalanced | `Debits and credits differ.` |
| TooFewLines | `Enter at least two lines.` |
| ZeroLine | `Remove lines with a zero amount.` |
| UnknownLedger | `Unknown ledger.` |
| ContraNonCashLedger | `Contra uses only cash and bank ledgers.` |
| ReceiptWithoutCashDebit | `A receipt must debit cash or bank.` |
| PaymentWithoutCashCredit | `A payment must credit cash or bank.` |

Permissions:
- Save needs `CreateVoucher`, edit needs `EditVoucher`, cancel needs `CancelVoucher`.
- `Get` and `List` need `ViewAllVouchers`, or they return only the caller's own vouchers when the caller has `CreateVoucher` but not `ViewAllVouchers`.
- `Get` of another user's voucher in that case: PermissionDenied.
- Data entry users may edit only their own vouchers.

Each save, edit, or cancel writes one audit entry with the voucher before and after as JSON, and publishes a `ChangeEvent(Voucher)`.

- [ ] **Step 1: Write the failing tests.** Cover these:
  - A receipt saves as `RCT/26-27/00001`, and the second as `00002`.
  - An unbalanced journal gives `Debits and credits differ.`
  - A voucher dated on the lock date is refused; the day after is accepted.
  - Editing a 10-05-2026 voucher to 10-04-2027 gives `Date must stay in FY 2026-27.`
  - Editing an unlocked voucher to a locked date is refused.
  - An opening difference of ₹100 blocks saving with `Opening balances differ by 100.00. Fix them in ACCT.`
  - A stale edit returns Conflict carrying the current row.
  - Cancel keeps the number; the next save does not reuse it.
  - A data entry user cannot `Get` another user's voucher, and `List` excludes it.
  - **Failure halfway:** inject an audit failure (a test only `Func<AuditEntry, AuditEntry>` hook on `CompanyHub` that throws). Afterwards no voucher, no postings, and the next save gets `00001`.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: voucher service for receipts, payments, journals, contras`.

---

### Task 11: Invoice saving with GST

Sale, Purchase, CreditNote, DebitNote. The host recalculates everything (spec 4, Host service).

**Files:**
- Modify: `Application/VoucherService.cs`, `Application/VoucherModels.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/InvoiceServiceTests.cs`

**Interfaces:**
- Consumes: `GstRules.Split`, `InvoiceCalculator.Calculate`, `GstPosting.Post`, `GstPostingAccounts.FromChart`, `GstData`.
- Produces:
  - `public sealed record InvoiceEntryLine(int? ItemId, LedgerId Ledger, string Description, string HsnSac, Quantity Quantity, Money UnitPrice, Money Discount, GstRate Rate, GstRate CessRate, bool PriceIncludesTax);` The name avoids a clash with `Core.Gst.InvoiceLineInput`.
  - `public sealed record InvoiceInput(VoucherType Type, DateOnly Date, string Narration, LedgerId Party, TradeDirection Direction, InvoiceKind Kind, StateCode PlaceOfSupply, bool ReverseCharge, string? PartyDocNumber, DateOnly? PartyDocDate, string? OriginalNumber, DateOnly? OriginalDate, IReadOnlyList<InvoiceEntryLine> Lines);`
  - `VoucherService.SaveInvoiceAsync(CallContext ctx, int company, InvoiceInput input)` and `EditInvoiceAsync(..., long id, long rowVersion, InvoiceInput input)`, both returning `SavedVoucher`.

Steps inside the transaction, after rules 2 to 4 and 6 to 8 from Task 10:

1. **Direction.** Sale is Outward and Purchase is Inward. Credit and debit notes take `Direction` from the input. Other combinations: Invalid `Sale must be outward and purchase inward.`
2. **Party.** The party must exist: Invalid `Unknown party.`
3. **Line ledgers.** Outward line ledgers must be Income. Inward line ledgers must be Expense, or Asset and not cash or bank. Otherwise Invalid `Line {n}: choose a sales or purchase ledger.`
4. **Rates** must be valid on the voucher date: `GST rate {r}% is not valid on {date}.`
5. **Tax split.** `GstRules.Split(company state, place of supply, sezOrExport, data)`. `sezOrExport` is true for every `InvoiceKind` except Regular and DeemedExport.
6. **Zero tax** applies to these kinds and registrations:
   - Kinds `ExportUnderLut` and `SezWithoutTax`: lines are calculated with rate and cess rate zero. The item rate is still stored.
   - Composition company sales are bills of supply: zero tax.
   - Unregistered company: zero tax on all documents.
   - Composition or Unregistered purchase with any non zero rate: Invalid `This company cannot take input tax credit. Enter the purchase with rate 0 and the full amount.` Owner review item.
7. **Reverse charge** only on Inward documents: Invalid `Reverse charge applies only to purchases.`
8. **Round off.** `roundOff` = company setting `invoice.roundOff`, default true.
9. **Calculate and post.** Calculate, post with `GstPosting.Post`, then validate with `VoucherValidator`. An `InvoiceException` maps to Invalid `Line {n}: {text}`, with text per `InvoiceError`:

   | InvoiceError | Text |
   |---|---|
   | NoLines | `Enter at least one line.` (no line prefix) |
   | NonPositiveQuantity | `quantity must be more than zero.` |
   | NegativePrice | `price cannot be negative.` |
   | NegativeDiscount | `discount cannot be negative.` |
   | NegativeTaxable | `discount is more than the amount.` |

10. **Store.** Store the computed per line taxable and tax amounts and the round off in `InvoiceHeader`.

- [ ] **Step 1: Write the failing tests.** Use a company in state 27 (Maharashtra), Regular.
  - **Intra state sale:** one line, quantity 1, price ₹1,000.00, 18%, to a party in 27. Taxable 1,000.00, CGST 90.00, SGST 90.00, grand total 1,180.00. The number is `INV/26-27/00001`. The party ledger has a 1,180.00 debit in the trial balance.
  - **Inter state sale:** the same line to place of supply 29 gives IGST 180.00.
  - **SEZ without tax:** place of supply 27 and kind `SezWithoutTax` give zero tax and taxable 1,000.00.
  - **Round off:** price 100.40 at 18% gives a grand total of 118.00 and round off -0.47.
  - **Client totals ignored:** the input has no totals field. Assert that the stored line amounts equal `InvoiceCalculator` output.
  - **Reverse charge** on a sale is refused.
  - **Reverse charge purchase** from a party in 27 credits RCM Payable CGST and SGST with 90.00 each.
  - **Composition company:** a sale gives zero tax; a purchase at 18% is refused with the exact message.
  - **Line ledger:** a sale line to the Cash ledger gives `Line 1: choose a sales or purchase ledger.`
  - **Credit note** outward reduces Sales: Sales has a debit of 1,000.00.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: GST invoice saving recalculated on the host`.

---

### Task 12: Report service and the books cache

Spec 5.1 and 10. Plan 1 owner note: report engine build for 1,000,000 lines takes about 0.55 s, so the host keeps it in memory.

**Files:**
- Create: `Application/Books/CompanyBooks.cs`, `Application/ReportService.cs`
- Modify: `VoucherService` to call `CompanyBooks.Apply` after commit, and `MasterService` to call `CompanyBooks.Invalidate` after chart or opening changes
- Test: `tests/OpenHisabKitab.Host.Tests/ReportServiceTests.cs`, `CompanyBooksPerformanceTests.cs`

**Interfaces:**
- Produces:
  - `CompanyBooks(CompanyHub hub)`:
    - `Books Get(int company)`.
    - `void Apply(int company, long voucherId, IReadOnlyList<PostingRecord> postings)`: replaces that voucher's postings; an empty list for cancel.
    - `void Invalidate(int company)`.
    - It keeps per company a `Dictionary<long, PostingRecord[]>` by voucher, plus the chart and openings, and builds `Books` lazily when `Get` follows a change.
    - Thread safe: a lock per company around the state; `Books` itself is immutable once built.
  - `ReportService`, needing `ViewReports` and company access:
    - `TrialBalance TrialBalance(CallContext ctx, int company, DateRange period)`.
    - `ProfitAndLoss ProfitAndLoss(...)`.
    - `BalanceSheet BalanceSheet(CallContext ctx, int company, DateOnly asAt)`.
    - `LedgerStatement Statement(CallContext ctx, int company, LedgerId ledger, DateRange period)`.
    - A period crossing 31 March: Invalid `Choose a period within one financial year.`

- [ ] **Step 1: Write the failing tests.**
  - **Matches a full rebuild:** after a sequence of save, edit, and cancel calls, `TrialBalance` from the cache equals one from `new Books(...)` built straight from `VoucherRepository.AllPostings`. Use `RandomBooks` style seeded data with 200 vouchers and assert every row.
  - **Masters change:** after `SetOpening`, the cache reflects it.
  - **Permissions:** a data entry user gets PermissionDenied.
  - **Period:** a period crossing 31 March gives the exact message.
  - **Performance** (`[Trait("Category", "Performance")]`, still runs in CI):
    - Load `CompanyBooks` with 1,000,000 in memory posting records (seeded, no SQLite).
    - Apply one voucher.
    - Assert a full year `TrialBalance` returns in under 1 s, using a `Stopwatch` around `Get` plus `TrialBalance`, best of 3.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.** If the performance test fails, profile before changing `Core.Books`, and report the numbers rather than relaxing the limit.
- [ ] **Step 5: Commit** `feat: report service over an in memory books cache`.

---

### Task 13: Locks, audit view, backups, and restore

Spec 5.5, 5.6, 9.

**Files:**
- Create: `Application/AdminService.cs`, `Application/BackupScheduler.cs`, `Application/MaintenanceGate.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/AdminServiceTests.cs`, `BackupSchedulerTests.cs`

**Interfaces:**
- Produces:
  - `AdminService`:
    - `void LockUpTo(CallContext ctx, int company, DateOnly date)`: needs `LockPeriod`. Moving the lock earlier counts as an unlock and needs `UnlockPeriod`.
    - `void Unlock(CallContext ctx, int company)`: needs `UnlockPeriod`.
    - `AuditVerification VerifyAudit(CallContext ctx, int? company)`: null means host.db. Needs `ViewAudit`.
    - `IReadOnlyList<StoredAuditEntry> Audit(CallContext ctx, int? company, AuditFilter f)`: needs `ViewAudit`.
    - `BackupFile BackupNow(CallContext ctx, int? company)`: needs `Backup`.
    - `IReadOnlyList<BackupFile> Backups(CallContext ctx, int? company)`.
    - `Task RestoreAsync(CallContext ctx, int company, string backupPath)`: needs `Restore`.
  - `MaintenanceGate`:
    - `bool IsClosed`.
    - `Task<IDisposable> CloseAsync(int company)`: waits for the company write lock and holds it.
    - Every service call checks `IsClosed` first: Unavailable `A restore is running. Try again in a minute.`
  - `BackupScheduler(TimeProvider, ...) : BackgroundService`:
    - Every minute, it backs up host.db and each company whose newest backup is 24 h old or older (setting `backup.intervalHours`, default 24).
    - It then prunes to `backup.keep` (default 30).
    - Each backup is audited in its own database with action `backup`.
    - A failure is logged and audited with action `backup-failed`, and the scheduler continues.

Restore sequence:
1. Close the gate.
2. `SessionStore.RemoveAll()`.
3. Publish `ChangeEvent(Restore)`.
4. Back up the live file.
5. `BackupManager.RestoreInto`.
6. Migrate.
7. `CompanyBooks.Invalidate`.
8. Append audit `restore` with the backup file name.
9. Open the gate.

If step 5 fails, the live file is unchanged, the gate reopens, and the error is Invalid `Backup file is damaged. Nothing was restored.`

- [ ] **Step 1: Write the failing tests.**
  - **Locking:**
    - The Accountant can lock but cannot move the lock earlier.
    - The Owner can unlock.
    - Lock and unlock are audited.
  - **Audit:**
    - `VerifyAudit` is OK after 20 mixed operations.
    - After tampering with a row (drop the trigger in the test), it reports that row's seq.
  - **Backup scheduler:**
    - With the fake clock, no backup at 23 h, one at 24 h.
    - `keep` is honoured.
  - **Restore:**
    - Save voucher A, back up, save voucher B, restore. B is gone, A remains, and all sessions are removed.
    - A damaged backup leaves B in place and gives the exact message.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: period locks, audit verification, scheduled backups, restore`.

---

### Task 14: gRPC contracts and adapters

**Files:**
- Create: `src/OpenHisabKitab.Contracts/OpenHisabKitab.Contracts.csproj` (`Grpc.Tools`, `Google.Protobuf`, `Grpc.AspNetCore` for the generated base classes; `GrpcServices="Both"`)
- Create: `Protos/common.proto`, `session.proto`, `companies.proto`, `masters.proto`, `vouchers.proto`, `reports.proto`, `admin.proto`, `changes.proto`
- Create: `src/OpenHisabKitab.Host/Grpc/*Endpoint.cs` (one per proto service), `Grpc/AuthInterceptor.cs`, `Grpc/VersionInterceptor.cs`, `Grpc/ErrorMapping.cs`, `Grpc/Mapping.cs`
- Test: `tests/OpenHisabKitab.Host.Tests/Grpc/GrpcEndToEndTests.cs` using `WebApplicationFactory<Program>` and `GrpcChannel.ForAddress(factory.Server.BaseAddress, new() { HttpHandler = factory.Server.CreateHandler() })`

**Interfaces:**
- Consumes: every service from Tasks 8 to 13.
- Produces:
  - Package `ohk.v1`, C# namespace `OpenHisabKitab.Contracts.V1`.
  - Services and RPCs:

    | Service | RPCs |
    |---|---|
    | `Session` | `GetInfo` (no auth; returns host version, `needs_setup`), `SetupOwner`, `Login`, `Logout`, `ChangePassword` |
    | `Companies` | `List`, `Create`, `Get`, `AddYear`, `SetSetting` |
    | `Masters` | `GetChart`, `AddGroup`, `AddLedger`, `RenameGroup`, `RenameLedger`, `RemoveLedger`, `SetOpening`, `SaveParty`, `ListParties`, `SaveItem`, `ListItems` |
    | `Vouchers` | `SaveJournal`, `EditJournal`, `SaveInvoice`, `EditInvoice`, `Cancel`, `Get`, `List` |
    | `Reports` | `TrialBalance`, `ProfitAndLoss`, `BalanceSheet`, `LedgerStatement` |
    | `Admin` | `LockUpTo`, `Unlock`, `VerifyAudit`, `ListAudit`, `BackupNow`, `ListBackups`, `Restore`, `CreateUser`, `SetUserCompanies`, `ListUsers` |
    | `Changes` | `Watch` (server stream of `ChangeEvent` for the companies the caller can access) |

  - Wire types:
    - Money is `int64 paise`. Quantity is `int64 thousandths`. Rate is `int32 basis_points`. Both ends are this codebase, so integers are safe here. The REST string rule is Plan 3.
    - Dates are `string` `yyyy-MM-dd`.
    - Every message that edits carries `int64 row_version`.
  - Metadata:
    - `authorization: Bearer {session token}` on all RPCs except `GetInfo`, `SetupOwner`, and `Login`.
    - `ohk-version: {version}` on all RPCs except `GetInfo`.
    - `ohk-machine: {pc name}` on `SetupOwner` and `Login`.
  - Version: the host's version is `typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()` with any `+commit` suffix removed. A mismatch is `FailedPrecondition` with message `Install Open Hisab Kitab {hostVersion} to connect.`
  - `ServiceError` to `StatusCode`:

    | ServiceError | StatusCode |
    |---|---|
    | NotAuthenticated | Unauthenticated |
    | PermissionDenied | PermissionDenied |
    | NotFound | NotFound |
    | Conflict | Aborted, with the current record serialised in trailer `ohk-current-bin` |
    | Locked | FailedPrecondition |
    | OpeningDifference | FailedPrecondition |
    | Invalid | InvalidArgument |
    | SetupRequired | FailedPrecondition |
    | Unavailable | Unavailable |

    The message is the `ServiceException` message. Any other exception is `Internal` with `Something went wrong. Details are in the host log.`, and the details are logged.
  - `SetupOwner` passes `isLoopback = IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress)`. Under `TestServer` the remote address is null. Treat null as not loopback, and let the test override it through a `IRemoteAddressAccessor` registered in DI.

- [ ] **Step 1: Write the failing end to end tests.**
  - `GetInfo` reports `needs_setup` true.
  - Setup with a loopback accessor, log in, create a company, save a receipt, and read the trial balance. The cash debit equals the amount.
  - A wrong `ohk-version` gives `FailedPrecondition` with the exact message.
  - A missing token gives `Unauthenticated`.
  - A stale edit gives `Aborted`, and the trailer decodes to the current voucher.
  - `Watch` receives a `Voucher` event after a save by another session.
  - A data entry user calling `Reports.TrialBalance` gets `PermissionDenied`.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.** Endpoints build the `CallContext` from the session and set `Source = ChangeSource.App`.
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: gRPC contracts and host endpoints`.

---

### Task 15: Host process: Kestrel, TLS certificate, Windows service

Spec 4, Transport.

**Files:**
- Create or modify: `src/OpenHisabKitab.Host/Program.cs`, `HostOptions.cs`, `appsettings.json`, `Security/CertificateStore.cs`
- Modify: `README.md` (a "Running the host" section)
- Test: `tests/OpenHisabKitab.Host.Tests/CertificateStoreTests.cs`, `KestrelTlsTests.cs`

**Interfaces:**
- Produces:
  - `HostOptions`, bound from section `Host`:
    - `DataDirectory`: default `%ProgramData%\OpenHisabKitab`.
    - `GrpcPort`: default `47250`.
  - `CertificateStore(string dataDirectory, TimeProvider time)`:
    - `X509Certificate2 GetOrCreate()`.
    - `string Fingerprint(X509Certificate2 c)`: SHA-256, uppercase hex in pairs separated by `:`.
  - On first run it creates the certificate:
    - RSA 3072, subject `CN=Open Hisab Kitab host`.
    - Subject alternative names: `localhost`, the machine name, and 127.0.0.1.
    - Valid from now minus 1 day for 10 years.
  - It saves `host-cert.pfx` with a random password. The password is protected with `ProtectedData.Protect(..., DataProtectionScope.LocalMachine)` in `host-cert.key`.
  - It writes the fingerprint to `host-cert-fingerprint.txt` and logs it at startup.
  - `Program`:
    - `builder.Host.UseWindowsService(o => o.ServiceName = "OpenHisabKitabHost")`.
    - Kestrel `ListenAnyIP(GrpcPort, l => { l.Protocols = Http2; l.UseHttps(cert); })`.
    - Maps all endpoints from Task 14.
    - Registers `BackupScheduler`.
    - Exposes `public partial class Program` for tests.
    - Opens host.db and migrates at startup, before accepting connections.

- [ ] **Step 1: Write the failing tests.**
  - `GetOrCreate` twice returns the same thumbprint.
  - The certificate has the three SANs.
  - A missing key file regenerates the certificate (new thumbprint) and logs a warning.
  - **Kestrel TLS:** start the real app on port 0 with a temp data directory and call `GetInfo` over TLS. The client callback compares the server certificate fingerprint with `host-cert-fingerprint.txt`.
- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.** In the README, document:
  - `dotnet run --project src/OpenHisabKitab.Host`.
  - Installing as a service: `sc.exe create OpenHisabKitabHost binPath= "...\OpenHisabKitab.Host.exe" start= auto`.
  - The firewall rule: `netsh advfirewall firewall add rule name="Open Hisab Kitab host" dir=in action=allow protocol=TCP localport=47250`.
  - That the installer does both in Plan 8.
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** `feat: host process with TLS and Windows service support`.

---

### Task 16: Contention tests, plan close out

**Files:**
- Test: `tests/OpenHisabKitab.Host.Tests/ConcurrencyTests.cs`
- Modify: `docs/owner-review.md`, `CLAUDE.md` (only if a new durable command appears)

- [ ] **Step 1: Write the tests.**
  - **Numbering under contention:**
    - 8 tasks, each with its own session over gRPC, each save 50 receipts at once to one company.
    - Collect the numbers: exactly `RCT/26-27/00001` to `RCT/26-27/00400`, no duplicates.
    - The trial balance cash debit equals the sum of all receipts.
    - Audit verify is OK, with 400 voucher entries.
  - **Conflicting edits:** two sessions read the same voucher and both edit. Exactly one succeeds, and the other gets `Aborted`.
  - **Readers during writes:** while the 400 saves run, a reader loop calling `TrialBalance` never sees debits differ from credits.
- [ ] **Step 2: Run the full suite in Release.**
  - `dotnet build OpenHisabKitab.slnx -c Release`: 0 warnings.
  - `dotnet test OpenHisabKitab.slnx -c Release --no-build`: all pass.
- [ ] **Step 3: Append a "Plan 2: Data and host" section to `docs/owner-review.md`.** It goes above the Plan 1 section. It holds the items listed under "Owner review items" below, plus any new decision taken while building.
- [ ] **Step 4: Commit** `test: numbering and edits under contention; owner review for plan 2`.

## Owner review items (copy into `docs/owner-review.md` in Task 16)

**Decisions made in the design for REST and MCP (spec 4.1)**

Each line says what was decided and what it costs if wrong.

1. The REST API and MCP listen on 127.0.0.1 only, and are off until the Owner turns them on. LAN access is a setting, off by default. Cost if wrong: one setting.
2. Amounts in REST and MCP are strings such as `"12345.60"`, never JSON numbers. Cost if wrong: client scripts must quote amounts.
3. MCP write tools are off by default, and need both a setting and a read and write token. Cost if wrong: one setting.
4. API tokens are per user and carry that user's permissions, read only unless chosen otherwise. Cost if wrong: token screen changes in Plan 3.
5. A stdio bridge program ships for assistants that only launch local programs. Cost if wrong: one small program.
6. Later plans were renumbered. REST and MCP are Plan 3, the client shell is Plan 4, and so on.

**Decisions made in Plan 2**

1. The first Owner can only be set up from the host PC itself. Cost if wrong: a remote first setup is not possible.
2. Passwords use PBKDF2 SHA-256 with 600,000 iterations. Logins lock for 30 s after 5 failures, doubling up to 15 minutes.
3. Sessions end after 12 hours idle. A host restart logs everyone out.
4. Composition and Unregistered companies cannot enter tax on purchases. They enter the full amount at rate 0. Cost if wrong: input tax shows as cost, which is correct for them, but per tax breakup is not kept.
5. Export under LUT and SEZ without tax are calculated with zero tax. The item's rate is still stored.
6. Default number series are `INV`, `PUR`, `CN`, `DN`, `RCT`, `PAY`, `JV`, and `CON`, followed by `/{FY}/` and 5 digits. Cost if wrong: the defaults change. Existing numbers stay.
7. A voucher's date can be edited only within its financial year, because its number belongs to that year.
8. The host gRPC port is 47250.
9. Bill by bill allocation and party opening bills move to Plan 5.
10. Custom roles and host.db restore move to Plan 8. Restore in Plan 2 covers company files.
11. Backups run when the newest backup is 24 hours old, checked every minute, so a PC that was off at night still backs up when it starts.

**Known limits carried to later plans**

1. A database written by a newer version is refused, not opened read only.
2. The REST and MCP storage (`api_tokens`) exists but is unused until Plan 3.

## Done when

- Every task's tests pass in Release, with 0 warnings.
- `dotnet run --project src/OpenHisabKitab.Host` starts.
- The host serves `GetInfo` over TLS on 47250.
- The owner review list is updated.
