# Plan 1: Foundation and Accounting Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set up the repository, CI, and a pure C# accounting core library that does money, Indian formatting, dates, command parsing, chart of accounts, vouchers, GST calculation and posting, voucher numbering, and all report balances. Every calculation is proven by tests.

**Architecture:** One class library, `OpenHisabKitab.Core`. It has no I/O and no UI. It holds every calculation the spec puts in `Core` (spec section 4). The host, client, and later plans call it and never re-implement it.

All money is `long` paise wrapped in a `Money` struct. Every report figure comes from one class, `Books`, which aggregates posting records (spec 5.1). Tests are xUnit, with seeded random property tests and a hand calculated golden fixture.

**Tech Stack:**
- .NET 10 (SDK 10.0.402), C# latest, nullable enabled.
- xUnit 2.9.3, which is what `dotnet new xunit` produces on this SDK.
- GitHub Actions on `windows-latest`.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`

**Later plans** (each gets its own document when this one is done):

| Plan | Covers |
|---|---|
| 2 | Data and Host: SQLite, migrations, gRPC host service, auth, numbering, audit trail, locks, backups |
| 3 | Client shell: window, prompt, tabs, F key row, settings, keybindings, theme, accessibility |
| 4 | Masters and voucher entry screens |
| 5 | Reports screens, `CFLO`, `OUTS`, `DAYB`, printing and PDF |
| 6 | GST files: `GST1`, `GS3B`, `G2BR`, `EINV` |
| 7 | Users, roles, installer, release |

## Global Constraints

- **Target and build settings.** Target `net10.0`. `Nullable` enabled. `TreatWarningsAsErrors` true.
- **No em dash or en dash.** U+2014 and U+2013 must not appear in any file in the repository: code, resources, or docs. A test enforces this (Task 1). Write ranges as "1 to 9" and use plain hyphens or colons instead.
- **No floating point in money, tax, or quantity logic.**
  - Money is `long` paise.
  - Quantity is `long` thousandths.
  - GST rate is `int` basis points, so 18% = `1800`.
  - Intermediate products use `decimal`, then round.
- **Rounding.** Round half away from zero, to the paisa, everywhere unless a step says otherwise (spec 5.3).
- **Culture.** All parsing and formatting uses `CultureInfo.InvariantCulture` or hand-written code. Results must not change with the PC's regional settings.
- **Sign convention.** Positive `Money` on a posting is a debit. Negative is a credit.
- **Financial year.** 1 April to 31 March. Labels are `2026-27`. The short label is `26-27`.
- **Dependencies.** No packages beyond the xUnit template set for Plan 1.
- **Licence.** MIT. Copyright line: `Copyright (c) 2026 Open Hisab Kitab contributors`.
- **Commits.** Commit after each task, using conventional prefixes (`feat:`, `test:`, `chore:`, `ci:`, `docs:`). End each commit message with the attribution lines the session provides.

## Review Focus

These inputs are not otherwise exercised by the spec's examples. Each is most likely to bite a real user and is pinned by a test in the owning task.

1. **Huge amounts.** Adding or negating `Money` beyond `long` range must throw `OverflowException`, never wrap to a wrong total. The test is in Task 2.
2. **Regional settings.** On a PC set to `de-DE`, which uses a comma as the decimal separator, `Money.Parse("12.50")` must still be ₹12.50 and formatting must still give `12,34,567.89`. The test is in Task 3.
3. **Impossible or leap dates typed at the prompt.** `29-2-27` must be rejected and `29-2-28` accepted. `31-4-2026` must be rejected. The test is in Task 4.
4. **Stray whitespace in names.** Tabs, non-breaking spaces, and double or trailing spaces in a typed name must still find `HDFC Bank`. The test is in Task 5.
5. **A report period crossing 31 March.** It must throw `ArgumentException`. It must never return a trial balance that silently mixes two years' profit and loss ledgers. The test is in Task 12.

---

## File Structure

```
OpenHisabKitab.slnx
global.json
Directory.Build.props
.gitignore  .gitattributes  .editorconfig
LICENSE  README.md  CONTRIBUTING.md
.github/workflows/ci.yml
.github/ISSUE_TEMPLATE/bug.yml
.github/ISSUE_TEMPLATE/feature.yml
src/OpenHisabKitab.Core/
  OpenHisabKitab.Core.csproj
  Amounts/Money.cs  Amounts/Quantity.cs  Amounts/Rounding.cs
  Formatting/IndianNumberFormat.cs
  Time/FinancialYear.cs  Time/DateRange.cs  Time/DateInput.cs  Time/PeriodInput.cs
  Commands/CommandParser.cs  Commands/FunctionCatalog.cs  Commands/NameMatcher.cs
  Accounts/Ids.cs  Accounts/AccountNature.cs  Accounts/GroupKind.cs  Accounts/RequiredLedger.cs
  Accounts/ChartOfAccounts.cs  Accounts/DefaultChart.cs
  Vouchers/VoucherType.cs  Vouchers/PostingLine.cs  Vouchers/VoucherDraft.cs
  Vouchers/VoucherValidator.cs  Vouchers/NumberSeries.cs
  Gst/GstRate.cs  Gst/StateCode.cs  Gst/GstData.cs  Gst/gst-data.json  Gst/GstRules.cs
  Gst/InvoiceModels.cs  Gst/InvoiceCalculator.cs  Gst/GstPosting.cs
  Reports/PostingRecord.cs  Reports/Books.cs  Reports/TrialBalance.cs
  Reports/LedgerStatement.cs  Reports/ProfitAndLoss.cs  Reports/BalanceSheet.cs
tests/OpenHisabKitab.Core.Tests/
  OpenHisabKitab.Core.Tests.csproj
  RepositoryTextTests.cs
  (one test file per production file, same folder names)
  Fixtures/GoldenCompany.cs  Fixtures/RandomBooks.cs
```

Namespaces follow folders, for example `OpenHisabKitab.Core.Amounts`. The folder is `Amounts`, not `Money`, so the namespace does not clash with the `Money` type.

---

### Task 1: Repository scaffold, CI, and the dash rule

**Files:**
- Create: `OpenHisabKitab.slnx`, `global.json`, `Directory.Build.props`, `.gitattributes`, `.editorconfig`, `LICENSE`, `README.md`, `CONTRIBUTING.md`, `.github/workflows/ci.yml`, `.github/ISSUE_TEMPLATE/bug.yml`, `.github/ISSUE_TEMPLATE/feature.yml`
- Create: `src/OpenHisabKitab.Core/OpenHisabKitab.Core.csproj`, `tests/OpenHisabKitab.Core.Tests/OpenHisabKitab.Core.Tests.csproj`
- Modify: `.gitignore`. Append `bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`. Keep the existing lines.
- Test: `tests/OpenHisabKitab.Core.Tests/RepositoryTextTests.cs`

**Interfaces:**
- Produces: `RepositoryText.FindForbiddenDashes(string text) -> IReadOnlyList<(int Line, int Column)>`. This is a static class inside the test project. It is not product code.

- [ ] **Step 1: Create the solution and projects**

  Run from the repo root:
  ```
  dotnet new sln -n OpenHisabKitab --format slnx
  dotnet new classlib -n OpenHisabKitab.Core -o src/OpenHisabKitab.Core
  dotnet new xunit -n OpenHisabKitab.Core.Tests -o tests/OpenHisabKitab.Core.Tests
  dotnet sln OpenHisabKitab.slnx add src/OpenHisabKitab.Core tests/OpenHisabKitab.Core.Tests
  dotnet add tests/OpenHisabKitab.Core.Tests reference src/OpenHisabKitab.Core
  ```
  Then:
  - Delete the template files `Class1.cs` and `UnitTest1.cs`.
  - Move `TargetFramework`, `Nullable`, and `ImplicitUsings` into `Directory.Build.props`, and add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<LangVersion>latest</LangVersion>` there.
  - In `global.json`, set `{"sdk":{"version":"10.0.402","rollForward":"latestFeature"}}`.
  - In `.gitattributes`, set `* text=auto eol=crlf` and `*.sh text eol=lf`.

- [ ] **Step 2: Write the failing test**

```csharp
public class RepositoryTextTests
{
    [Fact]
    public void FindForbiddenDashes_reports_line_and_column()
    {
        var text = "ok\nbad \u2014 here\nalso \u2013";
        Assert.Equal(new[] { (2, 5), (3, 6) }, RepositoryText.FindForbiddenDashes(text));
    }

    [Fact]
    public void FindForbiddenDashes_allows_ascii_hyphen() =>
        Assert.Empty(RepositoryText.FindForbiddenDashes("INV/26-27/00001"));

    [Fact]
    public void Repository_contains_no_en_or_em_dashes()
    {
        // Walk up from AppContext.BaseDirectory to the folder holding OpenHisabKitab.slnx.
        // Scan every file except under .git, bin, obj, .vs, .superpowers, .remember,
        // and skip binary extensions: .png .ico .dll .exe .pdb .zip .snk
        var offenders = RepositoryText.ScanRepository();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }
}
```

  Line and column are 1 based. `ScanRepository()` returns strings formatted as `path:line:column`. Source files must spell the characters as `\u2013` and `\u2014` escapes, never literally.

- [ ] **Step 3: Run the tests and confirm they fail to compile**

  Run: `dotnet test OpenHisabKitab.slnx`
  Expected: build error, `RepositoryText` does not exist.

- [ ] **Step 4: Implement `RepositoryText` in `tests/OpenHisabKitab.Core.Tests/RepositoryTextTests.cs`**

  Some file writing tools turn the escape text `\u2014` into the real character. After writing the file, run `grep -c 'u2014' tests/OpenHisabKitab.Core.Tests/RepositoryTextTests.cs`. Expected: 1 or more. If the result is 0, rewrite the line with the escape text. Apply the same check to the `u00A0` escape in `NameMatcherTests.cs` (Task 5).

