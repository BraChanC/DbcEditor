using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DbcEditor.Models;
using DbcEditor.Controls;

namespace DbcEditor.Views;

/// <summary>
/// Vector: ECUs / &lt;name&gt; → ECU 'name'
/// Tabs: Definition | Nodes | Env. variables | Attributes | Comment
/// </summary>
public partial class EcuEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly string _originalName;
    public EcuEditorVm Vm { get; }

    public EcuEditorWindow(DbcDatabase db, string ecuName)
    {
        InitializeComponent();
        _db = db;
        _originalName = ecuName;
        Vm = new EcuEditorVm(db, ecuName);
        DataContext = Vm;
        Title = $"ECU '{ecuName}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ApplyChanges();
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => ApplyChanges();

    private void OnViewNode(object sender, RoutedEventArgs e) => OpenLinkedNode();

    private void OnLinkedNodeDoubleClick(object sender, MouseButtonEventArgs e) => OpenLinkedNode();

    private void OnAddEnvVar(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.EnvVariables.Select(x => x.Name).ToList();
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.EnvVariables, excludeEnvNames: exclude)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || dlg.SelectedEnvVars.Count == 0) return;
        Vm.AddEnvVars(dlg.SelectedEnvVars);
    }

    private void OnRemoveEnvVar(object sender, RoutedEventArgs e) => Vm.RemoveSelectedEnvVar();

    private void OnViewEnvVar(object sender, RoutedEventArgs e) => OpenSelectedEnvVar();

    private void OnEnvVarDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedEnvVar();

    private void OpenSelectedEnvVar()
    {
        var ev = Vm.SelectedEnvVariable?.Model;
        if (ev == null) return;
        var dlg = new EnvVarEditorWindow(_db, ev) { Owner = this };
        if (dlg.ShowDialog() == true)
            Vm.RefreshEnvVars();
    }

    private void OnAttributeCommitted(object sender, AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OpenLinkedNode()
    {
        var name = Vm.SelectedLinkedNode?.Name ?? Vm.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        // Vector: ECU Nodes tab View… → ECU - Node 'name'
        var dlg = new EcuNodeEditorWindow(_db, name) { Owner = this };
        dlg.ShowDialog();
    }

    private void ApplyChanges()
    {
        var node = _db.FindNode(_originalName);
        if (node == null) return;

        var newName = (Vm.Name ?? "").Trim();
        if (string.IsNullOrEmpty(newName)) return;

        node.Comment = Vm.Comment ?? "";

        if (string.Equals(newName, _originalName, StringComparison.Ordinal))
            return;

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
        foreach (var ev in _db.EnvVars)
        {
            for (int i = 0; i < ev.AccessNodes.Count; i++)
            {
                if (string.Equals(ev.AccessNodes[i], _originalName, StringComparison.OrdinalIgnoreCase))
                    ev.AccessNodes[i] = newName;
            }
        }
        if (Vm.LinkedNodes.Count > 0)
        {
            Vm.LinkedNodes[0].Name = newName;
            Vm.SelectedLinkedNode = Vm.LinkedNodes[0];
        }
        Title = $"ECU '{newName}'";
    }
}

public sealed class EcuEditorVm : INotifyPropertyChanged
{
    private readonly DbcDatabase _db;
    private readonly string _ecuName;
    private EcuEnvRow? _selectedEnv;

    public DbcDatabase Database => _db;
    public string Name { get; set; }
    public string Comment { get; set; } = "";
    public ObservableCollection<EcuLinkedNodeRow> LinkedNodes { get; } = new();
    public ObservableCollection<EcuEnvRow> EnvVariables { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();
    public EcuLinkedNodeRow? SelectedLinkedNode { get; set; }

    public EcuEnvRow? SelectedEnvVariable
    {
        get => _selectedEnv;
        set
        {
            _selectedEnv = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedEnv));
        }
    }

    public bool HasSelectedEnv => SelectedEnvVariable != null;

    public EcuEditorVm(DbcDatabase db, string ecuName)
    {
        _db = db;
        _ecuName = ecuName;
        Name = ecuName;
        var node = db.FindNode(ecuName);
        Comment = node?.Comment ?? "";

        var row = new EcuLinkedNodeRow
        {
            Name = ecuName,
            Address = node?.Address ?? "0x0"
        };
        LinkedNodes.Add(row);
        SelectedLinkedNode = row;

        ReloadEnvVars();
        BuildAttributes(db, node);
    }

    public void RefreshEnvVars() => ReloadEnvVars();

    private void ReloadEnvVars()
    {
        EnvVariables.Clear();
        foreach (var ev in _db.EnvVars.Where(e =>
                     e.AccessNodes.Any(n => string.Equals(n, _ecuName, StringComparison.OrdinalIgnoreCase))))
        {
            EnvVariables.Add(new EcuEnvRow
            {
                Model = ev,
                Name = ev.Name,
                Type = EvTypeName(ev.Type),
                Unit = ev.Unit
            });
        }
    }

    public void AddEnvVars(IEnumerable<DbcEnvVar> vars)
    {
        foreach (var ev in vars)
        {
            if (!ev.AccessNodes.Any(n => string.Equals(n, _ecuName, StringComparison.OrdinalIgnoreCase)))
                ev.AccessNodes.Add(_ecuName);
        }
        ReloadEnvVars();
    }

    public void RemoveSelectedEnvVar()
    {
        if (SelectedEnvVariable?.Model == null) return;
        SelectedEnvVariable.Model.AccessNodes.RemoveAll(n =>
            string.Equals(n, _ecuName, StringComparison.OrdinalIgnoreCase));
        SelectedEnvVariable = null;
        ReloadEnvVars();
    }

    private static string EvTypeName(int type) => type switch
    {
        0 => "Integer",
        1 => "Float",
        2 => "String",
        3 => "Data",
        _ => type.ToString()
    };

    public void CommitAttribute(string name, string value)
    {
        // ECU-level attrs rarely used; keep hook for panel.
        _ = name;
        _ = value;
    }

    private void BuildAttributes(DbcDatabase db, DbcNode? node)
    {
        // Vector ECU Attributes empty for typical DBC (BU_ attrs belong to Node).
        AttributeGroups.Clear();
        AttributeGroups.Add(new EditableAttrGroup { Name = "(no ECU attributes)" });
        _ = db;
        _ = node;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class EcuLinkedNodeRow
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "0x0";
}

public sealed class EcuEnvRow
{
    public DbcEnvVar? Model { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Unit { get; set; } = "";
}

