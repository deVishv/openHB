# Plan 6b: Printing, PDF, and Printed Invoices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PRNT` prints the current tab, or saves it as PDF. Three kinds of tab print:
- every report, in the layout the screen shows;
- the GST documents this business issues (`SALE`, and `CRNT` and `DBNT` when issued), with every particular Rules 46 and 53 require and the copies Rule 48 asks for;
- every other voucher, as a plain voucher slip.

**Architecture:**
- **Core** gains:
  - amounts in words;
  - the legal wording of printed documents;
  - a check for missing Rule 46 and 53 particulars;
  - a check for the logo file;
  - the ₹50,000 threshold for unregistered buyers, as data;
  - the invoice print settings.
- **Data** gains company migration `0004`. It adds the invoice's Ship to fields, the buyer's and the company's details as saved, and the company logo table.
- **Host** fills the saved details on every invoice save, checks Ship to, and stores the logo. The gRPC, REST, and MCP invoice shapes carry Ship to and the saved details.
- **Client.Model** gains `Printing/`:
  - a page model that knows no renderer (positioned text, rules, and images, in points);
  - the report, invoice, and slip layouts that fill it;
  - the PDF writer on PDFsharp's Core build.
  
  Screens that print implement `IPrintable`. `PRNT` is handled by the shell, as `RFSH` is, so it never opens a tab.
- **Client (WPF)** gains a `DocumentPaginator` that draws the same page model for the printer, plus the save and print dialogs.

**Tech Stack:** As in Plan 6: .NET 10, WPF, `CommunityToolkit.Mvvm` 8.4.2, gRPC, SQLite, `ModelContextProtocol.AspNetCore` 2.3.0, xUnit 2.9.3, and `FlaUI.UIA3` 5.0.0. New packages:
- `PDFsharp` 6.2.4, pinned. It is the last MIT version; the owner chose it on 2026-11-02.
- `System.Security.Cryptography.Pkcs` 10.0.14, referenced directly. PDFsharp 6.2.4 brings in 8.0.1, which fails the NuGet audit (GHSA-846f-f764-76mx, high), and warnings are errors.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. These sections apply:
- 4.1 (REST and MCP invoice shapes; amounts on the wire);
- 5.1 (printed figures are the stored ones);
- 5.3 (the stored tax figures and round off);
- 5.4 (Rule 46(b) document numbers);
- 6.1 (F11 is `PRNT`);
- 6.3 (`PRNT`: "Print the current tab or save it as PDF"; "Every report can be exported to CSV and PDF");
- 6.5 and 6.6 (keys, fonts, Indian grouping, Dr and Cr);
- 7 (settings), and 8 (the export right);
- 13 ("Printed invoices carry every particular Rule 46 requires", with the five settings; and the export invoice fields).

**Scope, decided by the owner on 2026-11-03:**
- **In:**
  - `PRNT` and `PRNT pdf` for report tabs and voucher tabs;
  - the invoice layout for issued GST documents;
  - voucher slips for `PURC`, `RCPT`, `PYMT`, `JRNL`, `CNTR`, and received notes;
  - an optional Ship to on issued invoices and notes;
  - the buyer's and company's details saved on each invoice.
- **Out:**
  - IRN and the signed QR code, which come with `EINV` in Plan 7;
  - custom layouts, which spec 13 calls a later feature;
  - printing masters or settings screens.

**Owner approvals on 2026-11-03:**
- Plan 6b owner review items 1 to 5 are approved as recommended:
  - an invoice missing a required particular is refused, with one line saying what to add;
  - `invoice.copies` defaults to `byRule`, and a PDF holds the original only;
  - a PDF with text its fonts cannot show is refused, with the Microsoft Print to PDF hint;
  - a logo change is audited as its hash and size;
  - report printing needs `Export`, and voucher printing needs only the right to open the voucher.
- Execution is **native**: one session builds every task with superpowers:executing-plans, then one fresh reviewer on the most capable model checks the whole branch.
- The plan docs are committed to `main` before the branch.

**Roadmap as it now stands.**

| Plan | Covers |
|---|---|
| 6b | `PRNT`, PDF, printed invoices, Ship to (this plan) |
| 7 | GST files: `GST1`, `GS3B`, `G2BR`, `EINV` (IRN and QR on the printed invoice); checked cess rates |
| 8 | Custom roles, host.db restore, installer, release |

## Global Constraints

- **Earlier rules still hold.** Everything in the Global Constraints of Plans 2 to 6 still holds:
  - build settings and central package versions;
  - money as `long` paise;
  - `TimeProvider`;
  - one line messages;
  - no WPF in `Client.Model`;
  - all screen text in `Resources/Text.resx`;
  - `{t:Txt}` and `{t:Name}` in XAML;
  - UI tests behind `OHK_UI_TESTS=1`;
  - paging rules;
  - every screen loads in at most three host calls.
- **Branch.** All work happens on the branch `plan-6b-printing`, made from `main` after the plan docs are committed there.
  - Nothing is pushed or merged until the owner says so.
  - `/security-review` runs before merging.
- **Messages** in this plan are exact text.
- **Printed figures are the host's stored figures.** A layout never recalculates tax, taxable value, or round off. It adds the stored line amounts for totals, and a test ties each total to the voucher's postings.
- **Print what is stored.** A voucher prints from a fresh `Vouchers.Get`, never from the screen's fields.
- **Text on printed documents.**
  - Labels ("Invoice no.", "Bill to") are in `Text.resx`, as screen text is.
  - The legal wording (titles, endorsements, copy marks, the composition line) is in Core `InvoiceWording`, beside the tax rules. Each string there is quoted from the rule it serves.
- **Pages.** A4, 595.28 by 841.89 points, with margins of 36 points. Landscape is used only when a report's columns do not fit portrait.
- **Fonts.**
  - Text is Segoe UI, regular and bold. Numbers are Consolas, regular and bold.
  - The PDF embeds them from the Windows fonts folder.
  - Text is measured once, through PDFsharp, and both renderers draw at the measured positions.
- **Amounts on paper.**
  - Reports keep the screen's text: Indian grouping, the `ui.negativeAmounts` style, and Dr and Cr.
  - Invoices and slips use Indian grouping with a leading minus (`IndianNumberFormat.Format(m, NegativeStyle.Minus)`).
  - Dates on invoices and slips are always `dd-mm-yyyy`. Reports follow `ui.dateFormat`.
- **Permissions.**
  - Printing or saving a report as PDF needs `Export`, as CSV does.
  - Printing a voucher needs only the right to open it, so Data entry prints the bills they make.
  - Setting the logo needs `ManageCompanies`.

## Rule 46 and Rule 53 checklist

These particulars were checked against the CBIC text on 2026-11-03: taxinformation.cbic.gov.in, CGST Rules, Rules 46, 48, 49, and 53.
- A bill of supply follows Rule 49: (a) to (c) as 46, (d) buyer details only for a registered buyer, (e) HSN, (f) description, (g) value, (h) signature, and Rule 46's provisos.
- An Unregistered company is not a registered person. None of these rules apply to its `Invoice`, so nothing is refused. Each row says where the value comes from and which test pins it. Task 6 owns every test unless another task is named.

