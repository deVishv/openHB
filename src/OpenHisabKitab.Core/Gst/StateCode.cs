namespace OpenHisabKitab.Core.Gst;

/// <summary>A two digit GST state code, such as 24 for Gujarat.</summary>
public readonly record struct StateCode
{
    public StateCode(string value)
    {
        if (value is not { Length: 2 } || !value.All(char.IsAsciiDigit))
            throw new ArgumentException($"A state code is two digits: {value}", nameof(value));
        Value = value;
    }

    public string Value { get; }
}
