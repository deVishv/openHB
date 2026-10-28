# Open Hisab Kitab: Version 1 Design

Date: 26-10-2026
Status: Draft for owner review

Every decision below is marked with who made it:

- **[Owner]** means the project owner decided it.
- **[Designer]** means it was the designer's call. The owner can overrule it.
- **[Builder]** means it is left to whoever builds it.

## 1. Intent

Open Hisab Kitab is open source accounting software for Indian businesses. It should suit any business, from a single shop to a multi-user office. [Owner]

It does not imitate existing accounting packages. Instead it borrows the working style of the Bloomberg Terminal: everything is reached by typing a four letter function into a command prompt, and each function opens in a tab. [Owner] The aim is the fastest, most logical keyboard driven accounting tool available, with the visual calm of Windows 98. [Owner]

The owner's biggest fear is wrong totals or wrong calculations in reports. Correctness outranks every other goal, including speed and feature count. [Owner]

## 2. Goals, non-goals, anti-goals

### Goals

- **Complete books.** Double entry bookkeeping with every standard voucher type, plus the full set of financial reports. [Owner]
- **GST, file based.** GST invoicing, plus GST return data produced as files the user uploads to government portals. [Owner]
- **Shared access.** Several users on an office network work on the same books at the same time. [Owner]
- **Keyboard first.** Every action can be done from the keyboard, and every shortcut can be changed. [Owner]
- **Deep settings.** Settings work like VS Code: layered, searchable, and editable either as a form or as JSON. [Owner]
- **Accessible.** Font family, sizes, text colours and background colours all meet accessibility standards. [Owner]
- **Concise text.** All on-screen text is clear and short, with no filler. [Owner]
- **Local REST API and local MCP server.** Scripts, other local software, and AI assistants can read the books and, where allowed, enter data, under the same rules and permissions as the app. See section 4.1. [Owner asked for both; Designer set the details]
- **Fast, reliable, bug free.** [Owner]

### Non-goals for version 1

These are not rejected, only deferred to later releases, each with its own design. [Owner, after discussion]

- Stock tracking and inventory valuation. Items exist for invoicing, but quantities on hand are not tracked.
- TDS and TCS.
- Bank reconciliation and statement import.
- Payroll.
- E-way bills.
- Cost centres, budgets, foreign currency.
- Direct connection to any government portal (GST, IRP, e-way bill). File export and import only. [Owner]
- Access over the internet or from outside the office network.
- macOS and Linux. Windows only. [Owner]
- Languages other than English. All screen text lives in resource files so translations can be added later without code changes. [Designer]

### Anti-goals: failures even if everything else works

- Any report total that disagrees with another report, or with a hand calculation. [Owner]
- Silent data loss or corruption.
- A posted voucher that can be changed or removed without leaving a trace in the audit trail.
- Any task that cannot be completed without a mouse.
- Decorative, promotional, or explanatory filler text on screens.
- An em dash (U+2014) or en dash (U+2013) anywhere in the software or its documentation. [Owner]

## 3. Constraints

- **Language and runtime.** C# on .NET 10. The development machine has SDK 10.0.402. [Owner chose C#; Designer chose .NET 10]
- **Platform.** Windows 10 and Windows 11. [Owner]
- **Licence.** MIT. [Owner]
- **Hosting.** The source code is published on GitHub. [Owner]
- **No running cost to the owner.** No paid services, no GST Suvidha Provider (the paid intermediary for portal APIs), no cloud servers. [Owner]

## 4. Architecture [Designer]

```
 Office PC 1          Office PC 2          Host PC (may also be a user's PC)
+-------------+      +-------------+      +-------------------------------+
| WPF client  |      | WPF client  |      | WPF client                    |
+------+------+      +------+------+      +---------------+---------------+
       |   gRPC over TLS (LAN)   |                         |
       +------------+------------+-------------------------+
                    |
          +---------v-----------------------------------+
          | Host service (Windows service)              |
          |  auth, permissions, validation, numbering,  |
          |  posting, audit trail, reports, backups     |
          |  local REST API and MCP (127.0.0.1)  <------+-- scripts, AI assistants
          +---------+-----------------------------------+     on the host PC
                    |
          +---------v-----------------------------------+
          | SQLite: one file per company + host.db      |
          +---------------------------------------------+
```

