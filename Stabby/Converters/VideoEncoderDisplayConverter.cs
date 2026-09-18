using System.Globalization;
using System.Windows.Data;
using Stabby.Models;

namespace Stabby.Converters;

[ValueConversion(typeof(VideoEncoder), typeof(string))]
public class VideoEncoderDisplayConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is VideoEncoder encoder)
        {
            return encoder switch
            {
                VideoEncoder.H264 => "H.264 (Software - libx264)",
                VideoEncoder.H264_NVENC => "H.264 (NVIDIA NVENC)",
                VideoEncoder.H264_QSV => "H.264 (Intel QuickSync)",
                VideoEncoder.H264_AMF => "H.264 (AMD AMF)",
                _ => encoder.ToString()
            };
        }
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
