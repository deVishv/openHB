namespace OpenHisabKitab.Data.Audit;

/// <summary>Where a change came from: the desktop app, the local REST API, the MCP server, or the host itself.</summary>
public enum ChangeSource { App, Rest, Mcp, System }

/// <summary>One audited change. Before and After are JSON text, or null when there is no such state.</summary>
public sealed record AuditEntry(
    DateTimeOffset At, string User, string Machine, ChangeSource Source, string? TokenName,
    string Action, string Entity, string EntityKey, string? Before, string? After);

public sealed record StoredAuditEntry(long Seq, AuditEntry Entry, string PrevHash, string Hash);

public sealed record AuditVerification(long Count, long? FirstBadSeq)
{
    public bool Ok => FirstBadSeq is null;
}

public sealed record AuditFilter(
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? User = null, string? Entity = null, int Limit = 500);
