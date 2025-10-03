using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ACViewer.Converters
{
    public class ScaleToTransformConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d > 0)
                return new ScaleTransform(d, d);
            return new ScaleTransform(1, 1);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
