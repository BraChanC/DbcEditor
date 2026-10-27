using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DbcEditor.Models;

namespace DbcEditor.Views;

/// <summary>Vector: Signal Group 'Name' — Definition | Comment.</summary>
public partial class SignalGroupEditorWindow : Window
{
    private readonly DbcSignalGroup _group;
    public SignalGroupEditorVm Vm { get; }

    public SignalGroupEditorWindow(DbcSignalGroup group, DbcMessage? message)
    {
        InitializeComponent();
        _group = group;
        Vm = new SignalGroupEditorVm(group, message);
        DataContext = Vm;
        Title = $"Signal Group '{group.Name}'";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_group);
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_group);
        Title = $"Signal Group '{_group.Name}'";
    }
}

public sealed class SignalGroupEditorVm : INotifyPropertyChanged
{
    public string Name { get; set; }
    public string GroupType { get; set; }
    public string Multiplexer { get; set; }
    public string Comment { get; set; }

    public List<string> GroupTypes { get; } = new() { "Group" };
    public List<string> Multiplexers { get; } = new() { "– No Multiplexer –" };

    public SignalGroupEditorVm(DbcSignalGroup group, DbcMessage? message)
    {
        Name = group.Name;
        GroupType = "Group";
        Comment = group.Comment ?? "";
        if (message != null)
        {
            foreach (var s in message.Signals)
            {
                var mux = s.Multiplexing ?? "";
                var isMuxor = mux.Equals("Multiplexor", StringComparison.OrdinalIgnoreCase)
                    || mux.Contains(" / Multiplexor", StringComparison.OrdinalIgnoreCase);
                if (isMuxor && !Multiplexers.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                    Multiplexers.Add(s.Name);
            }
        }
        Multiplexer = "– No Multiplexer –";
    }

    public void ApplyTo(DbcSignalGroup group)
    {
        var name = Name?.Trim();
        if (!string.IsNullOrEmpty(name))
            group.Name = name;
        group.Comment = Comment ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