- [ ] **Step 5: Write the repository files**
  - **`LICENSE`:** standard MIT text with the copyright line from Global Constraints.
  - **`README.md`:** at most 40 lines. It has:
    - A one line description: "Open source accounting for Indian businesses."
    - Status: "In development. Not ready for use."
    - Build commands: `dotnet build`, `dotnet test`.
    - A licence line.
    - No badges or marketing text.
  - **`CONTRIBUTING.md`:** at most 30 lines. It covers:
    - The build and test commands.
    - The rules: no en or em dashes; all money in paise; every calculation needs a test; keep on-screen text short.
    - The conventional commit prefixes.
  - **Issue templates:** a bug template with fields for steps, expected, actual, and version, and a feature template with fields for problem and proposal.

- [ ] **Step 6: Write `.github/workflows/ci.yml`**

```yaml
name: CI
on: [push, pull_request]
jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet build OpenHisabKitab.slnx -c Release
      - run: dotnet test OpenHisabKitab.slnx -c Release --no-build
```

- [ ] **Step 7: Run the tests and confirm they pass**

  Run: `dotnet test OpenHisabKitab.slnx`
  Expected: `Passed!` with 3 tests and 0 failures.

- [ ] **Step 8: Record the build commands in the local `CLAUDE.md`**

  Append to `CLAUDE.md` under "Rules and guidelines": the build and test commands, and the dash rule. The file is gitignored, so it is not committed.

- [ ] **Step 9: Commit**

  ```
  git add -A
  git commit -m "chore: scaffold solution, CI, licence, and dash check"
  ```

---

### Task 2: Money, Quantity, Rounding

**Files:**
- Create: `src/OpenHisabKitab.Core/Amounts/Money.cs`, `Quantity.cs`, `Rounding.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Amounts/MoneyTests.cs`, `QuantityTests.cs`, `RoundingTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct Money(long Paise) : IComparable<Money>` with:
    - `static Money Zero`
    - `static Money FromRupees(long rupees)`
    - operators `+`, binary `-`, unary `-`, `<`, `>`, `<=`, `>=`, all using `checked` arithmetic
    - `static Money Parse(string)`, which throws `FormatException`
    - `static bool TryParse(string?, out Money)`
    - `static Money Sum(IEnumerable<Money>)`
    - `ToString()`, which returns invariant `"-1234.50"` style text
  - `static class Rounding` with `static long HalfAwayFromZero(decimal value)`.
  - `readonly record struct Quantity(long Thousandths)` with `decimal Times(Money unitPrice)`, the exact product in paise.

- [ ] **Step 1: Write the failing tests**

```csharp
public class MoneyTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 1200)]
    [InlineData("12.5", 1250)]
    [InlineData("12.05", 1205)]
    [InlineData("1,23,456.78", 12345678)]
    [InlineData("-35,000", -3500000)]
    [InlineData(" 7.10 ", 710)]
    public void Parse_accepts_valid_input(string text, long paise) =>
        Assert.Equal(new Money(paise), Money.Parse(text));

    [Theory]
    [InlineData("")] [InlineData("abc")] [InlineData("1.234")] [InlineData("1..2")]
    [InlineData("--1")] [InlineData("1e5")] [InlineData(",")] [InlineData("1,")]
    [InlineData(",1")] [InlineData("92233720368547758.08")]
    public void TryParse_rejects_invalid_input(string text) =>
        Assert.False(Money.TryParse(text, out _));

    [Fact]
    public void Addition_and_negation() {
        Assert.Equal(new Money(300), new Money(100) + new Money(200));
        Assert.Equal(new Money(-100), -new Money(100));
    }

    [Fact] // Review Focus 1
    public void Overflow_throws_instead_of_wrapping() {
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
        Assert.Throws<OverflowException>(() => -new Money(long.MinValue));
        Assert.Throws<OverflowException>(() => Money.Sum(new[] { new Money(long.MaxValue), new Money(1) }));
    }
}

public class RoundingTests
{
    [Theory]
    [InlineData("2.5", 3)] [InlineData("-2.5", -3)] [InlineData("2.4999", 2)]
    [InlineData("0.5", 1)] [InlineData("-0.5", -1)] [InlineData("7", 7)]
    public void HalfAwayFromZero(string value, long expected) =>
        Assert.Equal(expected, Rounding.HalfAwayFromZero(decimal.Parse(value, CultureInfo.InvariantCulture)));
}

public class QuantityTests
{
    [Fact]
    public void Times_is_exact() =>
        Assert.Equal(49997.5m, new Quantity(2500).Times(new Money(19999)));
}
```

  Parse rules: optional leading `-`, digits, and commas only between digits. Commas are accepted in any grouping. An optional `.` must be followed by 1 or 2 digits. Surrounding whitespace is trimmed.

- [ ] **Step 2: Run the tests and confirm they fail**

  Run: `dotnet test --filter "FullyQualifiedName~Amounts"`
  Expected: build errors because the types are missing.

- [ ] **Step 3: Implement `Money`, `Rounding`, and `Quantity` as specified in Interfaces**

  `Rounding` uses `decimal.Round(value, MidpointRounding.AwayFromZero)` and then a checked conversion to `long`.

- [ ] **Step 4: Run the tests and confirm they pass**

  Run: `dotnet test --filter "FullyQualifiedName~Amounts"`
  Expected: all pass.

- [ ] **Step 5: Commit**

  `feat: add Money, Quantity, and rounding`

---

### Task 3: Indian number formatting

**Files:**
- Create: `src/OpenHisabKitab.Core/Formatting/IndianNumberFormat.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Formatting/IndianNumberFormatTests.cs`

**Interfaces:**
- Consumes: `Money` (Task 2).
- Produces:
  - `enum NegativeStyle { Brackets, Minus }`
  - `static class IndianNumberFormat` with:
    - `string Format(Money m, NegativeStyle style = NegativeStyle.Brackets)`
    - `string FormatDrCr(Money m)`

- [ ] **Step 1: Write the failing tests**

```csharp
public class IndianNumberFormatTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(99999, "999.99")]
    [InlineData(100000, "1,000.00")]
    [InlineData(12345678, "1,23,456.78")]
    [InlineData(123456789, "12,34,567.89")]
    [InlineData(10000000000, "10,00,00,000.00")]
    [InlineData(-3500000, "(35,000.00)")]
    public void Format_uses_indian_grouping(long paise, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.Format(new Money(paise)));

    [Fact]
    public void Format_minus_style() =>
        Assert.Equal("-35,000.00", IndianNumberFormat.Format(new Money(-3500000), NegativeStyle.Minus));

    [Fact]
    public void Format_handles_long_min_value() =>
        Assert.Equal("(92,23,37,20,36,85,47,758.08)", IndianNumberFormat.Format(new Money(long.MinValue)));

    [Theory]
    [InlineData(24850000, "2,48,500.00 Dr")]
    [InlineData(-3500000, "35,000.00 Cr")]
    [InlineData(0, "0.00")]
    public void FormatDrCr(long paise, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.FormatDrCr(new Money(paise)));

    [Fact] // Review Focus 2
    public void Results_ignore_current_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("12,34,567.89", IndianNumberFormat.Format(new Money(123456789)));
            Assert.Equal(new Money(1250), Money.Parse("12.50"));
        } finally { CultureInfo.CurrentCulture = saved; }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Formatting"`

- [ ] **Step 3: Implement `IndianNumberFormat`**

  Work on the absolute value as `ulong`, so that `long.MinValue` is safe:
  - Split it into rupees and paise.
  - Rupees: the last 3 digits form one group. The digits before them are grouped in pairs from the right.

- [ ] **Step 4: Run the tests and confirm they pass.**

- [ ] **Step 5: Commit**

  `feat: add Indian number formatting`

---

### Task 4: Financial year, dates, and periods

**Files:**
- Create: `src/OpenHisabKitab.Core/Time/FinancialYear.cs`, `DateRange.cs`, `DateInput.cs`, `PeriodInput.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Time/FinancialYearTests.cs`, `DateInputTests.cs`, `PeriodInputTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct DateRange(DateOnly From, DateOnly To)`. The constructor throws `ArgumentException` if `From > To`. Members:
    - `bool Contains(DateOnly d)`
  - `readonly record struct FinancialYear(int StartYear)`. Members:
    - `DateOnly Start` (1 April)
    - `DateOnly End` (31 March)
    - `DateRange Range`
    - `string Label`, for example `2026-27`
    - `string ShortLabel`, for example `26-27`
    - `static FinancialYear Containing(DateOnly d)`
    - `DateRange Quarter(int q)`, where q is 1 to 4
    - `DateRange Month(int calendarMonth)`
  - `static class DateInput`. Members:
    - `bool TryParse(string token, DateOnly today, out DateOnly date)`
  - `static class PeriodInput`. Members:
    - `bool TryParse(IReadOnlyList<string> tokens, DateOnly today, out DateRange range)`

- [ ] **Step 1: Write the failing tests**