| Particular | Source | Test |
|---|---|---|
| 46(a), 53(a): supplier name, address, GSTIN | `company_details` saved on the invoice (Task 2), else the profile | `Reprint_shows_details_as_saved_after_party_changes` |
| 46(b), 53(c): serial number of up to 16 characters | `display_number` (Plan 5 checks the pattern) | `Intra_state_invoice_shows_cgst_sgst_and_totals_equal_the_party_posting` |
| 46(c), 53(d): date | voucher date | same |
| 53(b): nature of the document | `InvoiceWording.Title` | `Credit_note_shows_original_invoice` |
| 46(d), 53(e): registered buyer's name, address, GSTIN | `party_details` saved on the invoice | `Reprint_shows_details_as_saved_after_party_changes` |
| 46(e), 53(f): unregistered buyer's name, address, delivery address, state name and code. For 46(e), only from ₹50,000 taxable value | `party_details`, Ship to, and the threshold in `gst-data.json` | `Unregistered_buyer_at_fifty_thousand_without_address_is_refused` (Task 1 and here) |
| 46(g): HSN | stored line `hsn_sac` | `Optional_columns_hide_price_and_discount_but_never_hsn_or_quantity` |
| 46(h): description | stored line | `Line_without_description_is_refused` (Task 1) |
| 46(i): quantity and unit | stored quantity, plus the item's unit | `Optional_columns_hide_price_and_discount_but_never_hsn_or_quantity` |
| 46(j), (k), 53(h): total value, and taxable value after discount | stored line `taxable_paise`, and the sums | `Round_off_and_tax_inclusive_prices_use_stored_figures` |
| 46(l), (m), 53(h): rate and amount of each tax | stored line rate and amounts | intra-state, inter-state, and union territory tests |
| 46(n): place of supply with the state name | header `place_of_supply`, and `GstData.State` | `Inter_state_invoice_shows_igst` |
| 46(o): delivery address when it differs | Ship to (Task 2) | `Export_under_lut_shows_endorsement_port_and_country` |
| 46(p): whether tax is payable on reverse charge | always `No` for issued documents, since outward reverse charge is not modelled | `Intra_state_invoice_shows_cgst_sgst_and_totals_equal_the_party_posting` |
| 46(q), 53(i): signature | the `For {company}` box and `invoice.signatory` | `Logo_bank_details_and_signatory_print_from_settings` |
| 46(r): QR code with IRN | **Plan 7** (`EINV`) | none in this plan |
| 53(g): the original invoice's number and date | header `original_number` and `original_date` | `Credit_note_shows_original_invoice`, `Note_without_original_invoice_is_refused` (Task 1) |
| Export proviso: the endorsement, recipient, delivery address, and country of destination | `InvoiceWording.Endorsement`, and Ship to with its country | `Export_under_lut_shows_endorsement_port_and_country` |
| Rule 48: copy marks | `InvoiceWording.CopyLabels` | `Goods_print_three_copies_services_two_and_pdf_one` |
| Composition: bill of supply, with the composition line | `InvoiceWording` | `Composition_company_prints_bill_of_supply_without_tax_columns` |

## Review Focus

These failure modes are implied by the spec but not exercised by any example. Each has a test in the task named.

1. **A print that differs from the books.**
   - The screen is stale because another user edited the voucher.
   - The screen holds unsaved edits.
   - A layout recomputes a figure and rounds it differently.
   
   In each case, the printed totals must equal the stored voucher and its postings, or the print is refused. Tests in Tasks 6 and 8.
2. **Pages that lose or split lines.**
   - A 600-line `LEDG` prints every line once, in order, with the column headers on every page and `Page n of m`.
   - A row is never cut across pages.
   - A 60-line invoice continues onto a second page and puts its totals only on the last.
   - Tests in Tasks 5 and 6.
3. **Long and unusual text.**
   - A 300-character address, or a ledger name wider than its column, wraps inside its box and never overlaps another box or leaves the margins.
   - A newline in the bank details starts a new line.
   - A Hindi or Gujarati name is refused for PDF with a hint, never drawn as empty boxes.
   - Tests in Tasks 3, 5, 6, and 8.
4. **An invoice that lacks a required particular.**
   - An unregistered buyer at ₹50,000 with no address.
   - A note with no original invoice.
   - An export with no country of destination.
   - Settings that try to hide HSN or quantity.
   
   Each is refused with its one line, or the column cannot be hidden. Tests in Tasks 1 and 6.
5. **Permissions on every print path.**
   - Viewer and Data entry cannot print reports. Data entry prints their own invoice.
   - Another user's voucher stays refused by the host.
   - Only `ManageCompanies` sets the logo.
   - Tests in Tasks 4, 5, and 8.

---

### Task 1: Core: amounts in words, document wording, particulars, logo check, settings

**Files:**
- Create:
  - `src/OpenHisabKitab.Core/Formatting/AmountInWords.cs`
  - `src/OpenHisabKitab.Core/Gst/InvoiceWording.cs`, `InvoiceParties.cs`, `InvoiceParticulars.cs`
  - `src/OpenHisabKitab.Core/Formatting/LogoImage.cs`
- Modify:
  - `src/OpenHisabKitab.Core/Gst/gst-data.json` (a source row, plus the threshold `unregisteredDetailsValue`, 5000000 paise from 2017-07-01, open ended)
  - `GstData.cs`
  - `src/OpenHisabKitab.Core/Settings/SettingsCatalog.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Formatting/AmountInWordsTests.cs`, `LogoImageTests.cs`, `tests/OpenHisabKitab.Core.Tests/Gst/InvoiceWordingTests.cs`, `InvoiceParticularsTests.cs`, `tests/OpenHisabKitab.Core.Tests/Settings/SettingsCatalogTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class AmountInWords { public static string Rupees(Money m); }          // throws ArgumentOutOfRangeException below zero
public sealed record DocumentParty(string Name, string? Gstin, string Address, StateCode State);
public sealed record ShipTo(string Name, string Address, StateCode? State, string? Country);
public static class InvoiceWording
{
    public static string Title(VoucherType type, GstRegistration registration, InvoiceKind kind, bool everyLineTaxFree);
    public const string CompositionLine = "Composition taxable person, not eligible to collect tax on supplies";
    public static string? Endorsement(InvoiceKind kind);
    public static IReadOnlyList<string> CopyLabels(bool anyGoods, string copiesSetting);   // "byRule" or "original"
    public static bool IsService(string hsnSac);                                          // SAC codes start with "99"
}
public sealed record ParticularsInput(VoucherType Type, GstRegistration Registration, InvoiceKind Kind, bool EveryLineTaxFree,
    DateOnly Date, DocumentParty Company, DocumentParty Buyer, ShipTo? ShipTo, Money Taxable, string? OriginalNumber,
    DateOnly? OriginalDate, IReadOnlyList<string> LineDescriptions);
public static class InvoiceParticulars { public static string? FirstMissing(ParticularsInput p, GstData gst); }
public static class LogoImage { public const int MaxBytes = 204_800; public static string? Check(ReadOnlySpan<byte> bytes); }
// GstData:
public Money UnregisteredDetailsThreshold(DateOnly on);
```

- **`AmountInWords.Rupees`** uses the Indian scale (crore, lakh, thousand, hundred). Number words are separated by spaces, as on a cheque (`Thirty Four`), with no "and" between whole number words.
  - `1234567.89` gives `Rupees Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven and Eighty Nine Paise Only`.
  - `0` gives `Rupees Zero Only`. `0.50` gives `Rupees Zero and Fifty Paise Only`.
  - `1000000000.00` gives `Rupees One Hundred Crore Only`.
