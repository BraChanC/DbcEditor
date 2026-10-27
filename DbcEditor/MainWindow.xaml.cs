using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DbcEditor.ViewModels;

namespace DbcEditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;
    private bool _treeDoubleClickOpening;

    private void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeItemViewModel item)
            Vm.SelectedTreeItem = item;
    }

    /// <summary>
    /// Double-click on a row opens the editor and must not expand children.
    /// Expansion stays on the expander (+ / arrow) only.
    /// </summary>
    private void OnTreeItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not TreeViewItem item) return;
        var src = e.OriginalSource as DependencyObject;
        if (FindAncestor<TreeViewItem>(src) != item) return;
        if (FindAncestor<ToggleButton>(src) != null) return;

        // Swallow the click so TreeViewItem does not toggle IsExpanded,
        // then open the editor here — handled preview stops MouseDoubleClick.
        e.Handled = true;
        item.IsSelected = true;
        OpenTreeItem(item.DataContext as TreeItemViewModel);
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_treeDoubleClickOpening) return;
        if (FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null)
            return;
        OpenTreeItem(Vm.SelectedTreeItem);
        e.Handled = true;
    }

    private void OpenTreeItem(TreeItemViewModel? node)
    {
        if (node == null || _treeDoubleClickOpening) return;
        _treeDoubleClickOpening = true;
        try
        {
            Vm.ActivateTreeItem(node);
        }
        finally
        {
            _treeDoubleClickOpening = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? src) where T : DependencyObject
    {
        while (src != null)
        {
            if (src is T match) return match;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Template cells (warning !) still open Message Signal / editors
        if (Vm.EditSelectedCommand.CanExecute(null))
            Vm.EditSelectedCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Overall View: click a message-mapped signal row → Message Signal dialog.</summary>
    private void OnSignalGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.ClickCount != 1) return;
        if (grid.SelectedItem is not SignalRowViewModel row) return;
        if (row.CatalogAttributes) return; // Signals catalog stays select-only
        // Only when row visual was hit (not empty area / scrollbar)
        var src = e.OriginalSource as DependencyObject;
        while (src != null && src is not DataGridRow && src is not DataGrid)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridRow) return;

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
