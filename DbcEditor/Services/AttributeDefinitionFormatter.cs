using DbcEditor.Models;

namespace DbcEditor.Services;

/// <summary>
/// Vector Attributes footer: "Hex [ 0x100 ... 0x140 ] - 0x120*" / "Enumeration - Cyclic*" …
/// </summary>
public static class AttributeDefinitionFormatter
{
    /// <param name="objectType">SG_ / BO_ / BU_ / EV_ / empty for network</param>
    public static string Format(DbcDatabase db, string objectType, string name, string displayValue)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var def = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.ObjectType, objectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        def ??= db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        var shown = (displayValue ?? "").Trim().TrimEnd('*');
        if (def != null)
            shown = AttributeValueDisplay.Format(def, shown);

        // Vector Definition line always suffixes '*' on the value
        if (!string.IsNullOrEmpty(shown) && !shown.EndsWith('*'))
            shown += "*";

        if (def == null)
            return shown;

        var typeLabel = def.ValueType.ToUpperInvariant() switch
        {
            "INT" => "Integer",
            "HEX" => "Hex",
            "FLOAT" => "Float",
            "STRING" => "String",
            "ENUM" => "Enumeration",
            _ => string.IsNullOrEmpty(def.ValueType) ? "Attribute" : def.ValueType
        };

        if (def.ValueType.Equals("ENUM", StringComparison.OrdinalIgnoreCase) ||
            def.ValueType.Equals("STRING", StringComparison.OrdinalIgnoreCase))
        {
            var tail = string.IsNullOrEmpty(shown)
                ? AttributeValueDisplay.Format(def, def.DefaultValue) +
                  (string.IsNullOrEmpty(def.DefaultValue) ? "" : "*")
                : shown;
            if (!string.IsNullOrEmpty(tail) && !tail.EndsWith('*') && !string.IsNullOrEmpty(def.DefaultValue))
                tail += "*";
            return string.IsNullOrEmpty(tail) ? typeLabel : $"{typeLabel} - {tail}";
        }

        var isHex = def.ValueType.Equals("HEX", StringComparison.OrdinalIgnoreCase);
        var minRaw = string.IsNullOrEmpty(def.Minimum) ? "0" : def.Minimum;
        var maxRaw = string.IsNullOrEmpty(def.Maximum) ? "0" : def.Maximum;
        var min = isHex ? AttributeValueDisplay.FormatHex(minRaw) : minRaw;
        var max = isHex ? AttributeValueDisplay.FormatHex(maxRaw) : maxRaw;

        var numTail = string.IsNullOrEmpty(shown)
            ? (string.IsNullOrEmpty(def.DefaultValue)
                ? ""
                : AttributeValueDisplay.Format(def, def.DefaultValue) + "*")
            : shown;
        return string.IsNullOrEmpty(numTail)
            ? $"{typeLabel} [{min} ... {max}]"
            : $"{typeLabel} [{min} ... {max}] - {numTail}";
    }
}

/// <summary>Common contract for Attributes list / tree leaf rows.</summary>
public interface IAttrListItem
{
    string Name { get; }
    string Value { get; }
}