- **`InvoiceWording.Title`:**
  - Sale:
    - Regular company: `Tax invoice`. When the kind is Regular and every line has rate 0 and cess 0, it is `Bill of supply` (Rule 49).
    - Composition company: `Bill of supply`.
    - Unregistered company: `Invoice`.
  - Credit note: `Credit note`. Debit note: `Debit note`.
- **`Endorsement`** for the Rule 46 export proviso:
  - `ExportWithTax`: `SUPPLY MEANT FOR EXPORT ON PAYMENT OF INTEGRATED TAX`
  - `ExportUnderLut`: `SUPPLY MEANT FOR EXPORT UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX`
  - `SezWithTax`: `SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS ON PAYMENT OF INTEGRATED TAX`
  - `SezWithoutTax`: `SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX`
  - Every other kind: null.
- **`CopyLabels`** (Rule 48):
  - `byRule` with goods: `ORIGINAL FOR RECIPIENT`, `DUPLICATE FOR TRANSPORTER`, `TRIPLICATE FOR SUPPLIER`.
  - `byRule` with services only: `ORIGINAL FOR RECIPIENT`, `DUPLICATE FOR SUPPLIER`.
  - `original`: `ORIGINAL FOR RECIPIENT`.
- **`FirstMissing`** applies only the rule that governs the document. The rule follows from `Title`:
  - Rule 46 for a tax invoice;
  - Rule 49 for a bill of supply, which takes Rule 46's provisos;
  - Rule 53 for a note.
  
  An Unregistered company is not a registered person, so none of these rules apply and the result is always null. Otherwise it checks in this order and returns the first message, or null:
  1. A blank company address (46(a), 49(a), 53(a)): `Add the company address in CMPY before printing.`
  2. A line with a blank description (46(h), 49(f)): `Line {n}: add a description before printing.`
  3. A note with no original number or date (53(g)): `Add the original invoice number and date before printing.`
  4. An export kind with no Ship to country (the 46 proviso, which applies to 49 too): `Add the country of destination under Ship to before printing.`
  5. A blank buyer address and no Ship to address: `Add the buyer's address in PRTY, or a Ship to address, before printing.` This applies:
     - when the buyer has a GSTIN (46(d), 49(d), 53(e));
     - when the buyer has none, on a note (53(f));
     - when the buyer has none, on a tax invoice whose taxable value is at least `UnregisteredDetailsThreshold(date)` (46(e)).
     
     A bill of supply to a buyer with no GSTIN needs no address (49(d)).
- **`LogoImage.Check`** accepts only PNG, because PDFsharp's Core build refuses progressive JPEG (the spike showed this):
  - not a PNG signature: `Logo must be a PNG file.`
  - more than `MaxBytes`: `Logo must be at most 200 KB.`
  - IHDR width or height outside 1 to 4000: `Logo must be at most 4000 by 4000 pixels.`
- **New company settings** in `SettingsCatalog`:
  - `invoice.bankDetails`: Text, `""`, `Bank details printed on invoices.` At most 500 characters, through `Rule`.
  - `invoice.terms`: Text, `""`, `Terms printed on invoices.` At most 500 characters.
  - `invoice.signatory`: Text, `"Authorised signatory"`, `Signatory line printed on invoices.` At most 100 characters.
  - `invoice.showPrice`: Boolean, `true`, `Show unit price on printed invoices.`
  - `invoice.showDiscount`: Boolean, `true`, `Show discount on printed invoices.`
  - `invoice.copies`: Choice `byRule` or `original`, default `byRule`, `Printed invoice copies: by Rule 48, or the original only.`
  
  The length rule's text is `Setting {key} must be at most {n} characters.` No setting can hide HSN, quantity, unit, or any tax column.

- [ ] **Step 1: Write the failing tests:**
  - `Words_use_lakh_and_crore`: the four values above.
  - `Words_refuse_negative_amounts`
  - `Titles_follow_type_and_registration`: covers each title above, including the all-zero Regular sale.
  - `Endorsements_quote_the_export_proviso`
  - `Copy_labels_follow_rule_48_and_the_setting`
  - `Sac_codes_are_services`: `998314` is a service, and `8471` is not.
  - `Unregistered_buyer_at_fifty_thousand_without_address_is_refused`: taxable ₹50,000.00 is refused, ₹49,999.99 is null, and the same buyer with a Ship to address is null.
  - `Note_to_unregistered_buyer_needs_address_at_any_value`
  - `Registered_buyer_without_address_is_refused`
  - `Unregistered_company_sale_at_fifty_thousand_prints_without_buyer_address`
  - `Bill_of_supply_follows_rule_49_particulars`: a composition sale of ₹75,000 to a buyer with no GSTIN and no address is null, and the same sale to a buyer with a GSTIN and no address is refused.
  - `Note_without_original_invoice_is_refused`
  - `Export_without_country_is_refused`
  - `Line_without_description_is_refused`
  - `Blank_company_address_is_refused_first`
  - `Logo_accepts_png_and_refuses_jpeg_large_and_huge`
  - `Invoice_print_settings_validate`: `invoice.copies` `"all"` is refused, and a 501-character `invoice.terms` gives the length message.
  - `Threshold_is_data`: `UnregisteredDetailsThreshold(2026-05-01)` is 5000000 paise.
- [ ] **Step 2: Run them to see them fail.** Run `dotnet test tests/OpenHisabKitab.Core.Tests`. Expected: the new tests fail to compile.
- [ ] **Step 3: Implement.** Record the threshold's source row in `gst-data.json` as `Rule 46(e) CGST Rules, checked 2026-11-03`.
- [ ] **Step 4: Verify.** The Core tests pass, and `dotnet build OpenHisabKitab.slnx` shows 0 warnings.
- [ ] **Step 5: Commit** `feat: amounts in words, printed document wording, and invoice particulars checks`

---

### Task 2: Ship to, and the details saved on each invoice

**Files:**
- Create: `src/OpenHisabKitab.Data/Migrations/Company/0004_printing.sql`
- Modify:
  - `src/OpenHisabKitab.Data/CompanyDb/VoucherRepository.cs` (`InvoiceHeader`, insert, select)
  - `src/OpenHisabKitab.Host/Application/VoucherModels.cs` (`InvoiceInput`)
  - `VoucherService.Invoices.cs` (fill the details), `VoucherService.Bills.cs` (`CheckedShipTo`, beside `CheckedExport`)
  - `src/OpenHisabKitab.Contracts/Protos/vouchers.proto`
  - `src/OpenHisabKitab.Host/Grpc/Wire.cs`
  - `src/OpenHisabKitab.Host/Api/ApiModels.cs`, `ApiParse.cs`
  - `src/OpenHisabKitab.Client.Model/Screens/Entry/InvoiceEntryViewModel.cs`
  - `src/OpenHisabKitab.Client/Views/InvoiceEntryView.xaml`
  - `Text.resx`
- Test:
  - `tests/OpenHisabKitab.Data.Tests/MigratorTests.cs`
  - `tests/OpenHisabKitab.Host.Tests/` (a new `InvoiceShipToTests.cs`), `Grpc/GrpcEndToEndTests.cs`, and the REST invoice tests
  - `tests/OpenHisabKitab.Client.Model.Tests/Screens/InvoiceEntryTests.cs`