### Host service

- A Windows service that starts with the PC. It is the only process that ever opens the database files.
- It enforces every accounting rule. A client is never trusted to calculate totals, tax, or voucher numbers that get stored. A client may calculate them for display, using the same `Core` library, but the host calculates them again before saving.
- A single user installs the client and the host on the same PC. Shared use is the same setup: the other PCs install only the client and point it at the host. Solo and shared use run the same code path.

### Transport

- gRPC over HTTP/2 with TLS on a configurable port (default left to the builder).
- On first run the host generates a self signed certificate. The first time a client connects, it shows the certificate fingerprint and stores it once accepted (trust on first use). After that, any change of certificate blocks the connection until the user accepts it again.
- A server stream pushes change notifications to clients. When data changes under an open report, the report's tab shows a "Changed" marker in its status bar and F5 (`RFSH`) reloads it. Whether reports reload automatically is a setting, off by default.
- The installer adds the Windows Firewall rule for the host's gRPC port only.

### Application services [Designer]

- Inside the host, every operation (log in, save a voucher, run a report, lock a period, and so on) is one method on an application service. The service checks permissions, validates with `Core`, writes, and audits.
- gRPC, REST, and MCP are thin adapters over these services. No adapter contains accounting rules, permission checks, or SQL. A rule is enforced once, whichever door a request comes through.

### Storage

- **One SQLite file per company,** plus `host.db` for users, roles, user settings, and the company list. WAL mode and foreign keys are on.
- **Money** is stored as 64 bit integers in paise. Quantities are stored as integers in thousandths of a unit. Rates are stored as integers in hundredths of a percent, so 18% is stored as 1800. No floating point anywhere in money, tax, or quantity logic.
- **Concurrency.** Writes go through the host one transaction at a time. Masters and vouchers carry a row version. If two users edit the same record, the second save is rejected and that user sees the current version.
- **Migrations.** Schema changes run as versioned migrations. Before migrating, the host takes an automatic backup.

### Projects [Designer; folder names are Builder]

| Project | Purpose |
|---|---|
| `Core` | Pure domain logic: money, GST calculation, voucher validation, posting, report aggregation, Indian number formatting, command parsing. No I/O. Most tests target this. |
| `Data` | SQLite access and migrations. |
| `Contracts` | gRPC `.proto` definitions. |
| `Host` | The Windows service, including the application services and the gRPC, REST, and MCP adapters. |
| `McpBridge` | The stdio to HTTP bridge for MCP clients that only launch local programs. |
| `Client` | The WPF application. |
| `Tests.*` | One test project per production project. |

## 4.1 Local REST API and MCP server [Owner asked for both; Designer set the details]

### Purpose

- The **REST API** lets the owner's own scripts and other software on the host PC read and enter accounting data.
- The **MCP server** lets an AI assistant (such as Claude) on the host PC answer questions about the books and, if allowed, prepare entries.

### Where they run

- Both run inside the host service process, on one HTTP port separate from the gRPC port. The host stays the only process that opens the database files.
- **Local means loopback.** By default the port listens on 127.0.0.1 only, so only programs on the host PC can reach it. A setting, `api.allowLan`, off by default, also listens on the LAN, and then requires TLS with the host's certificate. Internet access stays a non-goal.
- The REST and MCP routes answer only on this port. The gRPC port answers 404 to them, and the API port does not serve gRPC.
- The API port is off until the Owner turns it on (`api.enabled`). The MCP endpoint has its own switch (`mcp.enabled`), and needs the API port on.

### Authentication

- Every request carries a bearer token. There is no cookie or session login, and no anonymous access, even on loopback, because web pages in a browser can send requests to loopback addresses.
- **API tokens** belong to a user. A user creates, names, and revokes their own tokens in `USER`. A token is shown once, when created. `host.db` stores only a hash.
- A token has the permissions of its user's role, including the Data entry limits, for each company the user can access. A token is either read only or read and write, chosen when it is created. Read only is the default.
- The host rejects any request whose `Origin` header is present and is not on the allowed list (empty by default). This blocks web pages and DNS rebinding.
- Repeated failed tokens from one address are slowed down.

