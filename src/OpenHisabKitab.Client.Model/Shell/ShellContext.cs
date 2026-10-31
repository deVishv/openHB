using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>What every screen shares: the host, the stores, the session, the open company and year, and the working date.</summary>
public sealed class ShellContext
{
    DateOnly workingDate;

    public required HostConnection Host { get; init; }
    public required SettingsStore Settings { get; init; }
    public required KeybindingStore Keys { get; init; }
    public required TimeProvider Time { get; init; }

    public CompanyEntry? Company { get; private set; }

    public FinancialYear? Year { get; set; }

    /// <summary>The default date for new vouchers and reports (spec 6.3, DATE).</summary>
    public DateOnly WorkingDate
    {
        get => workingDate;
        set
        {
            workingDate = value;
            WorkingDateChanged?.Invoke();
        }
    }

    public event Action? CompanyChanged;

    public event Action? WorkingDateChanged;

    /// <summary>A one line message for the status bar, from a function that acted without opening a tab.</summary>
    public event Action<string>? Reported;

    public bool Has(string permission) => Host.Session?.Permissions.Contains(permission) ?? false;

    public void OpenCompany(CompanyEntry company, FinancialYear year)
    {
        Company = company;
        Year = year;
        CompanyChanged?.Invoke();
    }

    public void Report(string message) => Reported?.Invoke(message);
}
