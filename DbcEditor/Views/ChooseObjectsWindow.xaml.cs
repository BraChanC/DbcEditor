using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DbcEditor.Models;

namespace DbcEditor.Views;

public enum ChooseObjectsMode
{
    Signals,
    Messages,
    EnvVariables,
    Nodes
}

/// <summary>
/// Vector "Choose Objects" picker (Mapped Rx Sig. / ECU Env. variables Add…).
/// </summary>
public partial class ChooseObjectsWindow : Window
{
    private readonly ChooseObjectsMode _mode;
    private readonly ObservableCollection<ChooseSignalRow> _signalRows = new();
    private readonly ObservableCollection<ChooseMessageRow> _messageRows = new();
    private readonly ObservableCollection<ChooseEnvRow> _envRows = new();
    private readonly ObservableCollection<ChooseNodeRow> _nodeRows = new();
    private ICollectionView? _view;

    public List<DbcSignal> SelectedSignals { get; } = new();
    public List<DbcMessage> SelectedMessages { get; } = new();
    public List<DbcEnvVar> SelectedEnvVars { get; } = new();
    public List<DbcNode> SelectedNodes { get; } = new();

    public ChooseObjectsWindow(DbcDatabase db, ChooseObjectsMode mode,
        IEnumerable<DbcSignal>? excludeSignals = null,
        IEnumerable<string>? excludeEnvNames = null,
        IEnumerable<string>? excludeNodeNames = null,
        IEnumerable<string>? excludeMessageNames = null)
    {
        InitializeComponent();
        _mode = mode;

        switch (mode)
        {
            case ChooseObjectsMode.Signals:
                SetupSignals(db, excludeSignals);
                break;
            case ChooseObjectsMode.Messages:
                SetupMessages(db, excludeMessageNames);
                break;
            case ChooseObjectsMode.EnvVariables:
                SetupEnvVars(db, excludeEnvNames);
                break;
            case ChooseObjectsMode.Nodes:
                SetupNodes(db, excludeNodeNames);
                break;
        }

        if (_view != null)
            _view.Filter = FilterPredicate;
    }

    private void SetupSignals(DbcDatabase db, IEnumerable<DbcSignal>? excludeSignals)
    {
        // Vector Choose Objects (signals): Filter Name | Length [Bit] | Byte Order | Value Type | Message(s)
        FilterField.ItemsSource = new[]
        {
            "Name", "Length [Bit]", "Byte Order", "Value Type", "Message(s)"
        };
        FilterField.SelectedIndex = 0;

        var exclude = new HashSet<DbcSignal>(excludeSignals ?? Array.Empty<DbcSignal>());
        foreach (var s in db.AllSignals)
        {
            if (exclude.Contains(s)) continue;
            _signalRows.Add(new ChooseSignalRow(s));
        }

        Grid.Columns.Add(CheckColumn(nameof(ChooseSignalRow.IsSelected)));
        Grid.Columns.Add(TextColumn("Name", nameof(ChooseSignalRow.Signal), "*"));
        Grid.Columns.Add(TextColumn("Length [Bit]", nameof(ChooseSignalRow.Length), "90"));
        Grid.Columns.Add(TextColumn("Byte Order", nameof(ChooseSignalRow.ByteOrder), "90"));
        Grid.Columns.Add(TextColumn("Value Type", nameof(ChooseSignalRow.ValueType), "90"));
        Grid.ItemsSource = _signalRows;
        _view = CollectionViewSource.GetDefaultView(_signalRows);
    }