### REST API

- Routes are versioned (`/api/v1/...`) and follow the host version rule in section 13: one host version serves one API version.
- An OpenAPI document is served at `/api/v1/openapi.json`.
- Resources in version 1: companies and financial years, groups and ledgers, parties, items, vouchers (list, read, create, edit, cancel), and every report in section 6.3 that exists in the build.
- **Amounts on the wire.** Money is a JSON string of rupees with exactly two decimals, such as `"12345.60"`, with a leading `-` for negative. It is never a JSON number, because many JSON readers turn numbers into floating point. Quantities use the same rule with three decimals, and rates with two (`"18.00"`). The host parses them with `Core`, and rejects any other form.
- **Dates** are `yyyy-mm-dd`.
- Each saved voucher returns its host assigned number. Edits send the row version they read. A stale row version gets HTTP 409 with the current record.
- Errors return one JSON shape with a code and a one line message, written to the same rules as on-screen text (section 6.7).

### MCP server

- MCP uses the Streamable HTTP transport at `/mcp` on the API port, and reports the host version.
- The installer also ships a small stdio bridge program for assistants that only launch local programs. It forwards to `/mcp` with a token from its configuration, and holds no data or rules itself.
- **Tools in version 1** cover: list companies, search ledgers and parties, read a ledger statement, run each report, find vouchers, and read one voucher.
- **Write tools** (create a voucher, create a party) exist but are off by default. They need `mcp.allowWrites` on and a read and write token. Every voucher created through MCP is audited like any other.
- Tool results carry amounts in the wire format above, plus the Indian formatted text, so the assistant quotes the same figures the screens show.

### Audit

- API and MCP calls that change data are audited like any other change (section 5.5). The record names the source (app, REST, or MCP) and the token name, in addition to the user.

## 5. Correctness rules

This section exists because wrong totals are the owner's main fear.

### 5.1 One source of truth for balances

- Every voucher produces posting lines, one per ledger per voucher, holding ledger, date, and a signed amount in paise.
- Every report (`TRLB`, `PNLS`, `BSHT`, `LEDG`, `DAYB`, `CFLO`, `OUTS`, and the GST reports) gets its figures from one shared aggregation component in `Core`, fed by those posting lines. No report has its own summing query.
- If a balance cache is added for speed, it is rebuilt from the posting lines and checked against them in tests.

### 5.2 Invariants

The host checks these. Tests check them on random and fixture data. A failure blocks saving, and in tests it fails the build.

1. Every voucher's debits equal its credits, to the paisa.
2. Trial balance total debits equal total credits, for any date range.
3. The balance sheet balances once the current period's profit or loss is included.
4. Net profit in `PNLS` equals the profit line in `BSHT` for the same period.
5. A ledger's closing balance in `LEDG` equals its balance in `TRLB` for the same date.
6. For each balance sheet ledger, the opening balance of a financial year equals the closing balance of the previous year. Profit and loss ledgers open at zero, and their net amount carries into a designated capital or reserves ledger.
7. On every GST invoice, CGST, SGST, UTGST, IGST, and cess totals equal the sum of their line amounts.
8. GSTR-1 and GSTR-3B totals for a period equal the totals of the vouchers that make them up.
9. Cash flow closing cash equals the cash and bank balances in `BSHT`.

### 5.3 GST calculation rule

- **Taxable value per line** = quantity times rate, minus line discount, rounded to the paisa, with halves rounded away from zero.
- **Tax per line** is calculated on that line's taxable value. Each component is rounded to the paisa, halves away from zero.
  - Intra state supply: CGST and SGST (or UTGST) are each calculated at half the rate, separately. Because both use the same base and the same half rate, they are always equal. No odd paisa ever needs to be split.
  - Inter state supply: IGST is calculated at the full rate.
