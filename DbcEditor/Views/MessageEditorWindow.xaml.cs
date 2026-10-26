using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Views;

/// <summary>
/// Vector: Message 'Name (0xID)' —
/// Definition | Signals | Transmitters | Receivers | Layout | Attributes | Comment
/// </summary>
public partial class MessageEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly DbcMessage _model;
    public MessageEditorVm Vm { get; }

    public MessageEditorWindow(DbcDatabase db, DbcMessage model)
    {
        InitializeComponent();
        _db = db;
        _model = model;
        Vm = new MessageEditorVm(db, model);
        DataContext = Vm;
        Title = $"Message '{model.Name} ({model.IdHex})'";
        BitLayout.SignalActivated += s => OpenSignal(s);
        BitLayout.EditSignalRequested += () =>
        {
            if (Vm.SelectedSignal != null) OpenSignal(Vm.SelectedSignal);
        };
        BitLayout.AddSignalRequested += () => OnAddSignal(BitLayout, new RoutedEventArgs());
        BitLayout.RemoveSignalRequested += () =>
        {
            if (Vm.SelectedSignal == null) return;
            Vm.RemoveSelectedSignal();
            RefreshBitLayout();
        };
    }

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        BitLayout.Message = null;
        BitLayout.Message = _model;
        Title = $"Message '{_model.Name} ({_model.IdHex})'";
    }

    private void OnSignalGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedSignal != null) OpenSignal(Vm.SelectedSignal);
    }

    private void OnViewSignal(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSignal != null) OpenSignal(Vm.SelectedSignal);
    }

    private void OnAddSignal(object sender, RoutedEventArgs e)
    {
        var exclude = _model.Signals.ToList();
        var hasOthers = _db.AllSignals.Any(s => !exclude.Contains(s));
        if (hasOthers)
        {
            var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Signals, excludeSignals: exclude)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && dlg.SelectedSignals.Count > 0)
            {
                Vm.AddSignals(dlg.SelectedSignals);
                RefreshBitLayout();
                return;
            }
            if (dlg.DialogResult != true) return;
        }

        // No other signals to pick — create a new one (Vector Add on empty set)
        var created = Vm.CreateNewSignal();
        RefreshBitLayout(created);
        OpenSignal(created);
    }

    private void OnRemoveSignal(object sender, RoutedEventArgs e)
    {
        Vm.RemoveSelectedSignal();
        RefreshBitLayout();
    }

    private void OnAddTransmitter(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.Transmitters.Select(t => t.Name).ToList();
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Nodes, excludeNodeNames: exclude)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || dlg.SelectedNodes.Count == 0) return;
        Vm.AddTransmitters(dlg.SelectedNodes);
    }

    private void OnRemoveTransmitter(object sender, RoutedEventArgs e) => Vm.RemoveSelectedTransmitter();

    private void OnViewTransmitter(object sender, RoutedEventArgs e)
    {
        var name = Vm.SelectedTransmitter?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        new NodeEditorWindow(_db, name) { Owner = this }.ShowDialog();
    }

    private void OnViewReceiver(object sender, RoutedEventArgs e)
    {
        var name = Vm.SelectedReceiver?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        new NodeEditorWindow(_db, name) { Owner = this }.ShowDialog();
    }

    private void OpenSignal(DbcSignal s)
    {
        // Vector: opened from Message → Attributes empty (not Signals catalog)
        var dlg = new SignalEditorWindow(_db, s, _model, catalogAttributes: false) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Vm.RefreshSignals();
            RefreshBitLayout(s);
        }
    }

    private void RefreshBitLayout(DbcSignal? selected = null)
    {
        BitLayout.Message = null;
        BitLayout.Message = _model;
        if (selected != null)
            BitLayout.SelectedSignal = selected;
    }
}

public sealed class MessageEditorVm : INotifyPropertyChanged
{
    private readonly DbcMessage _src;
    private readonly DbcDatabase _db;
    private DbcSignal? _selectedSignal;
    private MsgSignalRow? _selectedSignalRow;
    private MsgNodeRow? _selectedTransmitter;
    private MsgNodeRow? _selectedReceiver;

    public DbcMessage Model => _src;
    public string Name { get; set; }
    public string IdText { get; set; }
    public string IdFormat { get; set; }
    public int Dlc { get; set; }
    public string Transmitter { get; set; }
    public string TxMethod { get; set; }
    public string CycleTime { get; set; }
    public string Comment { get; set; }

    public ObservableCollection<MsgSignalRow> SignalRows { get; } = new();
    public ObservableCollection<MsgNodeRow> Transmitters { get; } = new();
    public ObservableCollection<MsgNodeRow> ReceiverNodes { get; } = new();
    public DbcDatabase Database => _db;
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();
    public ObservableCollection<string> TransmitterOptions { get; } = new();