```csharp
public class FinancialYearTests
{
    [Fact] public void Containing() {
        Assert.Equal(2026, FinancialYear.Containing(new DateOnly(2027, 3, 31)).StartYear);
        Assert.Equal(2027, FinancialYear.Containing(new DateOnly(2027, 4, 1)).StartYear);
    }
    [Fact] public void Labels() {
        Assert.Equal("2026-27", new FinancialYear(2026).Label);
        Assert.Equal("26-27", new FinancialYear(2026).ShortLabel);
        Assert.Equal("2099-00", new FinancialYear(2099).Label);
    }
    [Fact] public void Quarter_and_month() {
        Assert.Equal(new DateRange(new(2027, 1, 1), new(2027, 3, 31)), new FinancialYear(2026).Quarter(4));
        Assert.Equal(new DateRange(new(2027, 2, 1), new(2027, 2, 28)), new FinancialYear(2026).Month(2));
        Assert.Equal(new DateRange(new(2028, 2, 1), new(2028, 2, 29)), new FinancialYear(2027).Month(2));
    }
}

public class DateInputTests
{
    static readonly DateOnly Today = new(2026, 10, 26);

    [Theory]
    [InlineData("31-03-2027", 2027, 3, 31)]
    [InlineData("1/4/2026", 2026, 4, 1)]
    [InlineData("31-3-27", 2027, 3, 31)]
    [InlineData("5-1", 2027, 1, 5)]     // d-m resolves inside the current FY 2026-27
    [InlineData("5-4", 2026, 4, 5)]
    [InlineData("TODAY", 2026, 10, 26)]
    [InlineData("29-2-28", 2028, 2, 29)]
    public void Accepts(string token, int y, int m, int d) {
        Assert.True(DateInput.TryParse(token, Today, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Theory] // Review Focus 3
    [InlineData("29-2-27")] [InlineData("31-4-2026")] [InlineData("0-1-2026")]
    [InlineData("1-13")] [InlineData("abc")] [InlineData("")] [InlineData("1-1-1-1")]
    public void Rejects(string token) => Assert.False(DateInput.TryParse(token, Today, out _));
}

public class PeriodInputTests
{
    static readonly DateOnly Today = new(2026, 10, 26);
    static DateRange R(int y1, int m1, int d1, int y2, int m2, int d2) => new(new(y1, m1, d1), new(y2, m2, d2));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Accepts(string[] tokens, DateRange expected) {
        Assert.True(PeriodInput.TryParse(tokens, Today, out var range));
        Assert.Equal(expected, range);
    }
    public static TheoryData<string[], DateRange> Cases => new() {
        { new[] { "apr" }, R(2026, 4, 1, 2026, 4, 30) },
        { new[] { "January" }, R(2027, 1, 1, 2027, 1, 31) },
        { new[] { "q1" }, R(2026, 4, 1, 2026, 6, 30) },
        { new[] { "Q4" }, R(2027, 1, 1, 2027, 3, 31) },
        { new[] { "fy" }, R(2026, 4, 1, 2027, 3, 31) },
        { new[] { "fy25" }, R(2025, 4, 1, 2026, 3, 31) },
        { new[] { "1-4", "30-6" }, R(2026, 4, 1, 2026, 6, 30) },
    };

    [Theory]
    [InlineData("30-6", "1-4")] [InlineData("q5", null)] [InlineData("fyx", null)] [InlineData("xyz", null)]
    public void Rejects(string a, string? b) =>
        Assert.False(PeriodInput.TryParse(b is null ? new[] { a } : new[] { a, b }, Today, out _));
}
```

  Rules:
  - Two digit years mean `2000 + yy`.
  - Month names match the 3 letter form or the full English name, ignoring case.
  - `fyNN` means the financial year starting `2000 + NN`.
  - A bare `d-m` date resolves to the financial year that contains `today`: months 4 to 12 fall in the start year, months 1 to 3 in the following year.

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Time"`

- [ ] **Step 3: Implement the four types.** `PeriodInput.TryParse` returns false, rather than throwing, when the from date is after the to date. Separators are `-` or `/`. Use `DateOnly` construction inside a `try` block, or check `DateTime.DaysInMonth` first, to reject impossible dates.

- [ ] **Step 4: Run the tests and confirm they pass.**

- [ ] **Step 5: Commit**

  `feat: add financial year, date, and period parsing`

---

### Task 5: Command parser, function catalog, name matcher

**Files:**
- Create: `src/OpenHisabKitab.Core/Commands/CommandParser.cs`, `FunctionCatalog.cs`, `NameMatcher.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Commands/CommandParserTests.cs`, `FunctionCatalogTests.cs`, `NameMatcherTests.cs`

**Interfaces:**
- Produces:
  - `sealed record ParsedCommand(string Code, IReadOnlyList<string> Args)`
  - `static class CommandParser` with `bool TryParse(string? input, out ParsedCommand command)`
    - A code is exactly 4 characters, from A to Z and 0 to 9, starting with a letter.
    - The code is upper-cased. Arguments keep the case they were typed in.
    - Input is split on any whitespace, including tabs and U+00A0.
  - `enum FunctionCategory { System, Masters, Vouchers, Reports, Gst }`
  - `sealed record FunctionInfo(string Code, string Name, FunctionCategory Category)`
  - `static class FunctionCatalog` with:
    - `IReadOnlyList<FunctionInfo> All`
    - `FunctionInfo? Find(string code)`
    - `IReadOnlyList<FunctionInfo> Search(string text)`
  - `enum MatchKind { Exact, Prefix, Words, Ambiguous, None }`
  - `sealed record NameMatch(MatchKind Kind, IReadOnlyList<string> Candidates)`
  - `static class NameMatcher` with `NameMatch Match(string query, IReadOnlyList<string> names)`

`FunctionCatalog.All` lists exactly these entries, in this order. The names are copied from spec 6.3.

| Category | Entries |
|---|---|
| System | HELP Help, SETG Settings, KEYS Keybindings, CMPY Company, USER Users, AUDT Audit trail, LOCK Period lock, BKUP Backup, DATE Working date, FIND Find, RFSH Refresh, PRNT Print |
| Masters | ACCT Accounts, PRTY Parties, ITEM Items |
| Vouchers | SALE Sales invoice, PURC Purchase invoice, CRNT Credit note, DBNT Debit note, RCPT Receipt, PYMT Payment, JRNL Journal, CNTR Contra |
| Reports | DAYB Day book, LEDG Ledger, TRLB Trial balance, PNLS Profit and loss, BSHT Balance sheet, CFLO Cash flow, OUTS Outstanding |
| Gst | GST1 GSTR-1, GS3B GSTR-3B, G2BR GSTR-2B match, EINV E-invoice |

`Search` returns, without duplicates:
1. First, entries whose code starts with the text, in catalog order.
2. Then, entries where any word of the name starts with the text, in catalog order.

Matching ignores case. Empty text returns `All`.

`NameMatcher` normalises both the query and the names before comparing: it trims them, collapses any whitespace run (including tabs and U+00A0) to one space, and ignores case. It then tries these rules in order:
1. If one name equals the query, the result is `Exact`.
2. Otherwise, if exactly one name starts with the query, the result is `Prefix`. If several do, the result is `Ambiguous` with those names.
3. Otherwise, it takes the names that contain every word of the query as a substring. If there is exactly one, the result is `Words`. If there are several, the result is `Ambiguous`. If there are none, the result is `None`.

Candidates keep their original spelling and input order.

- [ ] **Step 1: Write the failing tests**

```csharp
public class CommandParserTests
{
    [Fact] public void Parses_code_and_args() {
        Assert.True(CommandParser.TryParse("  ledg \t hdfc  Bank ", out var c));
        Assert.Equal("LEDG", c.Code);
        Assert.Equal(new[] { "hdfc", "Bank" }, c.Args);
    }
    [Fact] public void Accepts_digit_codes() {
        Assert.True(CommandParser.TryParse("gst1 q2", out var c));
        Assert.Equal("GST1", c.Code);
    }
    [Theory] [InlineData("")] [InlineData("   ")] [InlineData(null)] [InlineData("TRL")]
    [InlineData("TRLB1")] [InlineData("12AB")] [InlineData("TR-B")]
    public void Rejects(string? input) => Assert.False(CommandParser.TryParse(input, out _));
}

public class FunctionCatalogTests
{
    [Fact] public void Has_34_unique_four_character_codes() {
        Assert.Equal(34, FunctionCatalog.All.Count);
        Assert.Equal(34, FunctionCatalog.All.Select(f => f.Code).Distinct().Count());
        Assert.All(FunctionCatalog.All, f => Assert.True(CommandParser.TryParse(f.Code, out _)));
    }
    [Fact] public void Search_code_prefix_first_then_name_words() {
        var r = FunctionCatalog.Search("tr").Select(f => f.Code).ToList();
        Assert.Equal("TRLB", r[0]);
        Assert.Contains("AUDT", r);
        Assert.Equal(new[] { "TRLB" }, FunctionCatalog.Search("trial").Select(f => f.Code));
    }
    [Fact] public void Find_ignores_case() => Assert.Equal("Trial balance", FunctionCatalog.Find("trlb")!.Name);
}

public class NameMatcherTests
{
    static readonly string[] Names = { "HDFC Bank", "HDFC Bank OD", "ICICI Bank", "Cash" };

    [Theory]
    [InlineData("hdfc bank", MatchKind.Exact, "HDFC Bank")]
    [InlineData("  hdfc\u00A0\tbank ", MatchKind.Exact, "HDFC Bank")] // Review Focus 4
    [InlineData("icici", MatchKind.Prefix, "ICICI Bank")]
    [InlineData("bank od", MatchKind.Words, "HDFC Bank OD")]
    public void Single_match(string q, MatchKind kind, string name) {
        var m = NameMatcher.Match(q, Names);
        Assert.Equal(kind, m.Kind);
        Assert.Equal(new[] { name }, m.Candidates);
    }
    [Fact] public void Ambiguous_prefix() =>
        Assert.Equal(new[] { "HDFC Bank", "HDFC Bank OD" }, NameMatcher.Match("hdfc", Names).Candidates);
    [Fact] public void Ambiguous_words() =>
        Assert.Equal(MatchKind.Ambiguous, NameMatcher.Match("bank", Names).Kind);
    [Fact] public void None() => Assert.Equal(MatchKind.None, NameMatcher.Match("xyz", Names).Kind);
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Commands"`

- [ ] **Step 3: Implement the three types.**

- [ ] **Step 4: Run the tests and confirm they pass.**

- [ ] **Step 5: Commit**

  `feat: add command parser, function catalog, and name matcher`

---

### Task 6: Chart of accounts and the default chart

**Files:**
- Create: `src/OpenHisabKitab.Core/Accounts/Ids.cs`, `AccountNature.cs`, `GroupKind.cs`, `RequiredLedger.cs`, `ChartOfAccounts.cs`, `DefaultChart.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Accounts/ChartOfAccountsTests.cs`

