using System.Collections.ObjectModel;
using System.Windows;
using DbcEditor.Models;

namespace DbcEditor.Views;

/// <summary>
/// Vector: Networks / Tx Messages / msg  →  "Node - Tx Message 'Name (0xID)'"
/// Tabs: Definition / Node / Message / Attributes.
/// </summary>
public partial class NodeTxMessageEditorWindow : Window
{
    private readonly DbcMessage _model;
    public NodeTxMessageEditorVm Vm { get; }

    public NodeTxMessageEditorWindow(DbcDatabase db, DbcMessage message, string? nodeName = null)
    {
        InitializeComponent();
        _model = message;
        var node = nodeName ?? message.Transmitter;
        Vm = new NodeTxMessageEditorVm(db, message, node);
        DataContext = Vm;
        Title = $"Node - Tx Message '{message.Name} ({message.IdHex})'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Vm.ApplyTo(_model);

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);
}

public sealed class NodeTxMessageEditorVm
{
    private readonly DbcDatabase _db;
    private readonly DbcMessage _msg;

    public DbcDatabase Database => _db;
    public string NodeName { get; set; }
    public string NodeAddress { get; set; } = "0x0";
    public string MessageName { get; set; }
    public string IdText { get; set; }
    public string IdFormat { get; set; }
    public string Dlc { get; set; }
    public string Transmitter { get; set; }
    public string TxMethod { get; set; }
    public string CycleTime { get; set; }
    public List<string> IdFormats { get; } = new()
    {
        "CAN Standard", "CAN Extended", "CAN FD Standard", "CAN FD Extended"
    };
    public List<string> TxMethods { get; } = new() { "Cyclic", "Event", "CE", "NoMsgSendType", "n/a" };
    public List<string> Transmitters { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();

    public NodeTxMessageEditorVm(DbcDatabase db, DbcMessage m, string nodeName)
    {
        _db = db;
        _msg = m;
        NodeName = nodeName;
        var node = db.FindNode(nodeName);
        NodeAddress = node?.Address ?? "0x0";

        MessageName = m.Name;
        IdText = m.IdHex;
        IdFormat = m.IdFormat;
        Dlc = m.Dlc.ToString();
        Transmitter = m.Transmitter;
        TxMethod = string.IsNullOrWhiteSpace(m.TxMethod) ? "n/a" : m.TxMethod;
        CycleTime = string.IsNullOrWhiteSpace(m.CycleTime) ? "0" : m.CycleTime;

        foreach (var n in db.Nodes)
            Transmitters.Add(n.Name);
        if (!Transmitters.Contains(Transmitter) && !string.IsNullOrEmpty(Transmitter))
            Transmitters.Add(Transmitter);

        BuildAttributes(db, m);
    }

    public void CommitAttribute(string name, string value)
    {
        _msg.Attributes[name] = value;
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

    private void BuildAttributes(DbcDatabase db, DbcMessage m)
    {
        AttributeGroups.Clear();
        var group = new EditableAttrGroup { Name = "No category assigned" };
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string value, bool isDefault)
        {
            if (string.IsNullOrEmpty(name) || !shown.Add(name)) return;
            var display = DisplayAttr(name, value);
            group.Children.Add(EditableAttrItem.FromDef(db, "BO_", name, display, isDefault));
        }

        string? Resolve(string name, out bool isDefault)
        {
            isDefault = false;
            if (m.Attributes.TryGetValue(name, out var v) && v != null)
                return v;
            var def = db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BO_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(a.DefaultValue));
            if (def != null)
            {
                isDefault = true;
                return def.DefaultValue;
            }
            return null;
        }

        foreach (var prefer in new[]
                 {
                     "GenMsgSendType", "GenMsgCycleTime", "GenMsgDelayTime", "GenMsgStartDelayTime",
                     "GenMsgILSupport", "VFrameFormat", "CANFD_BRS", "NmMessage", "DiagState"
                 })
        {
            var v = Resolve(prefer, out var isDef);
            if (v != null) Add(prefer, v, isDef);
        }

        foreach (var kv in m.Attributes)
            Add(kv.Key, kv.Value, false);

        foreach (var d in db.AttrDefs.Where(a => a.ObjectType == "BO_"))
        {
            var v = Resolve(d.Name, out var isDef);
            if (v != null) Add(d.Name, v, isDef);
        }

        if (group.Children.Count > 0) AttributeGroups.Add(group);
        else AttributeGroups.Add(new EditableAttrGroup { Name = "(no message attributes)" });
    }

    private static string DisplayAttr(string name, string raw)
    {
        if (name.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                "0" => "Cyclic",
                "1" => "Event",
                "2" => "CE",
                "3" => "NoMsgSendType",
                _ => raw
            };
        }
        if (name.Equals("VFrameFormat", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                "0" => "CAN Standard",
                "1" => "CAN Extended",
                "14" => "CAN FD Standard",
                "15" => "CAN FD Extended",
                _ => raw
            };
        }
        return raw;
    }

    public void ApplyTo(DbcMessage m)
    {
        m.Name = MessageName ?? m.Name;
        m.IdFormat = IdFormat;
        m.Transmitter = Transmitter;
        m.TxMethod = string.Equals(TxMethod, "n/a", StringComparison.OrdinalIgnoreCase) ||
                     TxMethod == "-"
            ? ""
            : TxMethod;
        m.CycleTime = CycleTime ?? "";
        if (int.TryParse(Dlc, out var dlc)) m.Dlc = dlc;
        var t = (IdText ?? "").Trim();
        uint canId = m.CanId;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            canId = Convert.ToUInt32(t, 16) & DbcMessage.CanIdMask;
        else if (uint.TryParse(t, out var id))
            canId = id & DbcMessage.CanIdMask;
        m.Id = DbcMessage.EncodeId(canId, IdFormat);
    }
}
