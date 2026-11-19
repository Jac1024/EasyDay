using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasyDay.App.Infrastructure;

/// <summary>true → Collapsed，false → Visible。用于“收起日历”这类反向绑定。</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
