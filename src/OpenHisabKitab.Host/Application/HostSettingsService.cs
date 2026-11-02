using System.Text.Json;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>The Owner reads and changes the host settings of spec 7. Every change is audited.</summary>
public sealed class HostSettingsService(HostData data, OpenHisabKitabOptions options, TimeProvider time)
{
    public IReadOnlyList<(HostSettingDefinition Definition, string Json)> List(CallContext ctx)
    {
        ctx.Require(Permission.ManageHostSettings);
        using var c = data.OpenHost();
        return [.. HostSettings.All.Select(d => (d, HostSettingsRepository.Get(c, d.Key) ?? d.DefaultJson))];
    }

    /// <summary>Stores the value in its plain JSON form. True when it applies only after the host service restarts.</summary>
    public bool Set(CallContext ctx, string key, string json)
    {
        ctx.Require(Permission.ManageHostSettings);
        var definition = HostSettings.All.FirstOrDefault(d => d.Key == key)
            ?? throw new ServiceException(ServiceError.Invalid, $"No host setting named {key}.");
        var value = Normalise(definition, json);
        return data.WriteHost(tx =>
        {
            var before = HostSettingsRepository.Get(tx.Connection!, key) ?? definition.DefaultJson;
            if (before == value) return false;
            HostSettingsRepository.Set(tx, key, value);
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "set", "host-setting", key, before, value));
            return key switch
            {
                HostSettings.ApiPort => true,
                HostSettings.ApiEnabled or HostSettings.ApiAllowLan => value == "true",
                _ => false,
            };
        });
    }

    string Normalise(HostSettingDefinition d, string json)
    {
        var invalid = new ServiceException(ServiceError.Invalid, $"{d.Key} must be {d.AllowedValues}.");
        JsonElement e;
        try { e = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { throw invalid; }
        switch (d.Type)
        {
            case "bool":
                return e.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", _ => throw invalid };
            case "int":
                if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var n) || n < d.Min || n > d.Max) throw invalid;
                if (d.Key == HostSettings.ApiPort && n == options.GrpcPort)
                    throw new ServiceException(ServiceError.Invalid, $"{d.Key} must differ from the gRPC port {options.GrpcPort}.");
                return JsonSerializer.Serialize(n);
            case "string" when d.Key == HostSettings.BackupFolder:
                return Folder(e);
            default:
                if (e.ValueKind != JsonValueKind.Array || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) throw invalid;
                var origins = e.EnumerateArray().Select(x => x.GetString()!).ToList();
                if (!origins.All(IsOrigin))
                    throw new ServiceException(ServiceError.Invalid, $"{d.Key} entries must look like https://example.com.");
                return JsonSerializer.Serialize(origins);
        }
    }

    /// <summary>
    /// Blank, or a full path on a fixed or removable drive of this PC that exists or can be made now, so backups never
    /// fail later for want of it. Network and device paths are refused before anything touches them: opening one would
    /// send the host's credentials to another machine. A folder the host makes gets the data folder's access rules.
    /// </summary>
    static string Folder(JsonElement e)
    {
        var bad = new ServiceException(ServiceError.Invalid, $"{HostSettings.BackupFolder} must be a full folder path.");
        var notLocal = new ServiceException(ServiceError.Invalid, $"{HostSettings.BackupFolder} must be a folder on a drive of this PC.");
        if (e.ValueKind != JsonValueKind.String) throw bad;
        var path = e.GetString()!.Trim();
        if (path.Length == 0) return JsonSerializer.Serialize("");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) throw notLocal;
        if (!Path.IsPathFullyQualified(path)) throw bad;
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':') throw notLocal;
        try
        {
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable)) throw notLocal;
            var made = !Directory.Exists(full);
            Directory.CreateDirectory(full);
            if (made) DataFolderSecurity.Restrict(full);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or InvalidOperationException)
        {
            throw new ServiceException(ServiceError.Invalid, $"Cannot use folder {full}.");
        }
        return JsonSerializer.Serialize(full);
    }

    static bool IsOrigin(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.AbsolutePath == "/"
        && u.Query.Length == 0 && u.Fragment.Length == 0 && u.UserInfo.Length == 0;
}
