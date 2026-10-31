using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// The user layer of settings (spec 7), kept in host.db so a user's settings and keybindings follow them to any PC.
/// A user reads and changes only their own. Every change is audited.
/// </summary>
public sealed class UserSettingsService(HostData data, TimeProvider time)
{
    public const string KeybindingsKey = "keybindings";

    public IReadOnlyDictionary<string, string> List(CallContext ctx)
    {
        RequireApp(ctx);
        using var c = data.OpenHost();
        return UserSettingRepository.List(c, ctx.UserId);
    }

    public void Set(CallContext ctx, string key, string json)
    {
        RequireApp(ctx);
        if (Problem(key, json) is { } problem) throw new ServiceException(ServiceError.Invalid, problem);
        data.WriteHost(tx =>
        {
            var before = UserSettingRepository.Get(tx.Connection!, ctx.UserId, key);
            if (before == json) return 0;
            UserSettingRepository.Set(tx, ctx.UserId, key, json);
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "set", "user-setting", AuditKey(ctx, key), before, json));
            return 0;
        });
    }

    /// <summary>Back to the layers below. Resetting a key that holds nothing changes and audits nothing.</summary>
    public void Reset(CallContext ctx, string key)
    {
        RequireApp(ctx);
        if (key != KeybindingsKey && SettingsCatalog.Find(key) is null) throw new ServiceException(ServiceError.Invalid, $"No setting named {key}.");
        data.WriteHost(tx =>
        {
            var before = UserSettingRepository.Get(tx.Connection!, ctx.UserId, key);
            if (before is null) return 0;
            UserSettingRepository.Remove(tx, ctx.UserId, key);
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "reset", "user-setting", AuditKey(ctx, key), before, null));
            return 0;
        });
    }

    static string? Problem(string key, string json)
    {
        if (key != KeybindingsKey) return SettingsCatalog.Validate(key, json, SettingLayer.User);
        var load = KeybindingFile.Parse(json);
        return load.Errors.Count == 0 ? null : $"keybindings.json line {load.Errors[0].Line}: {load.Errors[0].Message}";
    }

    static string AuditKey(CallContext ctx, string key) => $"{ctx.UserId}:{key}";

    // No REST or MCP route reaches these; the check keeps it so if one is added.
    static void RequireApp(CallContext ctx)
    {
        if (ctx.Source != ChangeSource.App) throw new ServiceException(ServiceError.PermissionDenied, "Change settings in Open Hisab Kitab.");
    }
}
