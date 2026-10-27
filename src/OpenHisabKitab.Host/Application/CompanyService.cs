using System.Text.Json;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>Creating and opening companies, their financial years, and company settings.</summary>
public sealed class CompanyService(HostData data, CompanyHub hub, ChangeFeed feed, SessionStore sessions, TimeProvider time)
{
    /// <summary>Creates a company file with the default chart. The creator is given access to it.</summary>
    public int Create(CallContext ctx, CompanyProfile p)
    {
        ctx.Require(Permission.ManageCompanies);
        var profile = Checked(p);
        var id = data.WriteHost(tx =>
        {
            var id = CompanyListRepository.Insert(tx, profile.Name);
            UserRepository.SetCompanies(tx, ctx.UserId, [.. UserRepository.Companies(tx.Connection!, ctx.UserId), id]);
            var after = JsonSerializer.Serialize(profile);
            try
            {
                using var c = hub.CreateFile(id);
                using var companyTx = SqliteDatabase.BeginWrite(c);
                CompanyRepository.Create(companyTx, profile, DefaultChart.Create());
                hub.Audit(companyTx, ctx.Audit(time.GetUtcNow(), "create", "company", Text.Key(id), null, after));
                companyTx.Commit();
            }
            catch
            {
                hub.DeleteFile(id);
                throw;
            }
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "create", "company", Text.Key(id), null, after));
            return id;
        });
        sessions.Update(ctx.UserId, s => s with { Companies = s.Companies.Append(id).ToHashSet() });
        return id;
    }

    /// <summary>The companies the caller can open, by name.</summary>
    public IReadOnlyList<CompanyListEntry> List(CallContext ctx)
    {
        using var c = data.OpenHost();
        return [.. CompanyListRepository.List(c).Where(e => ctx.AllCompanies || ctx.Companies.Contains(e.Id))];
    }

    public CompanyProfile Get(CallContext ctx, int id)
    {
        ctx.RequireCompany(id);
        using var c = hub.Open(id);
        return CompanyRepository.GetProfile(c);
    }

    public IReadOnlyList<FinancialYear> Years(CallContext ctx, int id)
    {
        ctx.RequireCompany(id);
        using var c = hub.Open(id);
        return CompanyRepository.Years(c);
    }

    /// <summary>Adds the financial year after the latest one.</summary>
    public FinancialYear AddYear(CallContext ctx, int id)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        return hub.Write(id, tx =>
        {
            var next = new FinancialYear(CompanyRepository.Years(tx.Connection!).Max(y => y.StartYear) + 1);
            CompanyRepository.AddYear(tx, next);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "add-year", "company", Text.Key(id), null, JsonSerializer.Serialize(next.Label)));
            return next;
        }, _ => feed.Publish(new ChangeEvent(id, ChangeKind.Settings, null)));
    }

    public void SetSetting(CallContext ctx, int id, string key, string json)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ServiceException(ServiceError.Invalid, $"Setting {key} needs a JSON value."); }
        hub.Write(id, tx =>
        {
            var before = CompanyRepository.GetSetting(tx.Connection!, key);
            CompanyRepository.SetSetting(tx, key, json);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "set", "setting", key, before, json));
            return 0;
        }, _ => feed.Publish(new ChangeEvent(id, ChangeKind.Settings, null)));
    }

    static CompanyProfile Checked(CompanyProfile p)
    {
        var name = p.Name.Trim();
        if (name.Length == 0 || name.Any(char.IsControl)) throw new ServiceException(ServiceError.Invalid, "Enter a company name.");
        if (p.Registration == GstRegistration.Regular && p.Gstin is null)
            throw new ServiceException(ServiceError.Invalid, "Enter the GSTIN.");
        if (p.Gstin is { } g && g.State != p.State)
            throw new ServiceException(ServiceError.Invalid, "GSTIN state does not match the company state.");
        return p with { Name = name };
    }
}
