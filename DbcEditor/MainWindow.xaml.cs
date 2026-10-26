using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DbcEditor.ViewModels;

namespace DbcEditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeItemViewModel item)
            Vm.SelectedTreeItem = item;
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedTreeItem != null)
            Vm.ActivateTreeItem(Vm.SelectedTreeItem);
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.EditSelectedCommand.CanExecute(null))
            Vm.EditSelectedCommand.Execute(null);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var dbc = files.FirstOrDefault(f => f.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase));
            if (dbc != null) Vm.LoadPath(dbc);
        }
    }
}
