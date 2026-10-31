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
    public required MachineFiles Files { get; init; }
    public required SettingsStore Settings { get; init; }
    public required KeybindingStore Keys { get; init; }
    public required TimeProvider Time { get; init; }

    /// <summary>The keyboard Yes/No box, for screens that ask before a change such as revoking a token. The shell sets it.</summary>
    public IConfirm Confirm { get; set; } = null!;

    public CompanyEntry? Company { get; private set; }

    FinancialYear? year;

    /// <summary>The open financial year. Changing it moves the title (spec 6.1).</summary>
    public FinancialYear? Year
    {
        get => year;
        set
        {
            year = value;
            CompanyChanged?.Invoke();
        }
    }

    /// <summary>The open company's financial years, oldest first.</summary>
    public IReadOnlyList<FinancialYear> Years { get; private set; } = [];

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

    public void OpenCompany(CompanyEntry company, FinancialYear year, IReadOnlyList<FinancialYear>? years = null)
    {
        Company = company;
        Years = years ?? [year];
        Year = year;
    }

    public void Report(string message) => Reported?.Invoke(message);

    /// <summary>No company open, as after logging out.</summary>
    public void CloseCompany()
    {
        Company = null;
        Years = [];
        Year = null;
    }
}
