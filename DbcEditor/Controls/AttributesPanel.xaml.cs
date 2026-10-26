using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Controls;

public partial class AttributesPanel : UserControl
{
    public static readonly DependencyProperty GroupsProperty =
        DependencyProperty.Register(nameof(Groups), typeof(IEnumerable), typeof(AttributesPanel),
            new PropertyMetadata(null, OnGroupsChanged));

    public static readonly DependencyProperty DatabaseProperty =
        DependencyProperty.Register(nameof(Database), typeof(DbcDatabase), typeof(AttributesPanel));

    public static readonly DependencyProperty ObjectTypeProperty =
        DependencyProperty.Register(nameof(ObjectType), typeof(string), typeof(AttributesPanel),
            new PropertyMetadata(""));

    public IEnumerable? Groups
    {
        get => (IEnumerable?)GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    public DbcDatabase? Database
    {
        get => (DbcDatabase?)GetValue(DatabaseProperty);
        set => SetValue(DatabaseProperty, value);
    }

    public string ObjectType
    {
        get => (string)GetValue(ObjectTypeProperty);
        set => SetValue(ObjectTypeProperty, value);
    }

    /// <summary>Raised when an attribute value is committed.</summary>
    public event EventHandler<AttrCommittedEventArgs>? AttributeCommitted;

    private EditableAttrItem? _editing;

    public AttributesPanel()
    {
        InitializeComponent();
    }

    private static void OnGroupsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (AttributesPanel)d;
        p.AttrTree.ItemsSource = e.NewValue as IEnumerable;
        p.DefinitionText.Text = "";
        p.EndEdit(commit: false);
    }

    private void OnSelectedChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!ReferenceEquals(e.NewValue, _editing))
            EndEdit(commit: true);

        if (e.NewValue is EditableAttrItem item && Database != null)
        {
            DefinitionText.Text = AttributeDefinitionFormatter.Format(
                Database, string.IsNullOrEmpty(item.ObjectType) ? ObjectType : item.ObjectType,
                item.Name, item.DisplayValue);
        }
        else
        {
            DefinitionText.Text = "";
        }
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAttrItem(e.OriginalSource as DependencyObject);
        if (item == null) return;
        BeginEdit(item);
        e.Handled = true;
    }

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Click elsewhere ends edit
        if (_editing == null) return;
        var hit = FindAttrItem(e.OriginalSource as DependencyObject);
        if (hit == null || !ReferenceEquals(hit, _editing))
            EndEdit(commit: true);
    }

    private void BeginEdit(EditableAttrItem item)
    {
        if (_editing != null && !ReferenceEquals(_editing, item))
            EndEdit(commit: true);

        _editing = item;
        item.IsEditing = true;
        if (Database != null)
        {
            DefinitionText.Text = AttributeDefinitionFormatter.Format(
                Database, string.IsNullOrEmpty(item.ObjectType) ? ObjectType : item.ObjectType,
                item.Name, item.DisplayValue);
        }
    }

    private void EndEdit(bool commit)
    {
        if (_editing == null) return;
        var item = _editing;
        item.IsEditing = false;
        _editing = null;
        if (!commit) return;

        var val = (item.Value ?? "").Trim();
        AttributeCommitted?.Invoke(this, new AttrCommittedEventArgs(item.Name, val));

        if (Database != null)
        {
            DefinitionText.Text = AttributeDefinitionFormatter.Format(
                Database, string.IsNullOrEmpty(item.ObjectType) ? ObjectType : item.ObjectType,
                item.Name, item.DisplayValue);
        }
    }

    private void OnComboClosed(object sender, EventArgs e) => EndEdit(commit: true);

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        // Delay so ComboBox selection / DropDownClosed can run first
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_editing == null) return;
            if (sender is ComboBox { IsDropDownOpen: true }) return;
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null && IsDescendantOf(focused, AttrTree) &&
                FindAttrItem(focused) == _editing)
                return;
            EndEdit(commit: true);
        });
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndEdit(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEdit(commit: false);
            e.Handled = true;
        }
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && cb.IsVisible)
        {
            cb.Focus();
            cb.IsDropDownOpen = true;
        }
        else if (sender is TextBox tb && tb.IsVisible)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private static EditableAttrItem? FindAttrItem(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is FrameworkElement { DataContext: EditableAttrItem item })
                return item;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        var p = child;
        while (p != null)
        {
            if (ReferenceEquals(p, ancestor)) return true;
            p = VisualTreeHelper.GetParent(p);
        }
        return false;
    }
}

public sealed class AttrCommittedEventArgs : EventArgs
{
    public string Name { get; }
    public string Value { get; }
    public AttrCommittedEventArgs(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
