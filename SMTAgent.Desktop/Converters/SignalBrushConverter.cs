using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SMTAgent.Core.Models;

namespace SMTAgent.Desktop.Converters;

public sealed class SignalBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            SmtSignalType.Bullish => new SolidColorBrush(Color.FromRgb(51, 221, 162)),
            SmtSignalType.Bearish => new SolidColorBrush(Color.FromRgb(255, 93, 121)),
            _ => new SolidColorBrush(Color.FromRgb(154, 166, 188))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
