using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DbcEditor.Models;

namespace DbcEditor.Views;

/// <summary>
/// Vector: Network nodes / &lt;name&gt; → Node 'name'
/// Tabs: Definition | Mapped Tx Sig. | Mapped Rx Sig. | Tx Messages |
///       Networks | Control units | Attributes | Comment
/// </summary>
public partial class NodeEditorWindow : Window
{
    private readonly DbcDatabase _db;
    public NodeEditorVm Vm { get; }

    public NodeEditorWindow(DbcDatabase db, string nodeName)
    {
        InitializeComponent();
        _db = db;
        Vm = new NodeEditorVm(db, nodeName);
        DataContext = Vm;
        Title = $"Node '{nodeName}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.Apply();
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Vm.Apply();

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OnTxMessageDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.DataGrid { SelectedItem: DbcMessage m })
            new MessageEditorWindow(_db, m) { Owner = this }.ShowDialog();
    }

    private void OnTxSignalDoubleClick(object sender, MouseButtonEventArgs e) => OpenSignalFromGrid(sender);

    private void OnRxSignalDoubleClick(object sender, MouseButtonEventArgs e) => OpenSignalFromGrid(sender);

    private void OnViewTxSignal(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTxSignal == null) return;
        OpenSignal(Vm.SelectedTxSignal);
    }

    private void OnAddRxIndividualSignals(object sender, RoutedEventArgs e)
    {
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Signals, Vm.RxSignals) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedSignals.Count == 0) return;
        Vm.AddRxSignals(dlg.SelectedSignals);
    }

    private void OnRemoveRxSignal(object sender, RoutedEventArgs e) => Vm.RemoveSelectedRxSignal();

    private void OnViewRxSignal(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedRxSignal == null) return;
        OpenSignal(Vm.SelectedRxSignal);
    }

    private void OnViewTxMessage(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTxMessage == null) return;
        new MessageEditorWindow(_db, Vm.SelectedTxMessage) { Owner = this }.ShowDialog();
    }

    private void OnViewNetwork(object sender, RoutedEventArgs e) => OpenSelectedNetwork();
    private void OnNetworkDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedNetwork();

    private void OpenSelectedNetwork()
    {
        if (Vm.SelectedNetwork == null) return;
        // Vector: Networks tab View… → Network '<name>'
        new NetworkEditorWindow(_db) { Owner = this }.ShowDialog();
    }

    private void OnViewControlUnit(object sender, RoutedEventArgs e) => OpenSelectedControlUnit();
    private void OnControlUnitDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedControlUnit();

    private void OpenSelectedControlUnit()
    {
        var name = Vm.SelectedControlUnit;
        if (string.IsNullOrWhiteSpace(name)) return;
        // Vector: Control units View… → ECU '<name>'
        new EcuEditorWindow(_db, name) { Owner = this }.ShowDialog();
    }

    private void OpenSignalFromGrid(object sender)
    {
        if (sender is not System.Windows.Controls.DataGrid { SelectedItem: DbcSignal s }) return;
        OpenSignal(s);
    }

    private void OpenSignal(DbcSignal s)
    {
        var msg = _db.Messages.FirstOrDefault(x => x.Name == s.MessageName)
                  ?? _db.Messages.FirstOrDefault(x => x.Id == s.MessageId);
        new SignalEditorWindow(_db, s, msg, catalogAttributes: false) { Owner = this }.ShowDialog();
    }
}

public sealed class NodeEditorVm : INotifyPropertyChanged
{
    private readonly DbcDatabase _db;
    private readonly string _originalName;
    private DbcSignal? _selectedTxSignal;
    private DbcSignal? _selectedRxSignal;
    private DbcMessage? _selectedTxMessage;
    private NetworkRefRow? _selectedNetwork;
    private string? _selectedControlUnit;

