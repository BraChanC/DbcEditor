using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.ViewModels;

public partial class SignalRowViewModel : ObservableObject
{
    public DbcSignal Model { get; }
    private readonly string? _displayName;

    /// <summary>
    /// Vector: Attributes from Signals catalog = true; from Messages/message context = false (empty).
    /// </summary>
    public bool CatalogAttributes { get; }

    [ObservableProperty] private bool _hasOverlapWarning;
    [ObservableProperty] private string _overlapTooltip = "";

    public SignalRowViewModel(DbcSignal model, string? displayName = null, bool catalogAttributes = false,
        bool hasOverlapWarning = false)
    {
        Model = model;
        _displayName = displayName;
        CatalogAttributes = catalogAttributes;
        HasOverlapWarning = hasOverlapWarning;
        OverlapTooltip = hasOverlapWarning ? DbcBitLayout.OverlapTooltip(model) : "";
    }

    public string Name => _displayName ?? Model.Name;
    public string Message => Model.MessageName;
    public string Multiplexing =>
        string.IsNullOrWhiteSpace(Model.Multiplexing) ? "-" : Model.Multiplexing;
    public int Startbit => DbcBitLayout.VectorStartBit(Model);
    public int Length => Model.Length;
    public string ByteOrder => Model.IsLittleEndian ? "Intel" : "Motorola";
    public string ValueType => Model.IsSigned ? "Signed" : "Unsigned";
    public string InitialValue =>
        string.IsNullOrWhiteSpace(Model.InitialValue) ? "0" : Model.InitialValue;
    public string Factor => Model.Factor.ToString(CultureInfo.InvariantCulture);
    public string Offset => Model.Offset.ToString(CultureInfo.InvariantCulture);
    public string Unit => Model.Unit;
    public string Comment => Model.Comment;
}