    public void CommitAttribute(string name, string value)
    {
        _src.Attributes[name] = value;
        if (name.Equals("GenMsgCycleTime", StringComparison.OrdinalIgnoreCase))
        {
            CycleTime = value;
            OnPropertyChanged(nameof(CycleTime));
        }
        if (name.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
        {
            TxMethod = DisplayAttr(name, value);
            OnPropertyChanged(nameof(TxMethod));
        }
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            item.Value = value;
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BO_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.IsDefault = def != null &&
                string.Equals(def.DefaultValue, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    public List<string> MultiplexorOptions { get; } = new() { "- No Multiplexor -" };
    public List<string> IdFormats { get; } = new()
    {
        "CAN Standard", "CAN Extended", "CAN FD Standard", "CAN FD Extended"
    };
    public List<string> TxMethods { get; } = new() { "Cyclic", "Event", "CE", "NoMsgSendType", "n/a" };
    public List<int> DlcOptions { get; } = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64 };

    public DbcSignal? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            _selectedSignal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSignal));
            if (value != null)
            {
                var row = SignalRows.FirstOrDefault(r => r.Model == value);
                if (row != null && !ReferenceEquals(_selectedSignalRow, row))
                {
                    _selectedSignalRow = row;
                    OnPropertyChanged(nameof(SelectedSignalRow));
                }
            }
        }
    }

    public MsgSignalRow? SelectedSignalRow
    {
        get => _selectedSignalRow;
        set
        {
            _selectedSignalRow = value;
            SelectedSignal = value?.Model;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSignal));
        }
    }

    public bool HasSelectedSignal => SelectedSignal != null;

    public MsgNodeRow? SelectedTransmitter
    {
        get => _selectedTransmitter;
        set
        {
            _selectedTransmitter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTransmitter));
        }
    }

    public bool HasSelectedTransmitter => SelectedTransmitter != null;

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

    public MessageEditorVm(DbcDatabase db, DbcMessage m)
    {
        _db = db;
        _src = m;
        Name = m.Name;
        IdText = m.IdHex;
        IdFormat = string.IsNullOrWhiteSpace(m.IdFormat) ? "CAN Standard" : m.IdFormat;
        Dlc = m.Dlc;
        Transmitter = m.Transmitter;
        TxMethod = string.IsNullOrWhiteSpace(m.TxMethod) ? "n/a" : m.TxMethod;
        CycleTime = string.IsNullOrWhiteSpace(m.CycleTime) ? "0" : m.CycleTime;
        Comment = m.Comment;

        foreach (var n in db.Nodes)
            TransmitterOptions.Add(n.Name);
        if (!string.IsNullOrEmpty(Transmitter) && !TransmitterOptions.Contains(Transmitter))
            TransmitterOptions.Add(Transmitter);

        RefreshSignals();
        BuildTransmitters();
        BuildReceivers();
        BuildAttributes();
    }

    public void RefreshSignals()
    {
        SignalRows.Clear();
        foreach (var s in _src.Signals)
        {
            SignalRows.Add(new MsgSignalRow
            {
                Model = s,
                Name = s.Name,
                Message = string.IsNullOrEmpty(s.MessageName) ? _src.Name : s.MessageName,
                Multiplexing = string.IsNullOrWhiteSpace(s.Multiplexing) ? "-" : s.Multiplexing,
                StartBit = s.StartBit,
                Length = s.Length,
                ByteOrder = s.IsLittleEndian ? "Intel" : "Motorola",
                ValueType = s.IsSigned ? "Signed" : "Unsigned"
            });
        }
    }

    private void BuildTransmitters()
    {
        Transmitters.Clear();
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(_src.Transmitter))
            names.Add(_src.Transmitter);
        if (_db.MessageTransmitters.TryGetValue(_src.Id, out var extra))
        {
            foreach (var n in extra)
            {
                if (!names.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                    names.Add(n);
            }
        }

        foreach (var name in names)
        {
            var node = _db.FindNode(name);
            Transmitters.Add(new MsgNodeRow
            {
                Name = name,
                Address = node?.Address ?? "0x0"
            });
        }
    }

    public DbcSignal CreateNewSignal()
    {
        var baseName = "NewSignal";
        var name = baseName;
        var i = 1;
        while (_db.AllSignals.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName}_{i++}";

        var sig = new DbcSignal
        {
            Name = name,
            StartBit = 0,
            Length = 1,
            IsLittleEndian = false,
            MessageName = _src.Name,
            MessageId = _src.Id,
            Multiplexing = "-"
        };
        sig.Receivers.Add("Vector__XXX");
        _src.Signals.Add(sig);
        _db.AllSignals.Add(sig);
        RefreshSignals();
        SelectedSignal = sig;
        return sig;
    }

    public void AddSignals(IEnumerable<DbcSignal> signals)
    {
        foreach (var s in signals)
        {
            if (_src.Signals.Contains(s)) continue;

            // Move off previous message if any
            foreach (var other in _db.Messages)
            {
                if (other == _src) continue;
                other.Signals.Remove(s);
            }

            s.MessageName = _src.Name;
            s.MessageId = _src.Id;
            _src.Signals.Add(s);
            if (!_db.AllSignals.Contains(s))
                _db.AllSignals.Add(s);
        }
        RefreshSignals();
        BuildReceivers();
    }

    public void RemoveSelectedSignal()
    {
        if (SelectedSignal == null) return;
        var s = SelectedSignal;
        _src.Signals.Remove(s);
        _db.AllSignals.Remove(s);
        SelectedSignalRow = null;
        SelectedSignal = null;
        RefreshSignals();
        BuildReceivers();
    }

    public void AddTransmitters(IEnumerable<DbcNode> nodes)
    {
        if (!_db.MessageTransmitters.TryGetValue(_src.Id, out var list))
        {
            list = new List<string>();
            _db.MessageTransmitters[_src.Id] = list;
        }

        foreach (var n in nodes)
        {
            if (Transmitters.Any(t => string.Equals(t.Name, n.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (string.IsNullOrWhiteSpace(_src.Transmitter))
            {
                _src.Transmitter = n.Name;
                Transmitter = n.Name;
            }
            else if (!string.Equals(_src.Transmitter, n.Name, StringComparison.OrdinalIgnoreCase) &&
                     !list.Contains(n.Name, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(n.Name);
            }

            Transmitters.Add(new MsgNodeRow { Name = n.Name, Address = n.Address });
        }
        OnPropertyChanged(nameof(Transmitter));
    }

    public void RemoveSelectedTransmitter()
    {
        if (SelectedTransmitter == null) return;
        var name = SelectedTransmitter.Name;

        if (_db.MessageTransmitters.TryGetValue(_src.Id, out var list))
            list.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(_src.Transmitter, name, StringComparison.OrdinalIgnoreCase))
        {
            var next = Transmitters.FirstOrDefault(t =>
                !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            _src.Transmitter = next?.Name ?? "";
            Transmitter = _src.Transmitter;
            if (next != null && _db.MessageTransmitters.TryGetValue(_src.Id, out var extra))
                extra.RemoveAll(x => string.Equals(x, next.Name, StringComparison.OrdinalIgnoreCase));
        }

        Transmitters.Remove(SelectedTransmitter);
        SelectedTransmitter = null;
        OnPropertyChanged(nameof(Transmitter));
    }

    private void BuildReceivers()
    {
        ReceiverNodes.Clear();
        SelectedReceiver = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _src.Signals)
        foreach (var r in s.Receivers)
        {
            if (string.IsNullOrWhiteSpace(r) || r == "Vector__XXX" || !names.Add(r)) continue;
            var node = _db.FindNode(r);
            ReceiverNodes.Add(new MsgNodeRow { Name = r, Address = node?.Address ?? "0x0" });
        }
    }

    private void BuildAttributes()
    {
        // Vector Message Attributes (Radar DBC): CAN FD | Interaction Layer | Net Management | No category
        // Empty BA_DEF_DEF_ ENUM "" → "<n.a.>*"; only BA_DEF_ BO_ attrs are listed.
        AttributeGroups.Clear();
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var canFd = new EditableAttrGroup { Name = "CAN FD" };
        var diagnostics = new EditableAttrGroup { Name = "Diagnostics" };
        var interaction = new EditableAttrGroup { Name = "Interaction Layer" };
        var netMgmt = new EditableAttrGroup { Name = "Net Management" };
        var other = new EditableAttrGroup { Name = "No category assigned" };

        void Add(string name, string value, bool isDefault)
        {
            if (string.IsNullOrEmpty(name) || !shown.Add(name)) return;
            // Pass raw (incl. enum index / empty→n.a.); FromDef maps ENUM labels
            PickGroup(name).Children.Add(EditableAttrItem.FromDef(_db, "BO_", name, value, isDefault));
        }

        EditableAttrGroup PickGroup(string name)
        {
            if (name.StartsWith("CANFD", StringComparison.OrdinalIgnoreCase)) return canFd;
            if (name.StartsWith("GenMsg", StringComparison.OrdinalIgnoreCase)) return interaction;
            if (name.StartsWith("Nm", StringComparison.OrdinalIgnoreCase)) return netMgmt;
            // Vector: DiagMessage stays in "No category"; only DiagRequest/Response/State → Diagnostics
            if (name.Equals("DiagRequest", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("DiagResponse", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("DiagState", StringComparison.OrdinalIgnoreCase))
                return diagnostics;
            return other;
        }

        bool HasBoDef(string name) =>
            _db.AttrDefs.Any(a =>
                a.ObjectType == "BO_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        string? Resolve(string name, out bool isDefault)
        {
            isDefault = false;
            if (!HasBoDef(name)) return null;

            if (_src.Attributes.TryGetValue(name, out var v) && v != null)
            {
                // Stored empty still means n.a. default for ENUM "" defs
                if (v.Length == 0)
                {
                    isDefault = true;
                    return "<n.a.>";
                }
                return v;
            }

            var def = _db.AttrDefs.First(a =>
                a.ObjectType == "BO_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            isDefault = true;
            // Vector: BA_DEF_DEF_ "" → <n.a.>*
            if (string.IsNullOrEmpty(def.DefaultValue))
                return "<n.a.>";
            return def.DefaultValue;
        }

        // Prefer Vector-ish order within groups
        foreach (var prefer in new[]
                 {
                     "CANFD_BRS",
                     "GenMsgCycleTime", "GenMsgCycleTimeFast", "GenMsgDelayTime",
                     "GenMsgILSupport", "GenMsgNrOfRepetition", "GenMsgSendType", "GenMsgStartDelayTime",
                     "NmMessage",
                     "DiagMessage", "DiagRequest", "DiagResponse", "DiagState",
                     "VFrameFormat"
                 })
        {
            var v = Resolve(prefer, out var isDef);
            if (v != null) Add(prefer, v, isDef);
        }

        foreach (var kv in _src.Attributes)
        {
            if (!HasBoDef(kv.Key)) continue;
            var v = Resolve(kv.Key, out var isDef);
            if (v != null) Add(kv.Key, v, isDef);
        }

        foreach (var d in _db.AttrDefs.Where(a => a.ObjectType == "BO_"))
        {
            var v = Resolve(d.Name, out var isDef);
            if (v != null) Add(d.Name, v, isDef);
        }

        // Vector category order
        if (canFd.Children.Count > 0) AttributeGroups.Add(canFd);
        if (diagnostics.Children.Count > 0) AttributeGroups.Add(diagnostics);
        if (interaction.Children.Count > 0) AttributeGroups.Add(interaction);
        if (netMgmt.Children.Count > 0) AttributeGroups.Add(netMgmt);
        if (other.Children.Count > 0) AttributeGroups.Add(other);
        if (AttributeGroups.Count == 0)
            AttributeGroups.Add(new EditableAttrGroup { Name = "(no message attributes)" });
    }

    private static string DisplayAttr(string name, string raw)
    {
        // Used by Definition tab TxMethod binding / CommitAttribute — map via common aliases
        if (name.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                "0" => "Cycle",
                "1" => "NoMsgSendType",
                "2" => "IfActive",
                "3" => "Event",
                "4" => "CA",
                "5" => "CE",
                "Cyclic" => "Cycle",
                _ => raw
            };
        }
        if (name is "DiagRequest" or "DiagResponse" or "DiagState" or "NmMessage" or "DiagMessage" or "GenMsgILSupport")
        {
            return raw switch
            {
                "0" => "No",
                "1" => "Yes",
                _ => raw
            };
        }
        return raw;
    }

    public void ApplyTo(DbcMessage m)
    {
        m.Name = Name;
        m.IdFormat = IdFormat;
        m.Dlc = Dlc;
        m.Transmitter = Transmitter;
        m.TxMethod = string.Equals(TxMethod, "n/a", StringComparison.OrdinalIgnoreCase) ||
                     TxMethod == "-"
            ? ""
            : TxMethod;
        m.CycleTime = CycleTime;
        m.Comment = Comment;
        var t = IdText.Trim();
        uint canId = m.CanId;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            canId = Convert.ToUInt32(t, 16) & DbcMessage.CanIdMask;
        else if (uint.TryParse(t, out var v))
            canId = v & DbcMessage.CanIdMask;
        m.Id = DbcMessage.EncodeId(canId, IdFormat);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class MsgSignalRow
{
    public DbcSignal Model { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";
    public string Multiplexing { get; set; } = "-";
    public int StartBit { get; set; }
    public int Length { get; set; }
    public string ByteOrder { get; set; } = "Motorola";
    public string ValueType { get; set; } = "Unsigned";
}

public sealed class MsgNodeRow
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "0x0";
}