- **Invoice tax totals** are the sums of the line amounts.
- **Invoice round off.** A setting, on by default, rounds the grand total to the nearest rupee, with 50 paise rounding up. The difference is posted to a Round Off ledger. Off means no rounding.
- **Supply type.** Place of supply versus the supplier's state decides intra state versus inter state. A supply to an SEZ (special economic zone) is always inter state. UTGST is used instead of SGST when the place of supply is a union territory without its own legislature.
- **Reverse charge** is a flag on purchase vouchers. The supplier is paid only the taxable value. The tax is debited to the input credit ledgers and credited to separate RCM Payable ledgers, because reverse charge tax must be paid in cash and cannot be set off against input credit. [Owner]
- **All tax rates, the state and union territory code list, and every threshold are data.** This includes the B2C large invoice limit, currently ₹1,00,000 for inter state supplies under Notification 12/2024 Central Tax. The data ships as a versioned JSON file with an effective date per entry and can be updated without a new release. None of it is hard-coded.

### 5.4 Voucher numbering

- Each voucher type has one or more series per financial year. A series is a prefix pattern plus a running number. For example `INV/26-27/` followed by `00001`.
- **GST documents** (`SALE`, `CRNT`, `DBNT`) follow Rule 46(b) of the CGST Rules:
  - At most 16 characters.
  - Only letters, digits, hyphen (`-`), and slash (`/`).
  - Unique and consecutive within the financial year and series.
  - The settings screen rejects any pattern that could exceed 16 characters.
  - The ASCII hyphen is allowed here because the law requires it. It is not an en or em dash.
- The host assigns the number inside the save transaction, so two users can never get the same number.
- **Posted vouchers are never deleted, only cancelled.** A cancelled voucher keeps its number, posts nothing, and appears as cancelled in the GSTR-1 document summary (Table 13).

### 5.5 Audit trail

This meets the requirement under the Companies (Accounts) Rules that the edit log cannot be switched off.

- Every create, edit, cancel, lock, unlock, and permission change on any voucher, master, or setting is recorded. Each record holds the user, PC name, source (app, REST, or MCP), token name when there is one, timestamp, and the before and after values.
- API token creation and revocation are recorded too.
- The log is append only. Each record includes a hash of the previous record, so a broken chain shows tampering. `AUDT` verifies the chain on demand and reports the result.
- After each audited company write, `host.db` keeps a copy of that company's newest record (its anchor). Verification also checks that the company file still holds the anchored record, so removing the newest records or rebuilding the whole chain shows. [Owner asked for this in version 1; Builder chose the anchor]
- There is no setting to disable it.

### 5.6 Period locks

`LOCK` locks the books up to a date. Locked vouchers cannot be created, edited, or cancelled. Only roles with the unlock permission can unlock, and every unlock is audited.

## 6. User interface

### 6.1 Main window [Owner chose layout; Designer filled details]

From top to bottom:

1. **Title bar.** `Open Hisab Kitab: <Company> FY <yyyy-yy>`.
2. **Menu bar.** File, Edit, View, Help, each with an Alt access key.
3. **Command prompt.** A `>` symbol followed by an input field. [Owner]
4. **Tab strip.** Each tab shows its number and its function, for example `2 LEDG HDFC Bank`. [Owner]
5. **Content pane.** The active tab's screen.
6. **F key row.** Twelve clickable buttons, F1 to F12. [Owner] The default assignments [Designer] are:

   | Key | Function |
   |---|---|
   | F1 | `HELP` |
   | F2 | `DATE` |
   | F3 | `FIND` |
   | F4 | `CMPY` |
   | F5 | `RFSH` |
   | F6 | `SALE` |
   | F7 | `PURC` |
   | F8 | `RCPT` |
   | F9 | `PYMT` |
   | F10 | `JRNL` |
   | F11 | `PRNT` |
   | F12 | `SETG` |

   Every key can be reassigned.
7. **Status bar.** Short context only, such as the balance or entry count, connection state, and the "Changed" marker.

The mockup lives in `.superpowers/brainstorm/` and is not committed. It is the visual reference for this layout.

