using System.Collections.ObjectModel;
using System.Windows;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Views;

/// <summary>
/// Vector: ECUs / &lt;name&gt; / &lt;name&gt; → "ECU - Node 'name'"
/// Tabs: Definition | ECU | Node | Attributes
/// </summary>
public partial class EcuNodeEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly string _originalName;
    public EcuNodeEditorVm Vm { get; }

    public EcuNodeEditorWindow(DbcDatabase db, string name)
    {
        InitializeComponent();
        _db = db;
        _originalName = name;
        Vm = new EcuNodeEditorVm(db, name);
        DataContext = Vm;
        Title = $"ECU - Node '{name}'";
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

        node.Address = string.IsNullOrWhiteSpace(Vm.Address) ? "0x0" : Vm.Address.Trim();

        var newEcu = (Vm.EcuName ?? "").Trim();
        var newNode = (Vm.NodeName ?? "").Trim();
        if (string.IsNullOrEmpty(newNode)) newNode = newEcu;
        if (string.IsNullOrEmpty(newEcu)) return;

        // In single-network DBC, ECU name and linked node name stay in sync
        var newName = newNode;
        if (string.Equals(newName, _originalName, StringComparison.Ordinal))
        {
            Title = $"ECU - Node '{newName}'";
            return;
        }

        node.Name = newName;
        foreach (var m in _db.Messages)
        {
            if (string.Equals(m.Transmitter, _originalName, StringComparison.OrdinalIgnoreCase))
                m.Transmitter = newName;
        }
        foreach (var s in _db.AllSignals)
        {
            for (var i = 0; i < s.Receivers.Count; i++)
            {
                if (string.Equals(s.Receivers[i], _originalName, StringComparison.OrdinalIgnoreCase))
                    s.Receivers[i] = newName;
            }
        }
        foreach (var ev in _db.EnvVars)
        {
            for (var i = 0; i < ev.AccessNodes.Count; i++)
            {
                if (string.Equals(ev.AccessNodes[i], _originalName, StringComparison.OrdinalIgnoreCase))
                    ev.AccessNodes[i] = newName;
            }
        }
        Vm.EcuName = newName;
        Vm.NodeName = newName;
        Title = $"ECU - Node '{newName}'";
    }
}

public sealed class EcuNodeEditorVm
{
    private readonly DbcDatabase _db;
    private readonly DbcNode? _node;

    public DbcDatabase Database => _db;
    public string EcuName { get; set; }
    public string NodeName { get; set; }
    public string Address { get; set; } = "0x0";
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();

    public EcuNodeEditorVm(DbcDatabase db, string name)
    {
        _db = db;
        EcuName = name;
        NodeName = name;
        _node = db.FindNode(name);
        Address = _node?.Address ?? "0x0";
        BuildAttributes();
    }

    public void CommitAttribute(string name, string value)
    {
        if (_node == null) return;
        _node.Attributes[name] = value;
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType == "BU_" &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.Value = AttributeValueDisplay.Format(def, value);
            item.IsDefault = AttributeValueDisplay.ValuesEqual(def, item.Value, def?.DefaultValue);
        }
    }

    /// <summary>
    /// Vector: ECUs / Name / Name → ECU-Node Attributes is empty.
    /// BU_ / CANoe attrs belong to Network Nodes (NodeEditorWindow), not here.
    /// </summary>
    private void BuildAttributes()
    {
        AttributeGroups.Clear();
        AttributeGroups.Add(new EditableAttrGroup { Name = "(no node attributes)" });
    }
}
