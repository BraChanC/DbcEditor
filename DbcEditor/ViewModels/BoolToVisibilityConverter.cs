using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DbcEditor.ViewModels;

public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var expected = parameter as string ?? "";
        var actual = value as string ?? "";
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