    private void SetupMessages(DbcDatabase db, IEnumerable<string>? excludeMessageNames = null)
    {
        FilterField.ItemsSource = new[] { "Name", "ID", "ID-Format", "DLC [Byte]" };
        FilterField.SelectedIndex = 0;

        var exclude = new HashSet<string>(excludeMessageNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var m in db.Messages)
        {
            if (exclude.Contains(m.Name)) continue;
            _messageRows.Add(new ChooseMessageRow(m));
        }

        Grid.Columns.Add(CheckColumn(nameof(ChooseMessageRow.IsSelected)));
        Grid.Columns.Add(TextColumn("Name", nameof(ChooseMessageRow.DisplayName), "*"));
        Grid.Columns.Add(TextColumn("ID", nameof(ChooseMessageRow.Id), "80"));
        Grid.Columns.Add(TextColumn("ID-Format", nameof(ChooseMessageRow.IdFormat), "120"));
        Grid.Columns.Add(TextColumn("DLC [Byte]", nameof(ChooseMessageRow.Dlc), "80"));
        Grid.ItemsSource = _messageRows;
        _view = CollectionViewSource.GetDefaultView(_messageRows);
    }

    private void SetupEnvVars(DbcDatabase db, IEnumerable<string>? excludeEnvNames)
    {
        // Vector screenshot: Filter by Name | Value | Filter ; columns Name / Type / Unit
        FilterField.ItemsSource = new[] { "Name", "Type", "Unit" };
        FilterField.SelectedIndex = 0;

        var exclude = new HashSet<string>(excludeEnvNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var ev in db.EnvVars)
        {
            if (exclude.Contains(ev.Name)) continue;
            _envRows.Add(new ChooseEnvRow(ev));
        }

        Grid.Columns.Add(CheckColumn(nameof(ChooseEnvRow.IsSelected)));
        Grid.Columns.Add(TextColumn("Name", nameof(ChooseEnvRow.Name), "*"));
        Grid.Columns.Add(TextColumn("Type", nameof(ChooseEnvRow.Type), "100"));
        Grid.Columns.Add(TextColumn("Unit", nameof(ChooseEnvRow.Unit), "100"));
        Grid.ItemsSource = _envRows;
        _view = CollectionViewSource.GetDefaultView(_envRows);
    }

    private void SetupNodes(DbcDatabase db, IEnumerable<string>? excludeNodeNames)
    {
        // Vector Message Transmitters Add: Filter by Name | Address ; columns Name / Address
        FilterField.ItemsSource = new[] { "Name", "Address" };
        FilterField.SelectedIndex = 0;

        var exclude = new HashSet<string>(excludeNodeNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var n in db.Nodes)
        {
            if (exclude.Contains(n.Name)) continue;
            _nodeRows.Add(new ChooseNodeRow(n));
        }

        Grid.Columns.Add(CheckColumn(nameof(ChooseNodeRow.IsSelected)));
        Grid.Columns.Add(TextColumn("Name", nameof(ChooseNodeRow.Name), "*"));
        Grid.Columns.Add(TextColumn("Address", nameof(ChooseNodeRow.Address), "100"));
        Grid.ItemsSource = _nodeRows;
        _view = CollectionViewSource.GetDefaultView(_nodeRows);
    }

    private static DataGridCheckBoxColumn CheckColumn(string path) => new()
    {
        Header = "",
        Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        Width = 28
    };

