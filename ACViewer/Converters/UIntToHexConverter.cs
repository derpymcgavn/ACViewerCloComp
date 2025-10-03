using System;
using System.Globalization;
using System.Windows.Data;

namespace ACViewer.Converters
{
    public class UIntToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            try
            {
                if (value is uint u) return $"0x{u:X8}";
                if (value is int i && i >= 0) return $"0x{(uint)i:X8}";
                if (value is string s && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)) return $"0x{parsed:X8}";
            }
            catch { }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // One-way usage only
            return Binding.DoNothing;
        }
    }
}
