using DbcEditor.Models;
using DbcEditor.Views;

namespace DbcEditor.ViewModels;

public sealed class EnvVarRowViewModel
{
    public DbcEnvVar Model { get; }

    public EnvVarRowViewModel(DbcEnvVar model) => Model = model;

    public string Name => Model.Name;
    public string Ecu => Model.AccessNodes.Count > 0
        ? string.Join(",", Model.AccessNodes)
        : "";
    public string Type => EnvVarEditorVm.MapValueType(Model);
    public string Unit => Model.Unit ?? "";
    public string Min => FormatHexOrDec(Model.Min);
    public string Max => FormatHexOrDec(Model.Max);
    public string InitialValue => FormatHexOrDec(Model.InitialValue);
    public string Length => Model.DataLength is int len ? len.ToString() : "";
    public string Access => EnvVarEditorVm.MapAccess(Model.AccessType);
    public string ValueTable => Model.ValueDescriptions.Count > 0
        ? $"VtEv_{Model.Name}"
        : "";

    /// <summary>Vector shows integer-ish env bounds as hex (0x0, 0x64, 0x14).</summary>
    private static string FormatHexOrDec(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9 && v >= 0 && v <= uint.MaxValue)
            return $"0x{(ulong)Math.Round(v):X}";
        return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