    private static DataGridTextColumn TextColumn(string header, string path, string width)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            IsReadOnly = true
        };
        if (width == "*") col.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        else if (double.TryParse(width, out var w)) col.Width = w;
        return col;
    }

    private void OnFilterClick(object sender, RoutedEventArgs e) => _view?.Refresh();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _view?.Refresh();
            e.Handled = true;
        }
    }

    private bool FilterPredicate(object obj)
    {
        var value = (FilterValue.Text ?? "").Trim();
        if (string.IsNullOrEmpty(value)) return true;
        var field = FilterField.SelectedItem as string ?? "";

        if (obj is ChooseSignalRow s)
        {
            var hay = field switch
            {
                "Length [Bit]" => s.Length.ToString(),
                "Byte Order" => s.ByteOrder,
                "Value Type" => s.ValueType,
                "Message(s)" => s.Message,
                _ => s.Signal
            };
            return hay.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (obj is ChooseMessageRow m)
        {
            var hay = field switch
            {
                "ID" => m.Id,
                "ID-Format" => m.IdFormat,
                "DLC [Byte]" => m.Dlc.ToString(),
                _ => m.Name
            };
            return hay.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (obj is ChooseEnvRow ev)
        {
            var hay = field switch
            {
                "Type" => ev.Type,
                "Unit" => ev.Unit,
                _ => ev.Name
            };
            return hay.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (obj is ChooseNodeRow n)
        {
            var hay = field == "Address" ? n.Address : n.Name;
            return hay.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        SelectedSignals.Clear();
        SelectedMessages.Clear();
        SelectedEnvVars.Clear();
        SelectedNodes.Clear();

        switch (_mode)
        {
            case ChooseObjectsMode.Signals:
                Collect(_signalRows.Where(x => x.IsSelected).Select(x => x.Model), SelectedSignals);
                if (SelectedSignals.Count == 0)
                    CollectSelectedGrid<ChooseSignalRow>(r => SelectedSignals.Add(r.Model));
                break;
            case ChooseObjectsMode.Messages:
                Collect(_messageRows.Where(x => x.IsSelected).Select(x => x.Model), SelectedMessages);
                if (SelectedMessages.Count == 0)
                    CollectSelectedGrid<ChooseMessageRow>(r => SelectedMessages.Add(r.Model));
                break;
            case ChooseObjectsMode.EnvVariables:
                Collect(_envRows.Where(x => x.IsSelected).Select(x => x.Model), SelectedEnvVars);
                if (SelectedEnvVars.Count == 0)
                    CollectSelectedGrid<ChooseEnvRow>(r => SelectedEnvVars.Add(r.Model));
                break;
            case ChooseObjectsMode.Nodes:
                Collect(_nodeRows.Where(x => x.IsSelected).Select(x => x.Model), SelectedNodes);
                if (SelectedNodes.Count == 0)
                    CollectSelectedGrid<ChooseNodeRow>(r => SelectedNodes.Add(r.Model));
                break;
        }

        DialogResult = true;
    }

    private static void Collect<T>(IEnumerable<T> src, List<T> dest)
    {
        foreach (var item in src)
            dest.Add(item);
    }

    private void CollectSelectedGrid<T>(Action<T> add)
    {
        if (Grid.SelectedItems is not IList list) return;
        foreach (var item in list)
        {
            if (item is T row)
                add(row);
        }
    }
}

public sealed class ChooseSignalRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public DbcSignal Model { get; }
    public string Signal => Model.Name;
    public string Message => Model.MessageName;
    public string Multiplexing =>
        string.IsNullOrWhiteSpace(Model.Multiplexing) ? "-" : Model.Multiplexing;
    public int Startbit => Model.StartBit;
    public int Length => Model.Length;
    public string ByteOrder => Model.IsLittleEndian ? "Intel" : "Motorola";
    public string ValueType => Model.IsSigned ? "Signed" : "Unsigned";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ChooseSignalRow(DbcSignal s) => Model = s;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ChooseMessageRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public DbcMessage Model { get; }
    public string Name => Model.Name;
    public string DisplayName => $"{Model.Name} ({Model.IdHex})";
    public string Id => Model.IdHex;
    public string IdFormat => Model.IdFormat;
    public int Dlc => Model.Dlc;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ChooseMessageRow(DbcMessage m) => Model = m;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ChooseEnvRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public DbcEnvVar Model { get; }
    public string Name => Model.Name;
    public string Type => Model.Type switch
    {
        0 => "Integer",
        1 => "Float",
        2 => "String",
        3 => "Data",
        _ => Model.Type.ToString()
    };
    public string Unit => Model.Unit;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ChooseEnvRow(DbcEnvVar ev) => Model = ev;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ChooseNodeRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public DbcNode Model { get; }
    public string Name => Model.Name;
    public string Address => string.IsNullOrWhiteSpace(Model.Address) ? "0x0" : Model.Address;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ChooseNodeRow(DbcNode n) => Model = n;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