### 6.2 Command grammar [Designer]

- **Form.** `CODE [arguments]`. Codes are not case sensitive. Enter runs the code in the current tab. Shift+Enter runs it in a new tab.
- **Missing or ambiguous arguments.** The function opens its own screen and asks for them there.
- **Dates.** Accepted forms are:
  - `dd-mm-yyyy`, `dd/mm/yyyy`, or `dd-mm-yy`.
  - `d-m` for the current financial year.
  - `today`.
- **Periods.** Accepted forms are:
  - A month name such as `apr`.
  - A quarter: `q1` to `q4`, using Indian financial year quarters starting in April.
  - `fy` for the current financial year.
  - `fy25` for financial year 2025-26.
  - A range written `from to`, for example `1-4 30-6`.
- **Names.** Ledger, party, and item names match:
  - first by exact name,
  - then by unique prefix,
  - then by words contained in the name, ignoring case.

  If more than one record matches, a pick list appears. If none match, the user is offered the option to create one.
- **Autocomplete.** Typing in the prompt shows a dropdown of matching codes with their plain names, for example `TRLB  Trial Balance`.
- **Unknown codes.** An unknown code shows one line in the status bar: `Unknown function: XXXX`. No dialog box.
- **Typing anywhere.** When no input field has focus, typed letters go into the prompt.

### 6.3 Functions in version 1

Function codes are [Designer]. The scope is [Owner], except `G2BR`, which is [Designer] and should be confirmed.

**System**

| Code | Name | Purpose |
|---|---|---|
| `HELP` | Help | Lists all functions and shortcuts. `HELP CODE` shows one function. |
| `SETG` | Settings | Searchable settings, as a form or as JSON. |
| `KEYS` | Keybindings | Searchable keybindings, as a form or as JSON. |
| `CMPY` | Company | Create, select, and edit companies and financial years. |
| `USER` | Users | Users, roles, and permissions. |
| `AUDT` | Audit trail | Filter and view the log, and verify the chain. |
| `LOCK` | Period lock | Lock or unlock the books up to a date. |
| `BKUP` | Backup | Back up now, restore, and set the backup schedule. |
| `DATE` | Working date | Set the default date for new vouchers and reports. |
| `FIND` | Find | Search vouchers by number, party, amount, or narration. |
| `RFSH` | Refresh | Reload the current tab. |
| `PRNT` | Print | Print the current tab or save it as PDF. |

**Masters**

| Code | Name | Purpose |
|---|---|---|
| `ACCT` | Accounts | Chart of accounts: groups and ledgers. |
| `PRTY` | Parties | Customers and suppliers, with GSTIN, state, addresses, and credit days. |
| `ITEM` | Items | Goods and services, with HSN/SAC code, GST rate, unit, and default price. |

**Vouchers**

| Code | Name |
|---|---|
| `SALE` | Sales invoice |
| `PURC` | Purchase invoice |
| `CRNT` | Credit note |
| `DBNT` | Debit note |
| `RCPT` | Receipt |
| `PYMT` | Payment |
| `JRNL` | Journal |
| `CNTR` | Contra |

**Reports**

| Code | Name | Purpose |
|---|---|---|
| `DAYB` | Day book | Vouchers by date. |
| `LEDG` | Ledger | One ledger's statement with running balance. |
| `TRLB` | Trial balance | |
| `PNLS` | Profit and loss | |
| `BSHT` | Balance sheet | Follows the Schedule III layout, with an option to show it grouped by the chart of accounts instead. |
| `CFLO` | Cash flow | Indirect method. |
| `OUTS` | Outstanding | Receivables and payables, bill by bill, with ageing buckets set in settings. |

Every report can drill down: Enter on a line opens the next level of detail, down to the voucher. Every report can be exported to CSV and PDF.

**GST**

