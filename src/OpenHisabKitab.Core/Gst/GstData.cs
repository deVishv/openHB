using System.Text.Json;
using System.Text.Json.Serialization;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Gst;

public enum StateType { State, UnionTerritoryWithLegislature, UnionTerritory, Other }

public sealed record StateInfo(StateCode Code, string Name, StateType Type);

/// <summary>GST reference data: states, valid rates, and dated thresholds. Loaded from a versioned JSON file.</summary>
public sealed class GstData
{
    const string B2clKey = "b2clInvoiceValue";
    const string UnregisteredDetailsKey = "unregisteredDetailsValue";

    readonly Dictionary<StateCode, StateInfo> states;
    readonly IReadOnlyList<RateDto> rates;
    readonly IReadOnlyList<ThresholdDto> thresholds;

    GstData(string version, IReadOnlyList<StateInfo> stateList, IReadOnlyList<RateDto> rates, IReadOnlyList<ThresholdDto> thresholds)
    {
        Version = version;
        States = stateList;
        states = stateList.ToDictionary(s => s.Code);
        this.rates = rates;
        this.thresholds = thresholds;
    }

    public string Version { get; }

    public IReadOnlyList<StateInfo> States { get; }

    public static GstData LoadEmbedded()
    {
        using var stream = typeof(GstData).Assembly.GetManifestResourceStream("OpenHisabKitab.Core.Gst.gst-data.json")
            ?? throw new InvalidOperationException("Embedded GST data is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static GstData Parse(string json)
    {
        FileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new FormatException("GST data is empty.");
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new FormatException($"GST data is invalid: {e.Message}", e);
        }

        if (dto.Version is null || dto.States is null || dto.Rates is null || dto.Thresholds is null)
            throw new FormatException("GST data is missing a required section.");

        List<StateInfo> stateList;
        try
        {
            stateList = dto.States.Select(s => new StateInfo(new StateCode(s.Code), s.Name ?? "", s.Type)).ToList();
        }
        catch (ArgumentException e)
        {
            throw new FormatException($"GST data is invalid: {e.Message}", e);
        }
        var duplicate = stateList.GroupBy(s => s.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new FormatException($"State code {duplicate.Key.Value} appears twice.");

        foreach (var r in dto.Rates) CheckRange(r.From, r.To, $"rate {r.BasisPoints}");
        foreach (var t in dto.Thresholds) CheckRange(t.From, t.To, $"threshold {t.Key}");
        foreach (var group in dto.Thresholds.GroupBy(t => t.Key))
        {
            var ordered = group.OrderBy(t => t.From).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i - 1].To is not { } end || end >= ordered[i].From)
                    throw new FormatException($"Threshold {group.Key} has overlapping dates.");
            }
        }

        return new GstData(dto.Version, stateList, dto.Rates, dto.Thresholds);
    }

    public StateInfo State(StateCode code) =>
        states.TryGetValue(code, out var s) ? s : throw new KeyNotFoundException($"Unknown state code {code.Value}.");

    public bool IsValidRate(GstRate rate, DateOnly on) =>
        rates.Any(r => r.BasisPoints == rate.BasisPoints && r.From <= on && (r.To is null || on <= r.To));

    /// <summary>The valid rates on a date, ascending.</summary>
    public IReadOnlyList<GstRate> RatesOn(DateOnly on) =>
        [.. rates.Where(r => r.From <= on && (r.To is null || on <= r.To)).Select(r => r.BasisPoints).Distinct().Order().Select(bp => new GstRate(bp))];

    /// <summary>Invoice value above which an inter state B2C invoice is reported individually (B2CL).</summary>
    public Money B2clThreshold(DateOnly on) => Threshold(B2clKey, on, "B2CL");

    /// <summary>Taxable value from which an unregistered buyer's name, address, and state must be on the invoice (Rule 46(e)).</summary>
    public Money UnregisteredDetailsThreshold(DateOnly on) => Threshold(UnregisteredDetailsKey, on, "unregistered buyer details");

    Money Threshold(string key, DateOnly on, string what)
    {
        var t = thresholds.FirstOrDefault(t => t.Key == key && t.From <= on && (t.To is null || on <= t.To))
            ?? throw new KeyNotFoundException($"No {what} threshold for {on:dd-MM-yyyy}.");
        return new Money(t.Paise);
    }

    static void CheckRange(DateOnly from, DateOnly? to, string what)
    {
        if (to is { } end && end < from) throw new FormatException($"GST data {what} ends before it starts.");
    }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    sealed record FileDto(string? Version, List<StateDto>? States, List<RateDto>? Rates, List<ThresholdDto>? Thresholds);

    sealed record StateDto(string Code, string Name, StateType Type);

    sealed record RateDto(int BasisPoints, DateOnly From, DateOnly? To);

    sealed record ThresholdDto(string Key, long Paise, DateOnly From, DateOnly? To);
}
