using System;
using System.Globalization;
using System.Windows.Data;
using ACViewer.Utilities;

namespace ACViewer.Converters
{
    public class UIntToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            try
            {
                if (value is uint u) return HexId.Format(u);
                if (value is int i && i >= 0) return HexId.Format((uint)i);
                if (value is string s && HexId.TryParse(s, out var parsed)) return HexId.Format(parsed);
            }
            catch { }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && HexId.TryParse(s, out var id))
                return id;

            return Binding.DoNothing;
        }
    }
}