| Code | Name | Purpose |
|---|---|---|
| `GST1` | GSTR-1 | Builds every GSTR-1 table from the vouchers and exports JSON in the GST portal's offline upload format. |
| `GS3B` | GSTR-3B | Summary by table, for entering on the portal. Exports to PDF and CSV. |
| `G2BR` | GSTR-2B match | Imports the GSTR-2B JSON downloaded from the portal and matches it against purchases. |
| `EINV` | E-invoice | Exports selected invoices as e-invoice JSON. Imports the IRP response file (IRN, acknowledgement number and date, signed QR code) and attaches it to each invoice. Printed invoices then show the IRN and QR code. |

GSTR-1 tables covered:

- B2B
- B2CL
- B2CS
- Credit and debit notes (registered and unregistered)
- Exports
- Nil rated, exempt, and non-GST supplies
- HSN summary
- Document summary

Advances (tables 11A and 11B) are out of scope for version 1.

`G2BR` puts each record into one of four buckets:

- matched
- amount mismatch
- in 2B but not in the books
- in the books but not in 2B

Matching uses the supplier GSTIN, a normalised invoice number, the date, and amounts within a tolerance set in settings.

### 6.4 Voucher entry

- **Grid entry.** Vouchers are entered in a keyboard grid. Tab and Enter move to the next field, and Shift+Tab moves back.
- **Ledgers and parties.** Ledger and party fields autocomplete. Creating a new party from inside a voucher opens `PRTY` in a sub screen and returns to the voucher afterwards.
- **Live totals.** Totals and tax update live in the footer. The difference between debit and credit is shown until it reaches zero.
- **Saving.** Ctrl+S saves, and the host returns the assigned voucher number. Esc on an unsaved voucher asks once before discarding.
- **Sales and purchase invoices** include party, place of supply (defaulted from the party), item lines, a reverse charge flag (purchases only), narration, and a reference to the original invoice (credit and debit notes only).

### 6.5 Keyboard

Every action has a shortcut, and every shortcut can be changed in `KEYS`, stored as `keybindings.json`. Bindings support VS Code style `when` conditions, such as "in a grid" or "in the prompt". [Owner asked for shortcuts and VS Code style settings; Designer chose the defaults]

| Keys | Action |
|---|---|
| Ctrl+L | Focus the prompt |
| Enter / Shift+Enter | Run in this tab / in a new tab |
| Esc | Cancel, or go back within the tab |
| Ctrl+T / Ctrl+W | New tab / close tab |
| Ctrl+Tab, Ctrl+Shift+Tab | Next tab / previous tab |
| Ctrl+1 to Ctrl+9 | Go to tab by number |
| Alt+Left / Alt+Right | Back / forward within the tab |
| Ctrl+S | Save |
| Ctrl+Shift+P | Search all functions and settings by name |
| Ctrl+F | Find within the current screen |
| F1 to F12 | F key row |

### 6.6 Visual design and accessibility [Owner set the goals; Designer set the numbers]

- **Look.** Windows 98: grey face, bevelled 3D borders, navy title bars and selection, square corners, and no animations.
- **Standard.** WCAG 2.2 AA is the minimum.
  - Text contrast is at least 4.5:1.
  - Control borders and focus indicators are at least 3:1.
- **Palette.** The base colours and their contrast ratios are:

  | Use | Colours | Contrast |
  |---|---|---|
  | Normal text | Black on `#C0C0C0` | about 11.5:1 |
  | Content areas | Black on white | 21:1 |
  | Selection and active title bar | White on `#000080` | about 16:1 |

- **Departures from Windows 98.** Two classic conventions fail the contrast standard, so they change:
  - **Disabled text.** The grey embossed disabled text is replaced by `#404040` text on `#C0C0C0` (about 5.7:1). Disabled state is also shown by other cues, not by colour alone.
  - **Inactive title bars and headers.** These use white on `#404040`, not white on `#808080`, which fails.
- **Focus.** A 2 pixel solid focus outline that contrasts at least 3:1 with its surroundings, replacing the classic thin dotted box.
- **Fonts.**
  - UI text: Segoe UI by default. Tahoma is offered in settings for a more period look.
  - Numbers and the prompt: Consolas, with tabular figures, so columns of amounts line up.
  - Default size: 14 px. Adjustable from 12 to 28 px in settings.
  - Windows text scaling and display scaling are respected.
