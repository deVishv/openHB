using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>Opening a company: its year, the company settings layer, and remembering it for the next login on this PC.</summary>
public static class CompanyOpener
{
    /// <summary>
    /// Opens the year holding the working date, else the latest year. The years and the company settings come first; then the
    /// lookup cache loads. A cache failure leaves the company open and says so; the next change reloads it.
    /// </summary>
    public static async Task OpenAsync(ShellContext ctx, CompanyEntry company)
    {
        var years = await ctx.Host.CallAsync((h, o) => h.Companies.ListYearsAsync(new CompanyRef { Company = company.Id }, o));
        var all = years.Years.Select(y => new FinancialYear(y.StartYear)).OrderBy(y => y.StartYear).ToList();
        ctx.OpenCompany(company, Pick(all, ctx.WorkingDate), all);
        Remember(ctx, company.Id);
        await ctx.Settings.LoadAsync(company.Id);
        try { await ctx.Lookups.LoadAsync(company.Id); }
        catch (HostException e) { ctx.Report(e.Message); }
    }

    /// <summary>Reopens the last company used with this host, when the user can still open it.</summary>
    public static async Task<bool> TryReopenAsync(ShellContext ctx)
    {
        if (ctx.Host.Address is not { } address || !ctx.Files.LoadState().LastCompanyByHost.TryGetValue(address.ToString(), out var id)) return false;
        var list = await ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o));
        if (list.Companies.FirstOrDefault(c => c.Id == id) is not { } company) return false;
        await OpenAsync(ctx, company);
        return true;
    }

    public static FinancialYear Pick(IReadOnlyList<FinancialYear> years, DateOnly workingDate)
    {
        foreach (var y in years) if (y.Range.Contains(workingDate)) return y;
        return years.MaxBy(y => y.StartYear);
    }

    static void Remember(ShellContext ctx, int id)
    {
        var state = ctx.Files.LoadState();
        var key = ctx.Host.Address!.ToString();
        ctx.Files.SaveState(state with
        {
            LastHost = key,
            LastCompanyByHost = new Dictionary<string, int>(state.LastCompanyByHost) { [key] = id },
        });
    }
}
