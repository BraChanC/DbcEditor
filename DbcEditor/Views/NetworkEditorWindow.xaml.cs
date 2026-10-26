using System.Collections.ObjectModel;
using System.Windows;
using DbcEditor.Models;

namespace DbcEditor.Views;

public partial class NetworkEditorWindow : Window
{
    private readonly DbcDatabase _db;
    public NetworkEditorVm Vm { get; }

    public NetworkEditorWindow(DbcDatabase db)
    {
        InitializeComponent();
        _db = db;
        Vm = new NetworkEditorVm(db);
        DataContext = Vm;
        Title = $"Network '{db.NetworkName}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.Apply(_db);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Vm.Apply(_db);

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);
}

public sealed class NetworkEditorVm
{
    private readonly DbcDatabase _db;

    public DbcDatabase Database => _db;
    public string Name { get; set; }
    public string Protocol { get; set; } = "CAN";
    public string Comment { get; set; } = "";
    public List<string> Protocols { get; } = new() { "CAN", "CAN FD", "J1939" };
    public ObservableCollection<NetworkNodeRow> Nodes { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();

    public NetworkEditorVm(DbcDatabase db)
    {
        _db = db;
        Name = db.NetworkName;
        Protocol = string.IsNullOrWhiteSpace(db.Protocol) ? "CAN" : db.Protocol;
        Comment = db.Comment ?? "";
        foreach (var n in db.Nodes)
            Nodes.Add(new NetworkNodeRow { Name = n.Name, Address = n.Address });
        BuildAttributes(db);
    }

    public void CommitAttribute(string name, string value)
    {
        _db.NetworkAttributes[name] = value;
        foreach (var g in AttributeGroups)
        foreach (var item in g.Children)
        {
            if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            item.Value = value;
            var def = _db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType.Length == 0 &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            item.IsDefault = def != null &&
                string.Equals(def.DefaultValue, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void BuildAttributes(DbcDatabase db)
    {
        AttributeGroups.Clear();
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var netMgmt = new EditableAttrGroup { Name = "Net Management" };
        var other = new EditableAttrGroup { Name = "No category assigned" };

        void Add(string name, string value, bool isDefault, EditableAttrGroup group)
        {
            if (string.IsNullOrEmpty(name) || name == "CM_") return;
            if (!shown.Add(name)) return;
            var display = FormatAttrValue(name, value);
            group.Children.Add(EditableAttrItem.FromDef(db, "", name, display, isDefault));
        }

        string? Resolve(string name, out bool isDefault)
        {
            isDefault = false;
            if (db.NetworkAttributes.TryGetValue(name, out var v) && v != null)
                return v;
            var def = db.AttrDefs.FirstOrDefault(a =>
                a.ObjectType.Length == 0 &&
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(a.DefaultValue));
            if (def != null)
            {
                isDefault = true;
                return def.DefaultValue;
            }
            return null;
        }

        foreach (var nm in new[] { "NmBaseAddress", "NmMessageCount" })
        {
            var v = Resolve(nm, out var isDef);
            if (v != null) Add(nm, v, isDef, netMgmt);
        }

        foreach (var kv in db.NetworkAttributes)
        {
            if (kv.Key is "CM_" or "NmBaseAddress" or "NmMessageCount") continue;
            Add(kv.Key, kv.Value, false,
                kv.Key.StartsWith("Nm", StringComparison.OrdinalIgnoreCase) ? netMgmt : other);
        }

        foreach (var d in db.AttrDefs)
        {
            if (d.ObjectType.Length != 0 || string.IsNullOrEmpty(d.Name) ||
                string.IsNullOrEmpty(d.DefaultValue))
                continue;
            if (ShouldSkipNetworkDefault(d.Name)) continue;
            Add(d.Name, d.DefaultValue, true,
                d.Name.StartsWith("Nm", StringComparison.OrdinalIgnoreCase) ? netMgmt : other);
        }

        if (netMgmt.Children.Count > 0) AttributeGroups.Add(netMgmt);
        if (other.Children.Count > 0) AttributeGroups.Add(other);
        if (AttributeGroups.Count == 0)
            AttributeGroups.Add(new EditableAttrGroup { Name = "(no network attributes)" });
    }

    private static bool ShouldSkipNetworkDefault(string name) =>
        name.StartsWith("GenMsg", StringComparison.Ordinal) ||
        name.StartsWith("GenSig", StringComparison.Ordinal) ||
        name.StartsWith("CANoe", StringComparison.Ordinal) ||
        name is "DiagState" or "DiagRequest" or "DiagResponse" or "NmMessage"
            or "VFrameFormat" or "CANFD_BRS" or "ECU" or "NodeLayerModules";

    private static string FormatAttrValue(string name, string value)
    {
        if (name.Contains("Address", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(value, out var n))
            return $"0x{n:X}";
        return value;
    }

    public void Apply(DbcDatabase db)
    {
        db.NetworkName = string.IsNullOrWhiteSpace(Name) ? "Network" : Name.Trim();
        db.Protocol = Protocol ?? "CAN";
        db.Comment = Comment ?? "";
        db.NetworkAttributes["BusType"] = db.Protocol;
        db.NetworkAttributes["CM_"] = db.Comment;
    }
}

public sealed class NetworkNodeRow
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "0x0";
}