**Interfaces:**
- Produces:
  - **Identifiers:** `readonly record struct GroupId(int Value)` and `readonly record struct LedgerId(int Value)`.
  - **Enums:**
    - `enum AccountNature { Asset, Liability, Income, Expense }`
    - `enum GroupKind { None, Capital, ReservesAndSurplus, TradePayables, DutiesAndTaxes, GstInputCredit, CashInHand, BankAccounts, TradeReceivables, RevenueFromOperations, OtherIncome, Purchases, OtherExpenses }`
    - `enum RequiredLedger { Cash, Capital, ProfitAndLoss, RoundOff, Sales, Purchases, OutputCgst, OutputSgst, OutputUtgst, OutputIgst, OutputCess, InputCgst, InputSgst, InputUtgst, InputIgst, InputCess }`
  - **Records:**
    - `sealed record AccountGroup(GroupId Id, string Name, GroupId? Parent, AccountNature Nature, GroupKind Kind)`
    - `sealed record LedgerAccount(LedgerId Id, string Name, GroupId Group, RequiredLedger? Required)`
  - **`sealed class ChartOfAccounts`** is immutable. It has these members:
    - `IReadOnlyList<AccountGroup> Groups`
    - `IReadOnlyList<LedgerAccount> Ledgers`, in creation order. This is the order reports use.
    - `AccountNature NatureOf(LedgerId)`
    - `bool IsCashOrBank(LedgerId)`, which is true when the ledger's group, or any ancestor group, has kind `CashInHand` or `BankAccounts`.
    - `bool Contains(LedgerId)`
    - `GroupId Group(GroupKind kind)`
    - `LedgerId Required(RequiredLedger r)`
    - `(ChartOfAccounts Chart, LedgerId Id) AddLedger(string name, GroupId group)`
    - `(ChartOfAccounts Chart, GroupId Id) AddGroup(string name, GroupId parent)`. A child group inherits its parent's nature.
    - Names must be unique, compared after trimming and ignoring case, separately among groups and among ledgers. A duplicate throws `ChartException`. A blank name also throws.
  - **`static class DefaultChart`** with `ChartOfAccounts Create()`.

The default chart is the designer's call under spec section 13. Root groups (no parent) are listed with their nature. Kinds are given in brackets.

| Root group (nature) | Child groups |
|---|---|
| Equity (Liability) | Capital [Capital], Reserves and Surplus [ReservesAndSurplus] |
| Non Current Liabilities (Liability) | Long Term Borrowings |
| Current Liabilities (Liability) | Short Term Borrowings, Trade Payables [TradePayables], Duties and Taxes [DutiesAndTaxes], Provisions, Other Current Liabilities |
| Non Current Assets (Asset) | Fixed Assets, Investments |
| Current Assets (Asset) | Inventories, Trade Receivables [TradeReceivables], Cash in Hand [CashInHand], Bank Accounts [BankAccounts], GST Input Credit [GstInputCredit], Loans and Advances, Other Current Assets |
| Income (Income) | Revenue from Operations [RevenueFromOperations], Other Income [OtherIncome] |
| Expenses (Expense) | Purchases [Purchases], Direct Expenses, Employee Benefits, Finance Costs, Depreciation, Other Expenses [OtherExpenses] |

Required ledgers and their groups:

| Ledger | Group |
|---|---|
| Cash | Cash in Hand |
| Capital | Capital |
| Profit and Loss | Reserves and Surplus |
| Round Off | Other Expenses |
| Sales | Revenue from Operations |
| Purchases | Purchases |
| Output CGST, Output SGST, Output UTGST, Output IGST, Output Cess | Duties and Taxes |
| Input CGST, Input SGST, Input UTGST, Input IGST, Input Cess | GST Input Credit |

- [ ] **Step 1: Write the failing tests**

```csharp
public class ChartOfAccountsTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();

    [Fact] public void Every_required_ledger_exists() =>
        Assert.All(Enum.GetValues<RequiredLedger>(), r => Assert.True(c.Contains(c.Required(r))));

    [Theory]
    [InlineData(RequiredLedger.Cash, AccountNature.Asset)]
    [InlineData(RequiredLedger.Capital, AccountNature.Liability)]
    [InlineData(RequiredLedger.ProfitAndLoss, AccountNature.Liability)]
    [InlineData(RequiredLedger.Sales, AccountNature.Income)]
    [InlineData(RequiredLedger.Purchases, AccountNature.Expense)]
    [InlineData(RequiredLedger.RoundOff, AccountNature.Expense)]
    [InlineData(RequiredLedger.OutputCgst, AccountNature.Liability)]
    [InlineData(RequiredLedger.InputIgst, AccountNature.Asset)]
    public void Natures(RequiredLedger r, AccountNature n) => Assert.Equal(n, c.NatureOf(c.Required(r)));

    [Fact] public void Cash_or_bank() {
        var (c2, hdfc) = c.AddLedger("HDFC Bank", c.Group(GroupKind.BankAccounts));
        Assert.True(c2.IsCashOrBank(hdfc));
        Assert.True(c2.IsCashOrBank(c2.Required(RequiredLedger.Cash)));
        Assert.False(c2.IsCashOrBank(c2.Required(RequiredLedger.Sales)));
    }

    [Fact] public void Sub_group_inherits_nature_and_cash_kind() {
        var (c2, g) = c.AddGroup("Petty Cash", c.Group(GroupKind.CashInHand));
        var (c3, l) = c2.AddLedger("Office Petty Cash", g);
        Assert.Equal(AccountNature.Asset, c3.NatureOf(l));
        Assert.True(c3.IsCashOrBank(l));
    }

    [Theory] [InlineData("cash ")] [InlineData("CASH")] [InlineData("  ")]
    public void Rejects_duplicate_or_blank_ledger_names(string name) =>
        Assert.Throws<ChartException>(() => c.AddLedger(name, c.Group(GroupKind.CashInHand)));

    [Fact] public void Add_returns_new_chart_and_leaves_original_unchanged() {
        var before = c.Ledgers.Count;
        c.AddLedger("Rent", c.Group(GroupKind.OtherExpenses));
        Assert.Equal(before, c.Ledgers.Count);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Accounts"`

- [ ] **Step 3: Implement the types.** Ids are assigned sequentially from 1, separately for groups and for ledgers.

- [ ] **Step 4: Run the tests and confirm they pass.**

- [ ] **Step 5: Commit**

  `feat: add chart of accounts and default chart`

---

### Task 7: Vouchers, validation, consolidation

**Files:**
- Create: `src/OpenHisabKitab.Core/Vouchers/VoucherType.cs`, `PostingLine.cs`, `VoucherDraft.cs`, `VoucherValidator.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Vouchers/VoucherValidatorTests.cs`

**Interfaces:**
- Consumes: `Money`, `ChartOfAccounts`, `LedgerId`.
- Produces:
  - `enum VoucherType { Sale, Purchase, CreditNote, DebitNote, Receipt, Payment, Journal, Contra }`
  - `sealed record PostingLine(LedgerId Ledger, Money Amount)`
  - `sealed record VoucherDraft(VoucherType Type, DateOnly Date, IReadOnlyList<PostingLine> Lines)`
  - `enum VoucherError { Unbalanced, TooFewLines, ZeroLine, UnknownLedger, ContraNonCashLedger, ReceiptWithoutCashDebit, PaymentWithoutCashCredit }`
  - `static class VoucherValidator` with `IReadOnlyList<VoucherError> Validate(VoucherDraft v, ChartOfAccounts chart)`. It returns every error that applies, each once.
  - `static class Postings` with `IReadOnlyList<PostingLine> Consolidate(IEnumerable<PostingLine> lines)`. It merges lines for the same ledger, keeps the order of first appearance, and drops ledgers that net to zero. This implements spec 5.1's rule of one posting line per ledger per voucher.

The validation rules are:
- At least 2 lines.
- No line amount of zero.
- Every ledger exists.
- The lines sum to zero.
- Contra: every ledger is cash or bank.
- Receipt: at least one debit line on a cash or bank ledger.
- Payment: at least one credit line on a cash or bank ledger.

- [ ] **Step 1: Write the failing tests**

