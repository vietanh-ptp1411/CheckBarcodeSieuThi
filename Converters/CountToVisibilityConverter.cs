using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CheckBarcodeSieuThi.Converters
{
    /// <summary>Số lượng > 0 -> Visible, ngược lại Collapsed (dùng cho danh sách rỗng).</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
