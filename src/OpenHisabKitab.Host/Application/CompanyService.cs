using System.Text.Json;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Settings;
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
            // A file can outlive its host.db row (a hand restored host.db, or a failure between the two commits). Never reuse it.
            while (File.Exists(data.CompanyPath(id)))
            {
                CompanyListRepository.Delete(tx, id);
                id = CompanyListRepository.Insert(tx, profile.Name);
            }
            UserRepository.SetCompanies(tx, ctx.UserId, [.. UserRepository.Companies(tx.Connection!, ctx.UserId), id]);
            var after = JsonSerializer.Serialize(profile);
            var c = hub.CreateFile(id);
            try
            {
                using var companyTx = SqliteDatabase.BeginWrite(c);
                CompanyRepository.Create(companyTx, profile, DefaultChart.Create());
                hub.Audit(companyTx, ctx.Audit(time.GetUtcNow(), "create", "company", Text.Key(id), null, after));
                companyTx.Commit();
            }
            catch
            {
                c.Dispose();
                hub.DeleteFile(id);
                throw;
            }
            CompanyHub.Anchor(tx, id, c);
            c.Dispose();
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
    public Task<FinancialYear> AddYearAsync(CallContext ctx, int id)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        return hub.WriteAsync(id, tx =>
        {
            var next = new FinancialYear(CompanyRepository.Years(tx.Connection!).Max(y => y.StartYear) + 1);
            CompanyRepository.AddYear(tx, next);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "add-year", "company", Text.Key(id), null, JsonSerializer.Serialize(next.Label)));
            return next;
        }, _ => feed.Publish(new ChangeEvent(id, ChangeKind.Settings, null)));
    }

    /// <summary>
    /// Edits the company profile. Name, address, and GSTIN can always change. State and registration cannot change once any
    /// voucher exists, because editing an old invoice would recalculate its tax under the new profile. The first year never changes.
    /// </summary>
    public async Task UpdateAsync(CallContext ctx, int id, CompanyProfile p)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        var profile = Checked(p);
        var (before, after) = await hub.WriteAsync(id, tx =>
        {
            var c = tx.Connection!;
            var current = CompanyRepository.GetProfile(c);
            if (profile.FirstFyStartYear != current.FirstFyStartYear)
                throw new ServiceException(ServiceError.Invalid, "The first financial year cannot change.");
            if ((profile.State != current.State || profile.Registration != current.Registration) && VoucherRepository.Any(c))
                throw new ServiceException(ServiceError.Invalid, "State and registration cannot change after vouchers are entered.");
            CompanyRepository.UpdateProfile(tx, profile);
            var (b, a) = (JsonSerializer.Serialize(current), JsonSerializer.Serialize(profile));
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "edit", "company", Text.Key(id), b, a));
            return (b, a);
        }, _ => feed.Publish(new ChangeEvent(id, ChangeKind.Settings, null))).ConfigureAwait(false);
        // The company list in host.db holds only the name, for display; the company file is the record.
        data.WriteHost(tx =>
        {
            CompanyListRepository.Rename(tx, id, profile.Name);
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "edit", "company", Text.Key(id), before, after));
            return 0;
        });
    }

    /// <summary>The company layer of settings (spec 7): what is stored, not the defaults.</summary>
    public IReadOnlyDictionary<string, string> ListSettings(CallContext ctx, int id)
    {
        ctx.RequireCompany(id);
        using var c = hub.Open(id);
        return CompanyRepository.ListSettings(c);
    }

    /// <summary>Back to the default. Resetting a key that holds nothing changes and audits nothing.</summary>
    public Task ResetSettingAsync(CallContext ctx, int id, string key)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        if (SettingsCatalog.Find(key) is null) throw new ServiceException(ServiceError.Invalid, $"No setting named {key}.");
        return hub.WriteAsync(id, tx =>
        {
            var before = CompanyRepository.GetSetting(tx.Connection!, key);
            if (before is null) return false;
            CompanyRepository.RemoveSetting(tx, key);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "reset", "setting", key, before, null));
            return true;
        }, changed => { if (changed) feed.Publish(new ChangeEvent(id, ChangeKind.Settings, null)); });
    }

    public Task SetSettingAsync(CallContext ctx, int id, string key, string json)
    {
        ctx.Require(Permission.ManageCompanies);
        ctx.RequireCompany(id);
        if (SettingsCatalog.Validate(key, json, SettingLayer.Company) is { } problem) throw new ServiceException(ServiceError.Invalid, problem);
        return hub.WriteAsync(id, tx =>
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
