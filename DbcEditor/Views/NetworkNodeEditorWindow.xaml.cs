using System.Collections.ObjectModel;
using System.Windows;
using DbcEditor.Models;

namespace DbcEditor.Views;

/// <summary>
/// Vector: double-click node under Networks / &lt;network&gt; → "Network Node 'xxx'".
/// Tabs: Definition / Network / Node / Attributes.
/// </summary>
public partial class NetworkNodeEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly string _originalName;
    public NetworkNodeEditorVm Vm { get; }

    public NetworkNodeEditorWindow(DbcDatabase db, string nodeName)
    {
        InitializeComponent();
        _db = db;
        _originalName = nodeName;
        Vm = new NetworkNodeEditorVm(db, nodeName);
        DataContext = Vm;
        Title = $"Network Node '{nodeName}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Apply();
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Apply();

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void Apply()
    {
        var node = _db.FindNode(_originalName);
        if (node == null) return;

        var newName = (Vm.NodeName ?? "").Trim();
        if (string.IsNullOrEmpty(newName)) return;

        if (!string.Equals(newName, _originalName, StringComparison.Ordinal))
        {
            node.Name = newName;
            foreach (var m in _db.Messages)
            {
                if (string.Equals(m.Transmitter, _originalName, StringComparison.OrdinalIgnoreCase))
                    m.Transmitter = newName;
            }
            foreach (var s in _db.AllSignals)
            {
                for (int i = 0; i < s.Receivers.Count; i++)
                {
                    if (string.Equals(s.Receivers[i], _originalName, StringComparison.OrdinalIgnoreCase))
                        s.Receivers[i] = newName;
                }
            }
            Title = $"Network Node '{newName}'";
        }
    }
}

public sealed class NetworkNodeEditorVm
{
    private readonly DbcDatabase _db;
    private DbcNode? _node;

    public DbcDatabase Database => _db;
    public string NetworkName { get; set; }
    public string NodeName { get; set; }
    public string Protocol { get; set; } = "CAN";
    public string Address { get; set; } = "0x0";
    public List<string> Protocols { get; } = new() { "CAN", "CAN FD", "J1939" };
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();

    public NetworkNodeEditorVm(DbcDatabase db, string nodeName)
    {
        _db = db;
        NetworkName = db.NetworkName;
        Protocol = string.IsNullOrWhiteSpace(db.Protocol) ? "CAN" : db.Protocol;
        NodeName = nodeName;
        _node = db.FindNode(nodeName);
        Address = _node?.Address ?? "0x0";
        BuildAttributes(db, _node);
    }

    public void CommitAttribute(string name, string value)
    {
        if (_node == null) return;
        var raw = name.Equals("NmNode", StringComparison.OrdinalIgnoreCase)
            ? value.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "1"
              : value.Equals("no", StringComparison.OrdinalIgnoreCase) ? "0" : value
            : value;
        _node.Attributes[name] = raw;
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            item.Value = DisplayBuAttr(name, value);
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.IsDefault = def != null && (
                string.Equals(def.DefaultValue, raw, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(DisplayBuAttr(name, def.DefaultValue), item.Value,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Vector: Networks / &lt;net&gt; / node → Network Node Attributes is empty.
    /// BU_ / CANoe attrs belong to Network Nodes (NodeEditorWindow), not here.
    /// </summary>
    private void BuildAttributes(DbcDatabase db, DbcNode? node)
    {
        AttributeGroups.Clear();
        AttributeGroups.Add(new EditableAttrGroup { Name = "(no node attributes)" });
        _ = db;
        _ = node;
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
        if (name.Contains("Address", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(raw, out var n))
            return $"0x{n:X}";
        return raw;
    }
}
