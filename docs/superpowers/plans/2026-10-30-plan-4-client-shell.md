# Plan 4: Client Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A WPF client that connects to the host, logs in, and gives the Bloomberg-style shell: a command prompt, tabs, an F key row, layered settings and keybindings, and the system screens `HELP`, `DATE`, `RFSH`, `CMPY`, `SETG`, `KEYS`, and `USER`. With these the Owner can turn on the API and create tokens (owner review item 10 of Plan 3).

**Architecture:**
- **`OpenHisabKitab.Client.Model`** is a plain `net10.0` class library. It holds every view model and all client logic: connection, trust on first use, reconnect, settings and keybinding stores, the shell, and the screens. It also holds all screen text in `Resources/Text.resx`. It contains no WPF types, so plain xUnit tests cover it against the real host in memory.
  - Text lives here and not in the WPF project for a reason found in a spike. WPF's temporary markup compile pass cannot see a strongly typed resx class generated in the same project.
- **`OpenHisabKitab.Client`** is the WPF executable. It holds only XAML views, the theme, key translation, and composition in `App`.
- Two pure pieces go in `Core`, because the host must validate what the client stores in `host.db`:
  - the settings catalog and its layering (`Core/Settings`);
  - keybindings and `when` clauses (`Core/Commands/Keys`).
- The host gains a user settings layer, which spec 7 puts in `host.db` and which carries user keybindings. It also gains reads for company settings.

**Tech Stack:**
- .NET 10 WPF.
- `CommunityToolkit.Mvvm` 8.4.2, using `[ObservableProperty]` on partial properties and `[RelayCommand]`.
- `Grpc.Net.Client`, as already used.
- `FlaUI.UIA3` 5.0.0 for UI smoke tests.
- xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-26-open-hisab-kitab-v1-design.md`. Sections 4 (Transport), 6.1, 6.2, 6.3 (System rows), 6.5, 6.6, 6.7, 7, 8, and 13 ("First run", "Version match", "Remote ready client") apply.

**Facts checked in a spike on 2026-10-30** (the plan depends on them):
- A `net10.0-windows` WPF app with `TreatWarningsAsErrors` builds clean with `CommunityToolkit.Mvvm` 8.4.2 partial-property `[ObservableProperty]` and a reference to `OpenHisabKitab.Contracts`.
- A `net10.0-windows` xUnit project with `FlaUI.UIA3` 5.0.0 can reference a WPF exe and builds clean.
- In a WPF project, any C# that names a resx class generated with `StronglyTypedClassName` fails in the `_wpftmp` markup pass (CS0234). The same resx metadata works in a plain class library, and a WPF project can use that class from C# and from a markup extension.

**Out of scope, recorded in owner review (Task 14):**
- Screens for masters, vouchers, reports, GST, `AUDT`, `LOCK`, `BKUP`, `FIND`, and `PRNT`.
- Client lookup caches for parties and items.
- Paged list RPCs.
- Editing a company profile.
- The MCP wrong-type message.

## Global Constraints

- Everything in the Global Constraints of Plans 2 and 3 still holds: build settings, central package versions, money as `long` paise, `TimeProvider`, one line messages, and commits.
- **New package versions** in `Directory.Packages.props`:
  - `CommunityToolkit.Mvvm` 8.4.2
  - `FlaUI.UIA3` 5.0.0
- **One version.** `Directory.Build.props` sets `<Version>0.1.0</Version>`. Client and host read it from `OpenHisabKitab.Contracts.ProductVersion.Current`, so the two cannot drift (spec 13, "Version match").
- **Target frameworks:**
  - `Client.Model` is `net10.0`.
  - `Client` is `net10.0-windows` with `<UseWPF>true</UseWPF>` and `<OutputType>WinExe</OutputType>`.
  - Both test projects are `net10.0-windows`.
- **No WPF in the model.**
  - `Client.Model` never references `System.Windows`.
  - View models never touch a `Dispatcher`. Async work resumes on the caller's `SynchronizationContext`.
  - `async void` appears only in view event handlers.
- **Screen text.**
  - Every user-visible string is in `src/OpenHisabKitab.Client.Model/Resources/Text.resx`, generated as the public class `OpenHisabKitab.Client.Model.Resources.Text`.
  - XAML uses `{t:Txt Key}` (Task 1), never a literal. Messages from the host are shown as received.
  - Text follows spec 6.7: labels are nouns, buttons are verbs, messages are one line, and there is no filler.
- **Amounts and dates.**
  - Amounts are shown only through `Core.Formatting.IndianNumberFormat`, honouring `ui.negativeAmounts`.
  - Dates are shown through `Client.Model.Formatting.DateText.Format(DateOnly, string dateFormat)`, honouring `ui.dateFormat`.
  - Dates are typed through `Core.Time.DateInput`.
- **Calls to the host.**
  - Every unary call has a 30 second deadline, computed as `DateTime.UtcNow + 30 s`, never from the injected `TimeProvider`. gRPC checks deadlines against the system clock, and `FakeTimeProvider` starts in 2000. `Changes.Watch` has no deadline.
  - `TimeProvider` drives only the retry delays and today's date.
  - Every call sends `ohk-version`. `SetupOwner` and `Login` also send `ohk-machine: Environment.MachineName`. Every call after login sends `authorization: Bearer {token}`.
- **Machine files** live in `ClientPaths.Root`:
  - The root is `%LOCALAPPDATA%\OpenHisabKitab`, or the folder given by `--profile <folder>` on the command line.
  - The files are `settings.json`, `keybindings.json`, `known-hosts.json`, and `state.json`.
  - JSON is read with `JsonCommentHandling.Skip` and `AllowTrailingCommas = true`, as VS Code does, and written indented.
  - A file with errors is never rewritten by a load.
- **Remote ready** (spec 13):
  - The host address is always typed as a name or IP plus a port. The default port is 47250.
  - Every screen loads in at most three calls.
  - A failed call never clears what the user typed.
- **UI tests** are skipped unless the environment variable `OHK_UI_TESTS` is `1`, so `dotnet test OpenHisabKitab.slnx` stays green without a desktop.

## Review Focus

These failure modes are implied by the spec but no example exercises them. Each has a test in the task named.

1. **The host restarts while a user has unsaved edits** (Plan 2: a restart logs everyone out).
   - The next call shows the login panel over the shell. Tabs and typed text stay.
   - After the user logs in again, the same save succeeds, and every tab shows "Changed".
   - Tests in Tasks 5 and 8.
2. **The host's certificate changes,** through a reinstall or someone in the middle.
   - The client shows both fingerprints and blocks.
   - No password is sent until the user accepts the new certificate.
   - Test in Task 5.
3. **A hand-edited `settings.json` or `keybindings.json` with a mistake,** such as a trailing comma, a comment, an unknown key, or a wrong type.
   - Comments and trailing commas are accepted.
   - Each bad entry is skipped and named with its line in the status bar. The rest applies.
   - The file is left as it was.
   - Tests in Tasks 2, 3, and 6.
4. **A user unbinds or rebinds a core key,** for example removing `ctrl+l` or binding `f6` to `run.PURC`.
   - The removed key does nothing.
   - The F key row relabels.
   - The menu (Alt access keys, which cannot be rebound) still reaches `KEYS`, so the user can always repair it.
   - Tests in Tasks 3, 7, and 14.
5. **The largest font, `ui.fontSize` 28.** The prompt, the tab strip, all twelve F keys, and the status bar stay fully inside a 1024 by 768 window. UI test in Task 14.

---

## File Structure

```
Directory.Build.props, Directory.Packages.props        Version 0.1.0; two packages (Task 1)
OpenHisabKitab.slnx                                    + four client projects (Task 1)
src/OpenHisabKitab.Contracts/
  ProductVersion.cs, CertificateFingerprint.cs         shared by client and host (Task 1)
  Protos/settings.proto, Protos/companies.proto        user settings; company setting reads (Task 4)
src/OpenHisabKitab.Core/Settings/
  SettingDefinition.cs, SettingsCatalog.cs             the catalog and its validation (Task 2)
  SettingsDocument.cs, EffectiveSettings.cs            layer JSON; layering (Task 2)
src/OpenHisabKitab.Core/Commands/Keys/
  KeyChord.cs, WhenClause.cs, KeyCommands.cs           (Task 3)
  KeybindingFile.cs, KeybindingSet.cs, DefaultKeybindings.cs  (Task 3)
src/OpenHisabKitab.Data/Migrations/Host/0003_user_settings.sql   (Task 4)
src/OpenHisabKitab.Data/HostDb/UserSettingRepository.cs          (Task 4)
src/OpenHisabKitab.Host/Application/UserSettingsService.cs       (Task 4)
src/OpenHisabKitab.Client.Model/
  Resources/Text.resx                                  all screen text (Task 1 on)
  Formatting/DateText.cs                               (Task 6)
  Connection/  ClientPaths, HostAddress, KnownHosts, IChannelFactory, TlsChannelFactory,
               HostConnection, HostClients, HostException, ConnectionState  (Task 5)
  Settings/    MachineFiles, ClientState, SettingsStore, KeybindingStore    (Task 6)
  Shell/       IScreen, ScreenViewModel, ShellContext, FunctionRouter, PromptViewModel,
               TabsViewModel, TabViewModel, StatusBarViewModel, PaletteViewModel,
               FKeyRow, ShellViewModel, IConfirm                            (Task 7)
               SessionViewModel, ChangeWatcher                              (Task 8)
  Theme/       ThemeColors.cs, Contrast.cs                                  (Task 9)
  Screens/     HelpViewModel, DateViewModel (Task 10), CompanyViewModel (Task 11),
               SettingsViewModel, KeysViewModel (Task 12), UsersViewModel (Task 13)
