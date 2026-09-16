using System.Globalization;
using System.Windows.Data;
using Stabby.Models;

namespace Stabby.Converters;

[ValueConversion(typeof(CaptureMode), typeof(string))]
public class CaptureModeDisplayConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CaptureMode mode)
        {
            return mode switch
            {
                CaptureMode.None => "Off",
                CaptureMode.Include => "Include",
                CaptureMode.Exclude => "Exclude",
                _ => "Off"
            };
        }
        return "Off";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