```csharp
public class VoucherValidatorTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();
    LedgerId Cash => c.Required(RequiredLedger.Cash);
    LedgerId Sales => c.Required(RequiredLedger.Sales);
    LedgerId Capital => c.Required(RequiredLedger.Capital);
    static readonly DateOnly D = new(2026, 10, 26);
    VoucherDraft V(VoucherType t, params (LedgerId l, long p)[] lines) =>
        new(t, D, lines.Select(x => new PostingLine(x.l, new Money(x.p))).ToList());

    [Fact] public void Balanced_journal_is_valid() =>
        Assert.Empty(VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100), (Capital, -100)), c));
    [Fact] public void Unbalanced() =>
        Assert.Equal(new[] { VoucherError.Unbalanced }, VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100), (Capital, -99)), c));
    [Fact] public void Single_line() =>
        Assert.Contains(VoucherError.TooFewLines, VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100)), c));
    [Fact] public void Zero_line() =>
        Assert.Contains(VoucherError.ZeroLine, VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 0), (Capital, 0)), c));
    [Fact] public void Unknown_ledger() =>
        Assert.Contains(VoucherError.UnknownLedger, VoucherValidator.Validate(V(VoucherType.Journal, (new LedgerId(9999), 1), (Cash, -1)), c));
    [Fact] public void Contra_rejects_non_cash() =>
        Assert.Contains(VoucherError.ContraNonCashLedger, VoucherValidator.Validate(V(VoucherType.Contra, (Cash, 1), (Sales, -1)), c));
    [Fact] public void Receipt_needs_cash_debit() =>
        Assert.Contains(VoucherError.ReceiptWithoutCashDebit, VoucherValidator.Validate(V(VoucherType.Receipt, (Sales, 1), (Cash, -1)), c));
    [Fact] public void Payment_with_cash_credit_is_valid() =>
        Assert.Empty(VoucherValidator.Validate(V(VoucherType.Payment, (Capital, 500), (Cash, -500)), c));

    [Fact] public void Consolidate_merges_and_drops_zero() {
        var r = Postings.Consolidate(new[] {
            new PostingLine(Cash, new Money(100)), new PostingLine(Sales, new Money(-150)),
            new PostingLine(Cash, new Money(50)), new PostingLine(Capital, new Money(10)),
            new PostingLine(Capital, new Money(-10)) });
        Assert.Equal(new[] { new PostingLine(Cash, new Money(150)), new PostingLine(Sales, new Money(-150)) }, r);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Vouchers"`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: add voucher validation and posting consolidation`

---

### Task 8: GST reference data and tax split

**Files:**
- Create: `src/OpenHisabKitab.Core/Gst/GstRate.cs`, `StateCode.cs`, `GstData.cs`, `gst-data.json`, `GstRules.cs`
- Modify: `src/OpenHisabKitab.Core/OpenHisabKitab.Core.csproj`. Add `<EmbeddedResource Include="Gst\gst-data.json" />`.
- Test: `tests/OpenHisabKitab.Core.Tests/Gst/GstDataTests.cs`, `GstRulesTests.cs`

**Interfaces:**
- Consumes: `Money`.
- Produces:
  - `readonly record struct GstRate(int BasisPoints)`. The constructor throws if the value is negative.
  - `readonly record struct StateCode(string Value)`. The constructor requires exactly 2 ASCII digits.
  - `enum StateType { State, UnionTerritoryWithLegislature, UnionTerritory, Other }`
  - `sealed record StateInfo(StateCode Code, string Name, StateType Type)`
  - `sealed class GstData` with:
    - `static GstData LoadEmbedded()`
    - `static GstData Parse(string json)`, which throws `FormatException` on bad data
    - `string Version`
    - `IReadOnlyList<StateInfo> States`
    - `StateInfo State(StateCode)`, which throws `KeyNotFoundException`
    - `bool IsValidRate(GstRate rate, DateOnly on)`
    - `Money B2clThreshold(DateOnly on)`
  - `enum TaxSplit { CgstSgst, CgstUtgst, Igst }`
  - `static class GstRules` with `TaxSplit Split(StateCode supplier, StateCode placeOfSupply, bool sezOrExport, GstData data)`

The split is decided as follows:
- If `sezOrExport` is true, or the two codes differ, the split is `Igst`.
- Otherwise, if the place of supply is a `UnionTerritory` (one without a legislature), the split is `CgstUtgst`.
- Otherwise it is `CgstSgst`.

Shape of `gst-data.json`. Dates are ISO strings, and `to` may be `null`:
```json
{
  "version": "2026.10.1",
  "sources": [ { "item": "states", "url": "...", "checked": "2026-10-26" } ],
  "states": [ { "code": "24", "name": "Gujarat", "type": "State" } ],
  "rates":  [ { "basisPoints": 1800, "from": "2017-07-01", "to": null } ],
  "thresholds": [ { "key": "b2clInvoiceValue", "paise": 10000000, "from": "2024-08-01", "to": null } ],
  "schemas": { "einvoice": "1.1", "gstr1": "", "gstr2b": "" }
}
```

- [ ] **Step 1: Fill `gst-data.json` from official sources and record each URL in `sources`**

  **States.** Use the GST state code list from the GST portal or CBIC. It must include:
  - 01 to 24, 26, 27, and 29 to 38
  - 97 Other Territory
  - 96 Other Country

  Mark these as `UnionTerritory`: 04 Chandigarh, 26 Dadra and Nagar Haveli and Daman and Diu, 31 Lakshadweep, 35 Andaman and Nicobar Islands, and 38 Ladakh.

  Mark these as `UnionTerritoryWithLegislature`: 01 Jammu and Kashmir, 07 Delhi, and 34 Puducherry. Mark 96 and 97 as `Other`.

  **Rates.** Include 0, 0.1, 0.25, 1, 1.5, 3, 5, 6, 12, 18, and 28 percent from 2017-07-01. Add 40 percent from 2025-09-22. Check each slab and its dates against the CBIC rate notifications before committing.

  **B2CL threshold.** It is 25000000 paise (₹2,50,000) from 2017-07-01 to 2024-07-31, and 10000000 paise (₹1,00,000) from 2024-08-01, under Notification 12/2024 Central Tax.

  **Schemas.** Set `einvoice` to the version shown on the e-invoice portal. Leave `gstr1` and `gstr2b` empty; Plan 6 fills them in.

- [ ] **Step 2: Write the failing tests**

```csharp
public class GstDataTests
{
    readonly GstData d = GstData.LoadEmbedded();

    [Fact] public void Loads_states() {
        Assert.Equal("Gujarat", d.State(new StateCode("24")).Name);
        Assert.Equal(StateType.UnionTerritory, d.State(new StateCode("04")).Type);
        Assert.Equal(StateType.UnionTerritoryWithLegislature, d.State(new StateCode("07")).Type);
        Assert.Equal(StateType.Other, d.State(new StateCode("96")).Type);
    }
    [Fact] public void B2cl_threshold_by_date() {
        Assert.Equal(Money.FromRupees(250000), d.B2clThreshold(new DateOnly(2024, 7, 31)));
        Assert.Equal(Money.FromRupees(100000), d.B2clThreshold(new DateOnly(2024, 8, 1)));
    }
    [Theory]
    [InlineData(1800, 2026, 10, 26, true)] [InlineData(4000, 2025, 9, 21, false)]
    [InlineData(4000, 2025, 9, 22, true)] [InlineData(1700, 2026, 10, 26, false)]
    [InlineData(25, 2026, 10, 26, true)]
    public void Rate_validity(int bp, int y, int m, int day, bool ok) =>
        Assert.Equal(ok, d.IsValidRate(new GstRate(bp), new DateOnly(y, m, day)));
    [Theory] [InlineData("2")] [InlineData("ab")] [InlineData("123")]
    public void StateCode_requires_two_digits(string v) => Assert.Throws<ArgumentException>(() => new StateCode(v));
    [Fact] public void Parse_rejects_malformed_json() => Assert.Throws<FormatException>(() => GstData.Parse("{"));
}

public class GstRulesTests
{
    readonly GstData d = GstData.LoadEmbedded();
    TaxSplit S(string a, string b, bool sez = false) => GstRules.Split(new StateCode(a), new StateCode(b), sez, d);

    [Fact] public void Intra_state() => Assert.Equal(TaxSplit.CgstSgst, S("24", "24"));
    [Fact] public void Delhi_uses_sgst() => Assert.Equal(TaxSplit.CgstSgst, S("07", "07"));
    [Fact] public void Chandigarh_uses_utgst() => Assert.Equal(TaxSplit.CgstUtgst, S("04", "04"));
    [Fact] public void Inter_state() => Assert.Equal(TaxSplit.Igst, S("24", "27"));
    [Fact] public void Sez_is_inter_state() => Assert.Equal(TaxSplit.Igst, S("24", "24", sez: true));
    [Fact] public void Export() => Assert.Equal(TaxSplit.Igst, S("24", "96"));
}
```

- [ ] **Step 3: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Gst"`

- [ ] **Step 4: Implement the types.**
  - Parse the JSON with `System.Text.Json`, using `JsonDocument` or DTO records.
  - Validate while loading: unique state codes, no overlapping threshold ranges for the same key, and `from <= to`.
  - Throw `FormatException` with the reason if any check fails.

- [ ] **Step 5: Run the tests and confirm they pass.**

- [ ] **Step 6: Commit**

  `feat: add GST reference data and tax split rules`

---

### Task 9: Invoice calculator

**Files:**
- Create: `src/OpenHisabKitab.Core/Gst/InvoiceModels.cs`, `InvoiceCalculator.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Gst/InvoiceCalculatorTests.cs`

**Interfaces:**
- Consumes: `Money`, `Quantity`, `GstRate`, `TaxSplit`, `Rounding`.
- Produces:
  - `sealed record InvoiceLineInput(Quantity Quantity, Money UnitPrice, Money Discount, GstRate Rate, GstRate CessRate, bool PriceIncludesTax = false)`
  - `sealed record InvoiceLineResult(Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess)` with `Money Tax` and `Money Total`
  - `sealed record InvoiceResult(IReadOnlyList<InvoiceLineResult> Lines, Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess, Money RoundOff, Money GrandTotal)` with `Money Tax`
  - `enum InvoiceError { NoLines, NonPositiveQuantity, NegativePrice, NegativeDiscount, NegativeTaxable }`
  - `sealed class InvoiceException(InvoiceError error, int? lineIndex) : Exception`
  - `static class InvoiceCalculator` with `InvoiceResult Calculate(IReadOnlyList<InvoiceLineInput> lines, TaxSplit split, bool roundOff, bool reverseCharge)`

Rules (spec 5.3). All rounding is half away from zero, to the paisa.

1. **Gross.** `gross = Quantity.Times(UnitPrice) - Discount.Paise`, kept as a decimal.
2. **Taxable value, tax exclusive prices:** `taxable = round(gross)`.
3. **Tax on a taxable value `t`.** Each component is rounded separately:
   - Half components: `round(t * rate / 20000)`, used for CGST, and for SGST or UTGST.
   - IGST: `round(t * rate / 10000)`.
   - Cess: `round(t * cessRate / 10000)`.
4. **Taxable value, tax inclusive prices.** The inclusive amount is `A = round(gross)`.
   - Start from `t0 = round(A * 10000 / (10000 + rate + cessRate))`.
   - Try the candidates `t0`, then `t0 - 1`, then `t0 + 1`. Pick the first one whose `t + tax(t)` equals `A`, so the line total matches what the user typed.
   - If no candidate matches, use `t0`.
   - This tie-break is the designer's call, filling a gap in spec 5.3.