src/OpenHisabKitab.Client/
  App.xaml(.cs), MainWindow.xaml(.cs), Txt.cs          (Task 1, Task 9)
  Theme/ThemeResources.cs, Theme/Styles.xaml           (Task 9)
  Input/KeyInput.cs                                    WPF key to KeyChord (Task 9)
  Views/*.xaml                                         one view per view model (Tasks 9 to 13)
tests/OpenHisabKitab.Client.Model.Tests/               model tests against the in-memory host
  ClientTestHost.cs                                    (Task 5)
tests/OpenHisabKitab.Client.UiTests/                   FlaUI smoke tests (Task 14)
```

---

### Task 1: Shared version, fingerprint, and the client projects

**Files:**
- Modify: `Directory.Build.props`, `Directory.Packages.props`, `OpenHisabKitab.slnx`
- Create: `src/OpenHisabKitab.Contracts/ProductVersion.cs`, `src/OpenHisabKitab.Contracts/CertificateFingerprint.cs`
- Modify: `src/OpenHisabKitab.Host/Grpc/HostVersion.cs` (`Current => ProductVersion.Current`), `src/OpenHisabKitab.Host/Security/CertificateStore.cs` (`Fingerprint` delegates)
- Create: `src/OpenHisabKitab.Client.Model/OpenHisabKitab.Client.Model.csproj`, `Resources/Text.resx`
- Create: `src/OpenHisabKitab.Client/OpenHisabKitab.Client.csproj`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Txt.cs`
- Create: `tests/OpenHisabKitab.Client.Model.Tests/OpenHisabKitab.Client.Model.Tests.csproj`, `ProductTests.cs`, `TextResourceTests.cs`

**Interfaces:**
- Produces:
  - `public static class ProductVersion { public static string Current { get; } }`. It reads `AssemblyInformationalVersion` of the Contracts assembly and strips anything from `+` on, as `HostVersion` does today.
  - `public static class CertificateFingerprint { public static string Of(X509Certificate cert); }`. It produces uppercase hex of the SHA-256 of `GetRawCertData()`, joined by `:`. This is the existing format, moved.
  - `OpenHisabKitab.Client.Txt` is a `MarkupExtension` with ctor `TxtExtension(string key)`. It returns `Text.ResourceManager.GetString(key, Text.Culture)`, and throws `KeyNotFoundException` for a missing key, so a typo fails the UI test rather than showing the key.
  - The resx metadata, verified in the spike: `<EmbeddedResource Update="Resources/Text.resx" StronglyTypedLanguage="CSharp" StronglyTypedClassName="Text" StronglyTypedNamespace="OpenHisabKitab.Client.Model.Resources" StronglyTypedFileName="$(IntermediateOutputPath)Text.Designer.cs" PublicClass="true" />`
- Project references:
  - `Client.Model` references Core and Contracts.
  - `Client` references Client.Model.
  - `Client.Model.Tests` references Client.Model and Host, with the same package set as `Host.Tests`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Client_and_host_report_one_version()
{
    Assert.Equal("0.1.0", ProductVersion.Current);
    Assert.Equal(ProductVersion.Current, OpenHisabKitab.Host.Grpc.HostVersion.Current);
}

[Fact] public void Fingerprint_keeps_the_host_format()
{
    using var cert = new CertificateStore(folder, TimeProvider.System).GetOrCreate();
    var f = CertificateFingerprint.Of(cert);
    Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", f);
    Assert.Equal(CertificateStore.Fingerprint(cert), f);
}
```

`TextResourceTests` has two tests over every `*.xaml` under `src/OpenHisabKitab.Client`. The test finds the folder by walking up from `AppContext.BaseDirectory` to the folder holding `OpenHisabKitab.slnx`.
- `Every_txt_key_exists`: every `{t:Txt Key}` names a key in `Text.ResourceManager`.
- `No_literal_text_in_xaml`: no `Text`, `Content`, `Header`, `Title`, `ToolTip`, or `AutomationProperties.Name` attribute has a value that contains a letter and does not start with `{`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/OpenHisabKitab.Client.Model.Tests`
Expected: build fails, because `ProductVersion` is not defined.

- [ ] **Step 3: Implement**
  - Add the version and the two classes.
  - Repoint `HostVersion` and `CertificateStore`.
  - Add the four projects to the slnx. `Client.UiTests` gets an empty project here and its tests in Task 14.
  - `MainWindow` shows only `{t:Txt AppName}` (`Open Hisab Kitab`) as its title.

- [ ] **Step 4: Verify**

Run: `dotnet build OpenHisabKitab.slnx` and then `dotnet test OpenHisabKitab.slnx`
Expected: 0 warnings, and all tests pass, including every existing host test.

- [ ] **Step 5: Commit** `feat: client projects, one product version, shared certificate fingerprint`

---

### Task 2: Settings catalog and layering in Core

**Files:**
- Create: `src/OpenHisabKitab.Core/Settings/SettingDefinition.cs`, `SettingsCatalog.cs`, `SettingsDocument.cs`, `EffectiveSettings.cs`
- Modify: `src/OpenHisabKitab.Host/Application/CompanyService.cs`. Replace `BooleanSettings` with `SettingsCatalog.Validate(key, json, SettingLayer.Company)`, keeping the JSON parse check.
- Test: `tests/OpenHisabKitab.Core.Tests/Settings/SettingsCatalogTests.cs`, `SettingsDocumentTests.cs`, `EffectiveSettingsTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum SettingLayer { Default, Machine, Company, User }          // later wins
[Flags] public enum SettingScope { Machine = 1, Company = 2, User = 4 }
public enum SettingType { Boolean, Integer, Text, Choice }
public sealed record SettingDefinition(string Key, SettingType Type, string DefaultJson, string Description,
    SettingScope Scope, int? Min = null, int? Max = null, IReadOnlyList<string>? Choices = null)
{ public string Category { get; } }                                   // text before the first dot
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingDefinition> All { get; }
    public static SettingDefinition? Find(string key);
    public static IReadOnlyList<SettingDefinition> Search(string text); // key or description words, ignoring case
    public static string? Validate(string key, string json, SettingLayer layer); // null when valid
}
public sealed record SettingError(int Line, string Message);
public sealed record SettingsLoad(IReadOnlyDictionary<string, string> Values, IReadOnlyList<SettingError> Errors);
public static class SettingsDocument
{
    public static SettingsLoad Parse(string text, SettingLayer layer);  // values are raw JSON text per key
    public static string Write(IReadOnlyDictionary<string, string> values); // indented, keys sorted ordinal
}
public sealed class EffectiveSettings(IReadOnlyDictionary<SettingLayer, IReadOnlyDictionary<string, string>> layers)
{
    public (string Json, SettingLayer From) Value(string key);  // highest layer holding a valid value
    public bool Bool(string key); public int Int(string key); public string Text(string key);
    public bool IsModified(string key);                           // differs from DefaultJson
}
```

- **The catalog.** A setting is listed only when something in the build reads it. Later plans add theirs. The descriptions are the exact text.

| Key | Type | Default | Scope | Limits | Description |
|---|---|---|---|---|---|
| `connection.host` | Text | `""` | Machine | | Host name or IP address. |
| `connection.port` | Integer | `47250` | Machine | 1 to 65535 | Host port. |
| `ui.fontFamily` | Choice | `"Segoe UI"` | Machine, User | `Segoe UI`, `Tahoma` | Font for screen text. |
| `ui.fontSize` | Integer | `14` | Machine, User | 12 to 28 | Text size in pixels. |
| `ui.negativeAmounts` | Choice | `"brackets"` | Machine, User | `brackets`, `minus` | How negative amounts are shown. |
| `ui.dateFormat` | Choice | `"dd-mm-yyyy"` | Machine, User | `dd-mm-yyyy`, `dd/mm/yyyy`, `yyyy-mm-dd` | How dates are shown. |
| `reports.autoReload` | Boolean | `false` | Machine, User | | Reload open reports when data changes. |
| `invoice.roundOff` | Boolean | `true` | Company | | Round invoice totals to the rupee. |

- **Messages**, which keep the wording the host already uses:
  - `No setting named {key}.`
  - `Setting {key} needs a JSON value.`
  - `Setting {key} must be true or false.`
  - `Setting {key} must be a whole number from {min} to {max}.`
  - `Setting {key} must be one of: {a}, {b}.`
  - `Setting {key} must be text.`
  - `Setting {key} cannot be set for {this PC|a company|a user}.`
- **`SettingsDocument.Parse`:**
  - It reads with comments and trailing commas allowed.
  - When the root is not an object or the JSON is broken, it returns no values and one error. The line is 1-based, from `JsonException.LineNumber + 1`, and the message is `This file is not valid JSON.`
  - Otherwise each property is validated. An invalid property is skipped and gets an error with its line, found by `Utf8JsonReader` position.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("ui.fontSize", "29", SettingLayer.User, "Setting ui.fontSize must be a whole number from 12 to 28.")]
[InlineData("ui.fontFamily", "\"Arial\"", SettingLayer.Machine, "Setting ui.fontFamily must be one of: Segoe UI, Tahoma.")]
[InlineData("invoice.roundOff", "true", SettingLayer.User, "Setting invoice.roundOff cannot be set for a user.")]
[InlineData("connection.port", "47250", SettingLayer.User, "Setting connection.port cannot be set for a user.")]
[InlineData("ui.fontsize", "14", SettingLayer.User, "No setting named ui.fontsize.")]
[InlineData("ui.fontSize", "14.5", SettingLayer.User, "Setting ui.fontSize must be a whole number from 12 to 28.")]
public void Validate_names_the_problem(string key, string json, SettingLayer layer, string message) =>
    Assert.Equal(message, SettingsCatalog.Validate(key, json, layer));

[Fact] public void Comments_and_trailing_commas_are_accepted_and_bad_entries_are_skipped_by_line()
{
    var load = SettingsDocument.Parse("{\n // mine\n \"ui.fontSize\": 16,\n \"ui.dateFormat\": \"mm-dd\",\n}", SettingLayer.Machine);
    Assert.Equal("16", load.Values["ui.fontSize"]);
    Assert.False(load.Values.ContainsKey("ui.dateFormat"));
    Assert.Equal(new SettingError(4, "Setting ui.dateFormat must be one of: dd-mm-yyyy, dd/mm/yyyy, yyyy-mm-dd."), load.Errors.Single());
}

[Fact] public void Later_layers_win_and_modified_means_differs_from_default()
{
    var s = new EffectiveSettings(new Dictionary<SettingLayer, IReadOnlyDictionary<string, string>>
    {
        [SettingLayer.Machine] = new Dictionary<string, string> { ["ui.fontSize"] = "16", ["ui.fontFamily"] = "\"Segoe UI\"" },
        [SettingLayer.User] = new Dictionary<string, string> { ["ui.fontSize"] = "20" },
    });
    Assert.Equal(("20", SettingLayer.User), s.Value("ui.fontSize"));
    Assert.False(s.IsModified("ui.fontFamily"));
    Assert.True(s.IsModified("ui.fontSize"));
    Assert.Equal(SettingLayer.Default, s.Value("reports.autoReload").From);
}
```

Also add:
- `Broken_json_gives_one_error_and_no_values`.
- `Write_then_parse_round_trips`.
- `Every_default_is_valid_for_its_own_scope`: for each definition, `Validate(Key, DefaultJson, layer)` is null for every layer in its scope.

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/OpenHisabKitab.Core.Tests --filter FullyQualifiedName~Settings` fails to build.
- [ ] **Step 3: Implement** the four files, then repoint `CompanyService.SetSettingAsync`. The existing test at `CompanyServiceTests.cs:169` keeps its message unchanged.
- [ ] **Step 4: Verify.** `dotnet test OpenHisabKitab.slnx`: all pass.
- [ ] **Step 5: Commit** `feat: settings catalog with layering, validated by the host`

---

### Task 3: Keybindings in Core

**Files:**
- Create: `src/OpenHisabKitab.Core/Commands/Keys/KeyChord.cs`, `WhenClause.cs`, `KeyCommands.cs`, `KeybindingFile.cs`, `KeybindingSet.cs`, `DefaultKeybindings.cs`
- Test: `tests/OpenHisabKitab.Core.Tests/Commands/Keys/KeyChordTests.cs`, `WhenClauseTests.cs`, `KeybindingSetTests.cs`

**Interfaces:**
- Produces:

```csharp
[Flags] public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)  // Key is lowercase canonical
{
    public static bool TryParse(string? text, out KeyChord chord);  // "Ctrl+Shift+P", any order, any case
    public override string ToString();                              // "ctrl+shift+p": ctrl, shift, alt, key
}
public sealed class WhenClause
{
    public static WhenClause Always { get; }
    public static bool TryParse(string? text, out WhenClause clause, out string? error); // null or blank is Always
    public bool Matches(IReadOnlySet<string> context);
    public override string ToString();
}
public static class KeyCommands
{
    public static IReadOnlyList<string> All { get; }   // fixed ids below, then "run.{CODE}" for each FunctionCatalog code
    public static bool IsKnown(string id);
}
public sealed record KeybindingEntry(KeyChord Key, string Command, WhenClause When, bool Remove);
public sealed record Keybinding(KeyChord Key, string Command, WhenClause When, SettingLayer From);
public sealed record KeybindingLoad(IReadOnlyList<KeybindingEntry> Entries, IReadOnlyList<SettingError> Errors);
public static class KeybindingFile
{
    public static KeybindingLoad Parse(string text);  // a JSON array of { "key", "command", "when"? }
    public static string Write(IReadOnlyList<KeybindingEntry> entries);
}
public sealed class KeybindingSet
{
    public static KeybindingSet Build(IReadOnlyList<KeybindingEntry> machine, IReadOnlyList<KeybindingEntry> user);
    public IReadOnlyList<Keybinding> All { get; }                        // effective, in precedence order
    public string? Resolve(KeyChord key, IReadOnlySet<string> context);  // highest precedence match
    public KeyChord? KeyFor(string command);                             // first binding with When.Always, for labels
}
public static class DefaultKeybindings { public static IReadOnlyList<KeybindingEntry> All { get; } }
```

- **Key names:** `a` to `z`, `0` to `9`, `f1` to `f12`, `enter`, `escape`, `tab`, `space`, `backspace`, `delete`, `insert`, `home`, `end`, `pageup`, `pagedown`, `up`, `down`, `left`, `right`.
- **`when` grammar:**
  - The forms are `a || b`, `a && b`, and `!a`. `!` binds tightest, then `&&`, then `||`. There are no parentheses.
  - The known keys are `promptFocus`, `inputFocus`, `gridFocus`, and `paletteOpen`.
  - Messages: `Unknown when key: {x}.` and `Cannot read when: {text}.`
- **Fixed command ids:** `prompt.focus`, `prompt.run`, `prompt.runNewTab`, `screen.cancel`, `screen.save`, `screen.find`, `tab.new`, `tab.close`, `tab.next`, `tab.previous`, `tab.goto.1` to `tab.goto.9`, `nav.back`, `nav.forward`, `palette.open`.
- **Defaults** (spec 6.5 and the 6.1 F key table):
  - `ctrl+l` prompt.focus
  - `enter` prompt.run, when `promptFocus`
  - `shift+enter` prompt.runNewTab, when `promptFocus`
  - `escape` screen.cancel, when `!paletteOpen`
  - `ctrl+t` tab.new
  - `ctrl+w` tab.close
  - `ctrl+tab` tab.next
  - `ctrl+shift+tab` tab.previous
  - `ctrl+1` to `ctrl+9` tab.goto.N
  - `alt+left` nav.back
  - `alt+right` nav.forward
  - `ctrl+s` screen.save
  - `ctrl+shift+p` palette.open
  - `ctrl+f` screen.find
  - `f1` to `f12`: run.HELP, run.DATE, run.FIND, run.CMPY, run.RFSH, run.SALE, run.PURC, run.RCPT, run.PYMT, run.JRNL, run.PRNT, run.SETG
- **Precedence**, as in VS Code: defaults, then machine, then user. Within one layer, a later entry beats an earlier one.
  - An entry whose command starts with `-` removes the earlier bindings of that command with that key. When it has a `when`, it removes only the binding with that same `when`.
- **`KeybindingFile.Parse` messages:**
  - `Unknown command: {c}.`
  - `Cannot read key: {k}.`
  - `This file is not valid JSON.`
  - `Each entry needs "key" and "command".`
  - Like `SettingsDocument`, a bad entry is skipped, and its error carries the entry's line.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("Ctrl+Shift+P", "ctrl+shift+p")] [InlineData("shift+ctrl+p", "ctrl+shift+p")] [InlineData("F5", "f5")]
public void Chords_are_canonical(string text, string canonical)
{ Assert.True(KeyChord.TryParse(text, out var c)); Assert.Equal(canonical, c.ToString()); }

[Theory] [InlineData("ctrl+")] [InlineData("ctrl+f13")] [InlineData("hyper+a")] [InlineData("")]
public void Bad_chords_are_refused(string text) => Assert.False(KeyChord.TryParse(text, out _));

[Fact] public void Enter_runs_only_in_the_prompt()
{
    var set = KeybindingSet.Build([], []);
    Assert.Equal("prompt.run", set.Resolve(Chord("enter"), new HashSet<string> { "promptFocus" }));
    Assert.Null(set.Resolve(Chord("enter"), new HashSet<string> { "gridFocus" }));
}

[Fact] public void User_rebinds_f6_and_removes_ctrl_l()
{
    var user = KeybindingFile.Parse("""[ {"key":"f6","command":"run.PURC"}, {"key":"ctrl+l","command":"-prompt.focus"} ]""");
    var set = KeybindingSet.Build([], user.Entries);
    Assert.Equal("run.PURC", set.Resolve(Chord("f6"), Empty));
    Assert.Null(set.Resolve(Chord("ctrl+l"), Empty));
    Assert.Equal(SettingLayer.User, set.All.First(b => b.Key == Chord("f6")).From);
}

[Fact] public void A_bad_entry_is_skipped_by_line_and_the_rest_apply()
{
    var load = KeybindingFile.Parse("[\n {\"key\":\"f6\",\"command\":\"run.PURC\"},\n {\"key\":\"f6\",\"command\":\"run.NOPE\"},\n]");
    Assert.Single(load.Entries);
    Assert.Equal(new SettingError(3, "Unknown command: run.NOPE."), load.Errors.Single());
}
```

Also add:
- `When_precedence_is_not_and_or`: `!a && b || c` with each context.
- `Unknown_when_key_is_an_error`.
- `Later_entry_in_one_layer_wins`.
- `Write_then_parse_round_trips`.
- `Every_default_command_is_known`.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Core.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: keybindings with when clauses and layered precedence`

---

### Task 4: User settings and company setting reads in the host

**Files:**
- Create: `src/OpenHisabKitab.Data/Migrations/Host/0003_user_settings.sql`, `src/OpenHisabKitab.Data/HostDb/UserSettingRepository.cs`, `src/OpenHisabKitab.Host/Application/UserSettingsService.cs`, `src/OpenHisabKitab.Contracts/Protos/settings.proto`
- Modify: `src/OpenHisabKitab.Contracts/Protos/companies.proto`, `src/OpenHisabKitab.Host/Application/CompanyService.cs`, `src/OpenHisabKitab.Host/Grpc/Endpoints.cs`, and host DI registration in `HostApp.cs`
- Test: `tests/OpenHisabKitab.Data.Tests/UserSettingRepositoryTests.cs`, `tests/OpenHisabKitab.Host.Tests/UserSettingsServiceTests.cs`, `tests/OpenHisabKitab.Host.Tests/CompanyServiceTests.cs`, `tests/OpenHisabKitab.Host.Tests/Grpc/SettingsGrpcTests.cs`

**Interfaces:**
- Consumes: `SettingsCatalog.Validate` (Task 2) and `KeybindingFile.Parse` (Task 3).
- Produces:

```sql
CREATE TABLE user_settings(
  user_id INTEGER NOT NULL REFERENCES users(id),
  key     TEXT NOT NULL,
  value   TEXT NOT NULL,
  PRIMARY KEY (user_id, key)
) STRICT;
```

```proto
// settings.proto. A user reads and changes only their own settings. Values are JSON text.
// The key "keybindings" holds the user's keybindings.json array.
service UserSettings {
  rpc List (Empty) returns (SettingValues);
  rpc Set (SettingValue) returns (Empty);
  rpc Reset (SettingKey) returns (Empty);
}
message SettingValue { string key = 1; string json = 2; }
message SettingValues { repeated SettingValue values = 1; }
message SettingKey { string key = 1; }
// companies.proto adds, importing settings.proto:
//   rpc ListSettings (CompanyRef) returns (SettingValues);
//   rpc ResetSetting (CompanySettingKey) returns (Empty);
//   message CompanySettingKey { int32 company = 1; string key = 2; }
```

```csharp
public static class UserSettingRepository
{
    public static IReadOnlyDictionary<string, string> List(SqliteConnection c, int userId);
    public static string? Get(SqliteConnection c, int userId, string key);
    public static void Set(SqliteTransaction tx, int userId, string key, string json);
    public static bool Remove(SqliteTransaction tx, int userId, string key);
}
public sealed class UserSettingsService   // same constructor shape as HostSettingsService
{
    public const string KeybindingsKey = "keybindings";
    public IReadOnlyDictionary<string, string> List(CallContext ctx);
    public Task SetAsync(CallContext ctx, string key, string json);
    public Task ResetAsync(CallContext ctx, string key);
}
// CompanyService adds:
public IReadOnlyDictionary<string, string> ListSettings(CallContext ctx, int id);   // RequireCompany only
public Task ResetSettingAsync(CallContext ctx, int id, string key);                 // ManageCompanies; publishes Settings
```

- **Rules:**
  - A value for `keybindings` must parse with no errors through `KeybindingFile.Parse`. The first error is refused as `keybindings.json line {n}: {message}`.
  - Any other key must pass `SettingsCatalog.Validate(key, json, SettingLayer.User)`.
  - `Reset` of a key that holds nothing succeeds and audits nothing.
  - Set and reset append to the host audit log with action `set` or `reset`, entity `user-setting`, key `{userId}:{key}`, and the before and after JSON.
  - Calls from REST or MCP are refused with `Change settings in Open Hisab Kitab.` No route exposes them, and the service checks anyway.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Each_user_sees_only_their_own_settings()
{
    using var h = new TestHost();
    var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, h.NewCompany());
    await h.UserSettings.SetAsync(h.Owner.Context, "ui.fontSize", "20");
    await h.UserSettings.SetAsync(clerk.Context, "ui.fontSize", "16");
    Assert.Equal("20", h.UserSettings.List(h.Owner.Context)["ui.fontSize"]);
    Assert.Equal("16", h.UserSettings.List(clerk.Context)["ui.fontSize"]);
}

[Fact] public async Task Company_setting_is_refused_for_a_user()
{
    using var h = new TestHost();
    var ex = await Assert.ThrowsAsync<ServiceException>(() => h.UserSettings.SetAsync(h.Owner.Context, "invoice.roundOff", "false"));
    Assert.Equal("Setting invoice.roundOff cannot be set for a user.", ex.Message);
}

[Fact] public async Task Bad_keybindings_are_refused_with_their_line()
{
    using var h = new TestHost();
    var ex = await Assert.ThrowsAsync<ServiceException>(() =>
        h.UserSettings.SetAsync(h.Owner.Context, "keybindings", "[\n {\"key\":\"ctrl+\",\"command\":\"tab.new\"}\n]"));
    Assert.Equal("keybindings.json line 2: Cannot read key: ctrl+.", ex.Message);
}
```

Also add:
- `Set_and_reset_are_audited_with_before_and_after`.
- `Reset_of_an_unset_key_writes_no_audit`.
- `Rest_caller_is_refused`.
- `CompanyServiceTests.List_settings_returns_stored_values_and_viewer_cannot_reset`.
- `SettingsGrpcTests.User_settings_round_trip_over_grpc`: through `HostFactory`, Set, List, Reset, List.
- `UserSettingRepositoryTests.Set_get_list_remove`.
- A migration test: a `host.db` at version 2 migrates to 3 with its users intact. Follow the existing migration test pattern in `Data.Tests`.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** Register `UserSettingsService` the way `HostSettingsService` is registered, and map the gRPC endpoint the way `TokensEndpoint` is mapped.
- [ ] **Step 4: Verify.** `dotnet test OpenHisabKitab.slnx`: all pass.
- [ ] **Step 5: Commit** `feat: user settings layer in host.db and company setting reads`

---

### Task 5: Connection, trust on first use, and reconnect

**Files:**
- Create in `src/OpenHisabKitab.Client.Model/Connection/`: `ClientPaths.cs`, `HostAddress.cs`, `KnownHosts.cs`, `IChannelFactory.cs`, `TlsChannelFactory.cs`, `HostClients.cs`, `HostException.cs`, `ConnectionState.cs`, `HostConnection.cs`
- Create: `tests/OpenHisabKitab.Client.Model.Tests/ClientTestHost.cs`, `Connection/HostConnectionTests.cs`, `Connection/TrustTests.cs`, `Connection/HostAddressTests.cs`

**Interfaces:**
- Consumes: `ProductVersion.Current` and `CertificateFingerprint.Of` (Task 1). Tests also consume `HostFactory` from `Host.Tests`, which is linked as a source file (`<Compile Include="..\OpenHisabKitab.Host.Tests\Grpc\HostFactory.cs" Link="HostFactory.cs" />`) so the test projects do not reference each other. Everything it and the client tests use from Host is public (`IRemoteAddressAccessor`, `IListenerAccessor`, `HostApp`, `SessionStore`, `ApiTokenService`, `ChangeSource`), so no `InternalsVisibleTo` is needed.
- Produces:

```csharp
public sealed record ClientPaths(string Root)
{
    public static ClientPaths Default();      // %LOCALAPPDATA%\OpenHisabKitab, created if missing
    public string Settings { get; } public string Keybindings { get; } public string KnownHosts { get; } public string State { get; }
}
public sealed record HostAddress(string Name, int Port)
{
    public static bool TryParse(string name, string port, out HostAddress address, out string? error);
    public override string ToString();        // "name:port"
}
public sealed class KnownHosts(string path)
{
    public string? Fingerprint(HostAddress host);
    public void Trust(HostAddress host, string fingerprint);
}
public sealed class ChannelOpening(GrpcChannel channel) : IDisposable
{ public GrpcChannel Channel { get; } public string? SeenFingerprint { get; set; } }
public interface IChannelFactory { ChannelOpening Open(HostAddress host, string? pinnedFingerprint); }
public sealed class TlsChannelFactory : IChannelFactory;  // SocketsHttpHandler; the callback records the fingerprint, accepts only the pin
public sealed class HostClients(CallInvoker invoker)     // one property per generated client
{ public Session.SessionClient Session { get; } public Companies.CompaniesClient Companies { get; } public Admin.AdminClient Admin { get; }
  public Tokens.TokensClient Tokens { get; } public UserSettings.UserSettingsClient UserSettings { get; } public Changes.ChangesClient Changes { get; }
  public Masters.MastersClient Masters { get; } public Vouchers.VouchersClient Vouchers { get; } public Reports.ReportsClient Reports { get; } }
public enum ConnectionState { Disconnected, Connecting, NeedsTrust, CertificateChanged, VersionMismatch, NeedsSetup, NeedsLogin, Connected, Reconnecting }
public enum HostErrorKind { Unreachable, NeedsLogin, VersionMismatch, Conflict, Refused }
public sealed class HostException(HostErrorKind kind, string message, Metadata trailers) : Exception(message)
{ public HostErrorKind Kind { get; } public T Current<T>(MessageParser<T> parser) where T : IMessage<T>; } // decodes "ohk-current-bin"
public sealed class HostConnection(IChannelFactory channels, KnownHosts known, TimeProvider time,
    string clientVersion, string machineName) : IAsyncDisposable
{
    public ConnectionState State { get; }
    public event Action<ConnectionState>? StateChanged;
    public event Action? Reconnected;           // raised when a lost connection comes back logged in
    public HostAddress? Address { get; }
    public string? SeenFingerprint { get; } public string? PinnedFingerprint { get; }
    public LoginReply? Session { get; }
    public string? Message { get; }             // the one line explaining State, or null
    public Task<ConnectionState> ConnectAsync(HostAddress host, CancellationToken ct = default);
    public Task<ConnectionState> TrustSeenCertificateAsync(CancellationToken ct = default); // pins and connects again
    public Task SetupOwnerAsync(string name, string password, CancellationToken ct = default);
    public Task LoginAsync(string name, string password, CancellationToken ct = default);
    public Task LogoutAsync(CancellationToken ct = default);
    public Task<T> CallAsync<T>(Func<HostClients, CallOptions, AsyncUnaryCall<T>> call, CancellationToken ct = default);
    public CallOptions StreamOptions(CancellationToken ct);  // headers, no deadline, for Changes.Watch
}
```

- **`ConnectAsync` order:**
  1. Open a channel with the stored pin, then call `GetInfo`.
  2. When the call fails with a seen fingerprint that is not the pin:
     - with no pin, the state is `NeedsTrust`;
     - otherwise it is `CertificateChanged`.
     No other call is made.
  3. When `info.Version != clientVersion`, the state is `VersionMismatch`, with message `Install Open Hisab Kitab {info.Version} to connect.`
  4. Otherwise the state is `NeedsSetup` or `NeedsLogin`, from `needs_setup`.
- **Credentials are never sent unless trusted.** `SetupOwnerAsync` and `LoginAsync` throw `InvalidOperationException` unless the state is `NeedsSetup` or `NeedsLogin`.
- **`CallAsync` maps `RpcException`:**
  - `Unavailable` or `DeadlineExceeded` becomes `Unreachable` with `Host not reachable. Trying again.` It also sets `Reconnecting` and starts the retry loop.
  - `Unauthenticated` becomes `NeedsLogin`, carrying the host's message (`Log in again.`), and sets the state `NeedsLogin`.
  - `FailedPrecondition` whose detail starts with `Install Open Hisab Kitab` becomes `VersionMismatch`.
  - `Aborted` becomes `Conflict`.
  - Anything else becomes `Refused`, carrying the host's detail.
  - The token is kept on `NeedsLogin`, so tabs stay. `LoginAsync` replaces it.
  - A `LoginAsync` that follows a lost session raises `Reconnected`, as the retry loop does, because change events were missed while logged out.
- **The retry loop** waits 1, 2, 4, 8, 15, then 30 seconds and keeps 30, all on `TimeProvider`. Each attempt calls `GetInfo`.
  - When the host answers and a session exists, it calls `Companies.List`. Success sets `Connected` and raises `Reconnected`. `Unauthenticated` sets `NeedsLogin`.
  - A changed certificate stops the loop with `CertificateChanged`.
- **Messages:** add `HostUnreachable`, `VersionMismatch`, `EnterHostName` (`Enter the host name or IP address.`), and `EnterPort` (`Enter a port from 1 to 65535.`) to `Text.resx`.
- **`ClientTestHost`** wraps `HostFactory`.
  - Its channel factory returns `Server.CreateHandler()` channels and sets `SeenFingerprint` to a fixed fake fingerprint, which tests pin.
  - `bool Offline { get; set; }` makes every channel's handler throw `HttpRequestException`, which Grpc reports as `Unavailable`.
  - `Task<HostConnection> LoggedInOwnerAsync()` sets up and logs in `owner` with password `owner-password-1`.
  - `FakeTimeProvider Time`, `HostAddress Address`, and `HostConnection Connection(string? clientVersion = null)`.
  - `void EndAllSessions()` calls `Services.GetRequiredService<SessionStore>().RemoveAll()`, which is exactly what a host restart does to sessions.
  - `HostClients OtherOwnerClient()` and `Metadata Headers()` give a second logged in Owner for changes made "by another PC".
  - `Task Settle()` waits for pending retry and watch work.
  - Tasks 7 and 8 add `ShellViewModel Shell()` (not logged in) and `Task<ShellViewModel> LoggedInShellAsync(string[] open, string? company = null)`, which create company `A Traders` when `company` is given. Both register the factories built so far.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Unknown_certificate_needs_trust_and_sends_no_password()
{
    // A real Kestrel host as in KestrelTlsTests, with TlsChannelFactory.
    var state = await connection.ConnectAsync(new HostAddress("localhost", port));
    Assert.Equal(ConnectionState.NeedsTrust, state);
    Assert.Equal(File.ReadAllText(Path.Combine(hostFolder, "host-cert-fingerprint.txt")).Trim(), connection.SeenFingerprint);
    await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync("owner", "x"));
    Assert.Equal(ConnectionState.NeedsSetup, await connection.TrustSeenCertificateAsync());
    Assert.Equal(connection.SeenFingerprint, known.Fingerprint(new HostAddress("localhost", port)));
}

[Fact] public async Task Changed_certificate_blocks_and_shows_both_fingerprints()
{
    known.Trust(address, "00:11");
    Assert.Equal(ConnectionState.CertificateChanged, await connection.ConnectAsync(address));
    Assert.Equal(("00:11", expected), (connection.PinnedFingerprint, connection.SeenFingerprint));
}

[Fact] public async Task Other_version_stops_before_login()
{
    using var t = new ClientTestHost();
    var c = t.Connection(clientVersion: "9.9.9");
    Assert.Equal(ConnectionState.VersionMismatch, await c.ConnectAsync(t.Address));
    Assert.Equal($"Install Open Hisab Kitab {ProductVersion.Current} to connect.", c.Message);
}

[Fact] public async Task Lost_session_needs_login_then_the_same_call_succeeds()   // Review Focus 1
{
    using var t = new ClientTestHost();
    var c = await t.LoggedInOwnerAsync();
    t.EndAllSessions();                     // as a host restart would
    var ex = await Assert.ThrowsAsync<HostException>(() => c.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o)));
    Assert.Equal((HostErrorKind.NeedsLogin, "Log in again."), (ex.Kind, ex.Message));
    Assert.Equal(ConnectionState.NeedsLogin, c.State);
    await c.LoginAsync("owner", "owner-password-1");
    Assert.NotNull(await c.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o)));
}

[Fact] public async Task Unreachable_host_retries_with_backoff_and_reports_reconnected()
{
    using var t = new ClientTestHost();
    var c = await t.LoggedInOwnerAsync();
    var reconnected = 0; c.Reconnected += () => reconnected++;
    t.Offline = true;
    var ex = await Assert.ThrowsAsync<HostException>(() => c.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o)));
    Assert.Equal((HostErrorKind.Unreachable, ConnectionState.Reconnecting), (ex.Kind, c.State));
    t.Time.Advance(TimeSpan.FromSeconds(1)); await t.Settle();   // first retry fails
    t.Offline = false;
    t.Time.Advance(TimeSpan.FromSeconds(2)); await t.Settle();
    Assert.Equal((ConnectionState.Connected, 1), (c.State, reconnected));
}
```

Also add:
- `Conflict_carries_the_current_party`: save a party, save it again with row version 0, and `ex.Current(PartyMsg.Parser)` returns the stored party.
- `Every_call_sends_version_and_machine_headers`: setup over a fake channel whose interceptor records the headers.
- `HostAddressTests`: blank name, port `0`, `65536`, and `abc` give the two messages; `"pc-1"` with `"47250"` parses.
- `KnownHosts_survive_a_new_instance`.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.** `HostClients` builds its clients over `channel.Intercept(headerInterceptor)`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: client connection with trust on first use, version check, and reconnect`

---

### Task 6: Client settings and keybinding stores

**Files:**
- Create in `src/OpenHisabKitab.Client.Model/`: `Settings/MachineFiles.cs`, `Settings/ClientState.cs`, `Settings/SettingsStore.cs`, `Settings/KeybindingStore.cs`, `Formatting/DateText.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Settings/SettingsStoreTests.cs`, `KeybindingStoreTests.cs`, `Formatting/DateTextTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3, 4, and 5.
- Produces:

```csharp
public sealed class MachineFiles(ClientPaths paths)
{
    public SettingsLoad LoadSettings();  public void SaveSettings(IReadOnlyDictionary<string, string> values);
    public KeybindingLoad LoadKeybindings(); public void SaveKeybindings(IReadOnlyList<KeybindingEntry> entries);
    public string ReadText(SettingLayer layer);   // raw machine file text for the JSON editors; "{}" or "[]" when missing
    public ClientState LoadState(); public void SaveState(ClientState state);
}
public sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool Maximised);
public sealed record ClientState(string? LastHost, IReadOnlyDictionary<string, int> LastCompanyByHost, WindowPlacement? Window);
public sealed class SettingsStore(MachineFiles files, HostConnection host)
{
    public EffectiveSettings Current { get; }
    public IReadOnlyList<string> LoadErrors { get; }   // "settings.json line {n}: {message}"
    public event Action? Changed;
    public Task LoadAsync(int? company, CancellationToken ct = default);   // machine file, UserSettings.List, Companies.ListSettings
    public Task SetAsync(string key, string json, SettingLayer layer, int? company);
    public Task ResetAsync(string key, SettingLayer layer, int? company);
    public string LayerText(SettingLayer layer);       // the layer as SettingsDocument.Write text
    public Task<IReadOnlyList<SettingError>> SaveLayerTextAsync(SettingLayer layer, string text, int? company);
}
public sealed class KeybindingStore(MachineFiles files, HostConnection host)
{
    public KeybindingSet Current { get; }
    public IReadOnlyList<string> LoadErrors { get; }   // "keybindings.json line {n}: {message}"
    public event Action? Changed;
    public Task LoadAsync(CancellationToken ct = default);
    public string LayerText(SettingLayer layer);       // Machine or User
    public Task<IReadOnlyList<SettingError>> SaveLayerTextAsync(SettingLayer layer, string text);
    public Task SetEntriesAsync(SettingLayer layer, IReadOnlyList<KeybindingEntry> entries);
}
public static class DateText { public static string Format(DateOnly d, string dateFormat); } // the three ui.dateFormat choices
```

- **Rules:**
  - A layer the client cannot read gives empty values. This covers the user or company layer while logged out or unreachable.
  - **`SaveLayerTextAsync`** parses the whole text first. When there are any errors, it returns them and writes nothing.
    - For Machine, it writes the file.
    - For User or Company, it sends `Set` for each new or changed key and `Reset` for each removed key, then reloads.
  - **A machine file with errors** keeps its good entries in force. Its errors go to `LoadErrors`, and the file is not rewritten until the user saves that layer.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Broken_machine_file_keeps_defaults_and_is_left_alone()   // Review Focus 3
{
    File.WriteAllText(paths.Settings, "{ \"ui.fontSize\": 99, \"ui.fontFamily\": \"Tahoma\", }");
    await store.LoadAsync(company: null);
    Assert.Equal(14, store.Current.Int("ui.fontSize"));
    Assert.Equal("Tahoma", store.Current.Text("ui.fontFamily"));
    Assert.Equal("settings.json line 1: Setting ui.fontSize must be a whole number from 12 to 28.", store.LoadErrors.Single());
    Assert.Equal("{ \"ui.fontSize\": 99, \"ui.fontFamily\": \"Tahoma\", }", File.ReadAllText(paths.Settings));
}

[Fact] public async Task User_layer_beats_machine_and_follows_the_user()
{
    await store.SetAsync("ui.fontSize", "16", SettingLayer.Machine, null);
    await store.SetAsync("ui.fontSize", "20", SettingLayer.User, null);
    var other = new SettingsStore(new MachineFiles(new ClientPaths(otherFolder)), connection);  // another PC
    await other.LoadAsync(null);
    Assert.Equal(20, other.Current.Int("ui.fontSize"));
}

[Fact] public async Task Json_save_with_one_bad_key_writes_nothing()
{
    var errors = await store.SaveLayerTextAsync(SettingLayer.User, "{ \"ui.fontSize\": 18, \"invoice.roundOff\": false }", null);
    Assert.Equal("Setting invoice.roundOff cannot be set for a user.", errors.Single().Message);
    Assert.Equal(14, store.Current.Int("ui.fontSize"));
}
```

Also add:
- `Company_layer_comes_from_the_open_company`.
- `Removed_key_in_user_json_is_reset_on_the_host`.
- `KeybindingStoreTests.User_keybindings_load_from_the_host_and_win`.
- `Broken_keybindings_file_reports_its_line`.
- `DateTextTests` for each of the three formats, using 5 April 2026.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: client settings and keybinding stores over three layers`

---

### Task 7: The shell model: prompt, tabs, router, keys, and palette

**Files:**
- Create in `src/OpenHisabKitab.Client.Model/Shell/`: `IScreen.cs`, `ScreenViewModel.cs`, `ShellContext.cs`, `IConfirm.cs`, `FunctionRouter.cs`, `PromptViewModel.cs`, `TabViewModel.cs`, `TabsViewModel.cs`, `StatusBarViewModel.cs`, `PaletteViewModel.cs`, `FKeyRow.cs`, `ShellViewModel.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Shell/PromptAndRouterTests.cs`, `TabsTests.cs`, `KeyDispatchTests.cs`, `PaletteTests.cs`. These use a `FakeScreen` and `FakeConfirm`, with no host.

**Interfaces:**
- Consumes: `CommandParser`, `FunctionCatalog`, `SettingsCatalog`, `KeybindingSet`, `KeyChord`.
- Produces:

```csharp
public interface IScreen
{
    string Code { get; }                 // "HELP"
    string Subject { get; }              // "" or a short subject for the tab title
    bool HasUnsavedEdits { get; }
    Task LoadAsync(CancellationToken ct);
    bool IsAffectedBy(ChangeEventMsg change);
    bool Cancel();                       // Esc; true when the screen handled it (closed a sub panel, cleared a field)
    Task SaveAsync();                    // Ctrl+S; screens without saving do nothing
    void Find();                         // Ctrl+F; focuses the screen's filter when it has one
}
public abstract partial class ScreenViewModel(ShellContext ctx) : ObservableObject, IScreen
{   // the base class every screen uses; it holds Message (one line shown in the screen) and Busy
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    protected Task RunAsync(Func<Task> work);   // sets Busy, catches HostException into Message, never clears input
}
public sealed class ShellContext
{   // what screens share: host, stores, the session, the open company and year, and the working date
    public required HostConnection Host { get; init; }
    public required SettingsStore Settings { get; init; }
    public required KeybindingStore Keys { get; init; }
    public required TimeProvider Time { get; init; }
    public CompanyEntry? Company { get; set; }  public FinancialYear? Year { get; set; }  public DateOnly WorkingDate { get; set; }
    public bool Has(string permission);          // from Host.Session.Permissions, ordinal
    public event Action? CompanyChanged;
    public void OpenCompany(CompanyEntry company, FinancialYear year);  // sets both, raises CompanyChanged
}
public interface IConfirm { Task<bool> AskAsync(string question); }   // the view shows a keyboard Yes/No box
public sealed class FunctionRouter(IReadOnlyDictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> built)
{   // a factory may act and return null, as DATE with a valid date does
    public RouteResult Route(string input, ShellContext ctx);
}
public sealed record RouteResult(IScreen? Screen, string? Status, bool Refresh, string? Command);
public sealed partial class TabViewModel : ObservableObject
{ public int Number { get; } public IScreen Screen { get; } public string Title { get; } public bool Changed { get; set; }
  public bool CanGoBack { get; } public bool CanGoForward { get; } }
public sealed partial class TabsViewModel : ObservableObject
{ public ObservableCollection<TabViewModel> Tabs { get; } public TabViewModel? Active { get; set; }
  public void Show(IScreen screen, string command, bool newTab); public Task<bool> CloseActiveAsync(IConfirm confirm);
  public void Next(); public void Previous(); public void GoTo(int number); public Task BackAsync(); public Task ForwardAsync(); }
// tab.new (Ctrl+T) sets ShellViewModel.NextRunInNewTab and focuses the prompt; the next prompt run opens a new tab and clears it.
public sealed partial class PromptViewModel : ObservableObject
{ public string Text { get; set; } public IReadOnlyList<FunctionInfo> Suggestions { get; }
  public int SelectedSuggestion { get; set; } public void Complete(); }   // Complete puts the selected code and a space
public sealed record FKeyLabel(string Key, string? Code);   // ("F6", "SALE"); Code null when the key runs no function
public static class FKeyRow { public static IReadOnlyList<FKeyLabel> From(KeybindingSet keys); }
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(ShellContext ctx, FunctionRouter router, IConfirm confirm);
    public PromptViewModel Prompt { get; } public TabsViewModel Tabs { get; } public StatusBarViewModel Status { get; }
    public PaletteViewModel Palette { get; } public IReadOnlyList<FKeyLabel> FKeys { get; } public string Title { get; }
    public ShellContext Context { get; }
    public event Action? FocusPromptRequested;
    public Task RunAsync(string input, bool newTab);
    public Task<bool> HandleKeyAsync(KeyChord key, IReadOnlySet<string> context);  // true when a binding ran
}
```

- **Behaviour** (spec 6.1, 6.2, 6.5):
  - **`RunAsync`:**
    - Text that does not parse, or a code not in `FunctionCatalog`, sets the status to `Unknown function: {first word uppercased}` and opens nothing.
    - A catalog code with no factory sets `{CODE} is not in this version yet.`
    - `RFSH` reloads the active tab and clears its `Changed`. With no tab, the status is `Nothing to refresh.`
    - Enter replaces the active tab's screen and pushes the old command onto the tab's back history. With no tabs, Enter opens a new tab. Shift+Enter always opens a new tab.
    - The prompt clears after a run. The status message clears at the start of the next run.
  - **Tabs.** Numbers are 1 to n in strip order and renumber after a close. The title is `"{n} {CODE}"`, plus `" {Subject}"` when the subject is not empty.
    - Closing a tab with unsaved edits asks `Discard unsaved changes?` once. No closes nothing.
    - `GoTo(n)` beyond the count does nothing.
    - Back and Forward re-run the stored command text into a new screen instance. They ask first when the current screen has unsaved edits.
    - `tab.new` (Ctrl+T) opens no empty tab. It focuses the prompt, and the next run from the prompt opens a new tab, as Shift+Enter does. Esc in the prompt clears this.
  - **Esc.** Esc calls `Screen.Cancel()`. When that returns false, Esc acts as Back.
  - **Keys.** `HandleKeyAsync` resolves the chord through `ctx.Keys.Current`:
    - `run.{CODE}` runs the code in the active tab.
    - `tab.goto.N` calls `GoTo(N)`.
    - `prompt.focus` raises `FocusPromptRequested`.
    - `prompt.run` and `prompt.runNewTab` run `Prompt.Text`.
  - **Suggestions.** They show `FunctionCatalog.Search(text)` while the text has no space. They are empty once a space is typed or the text is empty.
  - **Title.** The title is `Open Hisab Kitab` with no company open, otherwise `Open Hisab Kitab: {Company} FY {Year.Label}` (spec 6.1).
  - **Palette.** It lists functions as `CODE  Name` and settings as `key  Description`, filtered by `FunctionCatalog.Search` and `SettingsCatalog.Search`. Enter on a function runs it in the active tab. Enter on a setting runs `SETG {key}`. Esc closes the palette.
- **Messages to add to `Text.resx`:**
  - `UnknownFunction`
  - `NotInThisVersion`
  - `NothingToRefresh`
  - `DiscardChanges`
  - `TitleNoCompany`: `Open Hisab Kitab`
  - `TitleWithCompany`: `Open Hisab Kitab: {0} FY {1}`

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData("xyzq", "Unknown function: XYZQ")] [InlineData("12", "Unknown function: 12")]
[InlineData("trlb", "TRLB is not in this version yet.")]
public async Task Unbuilt_or_unknown_codes_show_one_line_and_open_nothing(string input, string status)
{
    var shell = Shell(built: ["HELP"]);
    await shell.RunAsync(input, newTab: false);
    Assert.Equal(status, shell.Status.Message);
    Assert.Empty(shell.Tabs.Tabs);
}

[Fact] public async Task Enter_replaces_the_active_tab_and_shift_enter_opens_another()
{
    var shell = Shell(built: ["HELP", "DATE"]);
    await shell.RunAsync("help", false); await shell.RunAsync("date", false); await shell.RunAsync("help", true);
    Assert.Equal(["1 DATE", "2 HELP"], shell.Tabs.Tabs.Select(t => t.Title));
    shell.Tabs.GoTo(1); await shell.Tabs.BackAsync();
    Assert.Equal("1 HELP", shell.Tabs.Active!.Title);
}

[Fact] public async Task Closing_unsaved_tab_asks_once_and_no_keeps_it()
{
    var confirm = new FakeConfirm(answer: false);
    var shell = Shell(built: ["SETG"], confirm: confirm, unsaved: true);
    await shell.RunAsync("setg", false);
    Assert.True(await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext));
    Assert.Equal((1, 1), (confirm.Asked, shell.Tabs.Tabs.Count));
}

[Fact] public async Task Removed_binding_is_not_handled_and_rebound_f6_relabels()   // Review Focus 4
{
    var shell = Shell(built: ["HELP"], userKeys: """[ {"key":"ctrl+l","command":"-prompt.focus"}, {"key":"f6","command":"run.PURC"} ]""");
    Assert.False(await shell.HandleKeyAsync(Chord("ctrl+l"), NoContext));
    Assert.Equal(new FKeyLabel("F6", "PURC"), shell.FKeys[5]);
}
```

Also add:
- `Tab_numbers_close_up_after_a_close`.
- `Ctrl_t_then_enter_opens_a_new_tab`.
- `Ctrl_digit_beyond_count_does_nothing`.
- `Esc_goes_back_when_the_screen_does_not_handle_it`.
- `Rfsh_reloads_the_active_tab_and_clears_changed`.
- `Rfsh_with_no_tab_says_nothing_to_refresh`.
- `Suggestions_follow_typing_and_stop_after_a_space`.
- `Complete_puts_the_selected_code`.
- `Palette_finds_settings_and_opens_setg_for_one`: typing `font` lists `ui.fontFamily` and `ui.fontSize`, and Enter on `ui.fontSize` routes `SETG ui.fontSize`.
- `Title_follows_the_open_company`.

The `ShellContext` in these tests is built over a `ClientTestHost` connection that is never logged in. The stores are loaded from machine files only.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: shell model with prompt, tabs, router, keybindings, and palette`

---

### Task 8: Session flow, change watching, and the status bar

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Shell/SessionViewModel.cs`, `src/OpenHisabKitab.Client.Model/Shell/ChangeWatcher.cs`
- Modify: `ShellViewModel.cs` (owns a `SessionViewModel` and a `ChangeWatcher`), `StatusBarViewModel.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Shell/SessionTests.cs`, `ChangeWatcherTests.cs`

**Interfaces:**
- Consumes: `HostConnection` (Task 5), the stores (Task 6), and the shell (Task 7).
- Produces:

```csharp
public enum SessionPanel { None, Connect, Trust, CertificateChanged, Setup, Login, VersionMismatch }
public sealed partial class SessionViewModel : ObservableObject
{
    public SessionPanel Panel { get; }                     // from HostConnection.State
    [ObservableProperty] public partial string HostName { get; set; }   // prefilled from connection.host
    [ObservableProperty] public partial string Port { get; set; }       // prefilled from connection.port
    [ObservableProperty] public partial string UserName { get; set; }   // kept after a lost session
    public string? Message { get; }
    public string? SeenFingerprint { get; } public string? PinnedFingerprint { get; }
    public Task ConnectAsync();            // validates HostAddress, saves connection.* and LastHost to machine files
    public Task TrustAsync();
    public Task SetupAsync(string password, string confirm);   // the password never lives in a property
    public Task LoginAsync(string password);
}
public sealed class ChangeWatcher(ShellContext ctx, TabsViewModel tabs)
{   // runs Changes.Watch while Connected; restarts it after Reconnected
    public Task RunAsync(CancellationToken ct);
}
public sealed partial class StatusBarViewModel : ObservableObject
{ public string? Message { get; set; } public string Connection { get; } public string WorkingDate { get; } public bool Changed { get; } }
```

- **Panels.**
  - `Panel` is `None` only while `Connected` or `Reconnecting`. Reconnecting keeps the shell usable, with the status line showing it.
  - While `Panel` is not `None`, `HandleKeyAsync` returns false for every binding, and typing anywhere is off. F keys, Ctrl+W, and typed letters must never act on the shell under the login panel. The panel's own Enter and Esc work as normal buttons.
  - `NeedsLogin` after a lost session shows the login panel over the shell. Tabs and typed text stay.
  - After a login, `ShellViewModel` reloads both stores, then:
    - reopens the last company for this host from `state.json`, when the user can still open it;
    - otherwise runs `CMPY` in a new tab, when no tab is open.
- **Setup.** When the two passwords differ, the message is `The passwords differ.` The host's refusals are shown as sent, for example `Set up the Owner on the host PC.` from a PC that is not the host.
- **Changes.**
  - A `ChangeEventMsg` for the open company goes to each tab whose `Screen.IsAffectedBy` is true. Kind `Settings` for the open company also reloads `SettingsStore`.
  - With `reports.autoReload` on, the tab's screen reloads. Otherwise the tab's `Changed` is set.
  - On `Reconnected`, every tab's `Changed` is set, because events were missed.
- **Status.**
  - `Connection` shows `Connected to {host}`, `Host not reachable. Trying again.`, or `Log in again.`
  - `WorkingDate` shows `Date {DateText}`.
  - `Changed` reflects the active tab.
  - The first item in `LoadErrors` of either store is shown as `Message` after load.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task First_run_goes_connect_setup_then_company()
{
    using var t = new ClientTestHost();
    var shell = t.Shell();
    Assert.Equal(SessionPanel.Connect, shell.Session.Panel);
    shell.Session.HostName = "localhost"; shell.Session.Port = "47250";
    await shell.Session.ConnectAsync();
    Assert.Equal(SessionPanel.Setup, shell.Session.Panel);
    shell.Session.UserName = "owner";
    await shell.Session.SetupAsync("owner-password-1", "owner-password-1");
    Assert.Equal(SessionPanel.None, shell.Session.Panel);
    Assert.Equal("1 CMPY", shell.Tabs.Active!.Title);
}

[Fact] public async Task Lost_session_keeps_tabs_and_marks_them_changed_after_login()   // Review Focus 1
{
    using var t = new ClientTestHost();
    var shell = await t.LoggedInShellAsync(open: ["HELP", "CMPY"]);
    t.EndAllSessions();
    await shell.RunAsync("rfsh", false);                    // the next call finds the session gone
    Assert.Equal((SessionPanel.Login, "owner", 2), (shell.Session.Panel, shell.Session.UserName, shell.Tabs.Tabs.Count));
    await shell.Session.LoginAsync("owner-password-1");
    Assert.All(shell.Tabs.Tabs, tab => Assert.True(tab.Changed));
}

[Fact] public async Task Settings_change_marks_an_affected_tab()
{
    using var t = new ClientTestHost();
    var shell = await t.LoggedInShellAsync(open: ["CMPY"], company: "A Traders");
    await t.OtherOwnerClient().Companies.SetSettingAsync(new() { Company = 1, Key = "invoice.roundOff", Json = "false" }, t.Headers());
    await t.Settle();
    Assert.True(shell.Tabs.Active!.Changed);
    Assert.False(shell.Context.Settings.Current.Bool("invoice.roundOff"));
}
```

Also add:
- `Passwords_that_differ_are_refused_before_calling_the_host`.
- `Shell_keys_are_off_while_a_panel_is_up`: with the login panel showing, `ctrl+w` and `f1` return false and change no tab.
- `Auto_reload_reloads_instead_of_marking`.
- `Unreachable_shows_trying_again_in_status`.
- `Load_error_shows_in_status`.
- `Last_company_reopens_after_login`.

- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: session panels, change marker, and status bar`

---

### Task 9: WPF shell window and theme

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Theme/ThemeColors.cs`, `Theme/Contrast.cs`
- Create: `src/OpenHisabKitab.Client/Theme/ThemeResources.cs`, `Theme/Styles.xaml`, `Input/KeyInput.cs`, `Views/SessionView.xaml`, `Views/ConfirmBox.xaml`
- Modify: `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Theme/ContrastTests.cs`

**Interfaces:**
- Consumes: `ShellViewModel` and everything it exposes.
- Produces:

```csharp
public sealed record ThemeColors(string Face, string Text, string Content, string ContentText, string Selection, string SelectionText,
    string DisabledText, string InactiveTitle, string InactiveTitleText, string Focus, string FocusOnSelection)
{ public static ThemeColors Classic { get; } }  // "#C0C0C0", "#000000", "#FFFFFF", "#000000", "#000080", "#FFFFFF",
                                               // "#404040", "#404040", "#FFFFFF", "#000000", "#FFFFFF"
public static class Contrast { public static double Ratio(string hexA, string hexB); }  // WCAG 2.2 relative luminance
// Client:
public static class ThemeResources { public static void Apply(ResourceDictionary target, ThemeColors colors, EffectiveSettings settings, bool highContrast); }
public static class KeyInput { public static KeyChord? From(KeyEventArgs e); public static IReadOnlySet<string> Context(IInputElement? focused, bool paletteOpen); }
```

- **Theme.**
  - `ThemeResources` builds every brush from `ThemeColors`, so the colours have one source. It sets these resource keys:
    - `UiFont` from `ui.fontFamily`;
    - `UiFontSize` from `ui.fontSize`;
    - `NumberFont` as `Consolas`.
  - It reapplies on `SettingsStore.Changed`.
  - When `SystemParameters.HighContrast` is true, the brushes are `SystemColors` brushes. It listens to `SystemParameters.StaticPropertyChanged`.
- **`Styles.xaml`** gives every control the Win98 look, using `DynamicResource` for each brush:
  - bevelled 3D borders;
  - square corners;
  - no animations;
  - a `FocusVisualStyle` with a 2 px solid `Focus` outline, and `FocusOnSelection` inside selected rows.
- **`MainWindow` rows, top to bottom** (spec 6.1):
  1. **Menu.** `_File` holds `_Log out` and `E_xit`. `_Edit` holds `_Find` and `_Save`. `_View` holds `Command _palette`, `_Next tab`, and `_Previous tab`. `_Help` holds `_Functions` (runs HELP) and `_Keybindings` (runs KEYS). Each item's `InputGestureText` comes from `KeyFor`.
  2. **Prompt.** A `>` label, then a `TextBox` named `Prompt`, with a suggestion `Popup` showing `CODE  Name` in `NumberFont`.
  3. **Tab strip.**
  4. **Content.** A `ContentControl` with one `DataTemplate` per screen view model, in `App.xaml`.
  5. **F key row.** A `UniformGrid` of 12 buttons labelled `F1 HELP`, or only `F1` when no code is bound.
  6. **Status bar.**
  - The session panel and the palette are overlays in the same grid.
- **Keys.**
  - Window `PreviewKeyDown` calls `KeyInput.From`, then `HandleKeyAsync`. When that returns true, the event is handled.
  - Window `PreviewTextInput` with focus outside any `TextBox` focuses `Prompt` and appends the text (spec 6.2, "Typing anywhere").
  - `KeyInput.From` reads `e.SystemKey` when `e.Key` is `Key.System`, because WPF reports Alt chords and F10 that way. F10 is bound to `run.JRNL` and must not open the menu bar when it is bound. A bare Alt still opens the menu.
  - `KeyInput.Context` gives `promptFocus` for the `Prompt` box, `inputFocus` for any other `TextBox`, `PasswordBox`, or `ComboBox`, and `gridFocus` for a `DataGrid`.
- **Accessibility.**
  - Every control sets `AutomationProperties.Name` through `{t:Txt}`.
  - **Display scaling** (spec 6.6): `app.manifest` declares `<dpiAwareness>PerMonitorV2</dpiAwareness>`.
  - **Text scaling** (spec 6.6): `ThemeResources` multiplies `ui.fontSize` by the Windows text scale. This is `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`, from 100 to 225 percent, or 100 when missing. The result is rounded to a whole pixel and is not capped at 28. Read it through `TextScale.Percent()` in `Client/Theme`.
  - **Disabled controls** show `DisabledText` and also lose their raised bevel, keeping a flat 1 px border, so disabled is not shown by colour alone (spec 6.6).
- **Composition in `App.xaml.cs`:**
  - It reads `--profile`, then builds `ClientPaths`, `MachineFiles`, `HostConnection(new TlsChannelFactory(), ...)`, the stores, `ShellContext`, the router with the factories from Tasks 10 to 13, and `ShellViewModel`.
  - It restores and saves `WindowPlacement` through `state.json`.
  - `IConfirm` is `ConfirmBox`. Y and N are access keys, and Esc means No.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(nameof(ThemeColors.Text), nameof(ThemeColors.Face), 4.5)]
[InlineData(nameof(ThemeColors.ContentText), nameof(ThemeColors.Content), 4.5)]
[InlineData(nameof(ThemeColors.SelectionText), nameof(ThemeColors.Selection), 4.5)]
[InlineData(nameof(ThemeColors.DisabledText), nameof(ThemeColors.Face), 4.5)]
[InlineData(nameof(ThemeColors.InactiveTitleText), nameof(ThemeColors.InactiveTitle), 4.5)]
[InlineData(nameof(ThemeColors.Focus), nameof(ThemeColors.Face), 3.0)]
[InlineData(nameof(ThemeColors.Focus), nameof(ThemeColors.Content), 3.0)]
[InlineData(nameof(ThemeColors.FocusOnSelection), nameof(ThemeColors.Selection), 3.0)]
public void Classic_theme_meets_wcag_aa(string fore, string back, double minimum) =>
    Assert.True(Contrast.Ratio(Get(fore), Get(back)) >= minimum);

[Fact] public void Ratio_matches_known_values()
{
    Assert.Equal(21.0, Contrast.Ratio("#000000", "#FFFFFF"), 1);
    Assert.Equal(5.7, Contrast.Ratio("#404040", "#C0C0C0"), 1);  // spec 6.6
}
```

- [ ] **Step 2: Run it to see it fail.**
- [ ] **Step 3: Implement** the model types, then the WPF files.
- [ ] **Step 4: Verify.**
  - `dotnet build OpenHisabKitab.slnx` has 0 warnings, and `dotnet test OpenHisabKitab.slnx` passes, including `TextResourceTests`.
  - Run `dotnet run --project src/OpenHisabKitab.Host` in one terminal and `dotnet run --project src/OpenHisabKitab.Client -- --profile <scratch folder>` in another.
  - The connect panel shows. After trusting and setting up, the shell shows `1 CMPY`. `xyzq` then Enter shows `Unknown function: XYZQ`.
- [ ] **Step 5: Commit** `feat: WPF shell window with Win98 theme and keyboard routing`

---

### Task 10: `HELP` and `DATE`

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Screens/HelpViewModel.cs`, `Screens/DateViewModel.cs`, `src/OpenHisabKitab.Client/Views/HelpView.xaml`, `Views/DateView.xaml`
- Modify: `Text.resx`, adding `Purpose_{CODE}` for every code with the purpose text from the spec 6.3 tables, and the name of each command id (`Cmd_prompt_focus`, and so on).
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/HelpTests.cs`, `DateTests.cs`

**Interfaces:**
- Consumes: `ScreenViewModel`, `ShellContext`, and `FunctionRouter` factories (Task 7).
- Produces:
  - `HelpViewModel.Create(ParsedCommand, ShellContext) : IScreen?`, with:
    - `Functions`: rows of code, name, category, and key;
    - `Shortcuts`: rows of key, command name, and when;
    - `Filter`;
    - `One`: the single function shown for `HELP CODE`.
  - `DateViewModel.Create(ParsedCommand, ShellContext) : IScreen?`, with an `Input` text property.
- **`HELP`:**
  - It lists every function grouped by category, with its F key or other key when bound, plus every effective keybinding. `Filter` matches codes, names, and command names. `Find()` focuses the filter.
  - `HELP TRLB` shows one function: code, name, purpose, and key.
  - `HELP XYZQ` shows the full list with `Message` set to `Unknown function: XYZQ`.
- **`DATE`:**
  - With a valid argument, it sets `ctx.WorkingDate` without opening a tab, and the status is `Working date {DateText}.` Parsing uses `DateInput.TryParse(arg, ctx.WorkingDate, out d)`.
  - With no argument or a bad one, it opens the screen. A bad one also shows `Enter a date like 15-04-2026, 15-4, or today.`
  - When a company is open and none of its years contains the date, the date is refused with `No financial year holds {DateText}. Add one in CMPY.`
  - When the date falls in another year of the open company, `ctx.Year` changes too, and the title follows.

- [ ] **Step 1: Write the failing tests**
  - `Help_lists_every_catalog_function_with_f1_on_help`
  - `Help_code_shows_one_function_with_its_purpose`
  - `Help_unknown_code_shows_the_list_and_one_line`
  - `Help_filter_matches_command_names`
  - `Date_with_valid_argument_sets_the_date_and_opens_no_tab`: `DATE 15-4` with working date 1 June 2026 gives 15 April 2026.
  - `Date_bad_argument_opens_the_screen_with_the_hint`
  - `Date_outside_every_year_is_refused`
  - `Date_in_another_year_moves_the_title`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** the two view models and views. Register `HELP` and `DATE` in the router in `App`, and in `ClientTestHost.Shell()`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: HELP and DATE`

---

### Task 11: `CMPY`

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Screens/CompanyViewModel.cs`, `src/OpenHisabKitab.Client/Views/CompanyView.xaml`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/CompanyTests.cs`

**Interfaces:**
- Consumes: `Companies.List`, `Create`, `ListYears`, and `AddYear`; `GstData.LoadEmbedded().States`; `NameMatcher.Match`; `ClientState.LastCompanyByHost`.
- Produces: `CompanyViewModel.Create(ParsedCommand, ShellContext) : IScreen?`, with:
  - `Companies`, `Selected`, `Years`, `SelectedYear`;
  - `OpenCommand` (Enter);
  - a create form: `NewName`, `NewState`, `NewRegistration`, `NewGstin`, `NewAddress`, `NewFirstYear`, and `CreateCommand` (Ctrl+S while the form has focus);
  - `AddYearCommand`.
- **Rules:**
  - **Loading** takes two calls: `List`, then `ListYears` for the selected company.
  - **Opening a company** picks the year holding `ctx.WorkingDate`, or else the latest year. It calls `ctx.OpenCompany`, saves the last company to `state.json`, reloads the company settings layer, and sets the status `Opened {name} FY {label}.`
  - **`CMPY name`** resolves the name with `NameMatcher`:
    - exact or unique prefix opens it without a tab;
    - ambiguous opens the screen filtered to the candidates;
    - none opens the screen with the create form and the name filled in (spec 6.2).
  - **Create.** The form shows only with `ManageCompanies`.
    - `NewFirstYear` defaults to the year holding the working date.
    - The state list shows `27 Maharashtra` and so on.
    - The host validates. Its message is shown in `Message`, and the form keeps its fields on any failure, including Unreachable.
    - On success, the new company is opened.
  - **Add year** shows only with `ManageCompanies`.
  - **Reloads.** `IsAffectedBy` is true for kinds `Settings` and `Restore`.
  - **`HasUnsavedEdits`** is true while the create form has a non-empty name.

- [ ] **Step 1: Write the failing tests**
  - `Open_sets_the_title_and_remembers_the_company`
  - `Cmpy_with_a_unique_prefix_opens_without_a_tab`
  - `Cmpy_with_an_unknown_name_offers_create_with_the_name`
  - `Regular_without_gstin_shows_the_host_message_and_keeps_the_form`: `Enter the GSTIN.`
  - `Unreachable_create_keeps_the_form`, using `ClientTestHost.Offline`.
  - `Viewer_sees_no_create_form`
  - `Add_year_then_working_date_in_it_opens_that_year`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement**, and register `CMPY`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: CMPY screen`

---

### Task 12: `SETG` and `KEYS`

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Screens/SettingsViewModel.cs`, `Screens/KeysViewModel.cs`, `src/OpenHisabKitab.Client/Views/SettingsView.xaml`, `Views/KeysView.xaml`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/SettingsScreenTests.cs`, `KeysScreenTests.cs`

**Interfaces:**
- Consumes: `SettingsStore` and `KeybindingStore` (Task 6); `Admin.ListHostSettings` and `SetHostSetting`; `SetHostSettingReply.RestartNeeded`.
- Produces:
  - `SettingsViewModel.Create(...)`, with:
    - `Rows`: grouped by `Category`; each row has key, description, value, from layer, modified, `Layers` (the layers this user may write), and `TargetLayer`;
    - `Filter`;
    - `SetCommand(row, json)` and `ResetCommand(row)`;
    - `JsonMode`, `JsonLayer`, `JsonText`, and `JsonErrors`;
    - `HostRows`, which are present only with `ManageHostSettings`.
  - `KeysViewModel.Create(...)`, with:
    - `Rows`: key, command, command name, when, and from;
    - `Filter`;
    - `Recording`;
    - `RecordKey(KeyChord)`;
    - `ResetCommand(row)`;
    - `TargetLayer` (Machine or User);
    - the same JSON members.
- **`SETG` rules:**
  - **Rows.** A modified row shows `*` before its key (spec 7). `SETG ui.fontSize` sets `Filter` to the key.
  - **Layers.** A row's `Layers` is its scope minus what this user cannot write: Company needs `ManageCompanies` and an open company. `TargetLayer` defaults to the highest layer in `Layers`.
  - **JSON mode.** Ctrl+S saves `JsonText` to `JsonLayer`, and `JsonErrors` lists `line {n}: {message}`. `HasUnsavedEdits` is true while the text differs from the stored layer.
  - **Host rows** use the host's own definitions (`HostSettingMsg`). When `restart_needed` is set, the status is `Saved. Restart the host service to apply.` (Plan 3 owner review item 2). Otherwise it is `Saved.`
  - **Connection settings.** A change to `connection.*` shows `Saved. Applies at the next connect.`
- **`KEYS` rules:**
  - **Recording.** Enter on a row starts `Recording`. The next chord, which the view passes in instead of the shell, becomes the row command's new key in `TargetLayer`. It is written as two entries, the removal of the old key and the new binding. Esc cancels recording.
  - **Reset.** Reset removes the target layer's entries for that command.
  - **JSON mode** works as in `SETG`, through `KeybindingStore.SaveLayerTextAsync`.
  - **`IsAffectedBy`** is false for both screens. Both reload when their store raises `Changed`.

- [ ] **Step 1: Write the failing tests**
  - `Modified_setting_is_marked`
  - `User_target_is_default_for_ui_settings`
  - `Company_layer_needs_manage_companies`: as `DataEntry`, the `invoice.roundOff` row has no writable layer and `SetCommand` is disabled.
  - `Setg_with_a_key_filters_to_it`
  - `Json_with_a_bad_key_saves_nothing_and_lists_the_line`
  - `Host_setting_change_says_restart_when_the_host_says_so`: set `api.enabled` true, and the status is `Saved. Restart the host service to apply.`
  - `Viewer_sees_no_host_rows`
  - `Recording_f6_as_run_purc_relabels_the_f_key_row`: assert on `shell.FKeys[5]`.
  - `Esc_cancels_recording`
  - `Reset_restores_the_default_key`
  - `Keys_json_with_bad_chord_saves_nothing`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement**, and register `SETG` and `KEYS`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: SETG and KEYS screens with form and JSON editing`

---

### Task 13: `USER`

**Files:**
- Create: `src/OpenHisabKitab.Client.Model/Screens/UsersViewModel.cs`, `src/OpenHisabKitab.Client/Views/UsersView.xaml`
- Test: `tests/OpenHisabKitab.Client.Model.Tests/Screens/UsersScreenTests.cs`

**Interfaces:**
- Consumes: `Session.ChangePassword`; `Tokens.Create`, `List`, and `Revoke`; `Admin.ListUsers`, `CreateUser`, and `SetUserCompanies`; `Companies.List`.
- Produces: `UsersViewModel.Create(...)`, with three sections.
  - **Password:** `ChangePasswordAsync(current, next, confirm)`.
  - **Tokens:** `Tokens`, `NewTokenName`, `NewTokenReadWrite` (default false), `CreateTokenCommand`, `CreatedToken` (a string shown once), and `RevokeCommand(row)`.
  - **Users**, present only with `ManageUsers`: `Users`, `NewUserName`, `NewUserRole` (Owner, Accountant, DataEntry, Viewer), `CreateUserAsync(password)`, `CompanyChoices` (checked rows for the selected user), and `SaveCompaniesCommand`.
- **Rules:**
  - **Load** takes three calls: `Tokens.List`, then `ListUsers` and `Companies.List` with `ManageUsers`.
  - **New passwords** that differ are refused with `The new passwords differ.` before any call.
  - **A created token** is shown once in a read only box, with `Copy this token now. It is not shown again.` The token is cleared from memory when the tab closes or the screen reloads.
  - **Revoke** asks `Revoke {name}?` through `IConfirm`.
  - **Token rows** show name, `Read only` or `Read and write`, the created date, the last used date or `Never`, and the revoked date. Dates go through `DateText` after converting the UTC timestamp to local time.
  - **Messages from the host** are shown as sent, for example `You already have a token named {name}.`

- [ ] **Step 1: Write the failing tests**
  - `Created_token_is_shown_once_and_gone_after_reload`: after `LoadAsync`, `CreatedToken` is null and no row text contains the token.
  - `Created_token_works_on_the_api`: a token created here authenticates through `ApiTokenService.Authenticate` on the test host. This proves the Plan 3 item 10 path.
  - `Revoke_asks_and_no_keeps_the_token`
  - `Duplicate_token_name_shows_the_host_message`
  - `Accountant_sees_tokens_but_not_users`
  - `Owner_creates_a_user_and_gives_one_company`
  - `Differing_new_passwords_call_nothing`
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement**, and register `USER`.
- [ ] **Step 4: Verify.** `dotnet test tests/OpenHisabKitab.Client.Model.Tests`: all pass.
- [ ] **Step 5: Commit** `feat: USER screen with password, API tokens, and users`

---

### Task 14: UI smoke tests, docs, and owner review

**Files:**
- Create: `tests/OpenHisabKitab.Client.UiTests/OpenHisabKitab.Client.UiTests.csproj`, `UiFactAttribute.cs`, `AppSession.cs`, `ShellSmokeTests.cs`
- Modify: `README.md`, `CLAUDE.md` (one line: the UI test command), `docs/owner-review.md`

**Interfaces:**
- Consumes: the built client exe and `HostApp.Build`, run on ports chosen by the OS as in `KestrelTlsTests`.
- Produces:
  - `UiFactAttribute : FactAttribute` sets `Skip = "Set OHK_UI_TESTS=1 to run UI tests."` unless the variable is `1`.
  - `AppSession` starts a real host in a temporary folder, then launches the client exe with `--profile <temp>`, and attaches with FlaUI `UIA3Automation`.

- [ ] **Step 1: Write the UI tests.** Each test drives the app with the keyboard only (spec 2, anti-goals).
  - `First_run_connects_trusts_sets_up_and_creates_a_company`:
    - type the host name and port, then Enter;
    - Alt+T (Trust);
    - enter and confirm the password, then Enter;
    - in `CMPY`, fill the form for an Unregistered company, then Ctrl+S;
    - the window title is `Open Hisab Kitab: A Traders FY {year}`.
  - `Unknown_code_shows_one_status_line`: type `xyzq` with no focused field, which tests typing anywhere, then Enter. The status bar text is `Unknown function: XYZQ`, and no dialog window exists.
  - `F1_opens_help_and_ctrl_w_closes_it`.
  - `Menu_reaches_keys_after_ctrl_l_is_removed` (Review Focus 4): write user keybindings removing `ctrl+l`, press Alt, H, K, and the `KEYS` tab opens.
  - `Every_focusable_element_has_a_name`: walk the UIA tree of the main window and each overlay.
  - `Largest_font_fits_1024_by_768` (Review Focus 5): set `ui.fontSize` 28 in the machine file and resize the window to 1024 by 768. The prompt, the tab strip, all 12 F key buttons, and the status bar each have bounding rectangles inside the window's client area and do not overlap.
- [ ] **Step 2: Run them.** `$env:OHK_UI_TESTS='1'; dotnet test tests/OpenHisabKitab.Client.UiTests` passes on the development PC. `dotnet test OpenHisabKitab.slnx` without the variable reports them as skipped.
- [ ] **Step 3: Docs.**
  - `README.md` gains a "Client" section: how to run the host and the client, the `--profile` switch, and that `SETG` and `USER` now turn on the API and create tokens.
  - `CLAUDE.md` gains: `UI tests: set OHK_UI_TESTS=1, then dotnet test tests/OpenHisabKitab.Client.UiTests.`
- [ ] **Step 4: Owner review.** Add to `docs/owner-review.md` only a major decision made while building. Record smaller ones under "Builder decisions" below.
- [ ] **Step 5: Final verification.**
  - `dotnet build OpenHisabKitab.slnx -c Release` has 0 warnings.
  - `dotnet test OpenHisabKitab.slnx -c Release --no-build` passes all tests, with the UI tests skipped.
  - The UI tests pass with the variable set.
- [ ] **Step 6: Commit** `test: keyboard-only UI smoke tests; docs and owner review for plan 4`

## Builder decisions

Only item 1 needs the owner, and it is in `docs/owner-review.md`. The rest are the builder's calls, kept here for the record.

1. **Plan 4 builds the shell and the system screens `HELP`, `DATE`, `RFSH`, `CMPY`, `SETG`, `KEYS`, and `USER`.** Other codes show `{CODE} is not in this version yet.` Masters, vouchers, and reports screens are next. `AUDT`, `LOCK`, `BKUP`, `FIND`, and `PRNT` follow with them. Cost if wrong: reorder later plans.
2. **Product version 0.1.0,** shared by client and host. Cost if wrong: one property.
3. **`CMPY` cannot edit a company profile,** because the host has no update call yet. Cost if wrong: one host method and a form.
4. **Paged list calls and client caches for parties and items** wait for the screens that use them. No Plan 4 screen lists vouchers, parties, or items. Cost if wrong: none now. Adding fields to the protos later stays compatible.
5. **User keybindings are stored as the user setting `keybindings`** in `host.db`, so they follow the user to any PC, like the other user settings. Cost if wrong: a separate table.
6. **Window placement lives in `state.json`, not in the settings list,** because no one types it. Cost if wrong: one setting.
7. **`connection.host` and `connection.port` apply at the next connect,** not at once. Cost if wrong: a live reconnect.
8. **`when` clauses support `!`, `&&`, and `||`, with no parentheses,** and only the keys `promptFocus`, `inputFocus`, `gridFocus`, and `paletteOpen`. Cost if wrong: a richer parser.
9. **`DATE` refuses a date outside the open company's financial years.** Cost if wrong: allow it and warn.
10. **The MCP wrong-type message** (deferred from Plan 3) stays deferred. The MCP SDK masks the error before the tool runs, and fixing it needs SDK work, not client work. Cost if wrong: assistants see a vague error for a number sent as a string.
11. **Ctrl+T opens no empty tab.** It focuses the prompt, and the next command opens in a new tab. Cost if wrong: an empty tab state in the shell.

## Done when

- Every task's tests pass in Release, with 0 warnings.
- With the host running, `dotnet run --project src/OpenHisabKitab.Client` connects, trusts, sets up the Owner, and creates a company. In `SETG` the Owner turns on `api.enabled` and sees the restart notice. In `USER` the Owner creates a token, and after a host restart the REST API accepts it.
- The UI smoke tests pass with `OHK_UI_TESTS=1`.
- `docs/owner-review.md` holds any new major decision.