**Interfaces:**
- Consumes: `ShipTo` and `DocumentParty` (Task 1).
- Produces:
  - `InvoiceHeader` gains `ShipTo? ShipTo = null, DocumentParty? PartyDetails = null, DocumentParty? CompanyDetails = null`, last.
  - `InvoiceInput` gains `ShipTo? ShipTo = null`, last.
  - Proto:

```proto
message ShipToMsg { string name = 1; string address = 2; string state = 3; string country = 4; }
message DocumentPartyMsg { string name = 1; string gstin = 2; string address = 3; string state = 4; }
// InvoiceMsg:       ShipToMsg ship_to = 19;
// InvoiceHeaderMsg: ShipToMsg ship_to = 15; DocumentPartyMsg party_details = 16; DocumentPartyMsg company_details = 17;
```

  - REST: `InvoiceRequest` gains `ShipToRequest? ShipTo = null` after `RequestId`, with `ShipToRequest(string Name, string Address, string? State, string? Country)`. `InvoiceDto` gains `ShipToDto? ShipTo, DocumentPartyDto? PartyDetails, DocumentPartyDto? CompanyDetails` at the end. MCP `create_invoice` takes Ship to through `ApiParse`, with no change of its own.
  - `InvoiceEntryViewModel` gains `string ShipToName`, `string ShipToAddress`, `StateOption? ShipToState`, `string ShipToCountry`, `bool ShowsShipTo` (=`IsOutward`), and `bool ShowsCountry` (=`ShowsExport`).

- **Migration `0004_printing.sql`:**
  - `invoices` gains these nullable TEXT columns:
    - `ship_to_name`, `ship_to_address`, `ship_to_state`, `ship_to_country`;
    - `party_name`, `party_gstin`, `party_address`, `party_state`;
    - `company_name`, `company_gstin`, `company_address`, `company_state`.
  - It also creates `company_logo(id INTEGER PRIMARY KEY CHECK (id = 1), png BLOB NOT NULL, sha256 TEXT NOT NULL)` for Task 4.
  - Old rows keep NULL details. Their prints fall back to the masters.
- **Saved details.**
  - Every invoice save, new or edit, writes the party's ledger name, GSTIN, address, and state, and the company profile's name, GSTIN, address, and state, as they are at that moment.
  - An edit takes them again, because an edit reissues the document.
  - Cancelling keeps them.
  - The details are not typed. The REST request has no field for them.
- **`CheckedShipTo(TradeDirection, InvoiceKind, ShipTo?)`:**
  - It trims every field. All blank means null.
  - A received document: `Ship to is only for documents this business issues.`
  - A name or address missing: `Enter the ship to name and address.`
  - A name over 100 characters, an address over 300, or a country over 60: `Ship to {field} is too long.` with `name`, `address`, or `country`.
  - A control character other than a newline in the address, or any control character in the other fields: `Ship to cannot contain control characters.`
  - A country on a kind that is not an export: `Country of destination is only for exports.`
  - No state on a kind that is not an export: `Choose the ship to state.` The state must be a known code.
- **Entry screen.** A `Ship to` group follows the export details, in Tab order: Name, Address (multi line, Enter adds a line), State, and Country (exports only). It is shown for issued documents only. Loading an invoice fills it, and `Clear` empties it.

- [ ] **Step 1: Write the failing tests:**
  - `Migration_0004_adds_ship_to_details_and_logo` (Data), following the `0002` downgrade pattern in `MigratorTests`.
  - `Ship_to_is_saved_and_returned` (Host)
  - `Ship_to_is_refused_on_purchases`
  - `Ship_to_needs_name_and_address`
  - `Country_only_for_exports_and_state_needed_otherwise`
  - `Invoice_keeps_party_and_company_details_as_saved`: save, change the party's GSTIN and address and the company address, then `Get` returns the old details. An edit then returns the new ones.
  - `Grpc_round_trips_ship_to_and_details` (end to end)
  - `Rest_invoice_takes_and_returns_ship_to`
  - `Ship_to_round_trips_through_the_entry_screen` (client)
  - `Country_shows_only_for_exports`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Follow the `PortCode` trail: migration, repository insert and select, service, `Wire.cs` both ways, proto, REST DTOs, entry model, XAML, resx. The text keys are `ShipToHeading` (`Ship to`), `ShipToName` (`Name`), `ShipToAddress` (`Address`), `ShipToState` (`State`), and `ShipToCountry` (`Country of destination`), each with a `{t:Name}` automation name.
- [ ] **Step 4: Verify.** The Data, Host, and client model tests pass, and the existing invoice tests stay green.
- [ ] **Step 5: Commit** `feat: ship to on issued invoices, and party and company details saved on each invoice`

---

### Task 3: Page model and PDF writer

**Files:**
- Create in `src/OpenHisabKitab.Client.Model/Printing/`: `PrintDocument.cs`, `TextMeasure.cs`, `TextWrap.cs`, `PageBuilder.cs`, `PdfFonts.cs`, `PdfExport.cs`, `PdfText.cs`, `IPrintable.cs`
- Modify: `Directory.Packages.props` (`PDFsharp` 6.2.4 and `System.Security.Cryptography.Pkcs` 10.0.14), and `OpenHisabKitab.Client.Model.csproj`, which references both, with a comment naming the audit advisory.
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Printing/PdfExportTests.cs`, `TextWrapTests.cs`, `PageBuilderTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum PrintFace { Text, Number }
public sealed record PrintFont(PrintFace Face, double Size, bool Bold = false);
public enum PrintAlign { Left, Right, Center }
public abstract record PrintItem;
/// Y is the top of the line; the text fits its box on one line (layouts wrap first).
public sealed record PrintText(double X, double Y, double Width, string Text, PrintFont Font, PrintAlign Align = PrintAlign.Left) : PrintItem;
public sealed record PrintRule(double X1, double Y1, double X2, double Y2, double Thickness = 0.5) : PrintItem;
public sealed record PrintImage(double X, double Y, double Width, double Height, byte[] Png) : PrintItem;
public sealed record PrintPage(IReadOnlyList<PrintItem> Items);
public sealed record PaperSize(double Width, double Height)
{
    public static PaperSize A4Portrait { get; } = new(595.28, 841.89);
    public static PaperSize A4Landscape { get; } = new(841.89, 595.28);
    public const double Margin = 36;
}
public sealed record PrintDocument(string Title, PaperSize Paper, IReadOnlyList<PrintPage> Pages);

public interface ITextMeasure { double Width(string text, PrintFont font); double LineHeight(PrintFont font); }
public sealed class PdfMeasure : ITextMeasure { public static PdfMeasure Instance { get; } }   // XGraphics.CreateMeasureContext
public static class TextWrap { public static IReadOnlyList<string> Lines(string text, double width, PrintFont font, ITextMeasure m); }

/// Places blocks top down; a block that does not fit below the cursor starts a new page. Footer space is kept free.
public sealed class PageBuilder(PaperSize paper, double footerHeight)
{
    public double Top { get; }  public double Bottom { get; }  public double Left { get; }  public double Right { get; }
    public double Y { get; }
    public int PageCount { get; }
    public bool Fits(double height);
    public void NewPage();
    public void Add(PrintItem item);
    public void Advance(double height);
    public IReadOnlyList<List<PrintItem>> Pages { get; }   // footers are added after, once the page count is known
}

