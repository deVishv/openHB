using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

public enum Permission
{
    ManageUsers,
    ManageCompanies,
    EditMasters,
    /// <summary>Opening balances and the required ledgers (cash, tax, round off, and so on). Not given to Data entry.</summary>
    EditOpeningsAndRequiredLedgers,
    CreateVoucher,
    EditVoucher,
    CancelVoucher,
    ViewAllVouchers,
    ViewReports,
    ViewGstReports,
    Export,
    LockPeriod,
    UnlockPeriod,
    Backup,
    Restore,
    ViewAudit,
    ManageHostSettings,
}

/// <summary>The built in roles of spec section 8.</summary>
public static class Roles
{
    static readonly IReadOnlySet<Permission> All = Enum.GetValues<Permission>().ToHashSet();

    static readonly IReadOnlySet<Permission> Accountant = All
        .Except([Permission.ManageUsers, Permission.UnlockPeriod, Permission.Restore, Permission.ManageHostSettings])
        .ToHashSet();

    static readonly IReadOnlySet<Permission> DataEntry =
        new HashSet<Permission> { Permission.EditMasters, Permission.CreateVoucher, Permission.EditVoucher };

    static readonly IReadOnlySet<Permission> Viewer =
        new HashSet<Permission> { Permission.ViewAllVouchers, Permission.ViewReports, Permission.ViewGstReports };

    public static IReadOnlySet<Permission> Of(BuiltInRole r) => r switch
    {
        BuiltInRole.Owner => All,
        BuiltInRole.Accountant => Accountant,
        BuiltInRole.DataEntry => DataEntry,
        BuiltInRole.Viewer => Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>Permissions a read only token keeps.</summary>
    public static bool IsRead(Permission p) =>
        p is Permission.ViewAllVouchers or Permission.ViewReports or Permission.ViewGstReports or Permission.ViewAudit or Permission.Export;
}
