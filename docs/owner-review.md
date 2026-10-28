# Owner Review

Items that need the owner's check. Newest plan first. Mark an item done by deleting it or writing "OK" after it.

## Plan 2: Data and host

### Decisions made in the design for the REST API and MCP server (spec 4.1)

Each line says what was decided and what it costs if wrong.

1. **Local only.** The REST API and the MCP server listen on 127.0.0.1, and are off until the Owner turns them on. LAN access is a separate setting, also off. Cost if wrong: one setting. OK
2. **Amounts as text.** Amounts in REST and MCP are strings such as `"12345.60"`, never JSON numbers. Cost if wrong: client scripts must quote amounts. **Builder decided: keep text.** JavaScript and many JSON tools read numbers as floating point, so large amounts would lose paise. Text keeps every paisa and matches the no floating point rule. Built in Plan 3.
3. **MCP write tools are off by default.** They need both a setting and a read and write token. Cost if wrong: one setting. OK
4. **API tokens** are per user and carry that user's permissions. They are read only unless created as read and write. Cost if wrong: the token screen changes in Plan 3. OK
5. **Stdio bridge.** A small bridge program ships for assistants that only launch local programs. Cost if wrong: one small program. OK
6. **Plans renumbered.** REST and MCP are Plan 3, the client shell is Plan 4, and so on. The table is in the Plan 2 document. OK

### Decisions made while building Plan 2

1. **First setup.** The first Owner can only be set up from the host PC itself. Cost if wrong: a remote first setup is not possible. Ok
2. **Passwords** use PBKDF2 SHA-256 with 600,000 iterations. After 5 failed logins for a user name, logins wait 30 seconds, doubling up to 15 minutes. Cost if wrong: settings to tune. Ok
3. **Sessions** end after 12 hours idle. A host restart logs everyone out. Cost if wrong: users log in more often. Ok
4. **Composition and unregistered companies** cannot enter tax on purchases. They enter the full amount at rate 0. Cost if wrong: input tax shows as cost (correct for them), but the tax breakup on purchases is not kept. Ok
5. **Export under LUT and SEZ without tax** are calculated with zero tax. The item's rate is still stored. Cost if wrong: a calculation change in Plan 7. Ok
6. **Tax split on purchases** uses the supplier's state (the party) against the place of supply. Sales use the company's state. Cost if wrong: wrong IGST or CGST and SGST on purchases. Ok
7. **Invoice lines** must use a sales ledger (income) or a purchase ledger (expense, or an asset other than cash and bank). Tax, round off, and party ledgers are refused. Cost if wrong: some ledgers cannot be picked on a line. Ok
8. **Default number series** are `INV`, `PUR`, `CN`, `DN`, `RCT`, `PAY`, `JV`, and `CON`, followed by `/{FY}/` and 5 digits. Cost if wrong: the defaults change. Existing numbers stay. Ok
9. **Editing a voucher's date** is allowed only within its financial year, because its number belongs to that year. Cost if wrong: users cancel and re-enter instead. Ok
10. **Data entry users** cannot cancel, even their own vouchers, because the role table gives them no cancel right. Cost if wrong: one permission. Ok
11. **A ledger with an opening balance** counts as used and cannot be deleted. Set the opening to zero first. Cost if wrong: one extra step. Ok
12. **The gRPC port** is 47250. Ok
13. **Backups** run when the newest backup is 24 hours old, checked every minute, so a PC that was off at night still backs up when it starts. A failed scheduled backup is recorded in the host audit trail. Ok
14. **The host audit trail and host.db backups** are for the Owner only, because host.db lists every user and company. Accountants see their companies' audit trails. Ok
15. **Restore** accepts only a backup from that company's backup list, logs everyone out, and backs up the current file first. That copy is named `prerestore` and is never deleted by backup pruning, because it holds any audit records made after the chosen backup. The restore is recorded in host.db too. Ok
18. **The data folder** is open only to the system, administrators, and the account the host runs as. It holds the books, password hashes, and the TLS key. Ok
19. **Income and expense ledgers** take no opening balance (spec 5.2, invariant 6). Ok
20. **Documents received from suppliers** must be of kind Regular. Export and SEZ kinds apply only to this business's own sales. Ok
16. **The host runs only on Windows** (`net10.0-windows`), because it protects its certificate key with Windows DPAPI. Ok
17. **Moved to later plans.** Bill by bill allocation and party opening bills move to Plan 5. Custom roles and host.db restore move to Plan 8. Plan 2 restore covers company files. Ok

### Questions from the final review

1. **Composition companies and reverse charge.** A composition company cannot enter tax on a purchase, so reverse charge tax it owes cannot be recorded through a purchase invoice. It needs a journal entry. Should purchases under reverse charge be allowed for composition companies? **Builder decided: yes, done.** A composition company owes reverse charge tax at the normal rates (CGST Act section 9(3) and 9(4)) but takes no input credit. The tax is credited to the RCM Payable ledgers and added to the line's cost. Other purchases stay at rate 0.
2. **Data entry and masters.** Data entry users can set opening balances, rename required ledgers, and delete unused ledgers, because spec 8 gives them "masters". Should opening balances and required ledgers be limited to Accountant and Owner? Yes. **Done:** new permission for openings and renaming required ledgers, held by Owner and Accountant only.
3. **Accountants and companies.** Accountants can create companies. Spec 8 does not say. Confirm. Yes. **Done:** written in the spec 8 role table.

### Known limits carried to later plans

