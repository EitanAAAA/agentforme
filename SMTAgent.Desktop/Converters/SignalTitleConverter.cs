using System.Globalization;
using System.Windows.Data;
using SMTAgent.Core.Models;

namespace SMTAgent.Desktop.Converters;

public sealed class SignalTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            SmtSignalType.Bullish => "Bullish SMT",
            SmtSignalType.Bearish => "Bearish SMT",
            _ => "SMT"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
