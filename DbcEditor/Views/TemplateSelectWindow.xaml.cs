using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DbcEditor.Services;

namespace DbcEditor.Views;

public partial class TemplateSelectWindow : Window
{
    public TemplateSelectWindow()
    {
        InitializeComponent();
        var items = DbcTemplateCatalog.Discover();
        TemplateList.ItemsSource = items;
        if (items.Count > 0)
            TemplateList.SelectedIndex = 0;
    }

    public DbcTemplateInfo? SelectedTemplate => TemplateList.SelectedItem as DbcTemplateInfo;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OkButton.IsEnabled = SelectedTemplate != null;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate == null) return;
        DialogResult = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedTemplate == null) return;
        DialogResult = true;
    }
}
