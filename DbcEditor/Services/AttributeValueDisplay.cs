using System.Globalization;
using DbcEditor.Models;

namespace DbcEditor.Services;

/// <summary>Vector attribute value display (HEX → 0x120, numeric equality for defaults).</summary>
public static class AttributeValueDisplay
{
    public static string Format(DbcAttrDef? def, string? raw)
    {
        var t = (raw ?? "").Trim();
        if (def != null && def.ValueType.Equals("HEX", StringComparison.OrdinalIgnoreCase))
            return FormatHex(t);
        return t;
    }

    public static string FormatHex(string? raw)
    {
        if (TryParseInteger(raw, out var v))
            return v < 0 ? $"-0x{-v:X}" : $"0x{v:X}";
        return (raw ?? "").Trim();
    }

    public static bool ValuesEqual(DbcAttrDef? def, string? a, string? b)
    {
        var left = (a ?? "").Trim().TrimEnd('*');
        var right = (b ?? "").Trim().TrimEnd('*');
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        var numeric = def != null &&
                      (def.ValueType.Equals("HEX", StringComparison.OrdinalIgnoreCase) ||
                       def.ValueType.Equals("INT", StringComparison.OrdinalIgnoreCase) ||
                       def.ValueType.Equals("FLOAT", StringComparison.OrdinalIgnoreCase));
        if (!numeric) return false;

        if (def!.ValueType.Equals("FLOAT", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
                double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
                return Math.Abs(da - db) < 1e-9;
            return false;
        }

        return TryParseInteger(left, out var ia) &&
               TryParseInteger(right, out var ib) &&
               ia == ib;
    }

    public static bool TryParseInteger(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimEnd('*');
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        if (t.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(t.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                return false;
            value = -u;
            return true;
        }
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
