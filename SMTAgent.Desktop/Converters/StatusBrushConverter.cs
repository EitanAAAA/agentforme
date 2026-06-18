using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SMTAgent.Core.Models;

namespace SMTAgent.Desktop.Converters;

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            AgentStatus.Running => new SolidColorBrush(Color.FromRgb(48, 211, 143)),
            AgentStatus.Paused => new SolidColorBrush(Color.FromRgb(245, 183, 67)),
            _ => new SolidColorBrush(Color.FromRgb(242, 91, 109))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