1. **Audit trail tampering.** The hash chain shows an edited or removed row, except the newest rows: deleting the last few rows leaves a valid chain. Someone who can write the database file can also rebuild the whole chain. Closing this needs a key kept outside the file, or a copy of the chain head kept elsewhere. Owner to decide whether version 1 needs it. Yes. **Done:** host.db keeps each company's newest audit record (its anchor), so removing the newest records or rebuilding a company's chain now fails verification. What is still open is listed below.
2. **Newer files.** A database written by a newer version of the software is refused, not opened read only. **Builder decided: keep refusing.** An older version reading a newer file could skip columns or voucher kinds it does not know and show wrong totals with no warning. The message tells the user to install the newer version.
3. **REST and MCP storage.** The `api_tokens` table exists but is unused until Plan 3. Ok
4. **Cess rates are not checked.** The GST data file has no cess rates, so any cess rate an item or invoice line gives is accepted. Adding checked cess rates is GST data work for Plan 7, and the rates need your confirmation. Ok
5. **Company saves wait on a server thread.** Masters, company, and lock changes wait for the company's write lock without freeing the thread. Harmless at office scale (a wait of milliseconds per save); worth changing if many users ever save at once. Account for many users saving at once. **Done:** every company save waits for its lock without holding a thread, and so does the host.db anchor write each save makes.
6. **Report speed.** A full year trial balance over 1,000,000 posting lines, rebuilt after a save, took 360 ms on the build PC (target under 1 s). Ok

### Decisions made while applying the owner's review

Each line says what was decided and what it costs if wrong.

1. **Unregistered companies and reverse charge.** They still enter reverse charge purchases at rate 0. A business liable to pay reverse charge must register (CGST Act section 24), so a liable unregistered business should change its registration first. Cost if wrong: one more case in the purchase rules.
2. **Data entry can still delete unused ledgers** they or others made, and add and rename ordinary ledgers. Required ledgers can never be deleted by anyone. Your answer covered openings and required ledgers only. Cost if wrong: one permission check.
3. **A company with no anchor fails verification.** Every company gets its anchor when it is created, so a missing anchor means the anchor row was removed. No companies exist yet from before this change. Cost if wrong: companies made by an older build would need a one time anchor.
4. **If host.db cannot take an anchor after a save,** the save still stands and the anchor stays one record behind until the next save of that company. Verification accepts an anchor that is behind. Cost if wrong: a short window where removing the newest record is not caught.

### Known limits after applying the owner's review

1. **Someone who can edit both files** (a company file and host.db, both in the data folder, which only administrators and the host account can open) can still hide a change by rewriting the chain and its anchor together. Closing this needs a copy kept off the PC.
2. **host.db's own audit trail has no anchor.** Removing its newest records is still not caught.
3. **Logins, user changes, and company creation** still wait for the host.db write lock while holding a thread. These are rare compared with saves; worth changing with the REST API in Plan 3 if needed.

## Plan 1: Foundation and accounting core

### Check before the first public release

1. **GST state codes.** `src/OpenHisabKitab.Core/Gst/gst-data.json` was checked against secondary lists, not the GST portal. Confirm every code, name, and type (State, union territory with or without legislature).
2. **GST rate slabs.** The same file keeps 12% and 28% valid with no end date after Notification 9/2025, and allows 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, 28, and 40 percent (40% from 22-09-2025). Confirm with a tax professional.
3. **E-invoice schema version** recorded as 1.1. Confirm on the e-invoice portal before Plan 6.

### Decisions made during the build

Each line says what was decided and what it costs if wrong.

1. **Reverse charge** tax goes to separate RCM Payable ledgers. Owner decided. Cost if wrong: a ledger change before data exists. Ok
2. **Tax inclusive prices.** The taxable value is nudged by up to 1 paisa so the line total equals the typed amount. When no nudge works, the line total can differ from the typed amount by 1 paisa, and screens must show the calculated total. Cost if wrong: a 1 paisa difference on some tax inclusive lines. Ok
3. **Credit and debit notes** follow GST meaning: a credit note reduces and a debit note increases the original, for either side. A purchase return is the supplier's credit note. Cost if wrong: screen labels in Plan 4. Ok
4. **Amounts with commas** must use Indian (12,34,567) or Western (1,234,567) grouping. `12,50` is rejected instead of being read as 1,250.00. Cost if wrong: some unusual typing is refused. Ok
5. **Amounts too large to store** stop with an error instead of wrapping to a wrong total. Cost if wrong: the host must show the error clearly (Plan 2). Ok
6. **Invisible characters** pasted into names (such as the zero width space) are removed. Cost if wrong: none expected. Ok
7. **State code 96** (other country) cannot be a supplier. Places of supply 96 and 97 always take IGST. Cost if wrong: an unusual registration is refused. Ok
8. **Typed years** must be from 1900 to 9998. Cost if wrong: none expected. Ok
9. **Bank Overdraft** group added under Short Term Borrowings. Its ledgers count as bank in contra vouchers. Cost if wrong: one extra default group. Ok
10. **Default chart of accounts** follows Schedule III headings. See `DefaultChart.cs`. Cost if wrong: renaming groups. Ok

### Known limits carried to later plans

1. **Report speed.** Building the report engine for 1,000,000 posting lines takes about 0.55 s. Plan 2 must keep it in memory and update it, not rebuild it for every report. Ok
2. **An empty date range value** (`default(DateRange)`) bypasses the start and end check. Code must always use the `bool` result of `PeriodInput.TryParse`. Plan 2 and Plan 3 code reviews should check this. Ok

## Where else to look

- **Design and every decision label** ([Owner], [Designer], [Builder]): `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`
- **Plan 1 tasks and tests**: `docs/superpowers/plans/2026-10-26-plan-1-foundation-and-core.md`
