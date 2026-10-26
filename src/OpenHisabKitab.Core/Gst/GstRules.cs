namespace OpenHisabKitab.Core.Gst;

public enum TaxSplit { CgstSgst, CgstUtgst, Igst }

public static class GstRules
{
    /// <summary>
    /// Inter state, SEZ, and export supplies take IGST. Intra state supplies take CGST with SGST,
    /// or CGST with UTGST in a union territory without a legislature.
    /// </summary>
    public static TaxSplit Split(StateCode supplier, StateCode placeOfSupply, bool sezOrExport, GstData data)
    {
        // Both lookups throw for an unknown code, so a typo never silently picks a split.
        _ = data.State(supplier);
        var pos = data.State(placeOfSupply);
        if (sezOrExport || supplier != placeOfSupply) return TaxSplit.Igst;
        return pos.Type == StateType.UnionTerritory ? TaxSplit.CgstUtgst : TaxSplit.CgstSgst;
    }
}
