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
            // Support editing: accept forms like 0x1234ABCD, 1234ABCD, decimal (discouraged but allowed)
            if (value is string s)
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
                // Try hex first
                if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    return hex;
                // Fallback decimal
                if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                    return dec;
            }
            return Binding.DoNothing; // do not update source on invalid input
        }
    }
}