- **High contrast.** When Windows High Contrast mode is on, the app switches to system colours.
- **Screen readers.** Every control has a UI Automation name, so screen readers can announce it.
- **Numbers.**
  - Amounts use Indian grouping, for example `12,34,567.89`.
  - Debit and credit are shown with `Dr` and `Cr`, not with colour alone.
  - Negative amounts are shown in brackets by default (a setting).
- **Dates.** Shown as `dd-mm-yyyy` by default (a setting).

### 6.7 Writing rules [Owner]

- Labels are nouns. Buttons are verbs. Messages state what happened and what to do, in one line where possible.
- No welcome screens, tips, marketing, or decorative text.
- No em dash or en dash characters anywhere. A CI check fails the build if U+2013 or U+2014 appears in source code, resource strings, or docs.

## 7. Settings [Owner asked for VS Code depth; Designer set the structure]

- **Layers,** each overriding the one above:
  1. Defaults, built in.
  2. Machine, in a local JSON file: window placement, font scaling, host address.
  3. Company, stored in the company database: numbering, round off, GST options, ageing buckets.
  4. User, stored in `host.db`, so a user's settings follow them to any PC.
- **Format.** Settings are dot named, for example `ui.fontSize` and `invoice.roundOff`. Each setting has a type, a default, a one line description, and allowed values.
- **Editing.** `SETG` shows a searchable list grouped by category and marks settings that differ from their defaults. Settings can also be edited as JSON with validation, and any setting can be reset to its default.
- **Keybindings** follow the same layering and are edited in `KEYS`.
- **Coverage.** Every behaviour described as "a setting" in this document must exist in the list.
- **Host settings,** stored in `host.db` and changed only by the Owner: `api.enabled` (off), `api.port`, `api.allowLan` (off), `api.allowedOrigins` (empty), `mcp.enabled` (off), `mcp.allowWrites` (off). See section 4.1.

## 8. Users and permissions [Designer]

- **Logins.** Users log in with a username and password. Passwords are hashed with PBKDF2 or Argon2 [Builder chooses].
- **Built-in roles:**

  | Role | Can do |
  |---|---|
  | Owner | Everything, including users, unlock, and restore. |
  | Accountant | All vouchers, reports, and GST. Can create companies. Cannot manage users or unlock. |
  | Data entry | Create and edit vouchers and masters, except opening balances and required ledgers (cash, tax, round off, and so on). Can view only their own vouchers, in `DAYB`. No financial or GST reports. |
  | Viewer | Read only. |

- **Custom roles** are built from a permission checklist covering each function, plus create, edit, cancel, export, and unlock rights.
- **Company access.** Each user has access to a chosen set of companies.
- **API tokens** act as their user, limited further to read only unless created as read and write. See section 4.1.

## 9. Backup [Designer]

- The host makes scheduled backups using SQLite's online backup. The default is daily, keeping 30 copies, and the destination folder is a setting.
- A backup is also taken before every restore and every migration.
- Only the Owner can restore. A restore disconnects all clients first.
- Backups are checked after writing with an integrity check, and each one is recorded in the audit trail.

## 10. Performance targets [Designer]

These are measured on a mid range office PC with 1,000,000 posting lines.

| Action | Target |
|---|---|
| Client start to usable prompt | Under 2 s |
| Command to rendered screen, for masters and vouchers | Under 150 ms |
| `TRLB`, `PNLS`, or `BSHT` for a full year | Under 1 s |
| Grid scrolling | Smooth (virtualised rows) |
| Saving a voucher on the LAN | Under 200 ms |

## 11. Quality and testing [Designer]

- **Unit tests** cover every `Core` rule.
- **Property based tests** generate random valid voucher sets and assert every invariant in section 5.2.
- **Golden fixtures.** A set of example companies with hand calculated expected reports and GST returns. The figures are checked by hand, not produced by the software.
- **Integration tests** run the host against real SQLite: concurrent saves, numbering under contention, row version conflicts, migrations, backup and restore.
- **GST file tests.** Exported GSTR-1 and e-invoice JSON is validated against the official schemas.
- **UI smoke tests** use FlaUI to check keyboard only flows for every function.
- **Accessibility check.** Every UI Automation element has a name, and contrast is checked for the theme colours.
- **CI.** GitHub Actions on Windows runs all of the above plus the dash check on every push and pull request.

