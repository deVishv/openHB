namespace OpenHisabKitab.Data.Audit;

/// <summary>Where a change came from: the desktop app, the local REST API, the MCP server, or the host itself.</summary>
public enum ChangeSource { App, Rest, Mcp, System }

/// <summary>One audited change. Before and After are JSON text, or null when there is no such state.</summary>
public sealed record AuditEntry(
    DateTimeOffset At, string User, string Machine, ChangeSource Source, string? TokenName,
    string Action, string Entity, string EntityKey, string? Before, string? After);

public sealed record StoredAuditEntry(long Seq, AuditEntry Entry, string PrevHash, string Hash);

/// <summary>The newest record of an audit trail: its number and hash.</summary>
public sealed record AuditHead(long Seq, string Hash);

/// <summary>
/// How a trail compares with its anchor, a copy of its newest record kept outside its file. NotUsed means only the
/// chain was checked. Missing means there is no anchor; Mismatch means the anchored record is gone or differs, so the
/// newest records were removed or the chain was rebuilt.
/// </summary>
public enum AnchorCheck { NotUsed, Matches, Missing, Mismatch }

public sealed record AuditVerification(long Count, long? FirstBadSeq, AnchorCheck Anchor = AnchorCheck.NotUsed)
{
    public bool Ok => FirstBadSeq is null && Anchor is AnchorCheck.NotUsed or AnchorCheck.Matches;
}

public sealed record AuditFilter(
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? User = null, string? Entity = null, int Limit = 500);
