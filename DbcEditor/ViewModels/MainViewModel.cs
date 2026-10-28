using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbcEditor.Models;
using DbcEditor.Services;
using DbcEditor.Views;
using Microsoft.Win32;

namespace DbcEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DbcAntlrLoader _loader = new();
    private DbcDatabase? _db;

    [ObservableProperty] private string _title = "Vector CANdb++ Editor (ANTLR4)";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private TreeItemViewModel? _selectedTreeItem;
    [ObservableProperty] private object? _selectedDetail;
    [ObservableProperty] private string _detailMode = "Messages";

    public ObservableCollection<TreeItemViewModel> TreeRoots { get; } = new();
    public ObservableCollection<MessageRowViewModel> MessageRows { get; } = new();
    public ObservableCollection<SignalRowViewModel> SignalRows { get; } = new();
    public ObservableCollection<EnvVarRowViewModel> EnvVarRows { get; } = new();
    public ObservableCollection<ValueTableRowViewModel> ValueTableRows { get; } = new();

    partial void OnSelectedTreeItemChanged(TreeItemViewModel? value) => ApplyTreeSelection(value);

    [RelayCommand]
    private void Open()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CANdb++ Database (*.dbc)|*.dbc|All files (*.*)|*.*",
            Title = "Open DBC"
        };
        if (dlg.ShowDialog() != true) return;
        LoadPath(dlg.FileName);
    }

    [RelayCommand]
    private void CreateDatabase()
    {
        var owner = Application.Current?.MainWindow;
        var templateDlg = new TemplateSelectWindow { Owner = owner };
        if (templateDlg.ShowDialog() != true || templateDlg.SelectedTemplate == null)
            return;

        var template = templateDlg.SelectedTemplate;
        var saveDlg = new SaveFileDialog
        {
            Filter = "CANdb++ Database (*.dbc)|*.dbc|All files (*.*)|*.*",
            Title = "Create Database",
            FileName = template.Name,
            AddExtension = true,
            DefaultExt = ".dbc",
            OverwritePrompt = true
        };
        if (saveDlg.ShowDialog() != true) return;

        try
        {
            var dest = saveDlg.FileName;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(template.FullPath, dest, overwrite: true);
            LoadPath(dest);
            StatusText = $"Created {dest}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Create Database failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Create failed";
        }
    }

    [RelayCommand]
    private void Reload()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            StatusText = "No file to reload";
            return;
        }
        LoadPath(FilePath);
    }

    private bool CanSave() => _db != null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => DoSave(promptIfNoPath: string.IsNullOrEmpty(FilePath));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAs() => DoSave(promptIfNoPath: true);

    private bool DoSave(bool promptIfNoPath)
    {
        if (_db == null) return false;
        var path = FilePath;
        if (promptIfNoPath || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CANdb++ Database (*.dbc)|*.dbc|All files (*.*)|*.*",
                FileName = string.IsNullOrEmpty(path)
                    ? $"{_db.NetworkName}.dbc"
                    : Path.GetFileName(path),
                Title = "Save DBC As"
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }

        try
        {
            var text = DbcWriter.Write(_db);
            DbcTextEncoding.WriteAllText(path, text, encodingHintPath: FilePath);
            FilePath = path;
            _db.FilePath = path;
            Title = $"Vector CANdb++ Editor (ANTLR4) - {path}";
            StatusText = $"Saved {path}";
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Save failed";
            return false;
        }
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedDetail is MessageRowViewModel row)
        {
            OpenMessage(row.Model);
            return;
        }
        if (SelectedDetail is SignalRowViewModel srow)
        {
            if (srow.CatalogAttributes)
                OpenSignal(srow.Model, catalogAttributes: true);
            else
                OpenMessageSignal(srow.Model);
            return;
        }
        if (SelectedDetail is EnvVarRowViewModel erow)
        {
            OpenEnvVar(erow.Model);
            return;
        }
        if (SelectedDetail is ValueTableRowViewModel vtrow)
        {
            if (vtrow.Signal != null) OpenValueTable(vtrow.Signal);
            else if (vtrow.EnvVar != null) OpenValueTable(vtrow.EnvVar);
            return;
        }
        if (SelectedDetail is DbcSignal sig)
        {
            OpenSignal(sig, catalogAttributes: SelectedTreeItem?.Kind == "Signal");
            return;
        }
        if (SelectedTreeItem != null)
            ActivateTreeItem(SelectedTreeItem);
    }

    [RelayCommand]
    private void EditSelectedMessage() => EditSelected();

    public void LoadPath(string path)
    {
        try
        {
            _db = _loader.Load(path);
            FilePath = path;
            Title = $"Vector CANdb++ Editor - {path}";
            RebuildTree();
            ShowMessages(_db.Messages);
            StatusText = $"{_db.Messages.Count} Message(s)  |  {_db.Nodes.Count} Node(s)  |  Ready";
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DBC load failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Load failed";
        }
    }

    public void ActivateTreeItem(TreeItemViewModel item)
    {
        if (_db == null) return;
        switch (item.Kind)
        {
            case "Network":
                OpenNetwork();
                break;
            case "Ecu":
                OpenEcu(item.Payload as string ?? item.Title);
                break;
            case "EcuNode":
                // Vector: ECUs / Name / Name → ECU - Node 'Name'
                OpenEcuNode(item.Payload as string ?? item.Title);
                break;
            case "EcuUnderNet":
                // Vector: under Networks/<net>/ — "Network Node 'xxx'"
                OpenNetworkNode(item.Payload as string ?? item.Title);
                break;
            case "Node":
                OpenNode(item.Payload as string ?? item.Title);
                break;
            case "NetworkTxMessage":
            case "NodeTxMessage":
                if (item.Payload is DbcMessage txMsg)
                    OpenNodeTxMessage(txMsg);
                break;
            case "Message":
            case "NodeRxMessage":
                if (item.Payload is DbcMessage msg)
                    OpenMessage(msg);
                break;
            case "SignalGroup":
                if (item.Payload is DbcSignalGroup grp)
                    OpenSignalGroup(grp);
                break;
            case "Signal":
                // Vector Signals tree → catalog Attributes (GenSig*)
                if (item.Payload is DbcSignal catalogSig)
                    OpenSignal(catalogSig, catalogAttributes: true);
                break;
            case "MessageSignal":
            case "NodeTxSignal":
            case "NodeRxSignal":
                // Vector: message-mapped signal → Message Signal dialog
                if (item.Payload is DbcSignal ctxSig)
                    OpenMessageSignal(ctxSig);
                break;
            case "VtSig":
                if (item.Payload is DbcSignal vts)
                    OpenValueTable(vts);
                break;
            case "EnvVar":
                if (item.Payload is DbcEnvVar env)
                    OpenEnvVar(env);
                break;
            case "VtEv":
                if (item.Payload is DbcEnvVar vte)
                    OpenValueTable(vte);
                break;
        }
    }

    private bool _openingValueTable;

    private void OpenValueTable(DbcSignal signal)
    {
        if (_db == null || _openingValueTable) return;
        _openingValueTable = true;
        try
        {
            var dlg = new ValueTableEditorWindow(signal) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                RebuildTree();
                ShowValueTables(new ValueTableRowViewModel(signal));
                StatusText = $"Value Table 'VtSig_{signal.Name}' updated";
            }
        }
        finally { _openingValueTable = false; }
    }

    private void OpenValueTable(DbcEnvVar env)
    {
        if (_db == null || _openingValueTable) return;
        _openingValueTable = true;
        try
        {
            var dlg = new ValueTableEditorWindow(env) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                RebuildTree();
                ShowValueTables(new ValueTableRowViewModel(env));
                StatusText = $"Value Table 'VtEv_{env.Name}' updated";
            }
        }
        finally { _openingValueTable = false; }
    }

    private bool _openingEnvVar;

    private void OpenEnvVar(DbcEnvVar model)
    {
        if (_db == null || _openingEnvVar) return;
        _openingEnvVar = true;
        try
        {
            var dlg = new EnvVarEditorWindow(_db, model) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                RebuildTree();
                ShowEnvVars(new[] { model });
                StatusText = $"Environment Variable '{model.Name}' updated";
            }
        }
        finally
        {
            _openingEnvVar = false;
        }
    }

    private void OpenNetwork()
    {
        if (_db == null) return;
        var dlg = new NetworkEditorWindow(_db) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            RebuildTree();
            StatusText = $"Network '{_db.NetworkName}' updated";
        }
    }

    private void OpenEcu(string name)
    {
        if (_db == null || string.IsNullOrWhiteSpace(name)) return;
        var dlg = new EcuEditorWindow(_db, name) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            RebuildTree();
            ApplyTreeSelection(SelectedTreeItem);
        }
    }

    private void OpenEcuNode(string name)
    {
        if (_db == null || string.IsNullOrWhiteSpace(name)) return;
        var dlg = new EcuNodeEditorWindow(_db, name) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            RebuildTree();
            ApplyTreeSelection(SelectedTreeItem);
            StatusText = $"ECU - Node '{name}' updated";
        }
    }

    private void OpenNetworkNode(string nodeName)
    {
        // Vector Networks / <net> / node → Network Node 'name'
        if (_db == null || string.IsNullOrWhiteSpace(nodeName)) return;
        var dlg = new NetworkNodeEditorWindow(_db, nodeName) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            RebuildTree();
            ApplyTreeSelection(SelectedTreeItem);
        }
    }

    private void OpenNode(string nodeName)
    {
        if (_db == null || string.IsNullOrWhiteSpace(nodeName)) return;
        var dlg = new NodeEditorWindow(_db, nodeName) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            RebuildTree();
            ApplyTreeSelection(SelectedTreeItem);
            StatusText = $"Node '{nodeName}' updated";
        }
    }

    private void OpenNodeTxMessage(DbcMessage model)
    {
        if (_db == null) return;
        var dlg = new NodeTxMessageEditorWindow(_db, model) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        RebuildTree();
        RefreshDetailAfterEdit();
    }

    private void OpenMessage(DbcMessage model)
    {
        if (_db == null) return;
        var dlg = new MessageEditorWindow(_db, model) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        RefreshDetailAfterEdit();
    }

    private void OpenSignal(DbcSignal model, bool catalogAttributes = false)
    {
        if (_db == null) return;
        var owner = ResolveOwnerMessage(model);
        var dlg = new SignalEditorWindow(_db, model, owner, catalogAttributes)
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
        RefreshDetailAfterEdit();
    }

    private void OpenSignalGroup(DbcSignalGroup group)
    {
        if (_db == null) return;
        var owner = _db.Messages.FirstOrDefault(m => SameMessageId(group.MessageId, m));
        var dlg = new SignalGroupEditorWindow(group, owner) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && SelectedTreeItem?.Payload == group)
            SelectedTreeItem.Title = group.Name;
        RefreshDetailAfterEdit();
    }

    /// <summary>Vector Overall View / Messages: Message Signal 'Msg::Sig' editor.</summary>
    private void OpenMessageSignal(DbcSignal model)
    {
        if (_db == null) return;
        var owner = ResolveOwnerMessage(model);
        if (owner == null)
        {
            OpenSignal(model, catalogAttributes: false);
            return;
        }

        var dlg = new MessageSignalEditorWindow(_db, owner, model)
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
        RefreshDetailAfterEdit();
    }

    private DbcMessage? ResolveOwnerMessage(DbcSignal model)
    {
        if (_db == null) return null;
        return _db.Messages.FirstOrDefault(m => m.Name == model.MessageName)
               ?? _db.Messages.FirstOrDefault(m => m.Id == model.MessageId && model.MessageId != 0)
               ?? _db.Messages.FirstOrDefault(m => m.Signals.Contains(model));
    }

    private void RefreshDetailAfterEdit() => ApplyTreeSelection(SelectedTreeItem);

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (_db == null) return;

        // Vector sorts nodes alphabetically in the tree
        var sortedNodes = _db.Nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Networks / <name> / Tx Messages + Signals + node names
        // Vector: Tx Messages / Signals under network are leaves (no expand); click → right list
        var networks = new TreeItemViewModel { Title = "Networks", Kind = "Root" };
        var net = new TreeItemViewModel { Title = _db.NetworkName, Kind = "Network", Payload = _db };
        net.Children.Add(new TreeItemViewModel { Title = "Tx Messages", Kind = "TxMessages", Payload = _db });
        net.Children.Add(new TreeItemViewModel { Title = "Signals", Kind = "NetSignalsRoot", Payload = _db });

        foreach (var n in sortedNodes)
            net.Children.Add(new TreeItemViewModel { Title = n.Name, Kind = "EcuUnderNet", Payload = n.Name });
        networks.Children.Add(net);

        // Vector: ECUs / ADCU / ADCU  (folder expands to same-named linked node)
        var ecus = new TreeItemViewModel { Title = "ECUs", Kind = "EcuRoot" };
        foreach (var n in sortedNodes)
        {
            var ecuFolder = new TreeItemViewModel { Title = n.Name, Kind = "Ecu", Payload = n.Name };
            ecuFolder.Children.Add(new TreeItemViewModel
            {
                Title = n.Name,
                Kind = "EcuNode",
                Payload = n.Name
            });
            ecus.Children.Add(ecuFolder);
        }

        // Environment variables with VAL_ children (Vector expand arrow)
        var env = new TreeItemViewModel { Title = "Environment variables", Kind = "Env" };
        foreach (var ev in _db.EnvVars.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            env.Children.Add(BuildEnvTreeItem(ev));

        var nodes = new TreeItemViewModel { Title = "Network nodes", Kind = "NodesRoot" };
        foreach (var n in sortedNodes)
            nodes.Children.Add(BuildNodeBranch(n.Name));

        // Top-level Messages — each message listed (0xIDx for extended)
        var messages = new TreeItemViewModel { Title = "Messages", Kind = "Messages", Payload = _db };
        foreach (var m in _db.Messages.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            messages.Children.Add(BuildMessageTreeItem(m, "Message"));

        // Top-level Signals — VAL_ signals expand to VtSig_* (no separate Value Tables root)
        var signals = new TreeItemViewModel { Title = "Signals", Kind = "AllSignals", Payload = _db };
        foreach (var s in _db.AllSignals.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            signals.Children.Add(BuildSignalTreeItem(s));

        TreeRoots.Add(networks);
        TreeRoots.Add(ecus);
        // Vector hides Environment variables when the DBC has none (e.g. Large.dbc)
        if (env.Children.Count > 0)
            TreeRoots.Add(env);
        TreeRoots.Add(nodes);
        TreeRoots.Add(messages);
        TreeRoots.Add(signals);
    }

    private TreeItemViewModel BuildMessageTreeItem(DbcMessage m, string kind)
    {
        var item = new TreeItemViewModel { Title = m.TreeTitle, Kind = kind, Payload = m };
        var groups = _db == null
            ? new List<DbcSignalGroup>()
            : _db.SignalGroups.Where(g => SameMessageId(g.MessageId, m)).ToList();
        var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var name in g.SignalNames)
                grouped.Add(name);

        // Vector lists signals directly under the message in reverse SG_ order.
        // Members inside a Signal Group stay in Windows name order below.
        for (int i = m.Signals.Count - 1; i >= 0; i--)
        {
            var s = m.Signals[i];
            if (!grouped.Contains(s.Name))
                item.Children.Add(BuildSignalTreeItem(s, "MessageSignal"));
        }
        foreach (var g in groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var groupNode = new TreeItemViewModel
            {
                Title = g.Name,
                Kind = "SignalGroup",
                Payload = g
            };
            foreach (var name in g.SignalNames.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            {
                var sig = m.Signals.FirstOrDefault(s =>
                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (sig != null)
                    groupNode.Children.Add(BuildSignalTreeItem(sig, "MessageSignal"));
            }
            item.Children.Add(groupNode);
        }
        return item;
    }

    private static bool SameMessageId(uint groupId, DbcMessage m) =>
        groupId == m.Id || (groupId & DbcMessage.CanIdMask) == m.CanId;

    private List<DbcSignal> SignalsInGroup(DbcSignalGroup grp)
    {
        var list = new List<DbcSignal>();
        if (_db == null) return list;
        var msg = _db.Messages.FirstOrDefault(m => SameMessageId(grp.MessageId, m));
        if (msg == null) return list;
        foreach (var name in grp.SignalNames.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
        {
            var sig = msg.Signals.FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (sig != null) list.Add(sig);
        }
        return list;
    }

    private static TreeItemViewModel BuildSignalTreeItem(DbcSignal s, string kind = "Signal")
    {
        var item = new TreeItemViewModel { Title = s.Name, Kind = kind, Payload = s };
        // Messages stop at the signal. The Signals catalog still expands to VtSig_*.
        if (kind == "Signal" && s.ValueDescriptions.Count > 0)
        {
            item.Children.Add(new TreeItemViewModel
            {
                Title = $"VtSig_{s.Name}",
                Kind = "VtSig",
                Payload = s
            });
        }
        return item;
    }

    private static TreeItemViewModel BuildEnvTreeItem(DbcEnvVar ev)
    {
        var item = new TreeItemViewModel { Title = ev.Name, Kind = "EnvVar", Payload = ev };
        if (ev.ValueDescriptions.Count > 0)
        {
            item.Children.Add(new TreeItemViewModel
            {
                Title = $"VtEv_{ev.Name}",
                Kind = "VtEv",
                Payload = ev
            });
        }
        return item;
    }

    /// <summary>
    /// Vector Networks/Signals/&lt;node&gt;: Tx message signals + Rx signals (duplicates allowed).
    /// </summary>
    private List<DbcSignal> SignalsForNetNode(string nodeName)
    {
        var list = new List<DbcSignal>();
        if (_db == null) return list;
        foreach (var m in _db.Messages.Where(x =>
                     string.Equals(x.Transmitter, nodeName, StringComparison.OrdinalIgnoreCase)))
            list.AddRange(m.Signals);
        foreach (var s in _db.AllSignals.Where(s =>
                     s.Receivers.Any(r => string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase))))
            list.Add(s);
        return list;
    }

    /// <summary>
    /// Vector Network nodes / &lt;node&gt;:
    /// Tx Messages | Rx Messages | Mapped Tx Signals | Mapped Rx Signals,
    /// each expandable to message/signal leaves when non-empty.
    /// </summary>
    private TreeItemViewModel BuildNodeBranch(string nodeName)
    {
        var node = new TreeItemViewModel { Title = nodeName, Kind = "Node", Payload = nodeName };

        var txMsgFolder = new TreeItemViewModel { Title = "Tx Messages", Kind = "NodeTxFolder", Payload = nodeName };
        foreach (var m in _db!.Messages
                     .Where(x => string.Equals(x.Transmitter, nodeName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            txMsgFolder.Children.Add(new TreeItemViewModel
            {
                Title = m.TreeTitle,
                Kind = "NodeTxMessage",
                Payload = m
            });
        }
        node.Children.Add(txMsgFolder);

        var rxMsgFolder = new TreeItemViewModel { Title = "Rx Messages", Kind = "NodeRxFolder", Payload = nodeName };
        foreach (var m in _db.Messages
                     .Where(x => x.Signals.Any(sig =>
                         sig.Receivers.Any(r => string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase))))
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            rxMsgFolder.Children.Add(new TreeItemViewModel
            {
                Title = m.TreeTitle,
                Kind = "NodeRxMessage",
                Payload = m
            });
        }
        node.Children.Add(rxMsgFolder);

        var txSigFolder = new TreeItemViewModel { Title = "Mapped Tx Signals", Kind = "NodeTxSigFolder", Payload = nodeName };
        foreach (var s in _db.Messages
                     .Where(x => string.Equals(x.Transmitter, nodeName, StringComparison.OrdinalIgnoreCase))
                     .SelectMany(x => x.Signals)
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            txSigFolder.Children.Add(new TreeItemViewModel
            {
                Title = s.Name,
                Kind = "NodeTxSignal",
                Payload = s
            });
        }
        node.Children.Add(txSigFolder);

        var rxSigFolder = new TreeItemViewModel { Title = "Mapped Rx Signals", Kind = "NodeRxSigFolder", Payload = nodeName };
        foreach (var s in _db.AllSignals
                     .Where(s => s.Receivers.Any(r =>
                         string.Equals(r, nodeName, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            rxSigFolder.Children.Add(new TreeItemViewModel
            {
                Title = s.Name,
                Kind = "NodeRxSignal",
                Payload = s
            });
        }
        node.Children.Add(rxSigFolder);

        return node;
    }

    private void ApplyTreeSelection(TreeItemViewModel? item)
    {
        if (item == null || _db == null) return;
        switch (item.Kind)
        {
            case "Messages":
            case "TxMessages":
            case "Network":
            case "Root":
                ShowMessages(_db.Messages);
                break;
            case "AllSignals":
                ShowSignals(_db.AllSignals, catalogAttributes: true);
                break;
            case "NetSignalsRoot":
                // Network Signals list — treat like catalog
                ShowSignals(_db.AllSignals, catalogAttributes: true);
                break;
            case "NetSignalsNode":
            {
                var name = item.Payload as string ?? item.Title;
                ShowSignals(SignalsForNetNode(name), nodeContext: name, catalogAttributes: false);
                break;
            }
            case "Message":
            case "NetworkTxMessage":
            case "NodeTxMessage":
            case "NodeRxMessage":
                if (item.Payload is DbcMessage m)
                    ShowSignals(m.Signals, catalogAttributes: false);
                break;
            case "SignalGroup":
                if (item.Payload is DbcSignalGroup grp)
                    ShowSignals(SignalsInGroup(grp), catalogAttributes: false);
                break;
            case "Signal":
                if (item.Payload is DbcSignal sCat)
                    ShowSignals(new[] { sCat }, catalogAttributes: true);
                break;
            case "MessageSignal":
            case "NodeTxSignal":
            case "NodeRxSignal":
                if (item.Payload is DbcSignal sMsg)
                    ShowSignals(new[] { sMsg }, catalogAttributes: false);
                break;
            case "VtSig":
                // Single-click → right list; double-click tree/grid → dialog
                if (item.Payload is DbcSignal vs)
                    ShowValueTables(new ValueTableRowViewModel(vs));
                break;
            case "Env":
                ShowEnvVars(_db.EnvVars);
                break;
            case "EnvVar":
                // Single-click → right list; double-click tree/grid → dialog
                if (item.Payload is DbcEnvVar ev)
                    ShowEnvVars(new[] { ev });
                break;
            case "VtEv":
                if (item.Payload is DbcEnvVar ve)
                    ShowValueTables(new ValueTableRowViewModel(ve));
                break;
            case "ValueDesc":
                break;
            case "Ecu":
            case "EcuNode":
            case "Node":
            case "EcuUnderNet":
            case "NodeTxFolder":
            {
                // Click node / Tx Messages → Tx message list on the right
                var name = item.Payload as string ?? item.Title;
                ShowMessages(_db.Messages
                    .Where(x => string.Equals(x.Transmitter, name, StringComparison.OrdinalIgnoreCase))
                    .ToList(), nodeContext: name);
                break;
            }
            case "NodeRxFolder":
            {
                var name = item.Payload as string ?? item.Title;
                ShowMessages(_db.Messages.Where(x =>
                    x.Signals.Any(sig => sig.Receivers.Any(r =>
                        string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))).ToList(),
                    nodeContext: name);
                break;
            }
            case "NodeTxSigFolder":
            {
                var name = item.Payload as string ?? item.Title;
                var sigs = _db.Messages
                    .Where(x => string.Equals(x.Transmitter, name, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Signals)
                    .ToList();
                ShowSignals(sigs, nodeContext: name, catalogAttributes: false);
                break;
            }
            case "NodeRxSigFolder":
            {
                var name = item.Payload as string ?? item.Title;
                var sigs = _db.AllSignals
                    .Where(s => s.Receivers.Any(r =>
                        string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                ShowSignals(sigs, nodeContext: name, catalogAttributes: false);
                break;
            }
            case "EcuRoot":
            case "NodesRoot":
                ShowMessages(_db.Messages);
                break;
            default:
                break;
        }
    }

    private void ClearDetailGrids()
    {
        MessageRows.Clear();
        SignalRows.Clear();
        EnvVarRows.Clear();
        ValueTableRows.Clear();
    }

    private void ShowMessages(IList<DbcMessage> messages, string? nodeContext = null)
    {
        DetailMode = "Messages";
        SelectedDetail = null;
        ClearDetailGrids();
        foreach (var m in messages)
            MessageRows.Add(new MessageRowViewModel(m));
        StatusText = string.IsNullOrEmpty(nodeContext)
            ? $"{messages.Count} Message(s)  |  Ready"
            : $"Node: {nodeContext}  |  {messages.Count} Message(s)  |  Ready";
    }

    private void ShowSignals(IList<DbcSignal> signals, string? nodeContext = null, string? displayName = null,
        bool catalogAttributes = false)
    {
        DetailMode = "Signals";
        SelectedDetail = null;
        ClearDetailGrids();
        /* Vector Overall View: ! on signals whose bits overlap within the same message. */
        var overlapping = new HashSet<DbcSignal>();
        foreach (var g in signals.GroupBy(s => s.MessageId))
            foreach (var s in DbcBitLayout.FindOverlappingSignals(g))
                overlapping.Add(s);
        foreach (var s in signals)
            SignalRows.Add(new SignalRowViewModel(s, displayName, catalogAttributes, overlapping.Contains(s)));
        StatusText = string.IsNullOrEmpty(nodeContext)
            ? $"{signals.Count} Signal(s)  |  Ready"
            : $"Node: {nodeContext}  |  {signals.Count} Signal(s)  |  Ready";
    }

    private void ShowEnvVars(IList<DbcEnvVar> envVars)
    {
        DetailMode = "EnvVars";
        SelectedDetail = null;
        ClearDetailGrids();
        foreach (var e in envVars)
            EnvVarRows.Add(new EnvVarRowViewModel(e));
        StatusText = $"{envVars.Count} Environment Variable(s)  |  Ready";
    }

    private void ShowValueTables(ValueTableRowViewModel row)
    {
        DetailMode = "ValueTables";
        SelectedDetail = null;
        ClearDetailGrids();
        ValueTableRows.Add(row);
        StatusText = $"Value Table '{row.Name}'  |  Ready";
    }
}