    public string Name { get; set; }
    public string Comment { get; set; } = "";
    public string Address { get; set; } = "0x0";
    public ObservableCollection<DbcMessage> TxMessages { get; } = new();
    public ObservableCollection<DbcMessage> RxMessages { get; } = new();
    public ObservableCollection<DbcSignal> TxSignals { get; } = new();
    public ObservableCollection<DbcSignal> RxSignals { get; } = new();
    public DbcDatabase Database => _db;
    public ObservableCollection<NetworkRefRow> Networks { get; } = new();
    public ObservableCollection<string> ControlUnits { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();
    private DbcNode? _node;

    public void CommitAttribute(string name, string value)
    {
        if (_node == null) return;
        // Store raw DBC form when possible (yes/no → 1/0 for NmNode)
        var raw = name.Equals("NmNode", StringComparison.OrdinalIgnoreCase)
            ? value.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "1"
              : value.Equals("no", StringComparison.OrdinalIgnoreCase) ? "0" : value
            : value;
        _node.Attributes[name] = raw;
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            item.Value = value;
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.IsDefault = def != null && (
                string.Equals(def.DefaultValue, raw, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(DisplayBuAttr(name, def.DefaultValue), value, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DbcSignal? SelectedTxSignal
    {
        get => _selectedTxSignal;
        set
        {
            _selectedTxSignal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTxSignal));
        }
    }

    public bool HasSelectedTxSignal => SelectedTxSignal != null;

    public DbcSignal? SelectedRxSignal
    {
        get => _selectedRxSignal;
        set
        {
            _selectedRxSignal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedRxSignal));
        }
    }

    public bool HasSelectedRxSignal => SelectedRxSignal != null;

    public DbcMessage? SelectedTxMessage
    {
        get => _selectedTxMessage;
        set
        {
            _selectedTxMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTxMessage));
        }
    }

    public bool HasSelectedTxMessage => SelectedTxMessage != null;

    public NetworkRefRow? SelectedNetwork
    {
        get => _selectedNetwork;
        set
        {
            _selectedNetwork = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedNetwork));
        }
    }

    public bool HasSelectedNetwork => SelectedNetwork != null;

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

    public string CurrentNodeName =>
        string.IsNullOrWhiteSpace(Name) ? _originalName : Name.Trim();

    public NodeEditorVm(DbcDatabase db, string nodeName)
    {
        _db = db;
        _originalName = nodeName;
        Name = nodeName;
        var node = db.FindNode(nodeName);
        Comment = node?.Comment ?? "";
        Address = node?.Address ?? "0x0";
        Networks.Add(new NetworkRefRow
        {
            Name = db.NetworkName,
            Protocol = string.IsNullOrWhiteSpace(db.Protocol) ? "CAN" : db.Protocol
        });
        ControlUnits.Add(nodeName);

        RebuildTxFromDb();

        foreach (var m in db.Messages.Where(m =>
                     m.Signals.Any(s => s.Receivers.Any(r =>
                         string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase)))))
            RxMessages.Add(m);

        foreach (var s in db.AllSignals.Where(s =>
                     s.Receivers.Any(r => string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase))))
            RxSignals.Add(s);

        BuildAttributes(node);
    }

    private bool NodeTransmits(DbcMessage m, string nodeName)
    {
        if (string.Equals(m.Transmitter, nodeName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (_db.MessageTransmitters.TryGetValue(m.Id, out var list))
            return list.Any(n => string.Equals(n, nodeName, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    private void RebuildTxFromDb()
    {
        var nodeName = CurrentNodeName;
        TxMessages.Clear();
        TxSignals.Clear();
        foreach (var m in _db.Messages.Where(m => NodeTransmits(m, nodeName)))
        {
            TxMessages.Add(m);
            foreach (var s in m.Signals)
                TxSignals.Add(s);
        }
    }

    public void AddRxSignals(IEnumerable<DbcSignal> signals)
    {
        var nodeName = CurrentNodeName;
        foreach (var s in signals)
        {
            if (s.Receivers.Any(r => string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase)))
                continue;
            s.Receivers.RemoveAll(r => r == "Vector__XXX");
            s.Receivers.Add(nodeName);
            if (!RxSignals.Contains(s))
                RxSignals.Add(s);
        }
        RefreshRxMessages();
    }

    public void RemoveSelectedRxSignal()
    {
        if (SelectedRxSignal == null) return;
        var s = SelectedRxSignal;
        var nodeName = CurrentNodeName;
        s.Receivers.RemoveAll(r => string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase));
        if (s.Receivers.Count == 0)
            s.Receivers.Add("Vector__XXX");
        RxSignals.Remove(s);
        SelectedRxSignal = null;
        RefreshRxMessages();
    }

    private void RefreshRxMessages()
    {
        var nodeName = CurrentNodeName;
        RxMessages.Clear();
        foreach (var m in _db.Messages.Where(m =>
                     m.Signals.Any(s => s.Receivers.Any(r =>
                         string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase)))))
            RxMessages.Add(m);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private void BuildAttributes(DbcNode? node)
    {
        _node = node;
        AttributeGroups.Clear();
        var canoe = new EditableAttrGroup { Name = "CANoe" };
        var netMgmt = new EditableAttrGroup { Name = "Net Management" };
        var other = new EditableAttrGroup { Name = "No category assigned" };
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string value, bool isDefault)
        {
            if (string.IsNullOrEmpty(name) || !shown.Add(name)) return;
            PickGroup(name).Children.Add(EditableAttrItem.FromDef(_db, "BU_", name, value, isDefault));
        }

        EditableAttrGroup PickGroup(string name)
        {
            if (name.Equals("NodeLayerModules", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("CANoe", StringComparison.OrdinalIgnoreCase))
                return canoe;
            if (name.StartsWith("Nm", StringComparison.OrdinalIgnoreCase))
                return netMgmt;
            return other;
        }

        bool HasBuDef(string name) =>
            _db.AttrDefs.Any(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        (string? value, bool isDefault) Resolve(string name)
        {
            // Vector: no BA_DEF_ BU_ → attribute (and its category) must not appear
            if (!HasBuDef(name)) return (null, false);

            if (node != null && node.Attributes.TryGetValue(name, out var v) && v != null)
                return (DisplayBuAttr(name, v), IsDefault(name, v));

            var def = _db.AttrDefs.First(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(def.DefaultValue))
                return (DisplayBuAttr(name, def.DefaultValue), true);
            return (null, false);
        }

        bool IsDefault(string name, string value)
        {
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (def == null || string.IsNullOrEmpty(def.DefaultValue)) return false;
            return string.Equals(def.DefaultValue, value, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(DisplayBuAttr(name, def.DefaultValue), DisplayBuAttr(name, value),
                       StringComparison.OrdinalIgnoreCase);
        }

        foreach (var prefer in new[] { "NodeLayerModules", "NmNode", "NmStationAddress", "NmNodeId" })
        {
            var (val, isDef) = Resolve(prefer);
            if (val != null) Add(prefer, val, isDef);
        }

        if (node != null)
        {
            foreach (var kv in node.Attributes)
            {
                if (!HasBuDef(kv.Key)) continue;
                Add(kv.Key, DisplayBuAttr(kv.Key, kv.Value), IsDefault(kv.Key, kv.Value));
            }
        }

        foreach (var d in _db.AttrDefs.Where(a => a.ObjectType == "BU_"))
        {
            var (val, isDef) = Resolve(d.Name);
            if (val != null) Add(d.Name, val, isDef);
        }

        // Empty groups stay hidden — matches Vector (no CANoe/NM section without defs)
        if (canoe.Children.Count > 0) AttributeGroups.Add(canoe);
        if (netMgmt.Children.Count > 0) AttributeGroups.Add(netMgmt);
        if (other.Children.Count > 0) AttributeGroups.Add(other);
        if (AttributeGroups.Count == 0)
            AttributeGroups.Add(new EditableAttrGroup { Name = "(no node attributes)" });
    }

    private static string DisplayBuAttr(string name, string raw)
    {
        if (name.Equals("NmNode", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                "0" => "no",
                "1" => "yes",
                _ => raw
            };
        }
        return raw;
    }

    public void Apply()
    {
        var newName = (Name ?? "").Trim();
        if (string.IsNullOrEmpty(newName)) return;

        var node = _db.FindNode(_originalName);
        if (node != null)
        {
            node.Comment = Comment ?? "";
            node.Address = string.IsNullOrWhiteSpace(Address) ? "0x0" : Address.Trim();
        }

        if (string.Equals(newName, _originalName, StringComparison.Ordinal))
            return;

        if (node != null)
            node.Name = newName;

        foreach (var m in _db.Messages)
        {
            if (string.Equals(m.Transmitter, _originalName, StringComparison.OrdinalIgnoreCase))
                m.Transmitter = newName;
        }
        foreach (var kv in _db.MessageTransmitters)
        {
            for (int i = 0; i < kv.Value.Count; i++)
            {
                if (string.Equals(kv.Value[i], _originalName, StringComparison.OrdinalIgnoreCase))
                    kv.Value[i] = newName;
            }
        }
        foreach (var s in _db.AllSignals)
        {
            for (int i = 0; i < s.Receivers.Count; i++)
            {
                if (string.Equals(s.Receivers[i], _originalName, StringComparison.OrdinalIgnoreCase))
                    s.Receivers[i] = newName;
            }
        }
        if (ControlUnits.Count > 0 &&
            string.Equals(ControlUnits[0], _originalName, StringComparison.OrdinalIgnoreCase))
            ControlUnits[0] = newName;
    }
}

public sealed class NetworkRefRow
{
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "CAN";
}

