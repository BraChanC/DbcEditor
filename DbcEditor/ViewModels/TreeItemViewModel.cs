using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DbcEditor.ViewModels;

public partial class TreeItemViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _kind = "";
    [ObservableProperty] private object? _payload;
    public ObservableCollection<TreeItemViewModel> Children { get; } = new();
}
