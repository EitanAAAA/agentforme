using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SMTAgent.Core.Models;

namespace SMTAgent.Desktop.Converters;

public sealed class DataStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            DataConnectionStatus.Connected => new SolidColorBrush(Color.FromRgb(48, 211, 143)),
            DataConnectionStatus.Updating => new SolidColorBrush(Color.FromRgb(87, 166, 255)),
            DataConnectionStatus.Delayed => new SolidColorBrush(Color.FromRgb(245, 183, 67)),
            DataConnectionStatus.Error => new SolidColorBrush(Color.FromRgb(242, 91, 109)),
            _ => new SolidColorBrush(Color.FromRgb(154, 166, 188))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