5. **Totals.** Invoice totals are the sums of the line amounts.
6. **Raw total.** It is `Taxable + Tax`, or just `Taxable` when `reverseCharge` is true, because the supplier is not paid the tax.
7. **Round off.**
   - When `roundOff` is true, the grand total is the raw total rounded to the nearest rupee, with 50 paise rounding up, and `RoundOff = GrandTotal - raw`.
   - When `roundOff` is false, `RoundOff` is 0.

- [ ] **Step 1: Write the failing tests**

```csharp
public class InvoiceCalculatorTests
{
    static InvoiceLineInput L(long qtyThousandths, long pricePaise, int bp, long discount = 0, int cess = 0, bool incl = false) =>
        new(new Quantity(qtyThousandths), new Money(pricePaise), new Money(discount), new GstRate(bp), new GstRate(cess), incl);

    [Fact] public void Intra_state_18_percent_with_round_off() {
        var r = InvoiceCalculator.Calculate(new[] { L(3000, 33333, 1800) }, TaxSplit.CgstSgst, roundOff: true, reverseCharge: false);
        Assert.Equal(new Money(99999), r.Taxable);
        Assert.Equal(new Money(9000), r.Cgst);
        Assert.Equal(new Money(9000), r.Sgst);
        Assert.Equal(new Money(1), r.RoundOff);
        Assert.Equal(new Money(118000), r.GrandTotal);
    }
    [Fact] public void Each_half_rounds_separately() {
        var intra = InvoiceCalculator.Calculate(new[] { L(1000, 30, 1800) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(new Money(3), intra.Cgst);   // 30 * 1800 / 20000 = 2.7, rounds to 3
        Assert.Equal(new Money(3), intra.Sgst);
        var inter = InvoiceCalculator.Calculate(new[] { L(1000, 30, 1800) }, TaxSplit.Igst, false, false);
        Assert.Equal(new Money(5), inter.Igst);   // 30 * 1800 / 10000 = 5.4, rounds to 5
    }
    [Fact] public void Quarter_percent_half_rate() =>
        Assert.Equal(new Money(125), InvoiceCalculator.Calculate(new[] { L(1000, 100000, 25) }, TaxSplit.CgstSgst, false, false).Cgst);
    [Fact] public void Utgst_split() {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 10005, 500) }, TaxSplit.CgstUtgst, false, false);
        Assert.Equal(new Money(250), r.Cgst);
        Assert.Equal(new Money(250), r.Utgst);
        Assert.Equal(Money.Zero, r.Sgst);
    }
    [Fact] public void Discount_rounds_once() =>
        Assert.Equal(new Money(48998), InvoiceCalculator.Calculate(new[] { L(2500, 19999, 0, discount: 1000) }, TaxSplit.Igst, false, false).Taxable);
    [Fact] public void Cess() =>
        Assert.Equal(new Money(1200), InvoiceCalculator.Calculate(new[] { L(1000, 10000, 2800, cess: 1200) }, TaxSplit.Igst, false, false).Cess);
    [Theory]
    [InlineData(123450, 50, 123500)]
    [InlineData(123449, -49, 123400)]
    public void Round_off_to_rupee(long taxable, long roundOff, long grand) {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, taxable, 0) }, TaxSplit.Igst, true, false);
        Assert.Equal(new Money(roundOff), r.RoundOff);
        Assert.Equal(new Money(grand), r.GrandTotal);
    }
    [Fact] public void Inclusive_price_matches_typed_total() {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 10000, 1800, incl: true) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(new Money(8474), r.Taxable);
        Assert.Equal(new Money(763), r.Cgst);
        Assert.Equal(new Money(763), r.Sgst);
        Assert.Equal(new Money(10000), r.GrandTotal);
    }
    [Fact] public void Reverse_charge_total_excludes_tax() {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 100000, 1800) }, TaxSplit.Igst, false, true);
        Assert.Equal(new Money(18000), r.Igst);
        Assert.Equal(new Money(100000), r.GrandTotal);
    }
    [Fact] public void Totals_equal_sum_of_lines() { // invariant 7
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 30, 1800), L(1000, 33333, 1800), L(7000, 1999, 500) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(Money.Sum(r.Lines.Select(l => l.Cgst)), r.Cgst);
        Assert.Equal(Money.Sum(r.Lines.Select(l => l.Taxable)), r.Taxable);
    }
    [Theory]
    [InlineData(0, 100, 0, InvoiceError.NonPositiveQuantity)]
    [InlineData(1000, -1, 0, InvoiceError.NegativePrice)]
    [InlineData(1000, 100, -1, InvoiceError.NegativeDiscount)]
    [InlineData(1000, 100, 101, InvoiceError.NegativeTaxable)]
    public void Rejects_bad_lines(long q, long p, long disc, InvoiceError e) =>
        Assert.Equal(e, Assert.Throws<InvoiceException>(() =>
            InvoiceCalculator.Calculate(new[] { L(q, p, 1800, disc) }, TaxSplit.Igst, false, false)).Error);
    [Fact] public void Rejects_empty() =>
        Assert.Equal(InvoiceError.NoLines, Assert.Throws<InvoiceException>(() =>
            InvoiceCalculator.Calculate(Array.Empty<InvoiceLineInput>(), TaxSplit.Igst, false, false)).Error);
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~InvoiceCalculator"`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: add GST invoice calculator`

---

### Task 10: GST posting

**Files:**
- Create: `src/OpenHisabKitab.Core/Gst/GstPosting.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Gst/GstPostingTests.cs`

**Interfaces:**
- Consumes: `InvoiceResult` (Task 9), `PostingLine`, `Postings.Consolidate` (Task 7), `LedgerId`, `ChartOfAccounts`, `RequiredLedger`.
- Produces:
  - `enum TradeDirection { Outward, Inward }`
  - `enum GstDocumentKind { Invoice, CreditNote, DebitNote }`
  - `sealed record TaxLedgers(LedgerId Cgst, LedgerId Sgst, LedgerId Utgst, LedgerId Igst, LedgerId Cess)`
  - `sealed record GstPostingAccounts(LedgerId Party, LedgerId RoundOff, TaxLedgers Output, TaxLedgers Input)` with `static GstPostingAccounts FromChart(ChartOfAccounts chart, LedgerId party)`
  - `static class GstPosting` with `IReadOnlyList<PostingLine> Post(InvoiceResult invoice, IReadOnlyList<LedgerId> lineLedgers, TradeDirection direction, GstDocumentKind kind, bool reverseCharge, GstPostingAccounts accounts)`
  - `lineLedgers[i]` is the sales or purchase ledger for invoice line `i`.

A note on document kinds, which is a designer clarification of spec 5.4. A credit note reduces the original document and a debit note increases it, whichever side issued it. Outward documents are the ones this business issues and numbers under Rule 46. Inward documents carry the supplier's number.

Posting rules. The sign for the document is +1 for `Invoice` and `DebitNote`, and -1 for `CreditNote`. Multiply every line below by that sign, then pass the result through `Postings.Consolidate`.

**Outward:**
- Party: debit `GrandTotal`.
- Each line ledger: credit the line's `Taxable`.
- Each output tax ledger with a non-zero amount: credit the tax.
- Round Off: credit `RoundOff`. The signed amount is `-RoundOff`.

**Inward, no reverse charge:**
- Each line ledger: debit the line's `Taxable`.
- Each input tax ledger: debit the tax.
- Party: credit `GrandTotal`.
- Round Off: the signed amount is `+RoundOff`.

**Inward, reverse charge:**
- Each line ledger: debit the line's `Taxable`.
- Each input tax ledger: debit the tax.
- Each output tax ledger: credit the tax. This is the liability that is paid in cash.
- Party: credit `GrandTotal`.
- Round Off: the signed amount is `+RoundOff`.

**Outward with reverse charge:** throw `ArgumentException`.

**`lineLedgers.Count != invoice.Lines.Count`:** throw `ArgumentException`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class GstPostingTests
{
    readonly ChartOfAccounts chart;
    readonly LedgerId party;
    readonly GstPostingAccounts acc;
    public GstPostingTests() {
        var c = DefaultChart.Create();
        (chart, party) = c.AddLedger("Gupta Enterprises", c.Group(GroupKind.TradeReceivables));
        acc = GstPostingAccounts.FromChart(chart, party);
    }
    LedgerId R(RequiredLedger r) => chart.Required(r);
    static InvoiceResult Sale999() => InvoiceCalculator.Calculate(
        new[] { new InvoiceLineInput(new Quantity(3000), new Money(33333), Money.Zero, new GstRate(1800), new GstRate(0)) },
        TaxSplit.CgstSgst, true, false);

    [Fact] public void Outward_invoice() {
        var p = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, false, acc);
        Assert.Equal(new[] {
            new PostingLine(party, new Money(118000)),
            new PostingLine(R(RequiredLedger.Sales), new Money(-99999)),
            new PostingLine(R(RequiredLedger.OutputCgst), new Money(-9000)),
            new PostingLine(R(RequiredLedger.OutputSgst), new Money(-9000)),
            new PostingLine(R(RequiredLedger.RoundOff), new Money(-1)),
        }, p);
    }
    [Fact] public void Outward_credit_note_is_exact_negation() {
        var inv = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, false, acc);
        var cn = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.CreditNote, false, acc);
        Assert.Equal(inv.Select(l => l with { Amount = -l.Amount }), cn);
    }
    [Fact] public void Inward_reverse_charge() {
        var r = InvoiceCalculator.Calculate(new[] { new InvoiceLineInput(new Quantity(1000), new Money(100000), Money.Zero, new GstRate(1800), new GstRate(0)) }, TaxSplit.Igst, false, true);
        var p = GstPosting.Post(r, new[] { R(RequiredLedger.Purchases) }, TradeDirection.Inward, GstDocumentKind.Invoice, true, acc);
        Assert.Equal(new[] {
            new PostingLine(R(RequiredLedger.Purchases), new Money(100000)),
            new PostingLine(R(RequiredLedger.InputIgst), new Money(18000)),
            new PostingLine(R(RequiredLedger.OutputIgst), new Money(-18000)),
            new PostingLine(party, new Money(-100000)),
        }, p);
    }
    [Fact] public void Outward_reverse_charge_is_rejected() =>
        Assert.Throws<ArgumentException>(() => GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, true, acc));
    [Fact] public void Line_ledger_count_must_match() =>
        Assert.Throws<ArgumentException>(() => GstPosting.Post(Sale999(), Array.Empty<LedgerId>(), TradeDirection.Outward, GstDocumentKind.Invoice, false, acc));
}
```

  The order of posting lines is: party first for outward documents, then line ledgers, then taxes in the order CGST, SGST, UTGST, IGST, Cess, then Round Off. For inward documents the party comes after the taxes. The tests above pin this order.

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~GstPosting"`
- [ ] **Step 3: Implement.** For inward documents, the line order is: line ledgers, input taxes, output taxes, party, Round Off.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: post GST documents to ledgers`

---

### Task 11: Voucher number series

**Files:**
- Create: `src/OpenHisabKitab.Core/Vouchers/NumberSeries.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Vouchers/NumberSeriesTests.cs`

**Interfaces:**
- Consumes: `FinancialYear`.
- Produces:
  - `enum SeriesError { TooLong, InvalidCharacter, BadWidth }`
  - `sealed record NumberSeries(string Pattern, int Width)` with:
    - `string Prefix(FinancialYear fy)`. This replaces `{FY}` with `fy.ShortLabel`.
    - `string Format(FinancialYear fy, long number)`, which zero pads the number to `Width`. If `number < 1` or `number > 10^Width - 1`, it throws `SeriesExhaustedException`.
    - `IReadOnlyList<SeriesError> ValidateForGst(FinancialYear fy)`. The rules are:
      - The expanded prefix length plus `Width` must be 16 or less. Otherwise the error is `TooLong`.
      - The prefix may contain only `[A-Za-z0-9/-]`. Otherwise the error is `InvalidCharacter`.
      - `Width` must be from 1 to 10. Otherwise the error is `BadWidth`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class NumberSeriesTests
{
    static readonly FinancialYear Fy = new(2026);
    [Fact] public void Formats() => Assert.Equal("INV/26-27/00001", new NumberSeries("INV/{FY}/", 5).Format(Fy, 1));
    [Fact] public void Valid_gst_series() => Assert.Empty(new NumberSeries("INV/{FY}/", 5).ValidateForGst(Fy));
    [Fact] public void Exactly_16_is_valid() => Assert.Empty(new NumberSeries("INV/{FY}/", 6).ValidateForGst(Fy));
    [Fact] public void Too_long() => Assert.Contains(SeriesError.TooLong, new NumberSeries("INVOICE/{FY}/", 5).ValidateForGst(Fy));
    [Fact] public void Space_is_invalid() => Assert.Contains(SeriesError.InvalidCharacter, new NumberSeries("INV {FY}", 4).ValidateForGst(Fy));
    [Fact] public void Width_zero_is_invalid() => Assert.Contains(SeriesError.BadWidth, new NumberSeries("S", 0).ValidateForGst(Fy));
    [Theory] [InlineData(0)] [InlineData(100000)]
    public void Out_of_range_numbers_throw(long n) =>
        Assert.Throws<SeriesExhaustedException>(() => new NumberSeries("INV/{FY}/", 5).Format(Fy, n));
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~NumberSeries"`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: add GST compliant number series`

---

### Task 12: Books, trial balance, ledger statement, year carry forward

**Files:**
- Create: `src/OpenHisabKitab.Core/Reports/PostingRecord.cs`, `Books.cs`, `TrialBalance.cs`, `LedgerStatement.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Reports/TrialBalanceTests.cs`, `LedgerStatementTests.cs`

**Interfaces:**
- Consumes: `ChartOfAccounts`, `LedgerId`, `Money`, `DateRange`, `FinancialYear`.
- Produces:
  - `sealed record PostingRecord(long VoucherId, DateOnly Date, LedgerId Ledger, Money Amount)`
  - `sealed class Books(ChartOfAccounts chart, DateOnly booksStart, IReadOnlyDictionary<LedgerId, Money> openingBalances, IEnumerable<PostingRecord> postings)` with:
    - `Money OpeningDifference`, the sum of the opening balances
    - `TrialBalance TrialBalance(DateRange period)`
    - `LedgerStatement Statement(LedgerId ledger, DateRange period)`
  - `sealed record TrialBalanceRow(LedgerId Ledger, Money Opening, Money Debits, Money Credits, Money Closing)`. `Credits` is a positive amount.
  - `sealed record TrialBalance(DateRange Period, IReadOnlyList<TrialBalanceRow> Rows, Money OpeningDifference, Money TotalDebit, Money TotalCredit)`
  - `sealed record StatementRow(DateOnly Date, long VoucherId, Money Amount, Money Balance)`
  - `sealed record LedgerStatement(LedgerId Ledger, DateRange Period, Money Opening, IReadOnlyList<StatementRow> Rows, Money Closing)`

**Constructor checks.** Each throws `ArgumentException`:
- `booksStart` is not 1 April.
- A posting is dated before `booksStart`.
- A posting or opening refers to an unknown ledger.
- An opening balance is given for an Income or Expense ledger.

**Index the postings** by ledger once, sorted by date and then voucher id. Each report must then run in time proportional to the postings it reads.

**Opening balance of a ledger for a period starting on `from`.** Let `fy = FinancialYear.Containing(from)`.
- **Asset and Liability ledgers:** the opening balance, plus all postings dated before `from`.
- **Income and Expense ledgers:** the postings dated from `fy.Start` up to the day before `from`. They do not carry across years.
- **The `ProfitAndLoss` required ledger** additionally gets the sum of every Income and Expense posting dated before `fy.Start`. This is the year carry forward (invariant 6).

**Rules for each report:**
- **`period` must sit inside one financial year.** If it does not, throw `ArgumentException` (Review Focus 5). `Statement` applies the same rule.
- **Rows** are in chart ledger order. A ledger is omitted if its opening, debits, credits, and closing are all zero.
- **`TotalDebit`** is the sum of the positive closing balances. **`TotalCredit`** is the sum of the negated negative closing balances.
- **When `OpeningDifference` is not zero,** it counts on the opposite side so that the totals still agree. A positive difference is added to `TotalCredit`. A negative difference adds its absolute value to `TotalDebit`. The client shows it as the "Opening difference" line (spec 13).

- [ ] **Step 1: Write the failing tests**

  Shared fixture in the test class:
  - Start from the default chart.
  - Add the ledger `Rent` under `GroupKind.OtherExpenses`.
  - Books start on 1 April 2025.
  - Opening balances: Cash +5,000,000 paise, Capital -5,000,000 paise.

  Postings:

  | Voucher | Date | Debit | Credit | Amount (paise) |
  |---|---|---|---|---|
  | 1 | 10-04-2025 | Cash | Sales | 1,000,000 |
  | 2 | 20-04-2025 | Rent | Cash | 200,000 |
  | 3 | 05-05-2026 | Cash | Sales | 300,000 |

```csharp
[Fact] public void Trial_balance_fy_2025_totals() {
    var tb = books.TrialBalance(new FinancialYear(2025).Range);
    Assert.Equal(new Money(6000000), tb.TotalDebit);
    Assert.Equal(new Money(6000000), tb.TotalCredit);
    var cash = tb.Rows.Single(r => r.Ledger == cashId);
    Assert.Equal(new TrialBalanceRow(cashId, new Money(5000000), new Money(1000000), new Money(200000), new Money(5800000)), cash);
}
```

  The remaining tests follow the same pattern, with these values.

  **FY 2025-26.**
  - Rows: Cash closing +5,800,000, Capital -5,000,000, Sales -1,000,000, Rent +200,000.
  - `TotalDebit` = 6,000,000 and `TotalCredit` = 6,000,000.
  - Cash row: Opening 5,000,000, Debits 1,000,000, Credits 200,000.

  **FY 2026-27.**
  - Cash opening is +5,800,000.
  - Sales opening is 0, and its closing is -300,000.
  - Rent is absent.
  - Profit and Loss opening is -800,000. This is the 1,000,000 of sales less the 200,000 of rent, carried as a credit.
  - `TotalDebit` = `TotalCredit` = 6,100,000.

  **Statement for Cash, FY 2025-26.**
  - Opening 5,000,000.
  - Rows: (10-04-2025, voucher 1, +1,000,000, balance 6,000,000), then (20-04-2025, voucher 2, -200,000, balance 5,800,000).
  - Closing 5,800,000.

  **Sub-period.** The trial balance for 1-5-2025 to 31-3-2026 gives Rent an opening of +200,000 with no debits.

  **Opening difference.** Build the books with Cash +5,000,000 and Capital -4,999,000. Then `OpeningDifference` = +1,000 and `TotalDebit == TotalCredit` for FY 2025-26.

  **Constructor and period errors**, each `Assert.Throws<ArgumentException>`:
  - The period 1-3-2026 to 30-4-2026 crosses 31 March (Review Focus 5).
  - A posting is dated 31-3-2025, before the books start.
  - An opening balance is given for Sales.
  - `booksStart` is 2-4-2025.

  Write these as separate `[Fact]` methods named for what they check. The fixture is built in the constructor using `PostingRecord` values.

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Reports"`
- [ ] **Step 3: Implement `Books.TrialBalance` and `Books.Statement`.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: add books engine with trial balance, ledger statement, and year carry forward`

---

### Task 13: Profit and loss, balance sheet

**Files:**
- Create: `src/OpenHisabKitab.Core/Reports/ProfitAndLoss.cs`, `BalanceSheet.cs`
- Modify: `src/OpenHisabKitab.Core/Reports/Books.cs`. Add two methods.
- Test: `tests/OpenHisabKitab.Core.Tests/Reports/ProfitAndLossTests.cs`, `BalanceSheetTests.cs`

**Interfaces:**
- Consumes: `Books` internals from Task 12.
- Produces:
  - `sealed record ReportRow(LedgerId Ledger, Money Amount)`
  - `sealed record ProfitAndLoss(DateRange Period, IReadOnlyList<ReportRow> Income, IReadOnlyList<ReportRow> Expenses, Money TotalIncome, Money TotalExpenses, Money NetProfit)`
    - An income amount is the ledger's period credit total net of its debits, shown positive. An expense amount is its debit total net of credits, shown positive.
    - `NetProfit = TotalIncome - TotalExpenses`.
  - `sealed record BalanceSheet(DateOnly AsAt, IReadOnlyList<ReportRow> Assets, IReadOnlyList<ReportRow> Liabilities, Money CurrentProfit, Money OpeningDifference, Money TotalAssets, Money TotalLiabilities)`
    - Assets are Asset ledgers' closing balances, debit positive.
    - Liabilities are Liability ledgers' closing balances, credit positive. They include the `ProfitAndLoss` ledger with its carried amount.
    - `CurrentProfit` is the net profit from the financial year's start to `AsAt`.
    - `TotalLiabilities` is the sum of the liability rows, plus `CurrentProfit`, plus `OpeningDifference`.
    - A ledger whose closing balance is zero is omitted from both lists, even if it had activity. The trial balance differs here: it keeps such ledgers.
  - `Books.ProfitAndLoss(DateRange period)`, under the same one-year rule.
  - `Books.BalanceSheet(DateOnly asAt)`

- [ ] **Step 1: Write the failing tests** using the Task 12 fixture.

  **P&L for FY 2025-26.**
  - Income: [Sales 1,000,000]. Expenses: [Rent 200,000].
  - `NetProfit` = 800,000.

  **Balance sheet as at 31-3-2026.**
  - Assets: [Cash 5,800,000], so `TotalAssets` = 5,800,000.
  - Liabilities: [Capital 5,000,000]. The Profit and Loss ledger is omitted because its closing is zero.
  - `CurrentProfit` = 800,000, so `TotalLiabilities` = 5,800,000.

  **Balance sheet as at 31-3-2027.**
  - Liabilities: [Capital 5,000,000, Profit and Loss 800,000].
  - `CurrentProfit` = 300,000.
  - Both totals are 6,100,000.

  **Opening difference fixture** (Capital -4,999,000). As at 31-3-2026, `TotalAssets == TotalLiabilities`.

  **Invariant 4.** `ProfitAndLoss(fy.Range).NetProfit == BalanceSheet(fy.End).CurrentProfit` for both years.

- [ ] **Step 2: Run the tests and confirm they fail.** Run: `dotnet test --filter "FullyQualifiedName~Reports"`
- [ ] **Step 3: Implement.** Both methods use the same per-ledger opening and closing helper as `TrialBalance` (spec 5.1). They must not have their own summing loop over raw postings.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**

  `feat: add profit and loss and balance sheet`

---

### Task 14: Golden company and random invariant tests

**Files:**
- Create: `tests/OpenHisabKitab.Core.Tests/Fixtures/GoldenCompany.cs`, `Fixtures/RandomBooks.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Reports/GoldenCompanyTests.cs`, `Reports/InvariantTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2 to 13.
- Produces:
  - `static class GoldenCompany` with `(Books Books, ChartOfAccounts Chart, IReadOnlyDictionary<string, LedgerId> Ledgers) Build()`
  - `sealed record GeneratedInvoice(InvoiceResult Result, TradeDirection Direction, GstDocumentKind Kind, bool ReverseCharge)`
  - `sealed record GeneratedVoucher(long Id, DateOnly Date, IReadOnlyList<PostingLine> Lines, GeneratedInvoice? Invoice)`
  - `static class RandomBooks` with `(Books Books, ChartOfAccounts Chart, IReadOnlyList<GeneratedVoucher> Vouchers) Generate(int seed)`