public static class PdfExport { public static void Write(PrintDocument doc, Stream to); public static bool CanDraw(byte[] png); }
public static class PdfText { public static string? FirstUnsupported(PrintDocument doc); }   // the first text with a character the PDF fonts cannot show

public enum PrintTarget { Paper, Pdf }
/// Document is null when Refusal says why; FileName is used for PDF.
public sealed record PrintResult(PrintDocument? Document, string FileName, string? Refusal);
public interface IPrintable { Task<PrintResult> PrintAsync(PrintTarget target); }
```

- **`PdfFonts`** is an `IFontResolver`:
  - It maps `Segoe UI` to `segoeui.ttf` and `segoeuib.ttf`, and `Consolas` to `consola.ttf` and `consolab.ttf`, read from `Environment.SpecialFolder.Fonts`.
  - It is installed once per process behind a `Lazy`, because `GlobalFontSettings.FontResolver` is global and xUnit runs classes in parallel.
  - `PdfMeasure` and `PdfExport` call it first.
- **`PdfExport.Write`** sets `Info.Title`, adds one page per `PrintPage` at the document's paper size, and draws:
  - text with `XStringFormats.TopLeft`, `TopRight`, or `TopCenter` in its box;
  - rules with an `XPen`;
  - images with `XImage.FromStream`.
  
  `CanDraw` returns false when `XImage.FromStream` throws.
- **`PdfText.FirstUnsupported`** allows these code points:
  - U+0009, U+000A, and U+0020 to U+024F (Latin);
  - U+2000 to U+206F (punctuation);
  - U+20A0 to U+20CF (currency, including ₹).
  
  It returns the first text that has any other character. Segoe UI has no Indic glyphs, and PDFsharp does not shape Indic scripts. The spike drew empty boxes.
- **`TextWrap.Lines`** breaks at spaces. It splits a word wider than the box at the last character that fits, keeps each `\n` as a line break, and never returns a line wider than `width`.

- [ ] **Step 1: Write the failing tests:**
  - `Pdf_has_one_page_per_print_page_and_the_title`:
    - The bytes start with `%PDF-`.
    - `PdfReader.Open(..., PdfDocumentOpenMode.Import)` gives `PageCount` 2 and `Info.Title`.
    - The first page is 595.28 points wide, within 0.01.
  - `Pdf_embeds_segoe_and_consolas`: the bytes contain `+Segoe#20UI` and `+Consolas`.
  - `Landscape_pages_are_wide`
  - `Png_draws_and_other_bytes_are_refused`: `CanDraw` is true for a 2 by 2 PNG and false for `"hello"u8`.
  - `Rupee_sign_and_latin_are_supported_devanagari_is_not`: `Total ₹ 1,00,000.00 é` is supported, and `राम Traders` is returned.
  - `Wrap_breaks_at_spaces_splits_long_words_and_keeps_newlines`
  - `Wrapped_lines_never_exceed_the_width`: 200 random strings at widths 20 to 300.
  - `Page_builder_starts_a_new_page_when_a_block_does_not_fit`
  - `Fonts_install_once_under_parallel_use`: 16 parallel `Write` calls succeed.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** If the restore reports any other audit warning, pin that package's patched version the same way, and record it under Builder decisions.
- [ ] **Step 4: Verify.** The client model tests pass, and `dotnet build OpenHisabKitab.slnx` shows 0 warnings, including NuGet audit.
- [ ] **Step 5: Commit** `feat: page model and PDF writer on PDFsharp 6.2.4`

---

### Task 4: Company logo

**Files:**
- Modify:
  - `src/OpenHisabKitab.Data/CompanyDb/CompanyRepository.cs` (`GetLogo`, `SetLogo`, `RemoveLogo`)
  - `src/OpenHisabKitab.Host/Application/CompanyService.cs`
  - `companies.proto`, `src/OpenHisabKitab.Host/Grpc/` (the Companies service)
  - `src/OpenHisabKitab.Client.Model/Lookups/LookupCache.cs`
  - `Screens/CompanyViewModel.cs`
  - `src/OpenHisabKitab.Client/Views/CompanyView.xaml(.cs)`
  - `Text.resx`
- Test: `tests/OpenHisabKitab.Host.Tests/` (a new `CompanyLogoTests.cs`), `tests/OpenHisabKitab.Client.Model.Tests/Screens/CompanyTests.cs`

**Interfaces:**
- Consumes: `company_logo` (Task 2), `LogoImage` (Task 1), `PdfExport.CanDraw` (Task 3).
- Produces:

```proto
rpc GetLogo (CompanyRef) returns (LogoMsg);
rpc SetLogo (SetLogoRequest) returns (Empty);          // empty png removes the logo
message LogoMsg { bytes png = 1; string sha256 = 2; }   // empty when there is none
message SetLogoRequest { int32 company = 1; bytes png = 2; }
```

  - `CompanyService.GetLogo(CallContext, int) -> (byte[] Png, string Sha256)?` and `SetLogoAsync(CallContext, int, byte[] png) -> Task`.
  - `LookupCache.LogoAsync(CancellationToken) -> Task<byte[]?>`. It is fetched on first use and dropped on a `Settings` or `Restore` change.
  - `CompanyViewModel` gains `string LogoText` (`Logo: {n} KB` or `No logo`), `bool CanSetLogo`, `Task SetLogoAsync(string path)`, and `Task RemoveLogoAsync()`.

- **Host.**
  - `SetLogoAsync` needs `ManageCompanies` and the company, and refuses with `LogoImage.Check`'s message.
  - It audits action `set` or `remove`, entity `logo`, key `logo`. Before and after are `{"sha256":"…","bytes":n}` or null, not the bytes, so the trail does not grow by twice the image on each change.
  - It publishes a `Settings` change.
  - `GetLogo` needs only company access.
- **Client.**
  - In CMPY's edit form, `Choose _logo...` opens a file dialog filtered to `PNG files (*.png)|*.png`. `_Remove logo` removes the logo.
  - The model reads the file and checks it with `LogoImage.Check`, then with `PdfExport.CanDraw`, before any host call:
    - `Cannot read file {path}.`
    - `Cannot read this logo. Save it as a PNG file again.`
  - On success it shows `Logo saved.` or `Logo removed.`
- No REST route or MCP tool for the logo (builder decision 6).

- [ ] **Step 1: Write the failing tests:**
  - `Logo_set_get_remove_audited_with_hash` (Host)
  - `Logo_refused_when_not_png_or_too_big`
  - `Data_entry_and_viewer_cannot_set_logo` (Review Focus 5)
  - `Choose_logo_saves_and_shows_size` (client)
  - `Unreadable_png_is_refused_before_the_host`
  - `Logo_change_drops_the_cached_logo`: another client sets the logo, then `LogoAsync` returns the new bytes after the change event.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The Host and client model tests pass.
- [ ] **Step 5: Commit** `feat: company logo for printed invoices`

---

