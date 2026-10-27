namespace OpenHisabKitab.Core.Gst;

/// <summary>A 15 character GST identification number with a valid state prefix and check digit.</summary>
public readonly record struct Gstin
{
    const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    Gstin(string value)
    {
        Value = value;
        State = new StateCode(value[..2]);
    }

    public string Value { get; }

    public StateCode State { get; }

    public static bool TryParse(string? text, GstData data, out Gstin gstin)
    {
        gstin = default;
        if (text is null) return false;
        var value = text.Trim().ToUpperInvariant();
        if (value.Length != 15 || !value.All(c => Alphabet.Contains(c))) return false;
        if (!value[..2].All(char.IsAsciiDigit)) return false;
        var state = new StateCode(value[..2]);
        if (!data.States.Any(s => s.Code == state)) return false;
        if (value[14] != CheckCharacter(value)) return false;
        gstin = new Gstin(value);
        return true;
    }

    // GSTN's check digit: weights alternate 1 and 2 over base 36 values, and each product adds its quotient and remainder by 36.
    static char CheckCharacter(string value)
    {
        int factor = 1, sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var p = Alphabet.IndexOf(value[i]) * factor;
            sum += p / 36 + p % 36;
            factor = factor == 1 ? 2 : 1;
        }
        return Alphabet[(36 - sum % 36) % 36];
    }

    public override string ToString() => Value;
}