- [ ] **Step 1: Build `GoldenCompany`**

  The figures were calculated by hand during planning, and the test asserts them. The software is not used to produce them. The chart is the default chart plus four ledgers:
  - `Gupta Enterprises` (Trade Receivables)
  - `Mehta and Sons` (Trade Payables)
  - `Rent` (Other Expenses)
  - `Interest Received` (Other Income)

  Books start on 1-4-2025, with Cash ₹50,000 Dr and Capital ₹50,000 Cr. All supplies are intra state at 18%, with round off on.

  | Voucher | Date | Entry | How it is built |
  |---|---|---|---|
  | V1 | 10-04-2025 | Sale to Gupta: qty 1, price ₹10,000 | `InvoiceCalculator` then `GstPosting`, Outward Invoice |
  | V2 | 15-04-2025 | Purchase from Mehta: qty 1, price ₹6,000 | `InvoiceCalculator` then `GstPosting`, Inward Invoice |
  | V3 | 20-04-2025 | Receipt: Dr Cash, Cr Gupta, ₹11,800 | Posting lines |
  | V4 | 25-04-2025 | Payment: Dr Rent, Cr Cash, ₹2,000 | Posting lines |
  | V5 | 30-04-2025 | Payment: Dr Mehta, Cr Cash, ₹7,080 | Posting lines |
  | V6 | 05-05-2026 | Journal: Dr Cash, Cr Interest Received, ₹1,000 | Posting lines |