### Task 5: Report printing

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Printing/PrintTable.cs`, `ReportPrint.cs`
- Modify: `Screens/Reports/ReportScreen.cs`, `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Printing/ReportPrintTests.cs`, `tests/OpenHisabKitab.Client.Model.Tests/Reports/ReportPrintScreenTests.cs`

**Interfaces:**
- Consumes: Task 3's page model, `ReportColumn`, `ReportLine`.
- Produces:

```csharp
public sealed record TableColumn(string Header, double Width, PrintAlign Align, PrintFace Face);
public sealed record TableRow(IReadOnlyList<string> Cells, bool Bold = false, int Depth = 0);
public static class PrintTable
{
    /// Header row, then rows; wraps text cells, never number cells; repeats the header after each page break;
    /// a row never splits.
    public static void Write(PageBuilder page, IReadOnlyList<TableColumn> columns, IEnumerable<TableRow> rows, double fontSize, ITextMeasure m);
    /// Column widths for the width, or null when even wrapped text columns cannot fit beside the number columns.
    public static IReadOnlyList<TableColumn>? Fit(IReadOnlyList<ReportColumn> columns, IEnumerable<IReadOnlyList<string>> cells, double width, double fontSize, ITextMeasure m);
}
public sealed record ReportPrintInput(string Company, string Heading, string PrintedOn, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportLine> Lines);
public static class ReportPrint { public static PrintDocument Build(ReportPrintInput input, ITextMeasure m); }
// ReportScreen : IPrintable; ReportScreen gains protected Task<bool> LoadAllAsync(), used by both export and print.
```

- **Layout.**
  - Page 1 opens with the company name (bold 12) and the heading (bold 11). Each later page repeats them in one line at 9.
  - The table body is 8.5 points.
  - Every page has a footer, `Printed {date} {HH:mm}` on the left and `Page {n} of {m}` on the right.
  - Depth indents the first column by 8 points a level. Emphasis rows are bold, with a rule above.
- **Fitting.** `Fit` gives each number column the width of its widest cell. Text columns share the rest, and the first takes at least 30%.
  - The result is portrait when the number columns plus 120 points per text column fit 523.28 points.
  - Otherwise it is landscape. If that still does not fit, the font drops to 7 points.
  - If even that does not fit, text columns wrap.
- **`ReportScreen.PrintAsync(target)`:**
  - Without `Export`: `Printing reports needs the export right.`
  - It loads every page first, as Ctrl+E does. If a page fails, it returns the screen's message as the refusal.
  - It builds from `Columns` and `Lines` with their screen text.
  - `FileName` is `Path.ChangeExtension(ExportName, ".pdf")`.

- [ ] **Step 1: Write the failing tests:**
  - `Report_rows_all_print_once_in_order_across_pages` (Review Focus 2): a 600-entry `LEDG`. The first-column texts of every page, without the repeated headers, equal `Lines` in order.
  - `Column_headers_repeat_on_every_page`
  - `Page_numbers_read_n_of_m`
  - `No_row_splits_across_pages`: each row's items sit on one page.
  - `Wide_outstanding_prints_landscape_and_trial_balance_portrait`
  - `Long_ledger_name_wraps_and_never_overlaps` (Review Focus 3): no two text boxes on a page intersect, and every box lies inside the margins.
  - `Amounts_keep_screen_text_with_dr_cr`
  - `Viewer_cannot_print_reports` (Review Focus 5)
  - `Print_loads_every_page_first`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** The text keys are `PrintedOn` (`Printed {0}`), `PageOf` (`Page {0} of {1}`), and `PrintNeedsExport`.
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: print and PDF for every report`

---

### Task 6: Printed invoices

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Printing/InvoicePrint.cs`, `InvoicePrintInput.cs`
- Modify:
  - `Screens/Entry/VoucherEntryViewModel.cs` (`IPrintable`, the shared checks)
  - `Screens/Entry/InvoiceEntryViewModel.cs`
  - `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Printing/InvoicePrintTests.cs`

**Interfaces:**
- Consumes:
  - Tasks 1 to 5;
  - `VoucherMsg` and `InvoiceHeaderMsg`, including `party_details`, `company_details`, and `ship_to`;
  - `LookupCache`, `EffectiveSettings`, and `GstData.LoadEmbedded()`.
- Produces:

```csharp
public sealed record InvoicePrintInput(VoucherMsg Voucher, DocumentParty Company, GstRegistration Registration,
    DocumentParty Buyer, ShipTo? ShipTo, IReadOnlyList<string> Units, byte[]? Logo,
    string BankDetails, string Terms, string Signatory, bool ShowPrice, bool ShowDiscount)
{
    /// Saved details when present, else the masters; units from the line's item.
    public static InvoicePrintInput From(VoucherMsg v, LookupCache lookups, EffectiveSettings s, byte[]? logo);
}
public static class InvoicePrint
{
    public static string? Check(InvoicePrintInput input, GstData gst);   // InvoiceParticulars.FirstMissing
    public static PrintDocument Build(InvoicePrintInput input, IReadOnlyList<string> copyLabels, GstData gst, ITextMeasure m);
}
// VoucherEntryViewModel : IPrintable
//   PrintAsync: refusals first, then a fresh Vouchers.Get, then protected abstract Task<PrintResult> BuildPrintAsync(VoucherMsg stored, PrintTarget target).
```

- **Shared checks** in `VoucherEntryViewModel.PrintAsync`:
  - A new voucher, or one with unsaved edits: `Save the voucher before printing.`
  - It fetches `Vouchers.Get`. If the row version differs from `Existing`: `This voucher was changed. Reload it with RFSH, then print.`
  - `FileName` is `FileSafe("{CODE} {display number}") + ".pdf"`, such as `SALE INV-26-27-00001.pdf`.
- **Which layout.** `SALE`, and `CRNT` and `DBNT` with direction Outward, use this layout. Everything else uses Task 7's slip.
- **Copies.**
  - Paper uses `InvoiceWording.CopyLabels(anyGoods, invoice.copies)`. A line is goods unless `IsService(hsn)`.
  - PDF is always `ORIGINAL FOR RECIPIENT` alone.
  - Each copy is a whole set of pages.
- **Page 1, top to bottom:**
  1. The copy label, right aligned and bold.
  2. The title from `InvoiceWording.Title`, centred, bold 14. A composition company adds `CompositionLine` under it. A cancelled voucher adds `CANCELLED`, bold 14.
  3. The left block: the logo (at most 120 by 60 points, keeping its shape), then the company name (bold), address, `GSTIN {gstin}` when registered, and `State {name} ({code})`.
  4. The right block:
     - `Invoice no.` (or `Note no.`) and the number;
     - `Date`;
     - `Place of supply {name} ({code})`;
     - `Reverse charge No`;
     - for notes, `Original invoice {number}, {date}`;
     - for exports, `Port code {code}` and `Shipping bill {number}, {date}` when they are present.
  5. `Bill to`: name (bold), address, `GSTIN {gstin}` when present, and `State {name} ({code})`. Beside it, when Ship to is set, comes `Ship to`: name, address, and `State {name} ({code})` or `Country {country}`.
  6. The endorsement, when there is one, in bold.
- **Line table:**
  - The columns are `#`, `Description`, `HSN/SAC`, `Qty`, `Unit`, then `Rate` and `Discount` when their settings are on, then `Taxable value`.
  - The tax columns come from the stored amounts, never from the current company state. Any line with stored CGST or SGST shows the intra state pair, and any with UTGST shows the UTGST pair. Any with IGST shows IGST. Only when every stored tax is zero, as under LUT, do the columns follow `GstRules.Split` for the saved `company_details` state against the place of supply:
    - intra state: `CGST %`, `CGST`, then `SGST %` and `SGST` (or `UTGST %` and `UTGST`);
    - inter state, export, or SEZ: `IGST %`, `IGST`;
    - `Cess %` and `Cess` when any line has cess.
  - Composition, Unregistered, and the all-zero Regular bill of supply show no tax columns.
  - Rates come from the stored line. CGST and SGST show half the rate, such as `9%` or `2.5%`.
  - Quantities show up to three decimals with no trailing zeros, such as `2`, `1.5`, or `0.125`.
