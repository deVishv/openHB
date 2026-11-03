namespace OpenHisabKitab.Core.Gst;

/// <summary>
/// A party as a GST document names it: the buyer's or the company's details as they were when the document was saved
/// (Rule 46(a), (d), (e)), so a reprint matches the original.
/// </summary>
public sealed record DocumentParty(string Name, string? Gstin, string Address, StateCode State);

/// <summary>
/// Where goods go when that differs from the buyer's address (Rule 46(o)). Exports name the country of destination
/// (the Rule 46 export proviso) and may have no Indian state.
/// </summary>
public sealed record ShipTo(string Name, string Address, StateCode? State, string? Country);
