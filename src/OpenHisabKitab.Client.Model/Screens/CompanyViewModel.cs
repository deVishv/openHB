using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>One line of the state list: "27 Maharashtra".</summary>
public sealed record StateOption(string Code, string Label)
{
    /// <summary>Every state and union territory in the GST data, by code.</summary>
    public static IReadOnlyList<StateOption> All { get; } =
        [.. GstData.LoadEmbedded().States.OrderBy(s => s.Code.Value, StringComparer.Ordinal).Select(s => new StateOption(s.Code.Value, $"{s.Code.Value} {s.Name}"))];
}

/// <summary>
/// CMPY: list, open, and create companies, and add financial years (spec 6.3). "CMPY name" opens a single match at once,
/// lists the candidates when several match, and offers to create one when none do (spec 6.2, Names).
/// </summary>
public sealed partial class CompanyViewModel : ScreenViewModel, IScreenCommands
{

    IReadOnlyList<string>? candidates;

    CompanyViewModel(ShellContext ctx) : base(ctx)
    {
        NewFirstYear = FinancialYear.Containing(ctx.WorkingDate).StartYear;
    }

    public static async Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx)
    {
        var screen = new CompanyViewModel(ctx);
        var name = string.Join(' ', cmd.Args);
        if (name.Length == 0) return screen;
        CompanyList list;
        try { list = await ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o)); }
        catch (HostException e)
        {
            screen.Message = e.Message;
            return screen;
        }
        var match = NameMatcher.Match(name, [.. list.Companies.Select(c => c.Name)]);
        switch (match.Kind)
        {
            case MatchKind.None:
                if (screen.CanCreate) screen.NewName = name;
                return screen;
            case MatchKind.Ambiguous:
            case not MatchKind.None when ctx.Replaying:
                screen.candidates = match.Candidates;
                return screen;
            default:
                await OpenAsync(ctx, list.Companies.First(c => c.Name == match.Candidates[0]));
                return null;
        }
    }

    public override string Code => "CMPY";

    public override bool HasUnsavedEdits => !Editing && NewName.Trim().Length > 0;

    public bool CanCreate => Ctx.Has("ManageCompanies");

    public bool CanAddYear => Ctx.Has("ManageCompanies") && Selected is not null;

    public IReadOnlyList<StateOption> States => StateOption.All;

    public IReadOnlyList<string> Registrations { get; } = [nameof(GstRegistration.Regular), nameof(GstRegistration.Composition), nameof(GstRegistration.Unregistered)];

    [ObservableProperty]
    public partial IReadOnlyList<CompanyEntry> Companies { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddYear))]
    public partial CompanyEntry? Selected { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<YearMsg> Years { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial StateOption? NewState { get; set; }

    [ObservableProperty]
    public partial string NewRegistration { get; set; } = nameof(GstRegistration.Regular);

    [ObservableProperty]
    public partial string NewGstin { get; set; } = "";

    [ObservableProperty]
    public partial string NewAddress { get; set; } = "";

    [ObservableProperty]
    public partial int NewFirstYear { get; set; }

    /// <summary>True while the form edits the selected company's profile rather than creating one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeading), nameof(CanSetLogo))]
    public partial bool Editing { get; private set; }

    int editingId;

    public string FormHeading => Editing ? Text.EditCompanyHeading : Text.NewCompanyHeading;

    public bool CanRun(string command) => command == "record.new" && Editing;

    public Task RunAsync(string command)
    {
        if (command == "record.new") StopEditing();
        return Task.CompletedTask;
    }

    /// <summary>Loads the selected company's profile into the form to edit it. The first year is shown but cannot change.</summary>
    public Task EditSelectedAsync() => Selected is { } company && CanCreate
        ? RunAsync(async () =>
        {
            var p = await Ctx.Host.CallAsync((h, o) => h.Companies.GetAsync(new CompanyRef { Company = company.Id }, o));
            editingId = company.Id;
            Editing = true;
            (NewName, NewGstin, NewAddress, NewRegistration, NewFirstYear) = (p.Name, p.Gstin, p.Address, p.Registration, p.FirstFyStartYear);
            NewState = States.FirstOrDefault(s => s.Code == p.State);
            var logo = await Ctx.Host.CallAsync((h, o) => h.Companies.GetLogoAsync(new CompanyRef { Company = company.Id }, o));
            LogoText = LogoSize(logo.Png.Length);
        })
        : Task.CompletedTask;

    /// <summary>"Logo: 12 KB" or "No logo", for the company being edited.</summary>
    [ObservableProperty]
    public partial string LogoText { get; private set; } = "";

    /// <summary>The logo is printed on invoices; it is set while editing a company, by those who manage companies.</summary>
    public bool CanSetLogo => Editing && CanCreate;

    /// <summary>Reads a PNG file and stores it as the company logo, after the checks the host and the PDF writer make.</summary>
    public Task SetLogoAsync(string path)
    {
        if (!CanSetLogo) return Task.CompletedTask;
        byte[] png;
        try
        {
            // A large file picked by mistake is refused before it is read.
            if (new FileInfo(path).Length > LogoImage.MaxBytes)
            {
                Message = LogoImage.TooLarge;
                return Task.CompletedTask;
            }
            png = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message = Format(Text.CannotReadFile, path);
            return Task.CompletedTask;
        }
        if (LogoImage.Check(png) is { } problem)
        {
            Message = problem;
            return Task.CompletedTask;
        }
        if (!PdfExport.CanDraw(png))
        {
            Message = Text.LogoUnreadable;
            return Task.CompletedTask;
        }
        return StoreLogoAsync(png, Text.LogoSaved);
    }

    public Task RemoveLogoAsync() => CanSetLogo ? StoreLogoAsync([], Text.LogoRemoved) : Task.CompletedTask;

    async Task StoreLogoAsync(byte[] png, string done)
    {
        var id = editingId;
        if (await RunAsync(() => Ctx.Host.CallAsync((h, o) => h.Companies.SetLogoAsync(new SetLogoRequest { Company = id, Png = ByteString.CopyFrom(png) }, o))))
        {
            LogoText = LogoSize(png.Length);
            Message = done;
        }
    }

    static string LogoSize(int bytes) => bytes == 0 ? Text.LogoNone : Format(Text.LogoKb, (bytes + 1023) / 1024);

    static string Format(string pattern, object arg) => string.Format(CultureInfo.InvariantCulture, pattern, arg);

    void StopEditing()
    {
        Editing = false;
        editingId = 0;
        (NewName, NewGstin, NewAddress) = ("", "", "");
        NewRegistration = nameof(GstRegistration.Regular);
        NewFirstYear = FinancialYear.Containing(Ctx.WorkingDate).StartYear;
    }

    async Task UpdateAsync(CompanyProfileMsg profile)
    {
        var id = editingId;
        await Ctx.Host.CallAsync((h, o) => h.Companies.UpdateAsync(new UpdateCompanyRequest { Company = id, Profile = profile }, o));
        // The title names the open company, and the name may have changed.
        if (Ctx.Company?.Id == id && Ctx.Year is { } year) Ctx.OpenCompany(new CompanyEntry { Id = id, Name = profile.Name }, year, Ctx.Years);
        var list = await Ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o));
        Companies = [.. list.Companies];
        Selected = Companies.FirstOrDefault(c => c.Id == id);
        StopEditing();
    }

    public override Task LoadAsync(CancellationToken ct) => RunAsync(async () =>
    {
        var list = await Ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o), ct);
        Companies = candidates is null ? [.. list.Companies] : [.. list.Companies.Where(c => candidates.Contains(c.Name))];
        Selected = Companies.FirstOrDefault(c => c.Id == Ctx.Company?.Id) ?? Companies.FirstOrDefault();
    });

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind is "Settings" or "Restore";

    /// <summary>Enter on a company: opens it in the year holding the working date, else its latest year.</summary>
    public Task OpenAsync() => Selected is { } company ? RunAsync(() => OpenAsync(Ctx, company)) : Task.CompletedTask;

    /// <summary>Ctrl+S: creates the company in the form, then opens it.</summary>
    public override Task SaveAsync()
    {
        if (!CanCreate || !Editing && !HasUnsavedEdits) return Task.CompletedTask;
        var profile = new CompanyProfileMsg
        {
            Name = NewName.Trim(), State = NewState?.Code ?? "", Registration = NewRegistration, Gstin = NewGstin.Trim(),
            Address = NewAddress.Trim(), FirstFyStartYear = NewFirstYear,
        };
        if (Editing) return RunAsync(() => UpdateAsync(profile));
        return RunAsync(async () =>
        {
            var id = (await Ctx.Host.CallAsync((h, o) => h.Companies.CreateAsync(profile, o))).Company;
            candidates = null;
            (NewName, NewGstin, NewAddress) = ("", "", "");
            await OpenAsync(Ctx, new CompanyEntry { Id = id, Name = profile.Name });
            var list = await Ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o));
            Companies = [.. list.Companies];
            Selected = Companies.FirstOrDefault(c => c.Id == id);
        });
    }

    public Task AddYearAsync() => Selected is { } company && CanAddYear
        ? RunAsync(async () =>
        {
            await Ctx.Host.CallAsync((h, o) => h.Companies.AddYearAsync(new CompanyRef { Company = company.Id }, o));
            await LoadYearsAsync(company);
        })
        : Task.CompletedTask;

    partial void OnSelectedChanged(CompanyEntry? value)
    {
        Years = [];
        if (value is not null) _ = RunAsync(() => LoadYearsAsync(value));
    }

    async Task LoadYearsAsync(CompanyEntry company)
    {
        var years = await Ctx.Host.CallAsync((h, o) => h.Companies.ListYearsAsync(new CompanyRef { Company = company.Id }, o));
        if (Selected?.Id == company.Id) Years = [.. years.Years.OrderBy(y => y.StartYear)];
    }

    static async Task OpenAsync(ShellContext ctx, CompanyEntry company)
    {
        await CompanyOpener.OpenAsync(ctx, company);
        ctx.Report(string.Format(CultureInfo.InvariantCulture, Text.OpenedCompany, company.Name, ctx.Year!.Value.Label));
    }
}