- **Totals**, right aligned:
  - `Taxable value`, each tax with a non-zero total, `Round off` when non-zero, and `Total` (bold);
  - then `Amount in words: {AmountInWords.Rupees(total)}`.
  
  Total is the sum of stored taxable values and taxes plus the stored round off.
- **Footer of the last page:**
  - `Bank details` and `Terms` (each only when set, with newlines kept) on the left;
  - `For {company name}`, 36 points of space, then the signatory, on the right.
- **More pages.**
  - Rows continue with the column headers repeated. Later pages open with `{title} {number}, {date} (continued)`.
  - Totals and the footer go on the last page, or on a new page when they do not fit.
  - Every page shows the copy label and `Page {n} of {m}`.

- [ ] **Step 1: Write the failing tests.** Each test saves through the host as Plan 5's tests do, then calls `PrintAsync` and reads the `PrintText` items.
  - `Intra_state_invoice_shows_cgst_sgst_and_totals_equal_the_party_posting` (Review Focus 1): the printed Total equals the party ledger's posting, and the screen's total.
  - `Inter_state_invoice_shows_igst`
  - `Union_territory_invoice_shows_utgst`
  - `Export_under_lut_shows_endorsement_port_and_country`
  - `Round_off_and_tax_inclusive_prices_use_stored_figures`
  - `Every_nonzero_stored_tax_appears_in_a_line_column` (Review Focus 1): after the company's state is changed, an old inter state invoice still prints its IGST under `IGST`.
  - `Composition_company_prints_bill_of_supply_without_tax_columns`
  - `Unregistered_company_prints_invoice_without_gstin`
  - `Credit_note_shows_original_invoice`
  - `Reprint_shows_details_as_saved_after_party_changes`
  - `Invoice_saved_before_0004_uses_the_masters`: a unit test of `InvoicePrintInput.From` with a `VoucherMsg` that has no `party_details` or `company_details`.
  - `Optional_columns_hide_price_and_discount_but_never_hsn_or_quantity` (Review Focus 4)
  - `Goods_print_three_copies_services_two_and_pdf_one`
  - `Unregistered_buyer_at_fifty_thousand_without_address_is_refused`
  - `Sixty_lines_continue_on_a_second_page_with_totals_last` (Review Focus 2)
  - `Long_address_wraps_inside_its_block` (Review Focus 3)
  - `Amount_in_words_matches_total`
  - `Logo_bank_details_and_signatory_print_from_settings`: the bank details keep their newline.
  - `Cancelled_invoice_prints_marked_cancelled`
  - `Unsaved_invoice_says_save_first`
  - `Invoice_changed_elsewhere_says_reload` (Review Focus 1)
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Labels go in `Text.resx` under `Print*` keys.
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: printed invoices with Rule 46 particulars and Rule 48 copies`

---

### Task 7: Voucher slips

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Printing/VoucherSlip.cs`
- Modify: `Screens/Entry/JournalEntryViewModel.cs`, `InvoiceEntryViewModel.cs` (purchase and received notes), `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Printing/VoucherSlipTests.cs`

**Interfaces:**
- Consumes: `VoucherEntryViewModel.BuildPrintAsync` (Task 6), `PrintTable` (Task 5).
- Produces:

```csharp
public sealed record SlipInput(VoucherMsg Voucher, string Title, DocumentParty Company,
    IReadOnlyList<(string Ledger, Money Debit, Money Credit)> Lines, string? SupplierDocument, bool ReceiverSigns);
public static class VoucherSlip { public static PrintDocument Build(SlipInput input, ITextMeasure m); }
```

- **Titles:**
  - `Purchase voucher`, `Receipt voucher`, `Payment voucher`, `Journal voucher`, `Contra voucher`;
  - `Credit note received` and `Debit note received`.
- **Layout.**
  - The company name and address, the title (bold 14), `Voucher no.` and `Date`.
  - For a purchase or received note, `Supplier document {number}, {date}`.
  - A table of Ledger, Debit, and Credit from the postings, in stored order, with a `Total` row.
  - `Narration`, and `Amount in words` for the debit total.
  - Signature boxes `Prepared by`, `Checked by`, and `Approved by`. Receipts and payments add `Receiver's signature`.
  - A cancelled voucher shows `CANCELLED`. A slip prints one copy, paginated like the reports.

- [ ] **Step 1: Write the failing tests:**
  - `Payment_slip_lists_dr_cr_lines_total_and_signature_boxes`
  - `Receipt_slip_has_receivers_signature`
  - `Purchase_slip_shows_supplier_document`
  - `Received_credit_note_prints_as_a_slip_not_an_invoice`
  - `Slip_amount_in_words_is_the_debit_total`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** The client model tests pass.
- [ ] **Step 5: Commit** `feat: voucher slips for purchases, receipts, payments, journals, and contras`

---

### Task 8: `PRNT`, the PDF key, and WPF printing

