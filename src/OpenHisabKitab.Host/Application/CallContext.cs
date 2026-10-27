using OpenHisabKitab.Data.Audit;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Who is calling and from where. Every application service takes one, whichever door (gRPC, REST, MCP) the call came through.
/// AllCompanies is true for the Owner, who can open every company.
/// </summary>
public sealed record CallContext(
    int UserId, string UserName, IReadOnlySet<Permission> Permissions, IReadOnlySet<int> Companies, string Machine,
    ChangeSource Source, string? TokenName, bool ReadOnly, bool AllCompanies = false)
{
    public bool Has(Permission p) => Permissions.Contains(p) && (!ReadOnly || Roles.IsRead(p));

    public void Require(Permission p)
    {
        if (!Has(p)) throw ServiceException.Denied();
    }

    public void RequireCompany(int id)
    {
        if (!AllCompanies && !Companies.Contains(id)) throw ServiceException.Denied();
    }

    public AuditEntry Audit(DateTimeOffset at, string action, string entity, string key, string? before, string? after) =>
        new(at, UserName, Machine, Source, TokenName, action, entity, key, before, after);
}
