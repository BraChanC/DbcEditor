using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DbcEditor.Models;

namespace DbcEditor.Views;

/// <summary>
/// Vector: Signal 'Name' —
/// Definition | Messages | Receivers | Attributes | Value Descriptions | Comment
/// </summary>
public partial class SignalEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly DbcSignal _model;
    public SignalEditorVm Vm { get; }

    /// <param name="catalogAttributes">
    /// Vector: true when opened from Signals tree (show GenSig*); false from Messages (Attributes empty).
    /// </param>
    public SignalEditorWindow(DbcDatabase db, DbcSignal model, DbcMessage? ownerMessage = null,
        bool catalogAttributes = false)
    {
        InitializeComponent();
        _db = db;
        _model = model;
        Vm = new SignalEditorVm(db, model, ownerMessage, catalogAttributes);
        DataContext = Vm;
        Title = $"Signal '{model.Name}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        Title = $"Signal '{_model.Name}'";
    }

    private void OnCalculateMinMax(object sender, RoutedEventArgs e) => Vm.CalculateMinMax();

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OnAddMessage(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.Messages.Select(m => m.Name).ToList();
        // Choose Objects for messages — exclude already linked
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Messages) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedMessages.Count == 0) return;
        foreach (var m in dlg.SelectedMessages)
        {
            if (exclude.Contains(m.Name, StringComparer.OrdinalIgnoreCase)) continue;
            Vm.AttachToMessage(m);
        }
    }

    private void OnRemoveMessage(object sender, RoutedEventArgs e) => Vm.DetachSelectedMessage();

    private void OnViewMessage(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedMessage == null) return;
        new MessageEditorWindow(_db, Vm.SelectedMessage) { Owner = this }.ShowDialog();
    }

    private void OnAddReceiver(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.ReceiverNodes.Select(r => r.Name).ToList();
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Nodes, excludeNodeNames: exclude)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || dlg.SelectedNodes.Count == 0) return;
        Vm.AddReceivers(dlg.SelectedNodes);
    }

    private void OnRemoveReceiver(object sender, RoutedEventArgs e) => Vm.RemoveSelectedReceiver();

    private void OnViewReceiver(object sender, RoutedEventArgs e)
    {
        var name = Vm.SelectedReceiver?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        new NodeEditorWindow(_db, name) { Owner = this }.ShowDialog();
    }
}

public sealed class SignalEditorVm : INotifyPropertyChanged
{
    private readonly DbcDatabase _db;
    private readonly DbcSignal _src;
    private DbcMessage? _selectedMessage;
    private MsgNodeRow? _selectedReceiver;
    private string _selectedValueTable = "-";

    public string Name { get; set; }
    public string Length { get; set; }
    public string StartBit { get; set; }
    public string ByteOrder { get; set; }
    public string ValueType { get; set; }
    public string Factor { get; set; }
    public string Offset { get; set; }
    public string Min { get; set; }
    public string Max { get; set; }
    public string Unit { get; set; }
    public string InitValue { get; set; }
    public string Comment { get; set; } = "";

