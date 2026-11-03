using System.Globalization;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Api;

/// <summary>
/// Reads what a caller sent. Anything not in the exact form is refused, never guessed. Body fields are named by their JSON
/// path, the way System.Text.Json names them, such as $.lines[2].amount.
/// </summary>
public static class ApiParse
{
    static ServiceException Invalid(string message) => new(ServiceError.Invalid, message);

    public static DateOnly Date(string? s, string name) =>
        s is null ? throw Invalid($"Add {name}.")
        : DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw Invalid($"{name} must look like 2026-04-01.");

    static DateOnly? OptionalDate(string? s, string name) => string.IsNullOrEmpty(s) ? null : Date(s, name);

    public static DateRange Range(string? from, string? to)
    {
        var f = Date(from, "from");
        var t = Date(to, "to");
        return f <= t ? new DateRange(f, t) : throw Invalid("from must be on or before to.");
    }

    public static VoucherType? Type(string? s) => s is null ? null : Choice<VoucherType>(s, "type");

    /// <summary>An enum value by its camelCase name, as the API writes it.</summary>
    public static T Choice<T>(string? s, string name) where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>())
            if (Camel(v.ToString()) == s) return v;
        throw Invalid($"{name} must be one of {string.Join(", ", Enum.GetValues<T>().Select(v => Camel(v.ToString())))}.");
    }

    public static Money Money(string? s, string name) =>
        WireAmount.TryParseMoney(s, out var m) ? m : throw Invalid($"{name} must be a string like \"12345.60\".");

    public static Quantity Quantity(string? s, string name) =>
        WireAmount.TryParseQuantity(s, out var q) ? q : throw Invalid($"{name} must be a string like \"2.500\".");

    public static GstRate Rate(string? s, string name) =>
        WireAmount.TryParseRate(s, out var r) ? r : throw Invalid($"{name} must be a string like \"18.00\".");

    /// <summary>The items whose name the search finds with the same matcher the command prompt uses; all of them when there is no search.</summary>
    public static IEnumerable<T> Search<T>(IReadOnlyList<T> items, Func<T, string> name, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return items;
        var found = NameMatcher.Match(search, [.. items.Select(name)]).Candidates.ToHashSet(StringComparer.Ordinal);
        return items.Where(i => found.Contains(name(i)));
    }

    public static JournalInput Journal(JournalRequest r, string root = "$") =>
        new(Choice<VoucherType>(r.Type, $"{root}.type"), Date(r.Date, $"{root}.date"), r.Narration ?? "",
            [.. (r.Lines ?? []).Select((l, i) => new PostingLine(new LedgerId(l.LedgerId), Money(l.Amount, $"{root}.lines[{i}].amount")))],
            Allocations(r.Allocations, root), Text(r.RequestId));

    // Null when the field is left out, so an edit keeps the voucher's stored allocations.
    static IReadOnlyList<AllocationRow>? Allocations(IReadOnlyList<AllocationRequest>? list, string root) =>
        list is null ? null : [.. list.Select((a, i) => new AllocationRow(new LedgerId(a.PartyId),
            new BillRef(Choice<BillSource>(a.BillSource, $"{root}.allocations[{i}].billSource"), a.BillId),
            Money(a.Amount, $"{root}.allocations[{i}].amount")))];

    public static IReadOnlyList<OpeningBillRow> OpeningBills(OpeningBillsRequest r) =>
        [.. (r.Bills ?? []).Select((b, i) => new OpeningBillRow(b.Id ?? 0, default, b.Number ?? "", Date(b.Date, $"$.bills[{i}].date"),
            OptionalDate(b.DueDate, $"$.bills[{i}].dueDate"), Money(b.Amount, $"$.bills[{i}].amount")))];

    /// <summary>The profile to save; the first financial year comes from the stored one, as it cannot change.</summary>
    public static CompanyProfile Profile(CompanyUpdateRequest r, int firstYear) =>
        new(r.Name ?? "", Wire.State(r.State ?? ""), Choice<GstRegistration>(r.Registration, "$.registration"), Wire.OptionalGstin(r.Gstin ?? ""),
            r.Address ?? "", firstYear);

    public static InvoiceInput Invoice(InvoiceRequest r, string root = "$") =>
        new(Choice<VoucherType>(r.Type, $"{root}.type"), Date(r.Date, $"{root}.date"), r.Narration ?? "", new LedgerId(r.PartyId),
            Choice<TradeDirection>(r.Direction, $"{root}.direction"), Choice<InvoiceKind>(r.Kind, $"{root}.kind"),
            Wire.State(r.PlaceOfSupply ?? ""), r.ReverseCharge, Text(r.PartyDocNumber), OptionalDate(r.PartyDocDate, $"{root}.partyDocDate"),
            Text(r.OriginalNumber), OptionalDate(r.OriginalDate, $"{root}.originalDate"),
            [.. (r.Lines ?? []).Select((l, i) =>
            {
                var at = $"{root}.lines[{i}]";
                return new InvoiceEntryLine(l.ItemId, new LedgerId(l.LedgerId), l.Description ?? "", l.HsnSac ?? "",
                    Quantity(l.Quantity, $"{at}.quantity"), Money(l.UnitPrice, $"{at}.unitPrice"), Money(l.Discount, $"{at}.discount"),
                    Rate(l.Rate, $"{at}.rate"), Rate(l.CessRate, $"{at}.cessRate"), l.PriceIncludesTax);
            })],
            new ExportDetails(Text(r.PortCode), Text(r.ShippingBillNumber), OptionalDate(r.ShippingBillDate, $"{root}.shippingBillDate")),
            Allocations(r.Allocations, root), Text(r.RequestId),
            r.ShipTo is { } t ? new ShipTo(t.Name ?? "", t.Address ?? "", Text(t.State) is { } st ? Wire.State(st) : null, Text(t.Country)) : null);

    public static Party Party(PartyRequest r, LedgerId ledger, long rowVersion) =>
        new(ledger, Wire.OptionalGstin(r.Gstin ?? ""), Wire.State(r.State ?? ""), r.CreditDays, r.Address ?? "", rowVersion);

    public static Item Item(ItemRequest r, int id, long rowVersion, string root = "$") =>
        new(id, r.Name ?? "", r.HsnSac ?? "", r.IsService, r.Unit ?? "", Rate(r.Rate, $"{root}.rate"), Rate(r.CessRate, $"{root}.cessRate"),
            Money(r.DefaultPrice, $"{root}.defaultPrice"), rowVersion);

    public static long RowVersion(long? rv) => rv ?? throw Invalid("Add rowVersion, from the record you read.");

    static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
