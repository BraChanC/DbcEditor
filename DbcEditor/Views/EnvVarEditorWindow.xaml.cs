using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Views;

public partial class EnvVarEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly DbcEnvVar _src;
    public EnvVarEditorVm Vm { get; }

    public EnvVarEditorWindow(DbcDatabase db, DbcEnvVar ev)
    {
        InitializeComponent();
        _db = db;
        _src = ev;
        Vm = new EnvVarEditorVm(db, ev);
        DataContext = Vm;
        Title = $"Environment Variable '{ev.Name}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_src);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Vm.ApplyTo(_src);

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OnAddControlUnit(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.ControlUnits.ToList();
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Nodes, excludeNodeNames: exclude)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || dlg.SelectedNodes.Count == 0) return;
        Vm.AddControlUnits(dlg.SelectedNodes);
    }

    private void OnRemoveControlUnit(object sender, RoutedEventArgs e) => Vm.RemoveSelectedControlUnit();

    private void OnViewControlUnit(object sender, RoutedEventArgs e)
    {
        var name = Vm.SelectedControlUnit;
        if (string.IsNullOrWhiteSpace(name)) return;
        // Vector: Control units View… → ECUs / Node (ECU 'name'), not Network nodes
        new EcuEditorWindow(_db, name) { Owner = this }.ShowDialog();
    }

    private void OnAddValueDesc(object sender, RoutedEventArgs e) => Vm.AddValueDesc();
    private void OnRemoveValueDesc(object sender, RoutedEventArgs e) => Vm.RemoveSelectedValueDesc();
}

public sealed class EnvVarEditorVm : INotifyPropertyChanged
{
    private readonly DbcDatabase _db;
    private readonly DbcEnvVar _src;
    private string? _selectedControlUnit;
    private ValueDescRow? _selectedValueDesc;
    private string _valueType = "Integer";
    private string _access = "Unrestricted";
    private readonly bool _isDataFromFile;

    public DbcDatabase Database => _db;
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string ValueTable { get; set; } = "<none>";
    public string Comment { get; set; } = "";
    public string InitialValueText { get; set; } = "0x0";
    public string MinimumText { get; set; } = "0x0";
    public string MaximumText { get; set; } = "0x0";
    public string LengthText { get; set; } = "0";
    public uint EvId { get; set; }