**Files:**
- Create: `src/OpenHisabKitab.Client/Printing/PrintPaginator.cs`
- Modify:
  - `src/OpenHisabKitab.Core/Commands/Keys/KeyCommands.cs`, `DefaultKeybindings.cs`
  - `src/OpenHisabKitab.Client.Model/Shell/FunctionRouter.cs`, `ShellViewModel.cs`
  - `src/OpenHisabKitab.Client/MainWindow.xaml.cs`
  - `Text.resx`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Shell/PrintCommandTests.cs`

**Interfaces:**
- Consumes: `IPrintable`, `PrintResult`, `PrintTarget`, `PdfExport`, and `PdfText` (Task 3).
- Produces:
  - `RouteResult` gains `PrintTarget? Print = null`, last.
  - `ShellViewModel.PrintRequested` is an `event Action<PrintDocument, string, PrintTarget>`, giving the document, the file name, and the target.
  - `ShellViewModel.Printed(string message)` sets the status line.
  - The command `screen.savePdf`, bound to Ctrl+Shift+E, runs `PRNT pdf`. F11 stays `run.PRNT`.

- **Routing.**
  - `PRNT` means paper, and `PRNT pdf` means PDF. Any other argument: `Type PRNT, or PRNT pdf.`
  - The router returns no screen and no command, so no tab is opened and nothing is added to Back.
- **Shell.**
  - With no tab, or a screen that is not `IPrintable`: `Nothing to print in this tab.`
  - A refusal shows as the status line.
  - For PDF, `PdfText.FirstUnsupported` gives `The PDF cannot show "{text}". Print it and choose Microsoft Print to PDF.`
  - Otherwise the shell raises `PrintRequested`.
- **`PrintPaginator : DocumentPaginator`** in WPF:
  - `PageSize` is the paper size times 96/72.
  - Each page is a `DrawingVisual`. Text uses `FormattedText` in `Segoe UI` or `Consolas` at size × 96/72, aligned in its box. Rules are drawn as lines, and images through `BitmapImage` from the PNG bytes.
  - Each text sets `MaxTextWidth` to its box width and `MaxLineCount` to 1, with `Trimming` off, and is clipped to its box. WPF falls back to Nirmala UI for Indic text and shapes it correctly; a check on 2026-11-03 showed this. Its widths differ from PDFsharp's measure, so the clip keeps a wider run from ever overlapping another box.
- **`MainWindow`:**
  - PDF: a `SaveFileDialog` with the file name and `PDF files (*.pdf)|*.pdf`, then `PdfExport.Write`, then `Saved {file}.`
  - Paper: a `PrintDialog`, with `PageOrientation` Landscape when the paper is wider than high. Then `PrintDocument(paginator, title)` and `Sent to {printer}.`
  - Cancelling a dialog says nothing.

- [ ] **Step 1: Write the failing tests:**
  - `Prnt_with_no_tab_or_an_unprintable_tab_says_nothing_to_print`: `HELP` is open.
  - `Prnt_pdf_raises_a_pdf_request_with_the_file_name`
  - `F11_and_ctrl_shift_e_run_print_and_pdf`
  - `Prnt_adds_no_tab_and_no_back_step`
  - `Prnt_with_other_words_says_how`
  - `Data_entry_prints_their_own_invoice` (Review Focus 5)
  - `Pdf_with_devanagari_is_refused_with_a_hint` (Review Focus 3): a party named `राम Traders`. Paper printing raises the request.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Register `screen.savePdf` in the shell's command switch, and keep the existing keybinding tests green.
- [ ] **Step 4: Verify.** The client model tests pass, and `dotnet build` shows 0 warnings.
- [ ] **Step 5: Commit** `feat: PRNT prints the current tab or saves it as PDF`

---

### Task 9: UI smoke tests, docs, owner review, and finishing

**Files:**
- Modify:
  - `tests/OpenHisabKitab.Client.UiTests/` (a new `PrintSmokeTests.cs`)
  - `README.md`, `docs/owner-review.md`
  - this plan's Builder decisions

- [ ] **Step 1: UI smoke tests,** keyboard only, through FlaUI:
  - `Sale_saves_as_pdf_from_the_keyboard`: on a saved `SALE`, press Ctrl+Shift+E, drive the save dialog by keys, and check that the file starts with `%PDF-`.
  - `Trlb_saves_as_pdf`
  - `F11_opens_the_print_dialog_and_esc_closes_it`
  - `Ship_to_fields_are_reached_by_tab_on_a_sale`
  - `Cmpy_sets_a_logo_by_keyboard`
- [ ] **Step 2: Run them.** `$env:OHK_UI_TESTS='1'; dotnet test tests/OpenHisabKitab.Client.UiTests` passes. Without the variable they are skipped.
- [ ] **Step 3: Docs.** `README.md` gains a short "Printing" section:
  - `PRNT`, `PRNT pdf`, F11, and Ctrl+Shift+E;
  - what prints for each tab;
  - the six `invoice.*` settings, and the logo in CMPY. Line breaks in the bank details and terms are typed in SETG's JSON view, as `\n`;
  - Ship to;
  - that PDF shows Latin script only.
- [ ] **Step 4: Owner review.** Mark done any items settled during the work, and add only new major decisions. Record smaller ones under Builder decisions.
- [ ] **Step 5: Final verification.**
  - `dotnet build OpenHisabKitab.slnx -c Release` has 0 warnings.
  - `dotnet test OpenHisabKitab.slnx -c Release --no-build` passes, with the UI tests skipped.
  - The UI tests pass with the variable set.
  - One sample of each layout is saved as PDF, opened, and read by eye: an intra-state tax invoice, an export under LUT, a credit note, a 600-line `LEDG`, `OUTS`, and a payment slip.
- [ ] **Step 6: Commit** `test: print smoke tests; docs and owner review for plan 6b`
- [ ] **Step 7: Security review and integration.**
  - Run `/security-review` on the branch and fix what it finds.
  - Then ask the owner whether to push the branch and merge. After a push, confirm that CI is green.

## Builder decisions

These are the builder's calls, kept here for the record. The major ones are in `docs/owner-review.md`.

1. **One page model, two renderers.** Layouts fill positioned text, rules, and images. PDFsharp draws them for PDF, and a WPF `DocumentPaginator` draws them for paper. Text is measured once, through PDFsharp, so page breaks are the same in both. Cost if wrong: one renderer's text may sit a fraction of a point off its box.
2. **`PRNT` is handled by the shell, like `RFSH`.** It acts on the active tab and never opens one, so Back never replays a print. Cost if wrong: none expected.
3. **Ctrl+Shift+E saves as PDF,** beside Ctrl+E for CSV. F11 prints. Both are rebindable. Cost if wrong: one default key.
4. **A voucher prints from a fresh `Vouchers.Get`.** It refuses when the screen is unsaved or stale, so paper never differs from the books. Cost if wrong: one extra reload for the user.
5. **The logo is stored in the company file, as PNG only, at most 200 KB.**
   - A path would work on one PC only.
   - A setting would put the bytes into every settings reload.
   - Progressive JPEG fails in PDFsharp's Core build.
   
   The trail records the image's hash and size (see owner review). Cost if wrong: users convert JPEG logos to PNG.
6. **No REST route or MCP tool for the logo or the PDF.** Scripts read invoices as data. Cost if wrong: one route later.
7. **An edit saves the buyer's and company's details again,** because an edit reissues the document. Cancelling keeps them. Cost if wrong: one rule.
8. **A line is a service when its HSN/SAC starts with `99`.** That decides the Rule 48 copies without an item lookup, and works for lines with no item. Cost if wrong: one more copy for an odd code.
9. **Invoices and slips always show `dd-mm-yyyy` and a leading minus,** whatever the user's screen settings, because they are documents for others. Reports keep the screen's format. Cost if wrong: one formatting call.
10. **Legal wording lives in Core `InvoiceWording`,** beside the tax rules. Labels live in `Text.resx`. Cost if wrong: strings move.
11. **A cancelled voucher prints, marked `CANCELLED`,** so the record can be filed. Cost if wrong: one refusal.
12. **Paper text is clipped to its measured box.** Indic text on paper takes WPF's fallback font, whose widths PDFsharp cannot measure. A long Indic name may be cut at its box edge, but it never overlaps. Cost if wrong: a wider column for such names.
13. **`System.Security.Cryptography.Pkcs` 10.0.14 is referenced directly** to clear the audit failure on PDFsharp's 8.0.1. Cost if wrong: none; remove it when PDFsharp updates.

## Done when

- Every task's tests pass in Release, with 0 warnings.
- With the host and client running, a user can do all of this from the keyboard only:
  - save a `SALE` with a Ship to, print it on paper with its Rule 48 copies, and save it as PDF;
  - print `TRLB` and a long `LEDG`, and save `OUTS` as PDF in landscape;
  - print a payment slip;
  - set the company logo and see it on the next invoice.
- The UI smoke tests pass with `OHK_UI_TESTS=1`.
- `docs/owner-review.md` holds the new major decisions.
- The owner has decided about pushing and merging.