- [ ] **Step 2: Write `GoldenCompanyTests` with these exact expectations (in rupees; use `Money.FromRupees`)**

  **Trial balance, FY 2025-26.** Closing balances:

  | Ledger | Closing |
  |---|---|
  | Cash | 52,720 Dr |
  | Capital | 50,000 Cr |
  | Sales | 10,000 Cr |
  | Output CGST | 900 Cr |
  | Output SGST | 900 Cr |
  | Purchases | 6,000 Dr |
  | Input CGST | 540 Dr |
  | Input SGST | 540 Dr |
  | Rent | 2,000 Dr |

  Gupta and Mehta have zero closing balances but had activity, so their rows are present with Closing 0. Both totals are 61,800.

  **P&L, FY 2025-26.**
  - TotalIncome is 10,000 and TotalExpenses is 8,000.
  - NetProfit is 2,000.

  **Balance sheet as at 31-3-2026.**
  - TotalAssets is 53,800: Cash 52,720, Input CGST 540, Input SGST 540.
  - TotalLiabilities is 53,800: Capital 50,000, Output CGST 900, Output SGST 900, CurrentProfit 2,000.

  **Trial balance, FY 2026-27.**
  - Profit and Loss opening: 2,000 Cr.
  - Cash closing: 53,720 Dr.
  - Interest Received: 1,000 Cr.
  - Purchases, Sales, and Rent are absent.
  - Both totals are 54,800.

  **Balance sheet as at 31-3-2027.**
  - Both totals are 54,800.
  - CurrentProfit is 1,000.

  **Cash statement, FY 2025-26.**
  - Opening is 50,000.
  - Running balances are 61,800, 59,800, and 52,720.
  - Closing is 52,720.

- [ ] **Step 3: Build `RandomBooks.Generate(seed)`** using `new Random(seed)`. The generated company has:
  - **Chart:** the default chart plus 5 party ledgers (3 receivables, 2 payables), `HDFC Bank`, `Rent`, and `Interest Received`.
  - **Books start:** 1-4-2025.
  - **Openings:** random amounts from ₹0 to ₹10,00,000 on Cash, the bank, and the parties. Capital balances them to zero.
  - **Vouchers:** 300, dated randomly across FY 2025-26 and FY 2026-27. Of these:
    - 60% are GST invoices: a random direction, kind, split, and 1 to 4 lines, and round off on or off. Each line has:
      - qty from 1 to 100000 thousandths
      - unit price from 1 to 10000000 paise
      - discount 0 on four lines in five, otherwise up to 10% of the gross
      - a rate from {0, 500, 1800, 2800}
      - `PriceIncludesTax` true on one line in four One inward invoice in five uses reverse charge. Outward invoices never do. They go through the calculator and `GstPosting`.
    - 40% are journals with 2 to 5 random lines of up to 100000000 paise each, balanced by a final line.
  - **Posting records:** every voucher's lines become `PostingRecord`s.

- [ ] **Step 4: Write `InvariantTests`**

  This is a `[Theory]` over seeds 1 to 100, using `MemberData`. For each seed, assert spec 5.2 invariants 1 to 7:
  1. Every voucher's lines sum to zero.
  2. For each financial year, and for 5 random sub-periods within a year, `TotalDebit == TotalCredit`.
  3. For 10 random dates, the balance sheet `TotalAssets == TotalLiabilities`.
  4. For each of those dates, the P&L `NetProfit` from the year start to the date equals the balance sheet `CurrentProfit`.
  5. For every ledger in every year, `Statement(l, fy.Range).Closing` equals that ledger's trial balance closing. A ledger absent from the trial balance counts as 0.
  6. For every Asset and Liability ledger, the opening for FY 2026-27 equals its closing for FY 2025-26. Every Income and Expense ledger opens FY 2026-27 at 0. The Profit and Loss ledger's FY 2026-27 opening equals its FY 2025-26 closing minus the FY 2025-26 `NetProfit`.
  7. For every invoice voucher, the posted amount on each tax ledger equals the matching `InvoiceResult` total, negated as the direction requires.

  Include the seed in every assertion message so a failure can be reproduced.

- [ ] **Step 5: Run all tests**

  Run: `dotnet test OpenHisabKitab.slnx -c Release`
  Expected: all pass, 0 failed. The test run reports more than 100 invariant cases.

- [ ] **Step 6: Commit**

  `test: add golden company and random invariant tests`

---

## Done when

- `dotnet build -c Release` succeeds with 0 warnings.
- `dotnet test -c Release` passes every test.
- The CI workflow file exists, ready to run when the repository is pushed to GitHub. Pushing waits for the owner's go-ahead.