## 12. GitHub and distribution [Owner chose GitHub and MIT; Designer the rest]

- The repository includes:
  - `README.md`
  - `LICENSE` (MIT)
  - `CONTRIBUTING.md`
  - issue templates
  - the CI workflow
- **Releases** ship a Windows installer containing the client and an optional host service. [Builder chooses the installer technology]

## 13. Further decisions [Designer, settled during review]

### Companies and GST registration

- **One company per GSTIN.** A business registered in several states creates one company per registration. Consolidated reporting across companies is a later feature.
- **Financial year** is fixed at April to March, as Indian law requires.
- **GST registration types.** A company's registration type is Regular, Composition, or Unregistered.
  - Composition companies issue bills of supply with no tax, and the GST functions other than `GST1` document summary are disabled. Composition returns (CMP-08, GSTR-4) are out of scope.
  - Unregistered companies keep books with GST functions hidden.
- **Sales invoice types:**
  - Regular
  - Export with tax paid
  - Export under LUT (letter of undertaking, no tax paid)
  - SEZ with tax paid
  - SEZ without tax paid
  - Deemed export

  Export invoices add a port code, shipping bill number, and shipping bill date.
- **Tax inclusive prices.** Item prices may be entered tax inclusive (a setting). The taxable value is then derived per line, and the section 5.3 rounding still applies to the derived figures.

### Setting up and connecting

- **Default chart of accounts.** New companies start with a standard set of groups that maps to the Schedule III balance sheet and profit and loss headings, plus required ledgers: Cash, Round Off, Profit and Loss, the GST input, output, and RCM Payable ledgers for each tax type, and Capital. Users may add groups and ledgers. They may rename but not delete the required ones.
- **Opening balances.** `ACCT` takes an opening balance per ledger as at the start of the first financial year in the software. `PRTY` takes opening bills (number, date, amount, due date) so that `OUTS` is correct from day one. The opening trial balance must balance before any voucher can be saved. Otherwise an "Opening difference" line is shown and blocks saving.
- **First run.** The host's first start creates the Owner account. Clients ask for the host address (name or IP and port) on first run and store it in machine settings.
- **Version match.** Client and host must run the same version. A mismatched client shows one line saying which version to install, and does not connect.

### Vouchers and invoices

- **Bill by bill settlement.** Receipt, payment, credit note, and debit note lines for a party are allocated to that party's open bills. The suggestion is oldest first, and the allocation can be changed. Unallocated amounts are kept "on account". `OUTS` uses these allocations.
- **Editing posted vouchers.** Posted vouchers can be edited until their date is locked. Each edit is audited with the before and after values. Voucher number, voucher type, and series cannot be changed by an edit.
- **Printed invoices** carry every particular Rule 46 requires. There is one built-in layout, with settings for:
  - logo
  - bank details
  - terms
  - signatory
  - which optional columns to show

  Custom layouts are a later feature.

### Permissions

- **Data entry role.** This role can open `DAYB` filtered to vouchers the user created. It cannot open financial or GST reports.

## 14. Left to the builder

- Project and folder names, gRPC message shapes, and the database schema details.
- REST route shapes beyond section 4.1, MCP tool names and descriptions, the token format, and the API port number.
- The default network port.
- The installer technology.
- The internal design of the WPF screens, as long as sections 6 and 7 hold.
- Which balance caching, if any, is needed to meet the performance targets, as long as section 5.1 holds.
- The order of building.

The builder must also:

- Get the current GSTR-1 offline upload JSON format, GSTR-2B JSON format, and e-invoice schema (NIC/IRP version 1.1 at the time of writing) from the official GSTN and IRP sources. Keep them in the repository as test fixtures, and record their versions in the GST data file.
- Check the current GST rate slabs, the state code list, and the B2CL threshold against official sources when building the GST data file.
