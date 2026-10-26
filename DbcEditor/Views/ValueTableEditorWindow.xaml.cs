using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using DbcEditor.Models;

namespace DbcEditor.Views;

public partial class ValueTableEditorWindow : Window
{
    private readonly DbcSignal? _signal;
    private readonly DbcEnvVar? _env;
    public ValueTableEditorVm Vm { get; }

    public ValueTableEditorWindow(DbcSignal signal)
    {
        InitializeComponent();
        _signal = signal;
        Vm = ValueTableEditorVm.ForSignal(signal);
        DataContext = Vm;
        Title = $"Value Table '{Vm.Name}'";
    }

    public ValueTableEditorWindow(DbcEnvVar env)
    {
        InitializeComponent();
        _env = env;
        Vm = ValueTableEditorVm.ForEnv(env);
        DataContext = Vm;
        Title = $"Value Table '{Vm.Name}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Apply();
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Apply();

    private void OnAdd(object sender, RoutedEventArgs e) => Vm.AddEntry();

    private void OnRemove(object sender, RoutedEventArgs e) => Vm.RemoveSelected();

    private void Apply()
    {
        if (_signal != null) Vm.ApplyToSignal(_signal);
        else if (_env != null) Vm.ApplyToEnv(_env);
    }
}

public sealed class ValueTableEditorVm : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Comment { get; set; } = "";
    public ObservableCollection<ValueDescRow> Entries { get; } = new();

    private ValueDescRow? _selectedEntry;
    public ValueDescRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            _selectedEntry = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedEntry != null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public static ValueTableEditorVm ForSignal(DbcSignal s)
    {
        var vm = new ValueTableEditorVm
        {
            Name = $"VtSig_{s.Name}",
            Comment = string.IsNullOrWhiteSpace(s.ValueTableComment)
                ? $"Used by {s.MessageName}::{s.Name}"
                : s.ValueTableComment
        };
        foreach (var v in s.ValueDescriptions.OrderBy(x => x.Value))
            vm.Entries.Add(ValueDescRow.From(v));
        return vm;
    }

    public static ValueTableEditorVm ForEnv(DbcEnvVar e)
    {
        var vm = new ValueTableEditorVm
        {
            Name = $"VtEv_{e.Name}",
            Comment = string.IsNullOrWhiteSpace(e.ValueTableComment)
                ? $"Used by {e.Name}"
                : e.ValueTableComment
        };
        foreach (var v in e.ValueDescriptions.OrderBy(x => x.Value))
            vm.Entries.Add(ValueDescRow.From(v));
        return vm;
    }

    public void AddEntry()
    {
        var next = Entries.Count == 0 ? 0 : Entries.Max(x => x.Value) + 1;
        // Vector default: Description for the value '0x4'
        var row = new ValueDescRow
        {
            Value = next,
            Description = $"Description for the value '0x{next:X}'"
        };
        Entries.Add(row);
        SelectedEntry = row;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry == null) return;
        Entries.Remove(SelectedEntry);
        SelectedEntry = null;
    }

    public void ApplyToSignal(DbcSignal s)
    {
        s.ValueTableComment = Comment ?? "";
        s.ValueDescriptions.Clear();
        foreach (var e in Entries.OrderBy(x => x.Value))
            s.ValueDescriptions.Add(new DbcValueDesc { Value = e.Value, Description = e.Description ?? "" });
    }

    public void ApplyToEnv(DbcEnvVar e)
    {
        e.ValueTableComment = Comment ?? "";
        e.ValueDescriptions.Clear();
        foreach (var r in Entries.OrderBy(x => x.Value))
            e.ValueDescriptions.Add(new DbcValueDesc { Value = r.Value, Description = r.Description ?? "" });
    }
}

public sealed class ValueDescRow : INotifyPropertyChanged
{
    private long _value;
    private string _description = "";
    private string _valueText = "0x0";

    public long Value
    {
        get => _value;
        set
        {
            _value = value;
            _valueText = $"0x{value:X}";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueText));
        }
    }

    public string ValueText
    {
        get => _valueText;
        set
        {
            _valueText = value ?? "";
            if (TryParse(_valueText, out var v))
                _value = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Value));
        }
    }

    public string Description
    {
        get => _description;
        set { _description = value ?? ""; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public static ValueDescRow From(DbcValueDesc v) => new()
    {
        Value = v.Value,
        Description = v.Description
    };

    private static bool TryParse(string text, out long value)
    {
        value = 0;
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