    public DbcDatabase Database => _db;
    public ObservableCollection<DbcMessage> Messages { get; } = new();
    public ObservableCollection<MsgNodeRow> ReceiverNodes { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();
    public ObservableCollection<DbcValueDesc> ValueDescriptions { get; } = new();
    public ObservableCollection<string> ValueTableOptions { get; } = new();
    public List<string> ByteOrders { get; } = new() { "Intel", "Motorola" };
    public List<string> ValueTypes { get; } = new() { "Unsigned", "Signed" };

    public DbcMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            _selectedMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedMessage));
        }
    }

    public bool HasSelectedMessage => SelectedMessage != null;

    public MsgNodeRow? SelectedReceiver
    {
        get => _selectedReceiver;
        set
        {
            _selectedReceiver = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedReceiver));
        }
    }

    public bool HasSelectedReceiver => SelectedReceiver != null;

    public string SelectedValueTable
    {
        get => _selectedValueTable;
        set { _selectedValueTable = value; OnPropertyChanged(); }
    }

    private readonly bool _catalogAttributes;

    public SignalEditorVm(DbcDatabase db, DbcSignal s, DbcMessage? owner, bool catalogAttributes = false)
    {
        _db = db;
        _src = s;
        _catalogAttributes = catalogAttributes;
        Name = s.Name;
        Length = s.Length.ToString();
        StartBit = s.StartBit.ToString();
        ByteOrder = s.IsLittleEndian ? "Intel" : "Motorola";
        ValueType = s.IsSigned ? "Signed" : "Unsigned";
        Factor = s.Factor.ToString(CultureInfo.InvariantCulture);
        Offset = s.Offset.ToString(CultureInfo.InvariantCulture);
        Min = s.Min.ToString(CultureInfo.InvariantCulture);
        Max = s.Max.ToString(CultureInfo.InvariantCulture);
        Unit = s.Unit;
        InitValue = string.IsNullOrWhiteSpace(s.InitialValue) ? "0" : s.InitialValue;
        if (s.Attributes.TryGetValue("GenSigStartValue", out var start) && !string.IsNullOrWhiteSpace(start))
            InitValue = start;
        Comment = s.Comment ?? "";

        if (owner != null) Messages.Add(owner);
        else
        {
            var msg = db.FindMessage(s.MessageId)
                      ?? db.Messages.FirstOrDefault(m => m.Name == s.MessageName);
            if (msg != null) Messages.Add(msg);
        }

        ReloadReceivers();

        foreach (var vd in s.ValueDescriptions.OrderBy(x => x.Value))
            ValueDescriptions.Add(vd);

        BuildValueTableOptions();
        BuildAttributes(s);
    }

    private void BuildValueTableOptions()
    {
        ValueTableOptions.Clear();
        ValueTableOptions.Add("-");
        foreach (var t in _db.ValueTables)
            ValueTableOptions.Add(t.Name);

        var msg = Messages.FirstOrDefault();
        if (ValueDescriptions.Count > 0 && msg != null)
        {
            var label = $"VtSig_{Name} ({msg.Name}::{Name})";
            if (!ValueTableOptions.Contains(label))
                ValueTableOptions.Add(label);
            SelectedValueTable = label;
        }
        else
        {
            SelectedValueTable = "-";
        }
    }

    private void ReloadReceivers()
    {
        ReceiverNodes.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _src.Receivers)
        {
            if (string.IsNullOrWhiteSpace(r) || r == "Vector__XXX" || !seen.Add(r)) continue;
            var node = _db.FindNode(r);
            ReceiverNodes.Add(new MsgNodeRow { Name = r, Address = node?.Address ?? "0x0" });
        }
    }

    public void CalculateMinMax()
    {
        if (!int.TryParse(Length, out var bits) || bits <= 0) return;
        if (!double.TryParse(Factor, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
            factor = 1;
        if (!double.TryParse(Offset, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            offset = 0;

        double rawMin, rawMax;
        if (ValueType == "Signed")
        {
            rawMin = -(1L << (bits - 1));
            rawMax = (1L << (bits - 1)) - 1;
        }
        else
        {
            rawMin = 0;
            rawMax = bits >= 63 ? double.MaxValue : (1L << bits) - 1;
        }

        Min = (rawMin * factor + offset).ToString(CultureInfo.InvariantCulture);
        Max = (rawMax * factor + offset).ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Min));
        OnPropertyChanged(nameof(Max));
    }

    public void AttachToMessage(DbcMessage msg)
    {
        // Move signal onto selected message
        foreach (var m in _db.Messages)
            m.Signals.Remove(_src);
        if (!msg.Signals.Contains(_src))
            msg.Signals.Add(_src);
        _src.MessageName = msg.Name;
        _src.MessageId = msg.Id;
        Messages.Clear();
        Messages.Add(msg);
        SelectedMessage = msg;
        BuildValueTableOptions();
    }

    public void DetachSelectedMessage()
    {
        if (SelectedMessage == null) return;
        SelectedMessage.Signals.Remove(_src);
        Messages.Remove(SelectedMessage);
        SelectedMessage = Messages.FirstOrDefault();
        if (SelectedMessage != null)
        {
            _src.MessageName = SelectedMessage.Name;
            _src.MessageId = SelectedMessage.Id;
        }
        else
        {
            _src.MessageName = "";
            _src.MessageId = 0;
        }
        BuildValueTableOptions();
    }

    public void AddReceivers(IEnumerable<DbcNode> nodes)
    {
        _src.Receivers.RemoveAll(r => r == "Vector__XXX");
        foreach (var n in nodes)
        {
            if (_src.Receivers.Any(r => string.Equals(r, n.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            _src.Receivers.Add(n.Name);
        }
        ReloadReceivers();
    }

    public void RemoveSelectedReceiver()
    {
        if (SelectedReceiver == null) return;
        _src.Receivers.RemoveAll(r =>
            string.Equals(r, SelectedReceiver.Name, StringComparison.OrdinalIgnoreCase));
        if (_src.Receivers.Count == 0)
            _src.Receivers.Add("Vector__XXX");
        SelectedReceiver = null;
        ReloadReceivers();
    }

    public void CommitAttribute(string name, string value)
    {
        _src.Attributes[name] = value;
        if (name.Equals("GenSigStartValue", StringComparison.OrdinalIgnoreCase))
        {
            InitValue = value;
            OnPropertyChanged(nameof(InitValue));
        }
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            item.Value = value;
            item.IsDefault = IsDefaultValue(name, value);
        }
    }

    private void BuildAttributes(DbcSignal s)
    {
        // Vector: Signals catalog → GenSig* Attributes; Messages/message context → empty.
        AttributeGroups.Clear();
        if (!_catalogAttributes)
        {
            AttributeGroups.Add(new EditableAttrGroup { Name = "(no signal attributes)" });
            return;
        }

        var interaction = new EditableAttrGroup { Name = "Interaction Layer" };
        var other = new EditableAttrGroup { Name = "No category assigned" };
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool HasSgDef(string name) =>
            _db.AttrDefs.Any(a =>
                a.ObjectType == "SG_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        void Add(string name, string value, bool isDefault)
        {
            if (string.IsNullOrEmpty(name) || !shown.Add(name)) return;
            var group = name.Equals("GenSigInvalidValue", StringComparison.OrdinalIgnoreCase)
                ? other
                : name.StartsWith("GenSig", StringComparison.OrdinalIgnoreCase)
                    ? interaction
                    : other;
            group.Children.Add(EditableAttrItem.FromDef(_db, "SG_", name, value, isDefault));
        }

        foreach (var prefer in new[]
                 {
                     "GenSigInactiveValue", "GenSigSendType", "GenSigStartValue", "GenSigInvalidValue"
                 })
        {
            var (val, isDef) = Resolve(s, prefer);
            if (val != null) Add(prefer, val, isDef);
        }

        foreach (var kv in s.Attributes)
        {
            if (!HasSgDef(kv.Key)) continue;
            var (val, isDef) = Resolve(s, kv.Key);
            if (val != null) Add(kv.Key, val, isDef);
        }

        foreach (var d in _db.AttrDefs.Where(a => a.ObjectType == "SG_"))
        {
            var (val, isDef) = Resolve(s, d.Name);
            if (val != null) Add(d.Name, val, isDef);
        }

        if (interaction.Children.Count > 0) AttributeGroups.Add(interaction);
        if (other.Children.Count > 0) AttributeGroups.Add(other);
        if (AttributeGroups.Count == 0)
            AttributeGroups.Add(new EditableAttrGroup { Name = "(no signal attributes)" });
    }

    private (string? value, bool isDefault) Resolve(DbcSignal s, string name)
    {
        var def = _db.AttrDefs.FirstOrDefault(a =>
            a.ObjectType == "SG_" &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null) return (null, false);

        if (s.Attributes.TryGetValue(name, out var v) && v != null)
        {
            if (v.Length == 0)
                return ("<n.a.>", true);
            return (v, IsDefaultValue(name, v));
        }

        // Empty BA_DEF_DEF_ → <n.a.>* (GenSigSendType default "" in Radar DBC)
        if (string.IsNullOrEmpty(def.DefaultValue))
            return ("<n.a.>", true);
        return (def.DefaultValue, true);
    }

    private bool IsDefaultValue(string name, string value)
    {
        var def = _db.AttrDefs.FirstOrDefault(a =>
            a.ObjectType == "SG_" &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null || string.IsNullOrEmpty(def.DefaultValue)) return false;
        return string.Equals(def.DefaultValue, value, StringComparison.OrdinalIgnoreCase);
    }

    public void ApplyTo(DbcSignal s)
    {
        s.Name = Name;
        if (int.TryParse(Length, out var len)) s.Length = len;
        if (int.TryParse(StartBit, out var sb)) s.StartBit = sb;
        s.IsLittleEndian = ByteOrder == "Intel";
        s.IsSigned = ValueType == "Signed";
        if (double.TryParse(Factor, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) s.Factor = f;
        if (double.TryParse(Offset, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.Offset = o;
        if (double.TryParse(Min, NumberStyles.Float, CultureInfo.InvariantCulture, out var mi)) s.Min = mi;
        if (double.TryParse(Max, NumberStyles.Float, CultureInfo.InvariantCulture, out var ma)) s.Max = ma;
        s.Unit = Unit;
        s.InitialValue = string.IsNullOrWhiteSpace(InitValue) ? "0" : InitValue.Trim();
        s.Attributes["GenSigStartValue"] = s.InitialValue;
        s.Comment = Comment ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