    public List<string> ValueTypes { get; } = new() { "Integer", "Float", "String", "Data" };
    public List<string> AccessModes { get; } = new()
    {
        "Unrestricted", "Read", "Write", "ReadWrite"
    };
    public ObservableCollection<string> ValueTableOptions { get; } = new();
    public ObservableCollection<string> ControlUnits { get; } = new();
    public ObservableCollection<ValueDescRow> ValueDescriptions { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();

    public string ValueType
    {
        get => _valueType;
        set
        {
            if (_valueType == value) return;
            _valueType = value ?? "Integer";
            OnPropertyChanged();
            NotifyLayout();
        }
    }

    public string Access
    {
        get => _access;
        set
        {
            if (_access == value) return;
            _access = value ?? "Unrestricted";
            OnPropertyChanged();
        }
    }

    /// <summary>Integer / Float → Initial / Min / Max.</summary>
    public bool ShowNumericRange =>
        ValueType is "Integer" or "Float";

    /// <summary>Data → Length [Byte].</summary>
    public bool ShowDataLength => ValueType == "Data";

    /// <summary>Vector: Value Table only editable for Integer.</summary>
    public bool IsValueTableEnabled => ValueType == "Integer";

    /// <summary>Vector locks Value Type for ENVVAR_DATA_ vars.</summary>
    public bool IsValueTypeEnabled => !_isDataFromFile;

    /// <summary>Vector locks Access for Data type.</summary>
    public bool IsAccessEnabled => ValueType != "Data";

    public string? SelectedControlUnit
    {
        get => _selectedControlUnit;
        set
        {
            _selectedControlUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedControlUnit));
        }
    }

    public bool HasSelectedControlUnit => !string.IsNullOrEmpty(SelectedControlUnit);

    public ValueDescRow? SelectedValueDesc
    {
        get => _selectedValueDesc;
        set
        {
            _selectedValueDesc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedValueDesc));
        }
    }

    public bool HasSelectedValueDesc => SelectedValueDesc != null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private void NotifyLayout()
    {
        OnPropertyChanged(nameof(ShowNumericRange));
        OnPropertyChanged(nameof(ShowDataLength));
        OnPropertyChanged(nameof(IsValueTableEnabled));
        OnPropertyChanged(nameof(IsAccessEnabled));
    }

    public EnvVarEditorVm(DbcDatabase db, DbcEnvVar ev)
    {
        _db = db;
        _src = ev;
        _isDataFromFile = ev.DataLength.HasValue;
        Name = ev.Name;
        Unit = ev.Unit ?? "";
        Comment = ev.Comment ?? "";
        EvId = ev.EvId;
        _valueType = MapValueType(ev);
        _access = MapAccess(ev.AccessType);
        LengthText = (ev.DataLength ?? 0).ToString(CultureInfo.InvariantCulture);

        if (_valueType == "Float")
        {
            InitialValueText = FormatDec(ev.InitialValue);
            MinimumText = FormatDec(ev.Min);
            MaximumText = FormatDec(ev.Max);
        }
        else
        {
            InitialValueText = FormatHex(ev.InitialValue);
            MinimumText = FormatHex(ev.Min);
            MaximumText = FormatHex(ev.Max);
        }

        foreach (var n in ev.AccessNodes)
            ControlUnits.Add(n);
        foreach (var v in ev.ValueDescriptions.OrderBy(x => x.Value))
            ValueDescriptions.Add(ValueDescRow.From(v));

        BuildValueTableOptions(ev);
        BuildAttributes(db, ev);
    }

    /// <summary>
    /// Vector Value Table combo: &lt;none&gt; + VAL_TABLE_ names + all VtEv_* / VtSig_* synthetics.
    /// </summary>
    private void BuildValueTableOptions(DbcEnvVar ev)
    {
        ValueTableOptions.Clear();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in _db.ValueTables)
            if (!string.IsNullOrWhiteSpace(t.Name))
                names.Add(t.Name);

        foreach (var e in _db.EnvVars)
            if (e.ValueDescriptions.Count > 0)
                names.Add($"VtEv_{e.Name}");

        foreach (var s in _db.AllSignals)
            if (s.ValueDescriptions.Count > 0)
                names.Add($"VtSig_{s.Name}");

        ValueTableOptions.Add("<none>");
        foreach (var n in names)
            ValueTableOptions.Add(n);

        var self = $"VtEv_{ev.Name}";
        if (ev.ValueDescriptions.Count > 0 && ValueTableOptions.Contains(self))
            ValueTable = self;
        else
            ValueTable = "<none>";
    }

    public void AddControlUnits(IEnumerable<DbcNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (ControlUnits.Any(x => string.Equals(x, n.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            ControlUnits.Add(n.Name);
        }
    }

    public void RemoveSelectedControlUnit()
    {
        if (SelectedControlUnit == null) return;
        ControlUnits.Remove(SelectedControlUnit);
        SelectedControlUnit = null;
    }

    public void AddValueDesc()
    {
        var next = ValueDescriptions.Count == 0 ? 0 : ValueDescriptions.Max(x => x.Value) + 1;
        var row = new ValueDescRow
        {
            Value = next,
            Description = $"Description for the value '0x{next:X}'"
        };
        ValueDescriptions.Add(row);
        SelectedValueDesc = row;
    }

    public void RemoveSelectedValueDesc()
    {
        if (SelectedValueDesc == null) return;
        ValueDescriptions.Remove(SelectedValueDesc);
        SelectedValueDesc = null;
    }

    public void CommitAttribute(string name, string value)
    {
        _src.Attributes[name] = value;
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "EV_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.Value = AttributeValueDisplay.Format(def, value);
            item.IsDefault = AttributeValueDisplay.ValuesEqual(def, item.Value, def?.DefaultValue);
        }
    }

    public void ApplyTo(DbcEnvVar ev)
    {
        ev.Name = Name?.Trim() ?? ev.Name;
        ev.Unit = Unit ?? "";
        ev.Comment = Comment ?? "";

        if (ValueType == "Data")
        {
            ev.Type = 0;
            if (int.TryParse(LengthText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var len) &&
                len >= 0)
                ev.DataLength = len;
            else
                ev.DataLength ??= 0;
        }
        else
        {
            ev.DataLength = null;
            ev.Type = ValueType switch
            {
                "Float" => 1,
                "String" => 2,
                _ => 0
            };
            if (ShowNumericRange)
            {
                if (TryParseNumber(InitialValueText, out var init)) ev.InitialValue = init;
                if (TryParseNumber(MinimumText, out var min)) ev.Min = min;
                if (TryParseNumber(MaximumText, out var max)) ev.Max = max;
            }
        }

        ev.AccessType = AccessToDummy(Access, ValueType == "String");
        ev.AccessNodes.Clear();
        foreach (var n in ControlUnits)
            if (!string.IsNullOrWhiteSpace(n))
                ev.AccessNodes.Add(n);
        ev.ValueDescriptions.Clear();
        foreach (var r in ValueDescriptions.OrderBy(x => x.Value))
            ev.ValueDescriptions.Add(new DbcValueDesc { Value = r.Value, Description = r.Description ?? "" });
    }

    private void BuildAttributes(DbcDatabase db, DbcEnvVar ev)
    {
        AttributeGroups.Clear();
        var group = new EditableAttrGroup { Name = "No category assigned" };
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in ev.Attributes)
        {
            shown.Add(kv.Key);
            var def = db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "EV_" &&
                string.Equals(a.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
            var isDefault = AttributeValueDisplay.ValuesEqual(def, kv.Value, def?.DefaultValue);
            group.Children.Add(EditableAttrItem.FromDef(db, "EV_", kv.Key, kv.Value, isDefault));
        }
        foreach (var d in db.AttrDefs.Where(a => a.ObjectType == "EV_"))
        {
            if (!shown.Add(d.Name)) continue;
            group.Children.Add(EditableAttrItem.FromDef(db, "EV_", d.Name, d.DefaultValue, true));
        }
        if (group.Children.Count > 0)
            AttributeGroups.Add(group);
    }

    /// <summary>
    /// Vector: ENVVAR_DATA_ → Data; DUMMY_NODE_VECTOR800x → String;
    /// else EV_ type digit Integer/Float/String.
    /// </summary>
    public static string MapValueType(DbcEnvVar ev)
    {
        if (ev.DataLength.HasValue)
            return "Data";
        var access = ev.AccessType ?? "";
        if (access.Contains("8000", StringComparison.Ordinal) ||
            access.Contains("8001", StringComparison.Ordinal) ||
            access.Contains("8002", StringComparison.Ordinal) ||
            access.Contains("8003", StringComparison.Ordinal))
            return "String";
        return ev.Type switch
        {
            1 => "Float",
            2 => "String",
            _ => "Integer"
        };
    }

    public static string MapAccess(string? dummy)
    {
        var d = dummy ?? "";
        if (d.Contains("VECTOR1", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("8001", StringComparison.Ordinal))
            return "Read";
        if (d.Contains("VECTOR2", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("8002", StringComparison.Ordinal))
            return "Write";
        if (d.Contains("VECTOR3", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("8003", StringComparison.Ordinal))
            return "ReadWrite";
        return "Unrestricted";
    }

    private static string AccessToDummy(string access, bool asString)
    {
        var key = (access ?? "").Trim().ToLowerInvariant().Replace("/", "");
        var baseCode = key switch
        {
            "read" => 1,
            "write" => 2,
            "readwrite" => 3,
            _ => 0
        };
        if (asString)
            return $"DUMMY_NODE_VECTOR{8000 + baseCode}";
        return $"DUMMY_NODE_VECTOR{baseCode}";
    }

    private static string FormatHex(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9 && v >= 0 && v <= uint.MaxValue)
            return $"0x{(ulong)Math.Round(v):X}";
        return FormatDec(v);
    }

    private static string FormatDec(double v) =>
        v.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            value = hex;
            return true;
        }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
